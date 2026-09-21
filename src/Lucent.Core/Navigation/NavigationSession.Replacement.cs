namespace Lucent.Core;

public sealed partial class NavigationSession
{
    private ReplacementReservation? _replacement;

    internal ReplacementReservation? TryReserveReplacement(
        INavigationTransactionParticipant participant,
        NavigationSnapshot current
    )
    {
        CheckOwner();
        if (
            _disposed
            || _terminated
            || _replacement is not null
            || _phase != NavigationPhase.Idle
            || !ReferenceEquals(_participant, participant)
            || !ReferenceEquals(_current, current)
        )
            return null;
        return _replacement = new ReplacementReservation(this, participant, current, _generation);
    }

    // Navigation requested by resolver/setup code records its intent immediately, but
    // preparation cannot use the participant's staged slot until replacement rolls back.
    internal sealed class ReplacementReservation(
        NavigationSession owner,
        INavigationTransactionParticipant participant,
        NavigationSnapshot current,
        long generation
    ) : IDisposable
    {
        internal bool IsCurrent =>
            ReferenceEquals(owner._replacement, this)
            && !owner._disposed
            && !owner._terminated
            && owner._phase == NavigationPhase.Idle
            && owner._generation == generation
            && ReferenceEquals(owner._participant, participant)
            && ReferenceEquals(owner._current, current);

        internal void Abort(Exception error)
        {
            owner.CheckOwner();
            // An ordinary failed render remains retryable after successful rollback.
            // A competing intent must never start while that failure is unwinding.
            if (owner._disposed || owner._activeAttempt is null)
                return;
            try
            {
                owner.EnterTerminal(error);
            }
            catch (Exception abortError)
            {
                throw new AggregateException(
                    "Replacement failure and navigation abort failed.",
                    error,
                    abortError
                );
            }
        }

        internal void Publish(Action publish, Action retire)
        {
            owner.CheckOwner();
            if (!IsCurrent)
                throw new InvalidOperationException(
                    "The replacement reservation is no longer current."
                );
            try
            {
                owner._phase = NavigationPhase.Publishing;
                publish();
                if (owner._disposed || owner._terminated)
                    throw new InvalidOperationException(
                        "Navigation terminated during replacement publication."
                    );
                owner._phase = NavigationPhase.Retiring;
                retire();
                if (owner._disposed || owner._terminated)
                    throw new InvalidOperationException(
                        "Navigation terminated during replacement retirement."
                    );
                owner._phase = NavigationPhase.Idle;
            }
            catch (Exception error)
            {
                owner.EnterTerminal(error);
                throw;
            }
        }

        public void Dispose()
        {
            owner.CheckOwner();
            if (!ReferenceEquals(owner._replacement, this))
                return;
            owner._replacement = null;
            owner.StartDeferredPreparation();
        }
    }
}
