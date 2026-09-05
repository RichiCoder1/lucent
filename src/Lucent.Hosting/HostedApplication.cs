using System.Runtime.ExceptionServices;
using Lucent.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lucent.Hosting;

/// <summary>Adapts the Microsoft Generic Host to one Lucent application session.</summary>
/// <remarks>Factories execute on the UI owner. Resolve models only in the root factory; pass typed
/// models to .lui components. One async DI scope owns application models, independent of elements.
/// Close preparation must stop accepting new writes and drain already accepted writes before returning true.
/// A false result or exception leaves the session available for retry. Hosted-service stop is terminal.</remarks>
public sealed class HostedApplication : IApplicationLifecycle
{
    private readonly Func<ApplicationSession, IHost> _createHost;
    private readonly Func<IServiceProvider, ApplicationSession, ComponentRecipe> _createRoot;
    private readonly Func<IServiceProvider, ValueTask<bool>>? _prepareClose;
    private IHost? _host;
    private AsyncServiceScope? _scope;
    private CancellationTokenRegistration _stopping;
    private int _started;
    private bool _disposed;

    /// <summary>Creates a one-shot adapter; the host factory transfers ownership to this adapter.</summary>
    public HostedApplication(
        Func<ApplicationSession, IHost> createHost,
        Func<IServiceProvider, ApplicationSession, ComponentRecipe> createRoot,
        Func<IServiceProvider, ValueTask<bool>>? prepareClose = null
    )
    {
        _createHost = createHost ?? throw new ArgumentNullException(nameof(createHost));
        _createRoot = createRoot ?? throw new ArgumentNullException(nameof(createRoot));
        _prepareClose = prepareClose;
    }

    /// <summary>Creates an explicit desktop host builder with no default configuration or logging providers.</summary>
    /// <remarks>Add configuration providers and logging sinks as needed. The desktop, rather than ConsoleLifetime,
    /// controls the loop. Prefer factory registrations or statically visible constructors for NativeAOT;
    /// use source-generated configuration binding when binding typed options.</remarks>
    public static HostApplicationBuilder CreateBuilder()
    {
        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        builder.Services.AddSingleton<IHostLifetime, DesktopLifetime>();
        builder.ConfigureContainer(
            new DefaultServiceProviderFactory(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            )
        );
        return builder;
    }

    /// <inheritdoc />
    public async ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Interlocked.Exchange(ref _started, 1) != 0 || _disposed)
            throw new InvalidOperationException("A hosted application can start only once.");
        _host =
            _createHost(session)
            ?? throw new InvalidOperationException("The host factory returned null.");
        _stopping = _host
            .Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.Register(session.RequestClose);
        await _host.StartAsync(CancellationToken.None);
        _scope = _host.Services.CreateAsyncScope();
        return _createRoot(_scope.Value.ServiceProvider, session)
            ?? throw new InvalidOperationException("The application root factory returned null.");
    }

    /// <inheritdoc />
    public ValueTask<bool> PrepareCloseAsync() =>
        _prepareClose is null
            ? ValueTask.FromResult(true)
            : _prepareClose(
                (
                    _scope
                    ?? throw new InvalidOperationException("Application services have not started.")
                ).ServiceProvider
            );

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        if (_host is not null)
            await _host.StopAsync(CancellationToken.None);
    }

    /// <summary>Releases the application model scope and host independently, preserving both failures.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        var errors = new List<Exception>();
        try
        {
            _stopping.Dispose();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        if (_scope is { } scope)
        {
            _scope = null;
            try
            {
                await scope.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        if (_host is { } host)
        {
            _host = null;
            try
            {
                if (host is IAsyncDisposable asyncHost)
                    await asyncHost.DisposeAsync();
                else
                    host.Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        if (errors.Count == 1)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1)
            throw new AggregateException("Application service cleanup failed.", errors);
    }

    private sealed class DesktopLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
