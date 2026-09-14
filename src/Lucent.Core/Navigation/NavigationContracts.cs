using System.Collections.ObjectModel;
using System.Globalization;

namespace Lucent.Core;

/// <summary>Identifies how a navigation request changes the in-memory journal.</summary>
public enum NavigationHistoryAction
{
    /// <summary>Appends a new entry and drops entries ahead of the current position.</summary>
    Push,

    /// <summary>Replaces the current entry while retaining the surrounding history.</summary>
    Replace,

    /// <summary>Activates the preceding committed entry.</summary>
    Back,

    /// <summary>Activates the following committed entry.</summary>
    Forward,
}

/// <summary>Identifies the source that requested a navigation.</summary>
public enum NavigationOrigin
{
    /// <summary>An application command or generated route reference.</summary>
    Application,

    /// <summary>An external or host activation that crossed the raw-location boundary.</summary>
    Activation,

    /// <summary>A route link or equivalent authored navigation control.</summary>
    Link,

    /// <summary>A keyboard navigation command.</summary>
    Keyboard,

    /// <summary>A menu navigation command.</summary>
    Menu,

    /// <summary>An accessibility or automation command.</summary>
    Automation,

    /// <summary>A redirect returned by a preparation participant.</summary>
    Redirect,
}

/// <summary>Names the owner-visible phase of one navigation transaction.</summary>
public enum NavigationPhase
{
    /// <summary>No transaction is active.</summary>
    Idle,

    /// <summary>The current branch is negotiating departure.</summary>
    PreparingLeave,

    /// <summary>The target branch is negotiating entry and required preparation.</summary>
    PreparingEnter,

    /// <summary>A replacement branch is being created without changing committed state.</summary>
    Staging,

    /// <summary>The journal and retained outlet are being published as one owner phase.</summary>
    Publishing,

    /// <summary>Obsolete entry state and roots are being released after publication.</summary>
    Retiring,

    /// <summary>The session has encountered an unrecoverable invariant or ownership failure.</summary>
    Terminal,

    /// <summary>The session has synchronously released its owner-thread resources.</summary>
    Disposed,
}

/// <summary>Names the completed result of a navigation operation.</summary>
public enum NavigationOutcomeKind
{
    /// <summary>The target route and journal position were published.</summary>
    Committed,

    /// <summary>The request made no state change, such as a blocked guard or history boundary.</summary>
    Stayed,

    /// <summary>A newer intent or explicit cancellation superseded the request.</summary>
    Superseded,

    /// <summary>The session was disposed before the request could complete.</summary>
    Disposed,

    /// <summary>The raw activation or canonical target could not be accepted.</summary>
    RejectedActivation,

    /// <summary>An expected guard, preparation, or route failure preserved the old state.</summary>
    Failed,
}

/// <summary>Provides a finite, redacted reason for an expected navigation failure.</summary>
public enum NavigationFailureKind
{
    /// <summary>No failure occurred.</summary>
    None,

    /// <summary>The target route was not present in the authoritative route table.</summary>
    RouteNotFound,

    /// <summary>The target's query was rejected by its matched route schema.</summary>
    RejectedQuery,

    /// <summary>The target exceeded the route table's location limits.</summary>
    RejectedLocation,

    /// <summary>The activation text could not be parsed as an in-app location.</summary>
    InvalidActivation,

    /// <summary>A guard or required preparation explicitly rejected the transition.</summary>
    PreparationRejected,

    /// <summary>A guard reported an expected failure while preserving the old route.</summary>
    PreparationFailed,

    /// <summary>A requested history direction has no target entry.</summary>
    HistoryBoundary,

    /// <summary>An invariant, mount, publication, or cleanup failure terminated the session.</summary>
    Terminal,
}

/// <summary>Names the ordered preparation walk owned by the route participant.</summary>
public enum NavigationPreparationPhase
{
    /// <summary>Leave guards run from the leaf route toward the root.</summary>
    Leave,

    /// <summary>Enter guards and required preparation run from the root toward the leaf.</summary>
    Enter,
}

/// <summary>An immutable committed or provisional route entry snapshot.</summary>
public sealed record NavigationSnapshot
{
    /// <summary>Creates a route snapshot.</summary>
    public NavigationSnapshot(long entryId, RouteLocation location, RouteMatch match)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryId);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(match);
        EntryId = entryId;
        Location = location;
        Match = match;
    }

    /// <summary>Gets the stable journal-entry identity.</summary>
    public long EntryId { get; }

    /// <summary>Gets the canonical route location.</summary>
    public RouteLocation Location { get; }

    /// <summary>Gets the matched route and typed captures.</summary>
    public RouteMatch Match { get; }

    /// <summary>Gets the stable authored route identity.</summary>
    public RouteDefinitionId DefinitionId => Match.DefinitionId;
}

/// <summary>An immutable view of the bounded journal and its active index.</summary>
public sealed class NavigationJournalSnapshot
{
    private readonly ReadOnlyCollection<NavigationSnapshot> _entries;

