using System.Runtime.CompilerServices;
using Lucent.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lucent.Hosting;

/// <summary>Adds Microsoft Generic Host services to a Lucent application builder.</summary>
public static class HostedApplicationBuilderExtensions
{
    /// <summary>Starts a host and exposes its async application scope to component requirements.</summary>
    /// <remarks>The host, scope and service binding are owned by the Lucent lifecycle. The root factory remains a separate deferred input to the application builder.</remarks>
    public static LucentApplicationBuilder UseHosting(
        this LucentApplicationBuilder builder,
        Func<ApplicationSession, IHost> createHost,
        Func<IServiceProvider, CancellationToken, ValueTask<bool>>? prepareClose = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(createHost);

        var hosting = new HostingStartup(createHost, prepareClose);
        builder.OnStart(hosting.StartAsync);
        if (prepareClose is not null)
            builder.OnPrepareClose(hosting.PrepareCloseAsync);
        return builder;
    }

    /// <summary>Starts a host that does not need the Lucent session during construction.</summary>
    public static LucentApplicationBuilder UseHosting(
        this LucentApplicationBuilder builder,
        Func<IHost> createHost,
        Func<IServiceProvider, CancellationToken, ValueTask<bool>>? prepareClose = null
    )
    {
        ArgumentNullException.ThrowIfNull(createHost);
        return UseHosting(builder, _ => createHost(), prepareClose);
    }

    private sealed class HostingStartup(
        Func<ApplicationSession, IHost> createHost,
        Func<IServiceProvider, CancellationToken, ValueTask<bool>>? prepareClose
    )
    {
        private readonly ConditionalWeakTable<ApplicationSession, SessionState> _sessions = new();

        internal async ValueTask StartAsync(ApplicationStartContext context)
        {
            var state = _sessions.GetValue(context.Session, static _ => new SessionState());
            var host =
                createHost(context.Session)
                ?? throw new InvalidOperationException("The host factory returned null.");

            context.OnDispose(_ => DisposeHostAsync(host));

            var stopping = host
                .Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping.Register(context.Session.RequestClose);
            context.OnDispose(_ =>
            {
                stopping.Dispose();
                return ValueTask.CompletedTask;
            });

            context.OnStop(_ => new ValueTask(host.StopAsync(CancellationToken.None)));
            await host.StartAsync(CancellationToken.None);

            var scope = host.Services.CreateAsyncScope();
            context.OnDispose(async _ =>
            {
                try
                {
                    await scope.DisposeAsync();
                }
                finally
                {
                    state.Services = null;
                }
            });
            var services = scope.ServiceProvider;
            state.Services = services;

            var binding = context.CreateServiceBinding(new ApplicationServiceSource(services));
            context.OnStop(_ =>
            {
                binding.StopAccepting();
                return ValueTask.CompletedTask;
            });
            context.OnDispose(_ =>
            {
                binding.Revoke();
                return ValueTask.CompletedTask;
            });
        }

        internal ValueTask<bool> PrepareCloseAsync(
            ApplicationCloseContext context,
            CancellationToken cancellationToken
        )
        {
            if (prepareClose is null)
                return ValueTask.FromResult(true);
            if (!_sessions.TryGetValue(context.Session, out var state) || state.Services is null)
                throw new InvalidOperationException("Application services have not started.");
            return prepareClose(state.Services, cancellationToken);
        }

        private static async ValueTask DisposeHostAsync(IHost host)
        {
            if (host is IAsyncDisposable asyncHost)
                await asyncHost.DisposeAsync();
            else
                host.Dispose();
        }

        private sealed class SessionState
        {
            internal IServiceProvider? Services { get; set; }
        }
    }
}

internal sealed class ApplicationServiceSource(IServiceProvider services)
    : IOptionalComponentServiceSource
{
    public T Resolve<T>()
        where T : class => services.GetRequiredService<T>();

    public bool TryResolve<T>(out T? value)
        where T : class
    {
        value = services.GetService<T>();
        return value is not null;
    }
}
