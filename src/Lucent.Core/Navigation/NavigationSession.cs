using System.Globalization;
using System.Text;

namespace Lucent.Core;

/// <summary>
/// Owns one bounded, owner-thread navigation journal and its guarded publication protocol.
/// </summary>
/// <remarks>
/// A session deliberately keeps the current route unchanged until leave and enter preparation,
/// route staging, and publication have succeeded. The retained outlet joins through the private
/// participant seam; route parsing and matching remain owned by <see cref="RouteTable"/>.
/// </remarks>
public sealed class NavigationSession : IDisposable
{
    /// <summary>The initial retention bound used when an application does not choose one.</summary>
    public const int DefaultMaximumEntries = 64;

    /// <summary>The maximum number of redirects one operation may follow.</summary>
    public const int DefaultMaximumRedirects = 16;

    private readonly ReactiveScope _scope;
    private readonly ReactiveGraph _graph;
    private readonly RouteTable _routeTable;
    private readonly NavigationJournal _journal;
    private readonly List<CommittedRegistration> _committedObservers = [];
    private readonly List<IdleRegistration> _idleObservers = [];
    private readonly Signal<NavigationSnapshot?> _currentSignal;
    private readonly Signal<NavigationPending?> _pendingSignal;
    private readonly Signal<NavigationJournalSnapshot> _journalSignal;
    private readonly Signal<long> _revisionSignal;
    private readonly int _maximumRedirects;
    private INavigationTransactionParticipant _participant = EmptyNavigationParticipant.Instance;
    private bool _participantAttached;
    private NavigationSnapshot? _current;
    private NavigationPending? _pending;
    private NavigationJournalSnapshot _journalSnapshot;
    private NavigationAttempt? _activeAttempt;
    private NavigationAttempt? _deferredAttempt;
    private bool _preparationInvocationActive;
    private long _nextOperationId = 1;
    private long _generation;
    private NavigationPhase _phase;
    private bool _terminated;
    private bool _disposed;
    private Exception? _terminalException;
    private string[] _lastRedirectDefinitions = [];

    /// <summary>Creates a session owned by <paramref name="owner"/>.</summary>
    public NavigationSession(
        ReactiveScope owner,
        RouteTable routeTable,
        RouteLocation? initialLocation = null,
        int maximumEntries = DefaultMaximumEntries,
        int maximumRedirects = DefaultMaximumRedirects
    )
    {
        ArgumentNullException.ThrowIfNull(routeTable);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRedirects);

        _routeTable = routeTable;
        _maximumRedirects = maximumRedirects;
        _journal = new(maximumEntries);
        _scope = owner.CreateChild("navigation");
        _graph = _scope.Graph;
        _currentSignal = _scope.Signal<NavigationSnapshot?>(null, "navigation.current");
        _pendingSignal = _scope.Signal<NavigationPending?>(null, "navigation.pending");
        _journalSnapshot = _journal.Snapshot();
        _journalSignal = _scope.Signal(_journalSnapshot, "navigation.journal");
        _revisionSignal = _scope.Signal(0L, "navigation.revision");
        _phase = NavigationPhase.Idle;

        if (initialLocation is not null)
        {
            var result = routeTable.Match(initialLocation);
            if (result.Status != RouteMatchStatus.Matched || result.Match is null)
            {
                _scope.Dispose();
                throw new ArgumentException(
                    "The initial route location did not match the route table.",
                    nameof(initialLocation)
                );
            }
            _current = _journal.Initialize(initialLocation, result.Match);
            _journalSnapshot = _journal.Snapshot();
            _currentSignal.Value = _current;
            _journalSignal.Value = _journalSnapshot;
            _revisionSignal.Value = 1;
        }