    internal NavigationJournalSnapshot(
        IReadOnlyList<NavigationSnapshot> entries,
        int currentIndex,
        int capacity
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (currentIndex < -1 || currentIndex >= entries.Count)
            throw new ArgumentOutOfRangeException(nameof(currentIndex));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _entries = Array.AsReadOnly(entries.ToArray());
        CurrentIndex = currentIndex;
        Capacity = capacity;
    }

    /// <summary>Gets the retained entries in journal order.</summary>
    public IReadOnlyList<NavigationSnapshot> Entries => _entries;

    /// <summary>Gets the active entry index, or -1 before the first commit.</summary>
    public int CurrentIndex { get; }

    /// <summary>Gets the configured retained-entry bound.</summary>
    public int Capacity { get; }

    /// <summary>Gets the active entry, or null while no route is committed.</summary>
    public NavigationSnapshot? Current => CurrentIndex < 0 ? null : _entries[CurrentIndex];

    /// <summary>Gets whether a preceding entry is available.</summary>
    public bool CanGoBack => CurrentIndex > 0;

    /// <summary>Gets whether a following entry is available.</summary>
    public bool CanGoForward => CurrentIndex >= 0 && CurrentIndex < _entries.Count - 1;
}

/// <summary>Describes a pending transition while committed state remains authoritative.</summary>
public sealed record NavigationPending(
    long OperationId,
    long Generation,
    NavigationSnapshot Target,
    NavigationHistoryAction History,
    NavigationOrigin Origin,
    NavigationPhase Phase,
    int RedirectCount
);

/// <summary>Immutable committed data delivered to synchronous application state observers.</summary>
public sealed record NavigationCommit(
    long OperationId,
    long Generation,
    NavigationSnapshot? Previous,
    NavigationSnapshot Current,
    NavigationJournalSnapshot Journal,
    NavigationHistoryAction History,
    NavigationOrigin Origin
);

/// <summary>Runs after the session has assigned its new Current and Journal state.</summary>
public delegate void NavigationCommittedHandler(NavigationCommit commit);

/// <summary>Describes the completed result of one navigation operation.</summary>
public sealed record NavigationOutcome(
    long OperationId,
    NavigationOutcomeKind Kind,
    NavigationFailureKind FailureKind = NavigationFailureKind.None
)
{
    /// <summary>Gets whether this outcome changed the committed route.</summary>
    public bool IsCommitted => Kind == NavigationOutcomeKind.Committed;

    /// <summary>Returns a safe diagnostic representation without route values.</summary>
    public override string ToString() =>
        "navigation-outcome operation="
        + OperationId.ToString(CultureInfo.InvariantCulture)
        + " kind="
        + Kind
        + " failure="
        + FailureKind;
}

/// <summary>A completed preparation result returned by a route transaction participant.</summary>
public sealed class NavigationPreparationResult
{
    private NavigationPreparationResult(
        NavigationPreparationResultKind kind,
        NavigationFailureKind failureKind,
        RouteLocation? redirectLocation,
        NavigationHistoryAction redirectHistory
    )
    {
        Kind = kind;
        FailureKind = failureKind;
        RedirectLocation = redirectLocation;
        RedirectHistory = redirectHistory;
    }

    /// <summary>Gets the finite preparation result kind.</summary>
    public NavigationPreparationResultKind Kind { get; }

    /// <summary>Gets the expected failure reason, when <see cref="Kind"/> is a failure.</summary>
    public NavigationFailureKind FailureKind { get; }

    /// <summary>Gets the canonical redirect target, when <see cref="Kind"/> is Redirect.</summary>
    public RouteLocation? RedirectLocation { get; }

    /// <summary>Gets the history action applied when <see cref="Kind"/> is Redirect.</summary>
    public NavigationHistoryAction RedirectHistory { get; }

    /// <summary>A successful preparation result.</summary>
    public static NavigationPreparationResult Allow { get; } =
        new(
            NavigationPreparationResultKind.Allow,
            NavigationFailureKind.None,
            null,
            NavigationHistoryAction.Replace
        );

    /// <summary>A guard decision that leaves the current route and journal unchanged.</summary>
    public static NavigationPreparationResult Stay { get; } =
        new(
            NavigationPreparationResultKind.Stay,
            NavigationFailureKind.None,
            null,
            NavigationHistoryAction.Replace
        );

    /// <summary>Creates a redirect result. Redirects replace by default.</summary>
    public static NavigationPreparationResult Redirect(
        RouteLocation location,
        NavigationHistoryAction history = NavigationHistoryAction.Replace
    )
    {
        ArgumentNullException.ThrowIfNull(location);
        if (history is not NavigationHistoryAction.Push and not NavigationHistoryAction.Replace)
            throw new ArgumentOutOfRangeException(nameof(history));
        return new(
            NavigationPreparationResultKind.Redirect,
            NavigationFailureKind.None,
            location,
            history
        );
    }

    /// <summary>Creates an expected preparation failure.</summary>
    public static NavigationPreparationResult Fail(
        NavigationFailureKind failureKind = NavigationFailureKind.PreparationFailed
    )
    {
        if (
            failureKind
            is NavigationFailureKind.None
                or NavigationFailureKind.Terminal
                or NavigationFailureKind.HistoryBoundary
        )
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        return new(
            NavigationPreparationResultKind.Fail,
            failureKind,
            null,
            NavigationHistoryAction.Replace
        );
    }

