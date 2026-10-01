using Lucent.Core;

namespace Lucent.Preview;

/// <summary>Scoped startup capabilities for one preview fixture; ownership is always explicit.</summary>
public sealed class PreviewSetupContext
{
    private readonly ApplicationStartContext _start;
    private ComponentServiceBinding? _services;
    private int _active = 1;

    internal PreviewSetupContext(
        ApplicationStartContext start,
        PreviewScenarioDescriptor descriptor,
        TimeProvider clock
    )
    {
        _start = start;
        Descriptor = descriptor;
        Clock = clock;
    }

    /// <summary>The immutable metadata for this scenario.</summary>
    public PreviewScenarioDescriptor Descriptor { get; }

    /// <summary>The caller's fresh controlled clock, shared with the chosen preview host.</summary>
    public TimeProvider Clock { get; }

    /// <summary>The borrowed startup session; this context does not own or dispose it.</summary>
    public ApplicationSession Session => _start.Session;

    /// <summary>Provides an explicitly supplied typed value to the application root.</summary>
    public PreviewSetupContext ProvideRootContext<T>(T value)
    {
        CheckActive();
        _start.ProvideRootContext(value);
        return this;
    }

    /// <summary>Creates the existing single application service binding with ordered stop and revocation.</summary>
    public ComponentServiceBinding CreateServiceBinding(IComponentServiceSource source)
    {
        CheckActive();
        var services = _start.CreateServiceBinding(source);
        _services = services;
        return services;
    }

    /// <summary>Registers explicit cleanup before composition disposal.</summary>
    public PreviewSetupContext OnStop(Func<ApplicationCleanupContext, ValueTask> cleanup)
    {
        CheckActive();
        _start.OnStop(cleanup);
        return this;
    }

    /// <summary>Registers explicit cleanup before composition disposal.</summary>
    public PreviewSetupContext OnStop(Func<ValueTask> cleanup)
    {
        CheckActive();
        _start.OnStop(cleanup);
        return this;
    }

    /// <summary>Registers explicit resource disposal after composition disposal.</summary>
    public PreviewSetupContext OnDispose(Func<ApplicationCleanupContext, ValueTask> cleanup)
    {
        CheckActive();
        _start.OnDispose(cleanup);
        return this;
    }

    /// <summary>Registers explicit resource disposal after composition disposal.</summary>
    public PreviewSetupContext OnDispose(Func<ValueTask> cleanup)
    {
        CheckActive();
        _start.OnDispose(cleanup);
        return this;
    }

    internal void RegisterServiceTeardown()
    {
        if (_services is not { } services)
            return;
        _start.OnStop(() =>
        {
            services.StopAccepting();
            return ValueTask.CompletedTask;
        });
        _start.OnDispose(() =>
        {
            services.Revoke();
            return ValueTask.CompletedTask;
        });
    }

    internal void Expire() => Interlocked.Exchange(ref _active, 0);

    private void CheckActive()
    {
        if (Volatile.Read(ref _active) == 0)
            throw new InvalidOperationException(
                "Preview setup registrations are available only while setup is running."
            );
    }
}
