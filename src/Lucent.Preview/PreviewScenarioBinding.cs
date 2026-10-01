using Lucent.Core;

namespace Lucent.Preview;

/// <summary>One launch's startup and root ownership; a scenario may create multiple fresh bindings.</summary>
public abstract class PreviewScenarioBinding
{
    private protected PreviewScenarioBinding(
        PreviewScenarioDescriptor descriptor,
        TimeProvider clock
    )
    {
        Descriptor = descriptor;
        Clock = clock;
    }

    /// <summary>The immutable metadata for this launch.</summary>
    public PreviewScenarioDescriptor Descriptor { get; }

    /// <summary>The explicit controlled clock supplied for this launch.</summary>
    public TimeProvider Clock { get; }

    /// <summary>Creates the root once, only for the session that completed this binding's setup.</summary>
    public abstract ComponentRecipe CreateRoot(ApplicationSession session);
}

internal sealed class TypedPreviewScenarioBinding<TFixture>(
    PreviewScenarioDescriptor descriptor,
    TimeProvider clock,
    Func<PreviewSetupContext, CancellationToken, ValueTask<TFixture>> setup,
    Func<TFixture, ApplicationSession, ComponentRecipe> createRoot,
    CancellationToken cancellationToken
) : PreviewScenarioBinding(descriptor, clock)
{
    private ApplicationSession? _session;
    private TFixture _fixture = default!;
    private int _startup;
    private int _ready;
    private int _root;
    private int _ownerThread;

    internal async ValueTask StartAsync(ApplicationStartContext start)
    {
        if (Interlocked.CompareExchange(ref _startup, 1, 0) != 0)
            throw new InvalidOperationException("A preview binding can start only once.");
        cancellationToken.ThrowIfCancellationRequested();
        if (start.Session.Composition.Design.Purpose != CompositionPurpose.Preview)
            throw new InvalidOperationException(
                "A preview scenario requires explicit Preview composition purpose."
            );
        _session = start.Session;
        _ownerThread = Environment.CurrentManagedThreadId;
        var context = new PreviewSetupContext(start, Descriptor, Clock);
        try
        {
            _fixture = await setup(context, cancellationToken);
        }
        finally
        {
            try
            {
                context.RegisterServiceTeardown();
            }
            finally
            {
                context.Expire();
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _ready, 1);
    }

    public override ComponentRecipe CreateRoot(ApplicationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!ReferenceEquals(session, _session))
            throw new InvalidOperationException("A preview root requires its own startup session.");
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "A preview root must be created on its owner thread."
            );
        if (Volatile.Read(ref _ready) == 0)
            throw new InvalidOperationException("Preview setup has not completed successfully.");
        cancellationToken.ThrowIfCancellationRequested();
        CheckStarting(session);
        if (Interlocked.CompareExchange(ref _root, 1, 0) != 0)
            throw new InvalidOperationException("A preview binding creates its root only once.");
        var root = createRoot(_fixture, session);
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();
        CheckStarting(session);
        return root;
    }

    private static void CheckStarting(ApplicationSession session)
    {
        if (
            session.IsCompleted
            || session.IsCloseRequested
            || session.Composition.IsDisposed
            || session.Status.Phase != ApplicationPhase.Starting
        )
            throw new InvalidOperationException(
                "A preview root requires an active starting session."
            );
    }
}