    /// <summary>Creates an activation rejection without running route preparation.</summary>
    public static NavigationPreparationResult RejectedActivation(
        NavigationFailureKind failureKind = NavigationFailureKind.InvalidActivation
    )
    {
        if (
            failureKind
            is NavigationFailureKind.None
                or NavigationFailureKind.Terminal
                or NavigationFailureKind.HistoryBoundary
        )
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        return new(
            NavigationPreparationResultKind.RejectedActivation,
            failureKind,
            null,
            NavigationHistoryAction.Replace
        );
    }
}

/// <summary>Names the finite result returned by preparation.</summary>
public enum NavigationPreparationResultKind
{
    /// <summary>Preparation permits staging.</summary>
    Allow,

    /// <summary>Preparation keeps the current route.</summary>
    Stay,

    /// <summary>Preparation restarts recognition at a new target.</summary>
    Redirect,

    /// <summary>Preparation failed while preserving the old route.</summary>
    Fail,

    /// <summary>The activation target was rejected before preparation.</summary>
    RejectedActivation,
}

/// <summary>Raised when user code attempts to re-enter publication.</summary>
public sealed class NavigationReentrancyException : InvalidOperationException
{
    internal NavigationReentrancyException(string message)
        : base(message) { }
}

/// <summary>An operation that completes when its owner-thread transition reaches an outcome.</summary>
public sealed class NavigationOperation
{
    private readonly TaskCompletionSource<NavigationOutcome> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private NavigationSession? _owner;

    internal NavigationOperation(NavigationSession owner, long id)
    {
        _owner = owner;
        Id = id;
    }

    /// <summary>Gets the stable operation identity.</summary>
    public long Id { get; }

    /// <summary>Gets the completion task for this operation.</summary>
    public Task<NavigationOutcome> Completion => _completion.Task;

    /// <summary>Requests cooperative cancellation of this pending operation.</summary>
    public void Cancel()
    {
        var owner = Volatile.Read(ref _owner);
        ObjectDisposedException.ThrowIf(owner is null, this);
        owner!.Cancel(this);
    }

    internal bool IsCompleted => _completion.Task.IsCompleted;

    internal bool TryComplete(NavigationOutcome outcome) => _completion.TrySetResult(outcome);

    internal void Detach() => Interlocked.Exchange(ref _owner, null);
}

/// <summary>Preparation input passed to the retained route participant.</summary>
internal sealed record NavigationPrepareRequest(
    long OperationId,
    long Generation,
    NavigationSnapshot? Current,
    NavigationSnapshot Target,
    NavigationHistoryAction History,
    NavigationOrigin Origin,
    NavigationPreparationPhase Phase,
    int RedirectCount
);

/// <summary>Staging input passed after all preparation has succeeded.</summary>
internal sealed record NavigationStageRequest(
    long OperationId,
    long Generation,
    NavigationSnapshot? Current,
    NavigationSnapshot Target,
    NavigationHistoryAction History,
    NavigationOrigin Origin,
    NavigationJournalSnapshot Journal
);

/// <summary>Coherent committed data supplied to the participant at publication.</summary>
internal sealed record NavigationPublication(
    long OperationId,
    long Generation,
    NavigationSnapshot? Previous,
    NavigationSnapshot Current,
    NavigationJournalSnapshot Journal,
    NavigationHistoryAction History,
    NavigationOrigin Origin
);

/// <summary>Retirement data supplied after the new state has been published.</summary>
internal sealed class NavigationRetirement
{
    internal NavigationRetirement(
        long operationId,
        long generation,
        NavigationSnapshot? previous,
        IReadOnlyList<NavigationSnapshot> entries
    )
    {
        OperationId = operationId;
        Generation = generation;
        Previous = previous;
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    internal long OperationId { get; }
    internal long Generation { get; }
    internal NavigationSnapshot? Previous { get; }
    internal IReadOnlyList<NavigationSnapshot> Entries { get; }
}

/// <summary>An opaque candidate branch owned by a retained route participant.</summary>
internal abstract class NavigationStage : IDisposable
{
    private int _state;

    internal bool IsPublished => Volatile.Read(ref _state) == 1;

    internal bool IsDisposed => Volatile.Read(ref _state) == 2;

    internal void MarkPublished()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("A navigation stage was published twice.");
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            DisposeCore();
    }

    protected abstract void DisposeCore();
}

/// <summary>Private Core seam consumed by the retained RouteOutlet.</summary>
internal interface INavigationTransactionParticipant
{
    /// <summary>
    /// Prepares route guards and required reads. The token belongs to this transition only;
    /// application services must use their own lifetime for writes already accepted for saving.
    /// </summary>
    ValueTask<NavigationPreparationResult> PrepareAsync(
        NavigationPrepareRequest request,
        CancellationToken cancellationToken
    );

    NavigationStage Stage(NavigationStageRequest request);

    void Publish(NavigationStage stage, NavigationPublication publication);

    void Retire(NavigationRetirement retirement);
}
