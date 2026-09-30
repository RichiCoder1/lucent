namespace Lucent.Core;

public sealed partial class NavigationSession
{
    private bool _restorationRequested;

    /// <summary>Replays a decoded startup location through ordinary preparation and publication.</summary>
    /// <remarks>
    /// Call once, after attaching the root outlet, on an empty idle session. Rejected snapshots
    /// use the configured fallback; an expected replay rejection tries that fallback once.
    /// Supersession, cancellation, disposal and terminal failures never initiate fallback.
    /// </remarks>
    public NavigationOperation Restore(NavigationRestorePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        CheckOwner();
        var operation = CreateOperation();
        if (_disposed || _terminated)
        {
            operation.TryComplete(
                new(
                    operation.Id,
                    _disposed ? NavigationOutcomeKind.Disposed : NavigationOutcomeKind.Failed,
                    _disposed ? NavigationFailureKind.None : NavigationFailureKind.Terminal
                )
            );
            return operation;
        }
        if (_restorationRequested || !CanRestore(plan.Restoration))
        {
            operation.TryComplete(
                new(
                    operation.Id,
                    NavigationOutcomeKind.Failed,
                    NavigationFailureKind.InvalidRestorationState
                )
            );
            return operation;
        }
        _restorationRequested = true;
        var target = plan.Target;
        if (target is not null && !plan.Restoration.Allows(target.Match))
            target = null;
        // Application policy is expected to be pure, but cannot overwrite a newer intent
        // even if it re-enters navigation or tears down the owner while deciding admission.
        if (!CanRestore(plan.Restoration) || _nextOperationId != operation.Id + 1)
        {
            operation.TryComplete(
                new(
                    operation.Id,
                    _disposed ? NavigationOutcomeKind.Disposed
                        : _terminated ? NavigationOutcomeKind.Failed
                        : NavigationOutcomeKind.Superseded,
                    _terminated ? NavigationFailureKind.Terminal : NavigationFailureKind.None
                )
            );
            return operation;
        }
        var selected = target ?? plan.Restoration.Fallback;
        StartIntent(
            operation,
            selected.Location,
            selected.Match,
            NavigationHistoryAction.Push,
            NavigationOrigin.Restoration,
            restorationFallback: target is null ? null : plan.Restoration
        );
        return operation;
    }

    internal bool TryCaptureRestorationLocation(
        RouteTable routeTable,
        out NavigationSnapshot? current
    )
    {
        CheckOwner();
        current = null;
        if (
            _disposed
            || _terminated
            || _replacement is not null
            || !ReferenceEquals(_routeTable, routeTable)
            || _phase
                is NavigationPhase.Staging
                    or NavigationPhase.Publishing
                    or NavigationPhase.Retiring
        )
            return false;
        current = _current;
        return true;
    }

    private bool CanRestore(NavigationRestoration restoration) =>
        !_disposed
        && !_terminated
        && _current is null
        && _journal.Count == 0
        && _phase == NavigationPhase.Idle
        && _activeAttempt is null
        && _replacement is null
        && _participantAttached
        && ReferenceEquals(_routeTable, restoration.RouteTable);

    private bool TryRestorationFallback(NavigationAttempt attempt, NavigationOutcomeKind kind)
    {
        if (
            attempt.RestorationFallback is not { } restoration
            || kind
                is not (
                    NavigationOutcomeKind.Stayed
                    or NavigationOutcomeKind.Failed
                    or NavigationOutcomeKind.RejectedActivation
                )
        )
            return false;
        attempt.RestorationFallback = null;
        var fallback = restoration.Fallback;
        attempt.Location = fallback.Location;
        attempt.Match = fallback.Match;
        attempt.History = NavigationHistoryAction.Push;
        attempt.Target = _journal.Preview(fallback.Location, fallback.Match, attempt.History);
        attempt.RedirectDefinitions.Add(fallback.Match.DefinitionId.Value);
        BeginPreparation(attempt, NavigationPreparationPhase.Enter);
        return true;
    }
}