        // The child scope is the owner-lifetime boundary. Its cleanup callback only releases
        // session state because ReactiveScope marks itself disposed before running callbacks.
        _scope.OnDispose(DisposeFromOwnerScope);
    }

    /// <summary>Gets the one authoritative route table used by this session.</summary>
    public RouteTable RouteTable => _routeTable;

    /// <summary>Gets the owning graph for Core navigation interaction integration.</summary>
    internal ReactiveGraph Graph => _graph;

    /// <summary>
    /// Registers a synchronous owner-thread callback after this session assigns a committed
    /// route and journal snapshot. The supplied scope owns the registration.
    /// </summary>
    /// <remarks>
    /// Callbacks run inside the publication batch before route retirement and must only apply
    /// prepared application state. Starting another navigation from a callback is a terminal
    /// reentrancy error.
    /// </remarks>
    public IDisposable RegisterCommitted(ReactiveScope owner, NavigationCommittedHandler callback)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(callback);
        CheckOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(owner.Graph, _graph))
            throw new ArgumentException(
                "A committed observer must belong to the session's reactive graph.",
                nameof(owner)
            );
        var registration = new CommittedRegistration(this, callback);
        _committedObservers.Add(registration);
        try
        {
            owner.OnDispose(registration.Dispose);
        }
        catch
        {
            registration.Dispose();
            throw;
        }
        return registration;
    }

    internal IDisposable RegisterIdle(ReactiveScope owner, Action callback)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(callback);
        CheckOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(owner.Graph, _graph))
            throw new ArgumentException(
                "An idle observer must belong to the session's reactive graph.",
                nameof(owner)
            );
        var registration = new IdleRegistration(this, callback);
        _idleObservers.Add(registration);
        try
        {
            owner.OnDispose(registration.Dispose);
        }
        catch
        {
            _idleObservers.Remove(registration);
            throw;
        }
        return registration;
    }

    /// <summary>Gets the committed route, or null before the first successful commit.</summary>
    public NavigationSnapshot? Current
    {
        get
        {
            CheckOwner();
            if (!_disposed)
                _ = _currentSignal.Value;
            return _current;
        }
    }

    /// <summary>Gets the pending transition while the committed route remains authoritative.</summary>
    public NavigationPending? Pending
    {
        get
        {
            CheckOwner();
            if (!_disposed)
                _ = _pendingSignal.Value;
            return _pending;
        }
    }

    /// <summary>Gets the current bounded journal snapshot.</summary>
    public NavigationJournalSnapshot Journal
    {
        get
        {
            CheckOwner();
            if (!_disposed)
                _ = _journalSignal.Value;
            return _journalSnapshot;
        }
    }

    /// <summary>Gets whether a committed preceding entry is available.</summary>
    public bool CanGoBack
    {
        get
        {
            CheckOwner();
            if (!_disposed)
                _ = _revisionSignal.Value;
            return _journal.CanGoBack;
        }
    }

    /// <summary>Gets whether a committed following entry is available.</summary>
    public bool CanGoForward
    {
        get
        {
            CheckOwner();
            if (!_disposed)
                _ = _revisionSignal.Value;
            return _journal.CanGoForward;
        }
    }

    /// <summary>Gets the current owner-thread transaction phase.</summary>
    public NavigationPhase Phase
    {
        get
        {
            CheckOwner();
            return _phase;
        }
    }

    /// <summary>Gets whether an invariant or ownership failure terminated the session.</summary>
    public bool IsTerminated
    {
        get
        {
            CheckOwner();
            return _terminated;
        }
    }

    /// <summary>Gets whether the owner-thread session has been synchronously disposed.</summary>
    public bool IsDisposed
    {
        get
        {
            CheckOwner();
            return _disposed;
        }
    }

    /// <summary>Requests a push or replace transition to a matched canonical location.</summary>
    public NavigationOperation Navigate(
        RouteReference reference,
        NavigationHistoryAction history = NavigationHistoryAction.Push,
        NavigationOrigin origin = NavigationOrigin.Application
    )
    {
        ArgumentNullException.ThrowIfNull(reference);
        CheckOwner();
        if (history is not NavigationHistoryAction.Push and not NavigationHistoryAction.Replace)
            throw new ArgumentOutOfRangeException(nameof(history));
        var operation = CreateOperation();
        if (!CanStart(operation))
            return operation;
        SupersedeActive();

        var result = _routeTable.Match(reference.Location);
        if (!TryGetMatch(result, operation))
            return operation;
        if (!ReferenceEquals(result.Match!.Pattern, reference.Pattern))
        {
            Complete(
                operation,
                NavigationOutcomeKind.RejectedActivation,
                NavigationFailureKind.InvalidActivation
            );
            return operation;
        }
        StartIntent(operation, reference.Location, result.Match, history, origin);
        return operation;
    }

    /// <summary>Requests a push or replace transition to a matched canonical location.</summary>
    public NavigationOperation Navigate(
        RouteLocation location,
        NavigationHistoryAction history = NavigationHistoryAction.Push,
        NavigationOrigin origin = NavigationOrigin.Application
    )
    {
        ArgumentNullException.ThrowIfNull(location);
        CheckOwner();
        if (history is not NavigationHistoryAction.Push and not NavigationHistoryAction.Replace)
            throw new ArgumentOutOfRangeException(nameof(history));
        var operation = CreateOperation();
        if (!CanStart(operation))
            return operation;
        SupersedeActive();

        var result = _routeTable.Match(location);
        if (!TryGetMatch(result, operation))
            return operation;
        StartIntent(operation, location, result.Match!, history, origin);
        return operation;
    }

    /// <summary>
    /// Parses one untrusted host activation through the route table's bounded location grammar.
    /// </summary>
    public NavigationOperation Activate(
        string? rawLocation,
        NavigationOrigin origin = NavigationOrigin.Activation
    )
    {
        CheckOwner();
        var operation = CreateOperation();
        if (!CanStart(operation))
            return operation;
        SupersedeActive();
        var parsed = RouteLocation.Parse(rawLocation, _routeTable.Limits);
        if (!parsed.Succeeded || parsed.Location is null)
        {
            Complete(
                operation,
                NavigationOutcomeKind.RejectedActivation,
                NavigationFailureKind.InvalidActivation
            );
            return operation;
        }
        var result = _routeTable.Match(parsed.Location);
        if (!TryGetMatch(result, operation))
            return operation;
        StartIntent(
            operation,
            parsed.Location,
            result.Match!,
            NavigationHistoryAction.Push,
            origin
        );
        return operation;
    }

    /// <summary>Requests the preceding committed journal entry.</summary>
    public NavigationOperation Back(NavigationOrigin origin = NavigationOrigin.Application)
    {
        CheckOwner();
        var operation = CreateOperation();
        if (!CanStart(operation))
            return operation;
        var target = _journal.Previous;
        if (target is null)
        {
            SupersedeActive();
            Complete(
                operation,
                NavigationOutcomeKind.Stayed,
                NavigationFailureKind.HistoryBoundary
            );
            return operation;
        }
        StartIntent(
            operation,
            target.Location,
            target.Match,
            NavigationHistoryAction.Back,
            origin,
            target
        );
        return operation;
    }

    /// <summary>Requests the following committed journal entry.</summary>
    public NavigationOperation Forward(NavigationOrigin origin = NavigationOrigin.Application)
    {
        CheckOwner();
        var operation = CreateOperation();
        if (!CanStart(operation))
            return operation;
        var target = _journal.Next;
        if (target is null)
        {
            SupersedeActive();
            Complete(
                operation,
                NavigationOutcomeKind.Stayed,
                NavigationFailureKind.HistoryBoundary
            );
            return operation;
        }
        StartIntent(
            operation,
            target.Location,
            target.Match,
            NavigationHistoryAction.Forward,
            origin,
            target
        );
        return operation;
    }

    /// <summary>Returns a deterministic redacted journal and phase diagnostic.</summary>
    public string Dump()
    {
        CheckOwner();
        var output = new StringBuilder();
        output
            .Append("navigation phase=")
            .Append(_phase)
            .Append(" disposed=")
            .Append(_disposed)
            .Append(" terminated=")
            .Append(_terminated)
            .Append(" terminal-error=")
            .Append(_terminalException is null ? "none" : "present")
            .Append(" entries=")
            .Append(_journal.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" current-index=")
            .Append(_journal.CurrentIndex.ToString(CultureInfo.InvariantCulture))
            .Append(" generation=")
            .Append(_generation.ToString(CultureInfo.InvariantCulture));
        if (_current is { } current)
        {
            output
                .Append(" current-entry=")
                .Append(current.EntryId.ToString(CultureInfo.InvariantCulture))
                .Append(" current-definition=")
                .Append(current.DefinitionId.Value);
        }
        else
            output.Append(" current=none");
        if (_activeAttempt is { } attempt)
        {
            output
                .Append(" pending-operation=")
                .Append(attempt.Operation.Id.ToString(CultureInfo.InvariantCulture))
                .Append(" pending-definition=")
                .Append(attempt.Target.DefinitionId.Value)
                .Append(" redirects=")
                .Append(attempt.RedirectCount.ToString(CultureInfo.InvariantCulture));
        }
        else if (_lastRedirectDefinitions.Length != 0)
        {
            output
                .Append(" redirect-definitions=")
                .Append(string.Join(',', _lastRedirectDefinitions));
        }
        foreach (var entry in _journalSnapshot.Entries)
        {
            output
                .Append('\n')
                .Append("entry id=")
                .Append(entry.EntryId.ToString(CultureInfo.InvariantCulture))
                .Append(" definition=")
                .Append(entry.DefinitionId.Value);
        }
        return output.Append('\n').ToString();
    }

    /// <summary>Synchronously releases the owner-thread session and completes pending work.</summary>
    public void Dispose()
    {
        CheckOwner();
        if (_disposed)
            return;
        if (
            _phase
            is NavigationPhase.Staging
                or NavigationPhase.Publishing
                or NavigationPhase.Retiring
        )
        {
            var error = new NavigationReentrancyException(
                "Navigation disposal cannot re-enter an active publication phase."
            );
            EnterTerminal(error);
            throw error;
        }

        DisposeState();
        _scope.Dispose();
    }

    private void DisposeFromOwnerScope() => DisposeState(scopeAlreadyDisposed: true);

    private void DisposeState(bool scopeAlreadyDisposed = false)
    {
        if (_disposed)
            return;
        _disposed = true;
        _generation = checked(_generation + 1);
        var active = _activeAttempt;
        _activeAttempt = null;
        _deferredAttempt = null;
        if (active is not null)
        {
            RequestCancellation(active);
            active.Operation.TryComplete(
                new NavigationOutcome(active.Operation.Id, NavigationOutcomeKind.Disposed)
            );
        }
        _pending = null;
        _phase = NavigationPhase.Disposed;
        _participant = EmptyNavigationParticipant.Instance;
        _participantAttached = false;
        _committedObservers.Clear();
        if (!scopeAlreadyDisposed)
            _pendingSignal.Value = null;
    }

    internal IDisposable AttachParticipant(INavigationTransactionParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        CheckOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_terminated)
            throw new InvalidOperationException("The navigation session is terminal.");
        if (_participantAttached)
            throw new InvalidOperationException("A navigation session has one root participant.");
        if (_phase is not NavigationPhase.Idle)
            throw new InvalidOperationException("A root participant can attach only while idle.");
        _participant = participant;
        _participantAttached = true;
        return new ParticipantRegistration(this, participant);
    }

    internal void Cancel(NavigationOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        CheckOwner();
        if (operation.IsCompleted || _activeAttempt?.Operation != operation)
            return;
        if (
            _phase
            is NavigationPhase.Staging
                or NavigationPhase.Publishing
                or NavigationPhase.Retiring
        )
        {
            var error = new NavigationReentrancyException(
                "Navigation cancellation cannot re-enter an active publication phase."
            );
            EnterTerminal(error);
            throw error;
        }
        var attempt = _activeAttempt;
        _activeAttempt = null;
        _deferredAttempt = null;
        RequestCancellation(attempt);
        operation.TryComplete(
            new NavigationOutcome(operation.Id, NavigationOutcomeKind.Superseded)
        );
        if (!_preparationInvocationActive)
        {
            _phase = NavigationPhase.Idle;
            _pending = null;
            _pendingSignal.Value = null;
            PublishIdle();
        }
    }

    private NavigationOperation CreateOperation()
    {
        var id = checked(_nextOperationId++);
        return new(this, id);
    }

    private bool CanStart(NavigationOperation operation)
    {
        if (_disposed)
        {
            Complete(operation, NavigationOutcomeKind.Disposed, NavigationFailureKind.None);
            return false;
        }
        if (_terminated)
        {
            Complete(operation, NavigationOutcomeKind.Failed, NavigationFailureKind.Terminal);
            return false;
        }
        if (
            _phase
            is NavigationPhase.Staging
                or NavigationPhase.Publishing
                or NavigationPhase.Retiring
        )
        {
            var error = new NavigationReentrancyException(
                "Navigation cannot re-enter staging, publication, or retirement."
            );
            EnterTerminal(error);
            throw error;
        }
        return true;
    }

    private static bool TryGetMatch(RouteMatchResult result, NavigationOperation operation)
    {
        if (result.Status == RouteMatchStatus.Matched && result.Match is not null)
            return true;
        var failure = result.Status switch
        {
            RouteMatchStatus.NotFound => NavigationFailureKind.RouteNotFound,
            RouteMatchStatus.RejectedQuery => NavigationFailureKind.RejectedQuery,
            RouteMatchStatus.RejectedLocation => NavigationFailureKind.RejectedLocation,
            _ => NavigationFailureKind.InvalidActivation,
        };
        Complete(operation, NavigationOutcomeKind.RejectedActivation, failure);
        return false;
    }

    private void StartIntent(
        NavigationOperation operation,
        RouteLocation location,
        RouteMatch match,
        NavigationHistoryAction history,
        NavigationOrigin origin,
        NavigationSnapshot? traversalTarget = null
    )
    {
        SupersedeActive();

        var target = traversalTarget ?? _journal.Preview(location, match, history);
        var attempt = new NavigationAttempt(
            operation,
            location,
            match,
            target,
            history,
            origin,
            checked(_generation + 1)
        );
        _generation = attempt.Generation;
        _activeAttempt = attempt;
        SetPhase(
            _current is null ? NavigationPhase.PreparingEnter : NavigationPhase.PreparingLeave
        );
        if (_preparationInvocationActive)
        {
            _deferredAttempt = attempt;
            return;
        }
        BeginPreparation(
            attempt,
            _current is null ? NavigationPreparationPhase.Enter : NavigationPreparationPhase.Leave
        );
    }

    private void SupersedeActive()
    {
        if (_activeAttempt is not { } superseded)
            return;
        _activeAttempt = null;
        RequestCancellation(superseded);
        superseded.Operation.TryComplete(
            new NavigationOutcome(superseded.Operation.Id, NavigationOutcomeKind.Superseded)
        );
        if (!_preparationInvocationActive)
        {
            _phase = NavigationPhase.Idle;
            _pending = null;
            _pendingSignal.Value = null;
        }
    }

    private void BeginPreparation(
        NavigationAttempt attempt,
        NavigationPreparationPhase preparationPhase
    )
    {
        if (!IsCurrent(attempt) || _terminated || _disposed)
            return;
        SetPhase(
            preparationPhase == NavigationPreparationPhase.Leave
                ? NavigationPhase.PreparingLeave
                : NavigationPhase.PreparingEnter
        );
        var request = new NavigationPrepareRequest(
            attempt.Operation.Id,
            attempt.Generation,
            _current,
            attempt.Target,
            attempt.History,
            attempt.Origin,
            preparationPhase,
            attempt.RedirectCount
        );
        ValueTask<NavigationPreparationResult> preparation;
        _preparationInvocationActive = true;
        try
        {
            preparation = _participant.PrepareAsync(request, attempt.Cancellation.Token);
        }
        catch (Exception exception)
        {
            _preparationInvocationActive = false;
            HandlePreparationException(attempt, exception);
            StartDeferredPreparation();
            return;
        }

        _preparationInvocationActive = false;
        if (preparation.IsCompletedSuccessfully)
        {
            try
            {
                HandlePreparationResult(
                    attempt,
                    preparationPhase,
                    preparation.GetAwaiter().GetResult()
                );
            }
            catch (Exception exception)
            {
                HandlePreparationException(attempt, exception);
            }
            StartDeferredPreparation();
            return;
        }

        _ = ObservePreparationAsync(attempt, preparationPhase, preparation);
        StartDeferredPreparation();
    }

    private async Task ObservePreparationAsync(
        NavigationAttempt attempt,
        NavigationPreparationPhase preparationPhase,
        ValueTask<NavigationPreparationResult> preparation
    )
    {
        try
        {
            var result = await preparation.ConfigureAwait(false);
            PostOwner(() =>
            {
                if (IsCurrent(attempt))
                    HandlePreparationResult(attempt, preparationPhase, result);
            });
        }
        catch (Exception exception)
        {
            PostOwner(() =>
            {
                if (IsCurrent(attempt))
                    HandlePreparationException(attempt, exception);
            });
        }
    }

    private void HandlePreparationResult(
        NavigationAttempt attempt,
        NavigationPreparationPhase preparationPhase,
        NavigationPreparationResult result
    )
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!IsCurrent(attempt))
            return;
        switch (result.Kind)
        {
            case NavigationPreparationResultKind.Allow:
                if (preparationPhase == NavigationPreparationPhase.Leave)
                {
                    BeginPreparation(attempt, NavigationPreparationPhase.Enter);
                    return;
                }
                BeginStage(attempt);
                return;

            case NavigationPreparationResultKind.Stay:
                CompleteAttempt(attempt, NavigationOutcomeKind.Stayed, NavigationFailureKind.None);
                return;

            case NavigationPreparationResultKind.Fail:
                CompleteAttempt(attempt, NavigationOutcomeKind.Failed, result.FailureKind);
                return;

            case NavigationPreparationResultKind.RejectedActivation:
                CompleteAttempt(
                    attempt,
                    NavigationOutcomeKind.RejectedActivation,
                    result.FailureKind
                );
                return;

            case NavigationPreparationResultKind.Redirect:
                ApplyRedirect(attempt, result);
                return;

            default:
                throw new InvalidOperationException("Unknown navigation preparation result.");
        }
    }

    private void ApplyRedirect(NavigationAttempt attempt, NavigationPreparationResult result)
    {
        if (result.RedirectLocation is null)
        {
            HandlePreparationException(
                attempt,
                new InvalidOperationException("A navigation redirect omitted its target.")
            );
            return;
        }
        attempt.RedirectCount = checked(attempt.RedirectCount + 1);
        if (attempt.RedirectCount > _maximumRedirects)
        {
            CompleteAttempt(
                attempt,
                NavigationOutcomeKind.Failed,
                NavigationFailureKind.PreparationRejected
            );
            return;
        }
        var match = _routeTable.Match(result.RedirectLocation);
        if (match.Status != RouteMatchStatus.Matched || match.Match is null)
        {
            CompleteAttempt(
                attempt,
                NavigationOutcomeKind.RejectedActivation,
                match.Status switch
                {
                    RouteMatchStatus.NotFound => NavigationFailureKind.RouteNotFound,
                    RouteMatchStatus.RejectedQuery => NavigationFailureKind.RejectedQuery,
                    RouteMatchStatus.RejectedLocation => NavigationFailureKind.RejectedLocation,
                    _ => NavigationFailureKind.InvalidActivation,
                }
            );
            return;
        }
        attempt.Location = result.RedirectLocation;
        attempt.Match = match.Match;
        attempt.RedirectDefinitions.Add(match.Match.DefinitionId.Value);
        attempt.History = result.RedirectHistory;
        attempt.Target = _journal.Preview(attempt.Location, attempt.Match, attempt.History);
        BeginPreparation(
            attempt,
            _current is null ? NavigationPreparationPhase.Enter : NavigationPreparationPhase.Leave
        );
    }

    private void BeginStage(NavigationAttempt attempt)
    {
        if (!IsCurrent(attempt) || _terminated || _disposed)
            return;
        SetPhase(NavigationPhase.Staging);
        NavigationJournalPlan plan;
        try
        {
            plan = _journal.Plan(attempt.History, attempt.Target);
        }
        catch (Exception exception)
        {
            EnterTerminal(exception);
            return;
        }
        var request = new NavigationStageRequest(
            attempt.Operation.Id,
            attempt.Generation,
            _current,
            plan.Target,
            attempt.History,
            attempt.Origin,
            plan.Snapshot(_journalSnapshot.Capacity)
        );
        NavigationStage? stage = null;
        try
        {
            stage = _participant.Stage(request);
            if (stage is null)
                throw new InvalidOperationException(
                    "The navigation participant returned no stage."
                );
            if (!IsCurrent(attempt) || _terminated || _disposed)
            {
                DisposeStage(stage);
                return;
            }
            Commit(attempt, plan, stage);
        }
        catch (Exception exception)
        {
            if (stage is not null && !stage.IsPublished)
                DisposeStage(stage);
            if (!_terminated && !_disposed)
                EnterTerminal(exception);
        }
    }

    private void Commit(
        NavigationAttempt attempt,
        NavigationJournalPlan plan,
        NavigationStage stage
    )
    {
        var publicationStarted = false;
        try
        {
            _graph.Batch(() =>
            {
                try
                {
                    SetPhase(NavigationPhase.Publishing);
                    publicationStarted = true;
                    _participant.Publish(
                        stage,
                        new NavigationPublication(
                            attempt.Operation.Id,
                            attempt.Generation,
                            _current,
                            plan.Target,
                            plan.Snapshot(_journalSnapshot.Capacity),
                            attempt.History,
                            attempt.Origin
                        )
                    );
                    if (_terminated || _disposed)
                        throw new InvalidOperationException(
                            "The navigation session terminated during publication."
                        );
                    if (stage.IsDisposed)
                        throw new InvalidOperationException(
                            "The navigation participant disposed a staged branch during publication."
                        );
                    stage.MarkPublished();
                    var previous = _current;
                    _journal.Commit(plan);
                    _current = plan.Target;
                    _journalSnapshot = _journal.Snapshot();
                    _currentSignal.Value = _current;
                    _journalSignal.Value = _journalSnapshot;
                    _revisionSignal.Value = checked(_revisionSignal.Value + 1);
                    PublishCommitted(
                        new NavigationCommit(
                            attempt.Operation.Id,
                            attempt.Generation,
                            previous,
                            _current,
                            _journalSnapshot,
                            attempt.History,
                            attempt.Origin
                        )
                    );
                    SetPhase(NavigationPhase.Retiring);
                    _participant.Retire(
                        new NavigationRetirement(
                            attempt.Operation.Id,
                            attempt.Generation,
                            previous,
                            plan.Retired
                        )
                    );
                    if (_terminated || _disposed)
                        throw new InvalidOperationException(
                            "The navigation session terminated during retirement."
                        );
                    _pending = null;
                    _pendingSignal.Value = null;
                }
                catch (Exception exception)
                {
                    EnterTerminalCore(exception, stage, publicationStarted);
                    throw;
                }
            });
        }
        catch (Exception exception)
        {
            if (!_terminated && !_disposed)
                EnterTerminalCore(exception, stage, publicationStarted);
            throw;
        }

        if (_terminated || _disposed || !ReferenceEquals(_activeAttempt, attempt))
            return;

        _activeAttempt = null;
        _phase = NavigationPhase.Idle;
        _lastRedirectDefinitions = attempt.RedirectDefinitions.ToArray();
        attempt.Cancellation.Dispose();
        attempt.Operation.TryComplete(
            new NavigationOutcome(attempt.Operation.Id, NavigationOutcomeKind.Committed)
        );
        PublishIdle();
    }

    private void HandlePreparationException(NavigationAttempt attempt, Exception exception)
    {
        if (!IsCurrent(attempt))
            return;
        EnterTerminal(exception);
    }

    private void CompleteAttempt(
        NavigationAttempt attempt,
        NavigationOutcomeKind kind,
        NavigationFailureKind failureKind
    )
    {
        if (!IsCurrent(attempt))
            return;
        _activeAttempt = null;
        _lastRedirectDefinitions = attempt.RedirectDefinitions.ToArray();
        attempt.Cancellation.Dispose();
        _pending = null;
        _phase = NavigationPhase.Idle;
        _pendingSignal.Value = null;
        attempt.Operation.TryComplete(
            new NavigationOutcome(attempt.Operation.Id, kind, failureKind)
        );
        PublishIdle();
    }

    private static void Complete(
        NavigationOperation operation,
        NavigationOutcomeKind kind,
        NavigationFailureKind failureKind
    ) => operation.TryComplete(new NavigationOutcome(operation.Id, kind, failureKind));

    private void SetPhase(NavigationPhase phase)
    {
        _phase = phase;
        if (_activeAttempt is { } attempt)
        {
            _pending = new NavigationPending(
                attempt.Operation.Id,
                attempt.Generation,
                attempt.Target,
                attempt.History,
                attempt.Origin,
                phase,
                attempt.RedirectCount
            );
            _pendingSignal.Value = _pending;
        }
    }

    private bool IsCurrent(NavigationAttempt attempt) =>
        !_disposed
        && !_terminated
        && ReferenceEquals(_activeAttempt, attempt)
        && attempt.Generation == _generation;

    private void StartDeferredPreparation()
    {
        if (_preparationInvocationActive)
            return;
        if (_deferredAttempt is not { } deferred)
        {
            if (
                _activeAttempt is null
                && _phase is (NavigationPhase.PreparingLeave or NavigationPhase.PreparingEnter)
            )
            {
                _phase = NavigationPhase.Idle;
                _pending = null;
                _pendingSignal.Value = null;
                PublishIdle();
            }
            return;
        }
        _deferredAttempt = null;
        if (!IsCurrent(deferred))
            return;
        BeginPreparation(
            deferred,
            _current is null ? NavigationPreparationPhase.Enter : NavigationPreparationPhase.Leave
        );
    }

    private void PostOwner(Action callback)
    {
        if (_disposed)
            return;
        try
        {
            _scope.Post(callback);
        }
        catch (ObjectDisposedException)
        {
            // Scope disposal is the owner-thread cancellation boundary. The producer task has
            // already been awaited by its observer, so this path has no unobserved fault.
        }
    }

    private static void RequestCancellation(NavigationAttempt attempt)
    {
        if (attempt.CancellationRequested)
            return;
        attempt.CancellationRequested = true;
        _ = CancelAsync(attempt.Cancellation);
    }

    private void DisposeStage(NavigationStage stage)
    {
        try
        {
            stage.Dispose();
        }
        catch (Exception exception)
        {
            EnterTerminal(exception);
        }
    }

    private static async Task CancelAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // Cancellation callback failures cannot restore or mutate a navigation transaction.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void EnterTerminal(Exception exception)
    {
        CheckOwner();
        EnterTerminalCore(
            exception,
            null,
            _phase is NavigationPhase.Publishing or NavigationPhase.Retiring
        );
    }

    private void EnterTerminalCore(
        Exception exception,
        NavigationStage? stage,
        bool publicationStarted
    )
    {
        _terminalException ??= exception;
        _terminated = true;
        _phase = NavigationPhase.Terminal;
        var active = _activeAttempt;
        _activeAttempt = null;
        if (active is not null)
            _lastRedirectDefinitions = active.RedirectDefinitions.ToArray();
        _deferredAttempt = null;
        if (active is not null)
        {
            RequestCancellation(active);
            active.Operation.TryComplete(
                new NavigationOutcome(
                    active.Operation.Id,
                    NavigationOutcomeKind.Failed,
                    NavigationFailureKind.Terminal
                )
            );
        }
        if (stage is not null && !stage.IsPublished)
        {
            try
            {
                stage.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                _terminalException = _terminalException is null
                    ? cleanupFailure
                    : new AggregateException(_terminalException, cleanupFailure);
            }
        }
        _pending = null;
        _pendingSignal.Value = null;
        if (publicationStarted)
            _journalSnapshot = _journal.Snapshot();
    }

    private void DetachParticipant(INavigationTransactionParticipant participant)
    {
        CheckOwner();
        if (!ReferenceEquals(_participant, participant))
            return;
        if (
            _phase
            is NavigationPhase.Staging
                or NavigationPhase.Publishing
                or NavigationPhase.Retiring
        )
        {
            var error = new NavigationReentrancyException(
                "A navigation participant cannot detach during publication."
            );
            EnterTerminal(error);
            throw error;
        }
        _participant = EmptyNavigationParticipant.Instance;
        _participantAttached = false;
    }

    private void CheckOwner() => _graph.CheckThread();

    private void PublishCommitted(NavigationCommit commit)
    {
        foreach (var observer in _committedObservers.ToArray())
            observer.Callback(commit);
    }

    private void PublishIdle()
    {
        if (_phase != NavigationPhase.Idle || _disposed || _terminated)
            return;
        foreach (var observer in _idleObservers.ToArray())
            observer.Callback();
    }

    private sealed class NavigationAttempt
    {
        internal NavigationAttempt(
            NavigationOperation operation,
            RouteLocation location,
            RouteMatch match,
            NavigationSnapshot target,
            NavigationHistoryAction history,
            NavigationOrigin origin,
            long generation
        )
        {
            Operation = operation;
            Location = location;
            Match = match;
            Target = target;
            History = history;
            Origin = origin;
            Generation = generation;
            RedirectDefinitions = [target.DefinitionId.Value];
        }

        internal NavigationOperation Operation { get; }
        internal RouteLocation Location { get; set; }
        internal RouteMatch Match { get; set; }
        internal NavigationSnapshot Target { get; set; }
        internal NavigationHistoryAction History { get; set; }
        internal NavigationOrigin Origin { get; }
        internal long Generation { get; }
        internal int RedirectCount { get; set; }
        internal List<string> RedirectDefinitions { get; }
        internal CancellationTokenSource Cancellation { get; } = new();
        internal bool CancellationRequested { get; set; }
    }

    private sealed class ParticipantRegistration(
        NavigationSession owner,
        INavigationTransactionParticipant participant
    ) : IDisposable
    {
        private NavigationSession? _owner = owner;
        private readonly INavigationTransactionParticipant _participant = participant;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.DetachParticipant(_participant);
        }
    }

    private sealed class CommittedRegistration(
        NavigationSession owner,
        NavigationCommittedHandler callback
    ) : IDisposable
    {
        private NavigationSession? _owner = owner;

        internal NavigationCommittedHandler Callback { get; } = callback;

        public void Dispose()
        {
            var session = Interlocked.Exchange(ref _owner, null);
            if (session is not null)
                session._committedObservers.Remove(this);
        }
    }

    private sealed class IdleRegistration(NavigationSession owner, Action callback) : IDisposable
    {
        private NavigationSession? _owner = owner;

        internal Action Callback { get; } = callback;

        public void Dispose()
        {
            var session = Interlocked.Exchange(ref _owner, null);
            if (session is not null)
                session._idleObservers.Remove(this);
        }
    }

    private sealed class EmptyNavigationParticipant : INavigationTransactionParticipant
    {
        internal static readonly EmptyNavigationParticipant Instance = new();

        public ValueTask<NavigationPreparationResult> PrepareAsync(
            NavigationPrepareRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(NavigationPreparationResult.Allow);

        public NavigationStage Stage(NavigationStageRequest request) => new EmptyNavigationStage();

        public void Publish(NavigationStage stage, NavigationPublication publication) { }

        public void Retire(NavigationRetirement retirement) { }
    }

    private sealed class EmptyNavigationStage : NavigationStage
    {
        protected override void DisposeCore() { }
    }
}
