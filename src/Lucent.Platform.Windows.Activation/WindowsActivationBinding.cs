using Lucent.Core;
using Lucent.Platform.Windows;

namespace Lucent.Platform.Windows.Activation;

/// <summary>Finite result of an application activation decision.</summary>
public enum ActivationBindingStatus
{
    /// <summary>A protocol route committed.</summary>
    Committed,

    /// <summary>The configured policy declined the request.</summary>
    PolicyRejected,

    /// <summary>The raw envelope or route was rejected.</summary>
    Rejected,

    /// <summary>A route guard or preparation declined without changing the warm route.</summary>
    NavigationStayed,

    /// <summary>A newer intent replaced this request.</summary>
    Superseded,

    /// <summary>A plain launch requested attention without navigation.</summary>
    LaunchAttention,

    /// <summary>The application was closing or disposed.</summary>
    Closing,
}

/// <summary>Observed routing and independently observed foreground request result.</summary>
public readonly record struct ActivationBindingResult(
    ActivationBindingStatus Status,
    WindowsAttentionResult? Attention = null
);

/// <summary>Application policy for accepting external routes and requesting attention.</summary>
public sealed record WindowsActivationPolicy(
    Func<ActivationEnvelope, bool> CanOpen,
    Func<ActivationEnvelope, bool> CanRequestAttention
);

/// <summary>Installs startup, mounted, close and cleanup hooks for one activation owner.</summary>
public static class WindowsActivationBinding
{
    /// <summary>
    /// Adds the full application binding. Call from the primary process while constructing
    /// the application, before Build. The navigation accessor returns the mounted router session.
    /// </summary>
    public static LucentApplicationBuilder UseWindowsActivation(
        this LucentApplicationBuilder builder,
        ActivationInbox inbox,
        Func<NavigationSession> navigation,
        NavigationRestoration restoration,
        Func<ValueTask<ReadOnlyMemory<byte>>> readSnapshot,
        WindowsActivationPolicy policy,
        Func<WindowsAttentionResult>? requestAttention = null,
        Action<ActivationBindingResult>? observe = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(restoration);
        ArgumentNullException.ThrowIfNull(readSnapshot);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policy.CanOpen);
        ArgumentNullException.ThrowIfNull(policy.CanRequestAttention);

        var state = new Binding(
            inbox,
            navigation,
            restoration,
            readSnapshot,
            policy,
            requestAttention,
            observe
        );
        builder.OnMounted(state.OnMounted);
        builder.OnPrepareClose(
            (context, _) =>
            {
                inbox.BeginClose();
                context.OnDeclined(() =>
                {
                    inbox.CancelClose();
                    state.OnCloseDeclined();
                });
                return ValueTask.FromResult(true);
            }
        );
        builder.OnDispose(_ =>
        {
            state.OnDisposed();
            inbox.Dispose();
            return ValueTask.CompletedTask;
        });
        return builder;
    }

    private sealed class Binding(
        ActivationInbox inbox,
        Func<NavigationSession> navigation,
        NavigationRestoration restoration,
        Func<ValueTask<ReadOnlyMemory<byte>>> readSnapshot,
        WindowsActivationPolicy policy,
        Func<WindowsAttentionResult>? requestAttention,
        Action<ActivationBindingResult>? observe
    )
    {
        private ApplicationSession? _session;
        private long _intent;
        private Task<NavigationOutcome>? _latestWarmOperation;
        private Func<ValueTask>? _deferredStartup;

        public void OnCloseDeclined()
        {
            var retry = _deferredStartup;
            _deferredStartup = null;
            if (retry is not null && _session is { IsCompleted: false } session)
                _ = session.Scope.Post(() => _ = ResumeDeferredAsync(retry));
        }

        public void OnDisposed() => _deferredStartup = null;

        private async Task ResumeDeferredAsync(Func<ValueTask> retry)
        {
            try
            {
                await retry();
            }
            catch
            {
                Report(ActivationBindingStatus.Rejected);
            }
        }

        public async ValueTask OnMounted(ApplicationSession session)
        {
            _session = session;
            var startup = inbox.TakeStartup();
            // Attach before storage I/O or navigation preparation, closing the claim/mount race.
            inbox.Attach(action => _ = session.Scope.Post(action), ReceiveWarm);
            var capturedDelivery = startup?.Sequence ?? inbox.Version;
            if (startup is { Kind: ActivationKind.Protocol })
            {
                var coldNavigation = navigation();
                if (
                    coldNavigation.Current is not null
                    || coldNavigation.Phase != NavigationPhase.Idle
                )
                    return;
                if (!Allows(policy.CanOpen, startup))
                {
                    Report(ActivationBindingStatus.PolicyRejected);
                    await RestoreFallback(capturedDelivery);
                    return;
                }
                await ActivateColdProtocol(startup, capturedDelivery);
                return;
            }
            if (startup is { Kind: ActivationKind.Rejected })
            {
                Report(ActivationBindingStatus.Rejected);
                await RestoreFallback(capturedDelivery);
                return;
            }
            ReadOnlyMemory<byte> snapshot;
            try
            {
                snapshot = await readSnapshot();
            }
            catch
            {
                snapshot = ReadOnlyMemory<byte>.Empty;
            }
            await ReplaySnapshot(snapshot, capturedDelivery);
        }

        private async ValueTask ReplaySnapshot(ReadOnlyMemory<byte> snapshot, long since)
        {
            await WaitForWarmProtocolDecision(since);
            if (TryDefer(() => ReplaySnapshot(snapshot, since)))
                return;
            var restoreNavigation = navigation();
            if (
                restoreNavigation.Current is not null
                || restoreNavigation.Phase != NavigationPhase.Idle
            )
                return;
            var outcome = await restoreNavigation
                .Restore(restoration.Decode(snapshot.Span))
                .Completion;
            if (outcome.Kind == NavigationOutcomeKind.Superseded)
                await RecoverSafeFallback(since);
        }

        private bool TryDefer(Func<ValueTask> retry)
        {
            if (_session is not { IsCompleted: false, IsCloseRequested: true })
                return false;
            _deferredStartup = retry;
            return true;
        }

        private async ValueTask RestoreFallback(long expectedDelivery)
        {
            var session = _session;
            if (session is null)
                return;
            await WaitForWarmProtocolDecision(expectedDelivery);
            if (TryDefer(() => RestoreFallback(expectedDelivery)) || inbox.IsClosing)
                return;
            var restoreNavigation = navigation();
            if (
                restoreNavigation.Current is not null
                || restoreNavigation.Phase != NavigationPhase.Idle
            )
                return;
            var outcome = await restoreNavigation.Restore(restoration.Decode([])).Completion;
            if (outcome.Kind == NavigationOutcomeKind.Superseded)
                await RecoverSafeFallback(expectedDelivery);
        }

        private async ValueTask ActivateColdProtocol(ActivationEnvelope envelope, long since)
        {
            await WaitForWarmProtocolDecision(since);
            if (TryDefer(() => ActivateColdProtocol(envelope, since)) || inbox.IsClosing)
                return;
            if (inbox.ProtocolVersion > envelope.Sequence)
            {
                await RecoverSafeFallback(since);
                return;
            }
            var coldNavigation = navigation();
            if (coldNavigation.Current is not null || coldNavigation.Phase != NavigationPhase.Idle)
                return;
            var outcome = await coldNavigation.Activate(envelope.EscapedPathAndQuery).Completion;
            if (outcome.IsCommitted)
            {
                Report(
                    ActivationBindingStatus.Committed,
                    IsCurrent(envelope) ? Attention(envelope) : null
                );
                return;
            }
            if (outcome.Kind == NavigationOutcomeKind.Superseded)
            {
                Report(ActivationBindingStatus.Superseded);
                await RecoverSafeFallback(since);
                return;
            }
            if (
                outcome.Kind
                    is NavigationOutcomeKind.RejectedActivation
                        or NavigationOutcomeKind.Stayed
                        or NavigationOutcomeKind.Failed
                && outcome.FailureKind != NavigationFailureKind.Terminal
            )
            {
                Report(ActivationBindingStatus.Rejected);
                await RestoreFallback(since);
            }
        }

        private async ValueTask RecoverSafeFallback(long since)
        {
            await WaitForWarmProtocolDecision(since);
            if (TryDefer(() => RecoverSafeFallback(since)) || inbox.IsClosing)
                return;
            var restoreNavigation = navigation();
            if (
                restoreNavigation.IsDisposed
                || restoreNavigation.IsTerminated
                || restoreNavigation.Current is not null
                || restoreNavigation.Phase != NavigationPhase.Idle
            )
                return;
            await restoreNavigation
                .Navigate(restoration.SafeFallback, origin: NavigationOrigin.Restoration)
                .Completion;
        }

        private async ValueTask WaitForWarmProtocolDecision(long since)
        {
            while (!inbox.IsClosing)
            {
                var latest = inbox.ProtocolVersion;
                if (latest <= since)
                    return;
                await inbox.WaitForProtocolDeliveryAsync(latest);
                var operation = _latestWarmOperation;
                if (operation is not null)
                    await operation;
                if (inbox.ProtocolVersion == latest)
                    return;
            }
        }

        private void ReceiveWarm(ActivationEnvelope envelope)
        {
            try
            {
                ReceiveWarmCore(envelope);
            }
            catch
            {
                Report(ActivationBindingStatus.Rejected);
            }
        }

        private void ReceiveWarmCore(ActivationEnvelope envelope)
        {
            var session = _session;
            if (session is null || session.IsCloseRequested || inbox.IsClosing)
            {
                Report(ActivationBindingStatus.Closing);
                return;
            }
            if (envelope.Kind == ActivationKind.Rejected)
            {
                Report(ActivationBindingStatus.Rejected);
                return;
            }
            if (envelope.Kind == ActivationKind.Launch)
            {
                Report(ActivationBindingStatus.LaunchAttention, Attention(envelope));
                return;
            }
            var warmNavigation = navigation();
            var previous = warmNavigation.Current;
            var previousPhase = warmNavigation.Phase;
            var previousPendingOperation = warmNavigation.Pending?.OperationId;
            if (!Allows(policy.CanOpen, envelope))
            {
                Report(ActivationBindingStatus.PolicyRejected);
                return;
            }
            if (
                !IsCurrent(envelope)
                || !ReferenceEquals(previous, warmNavigation.Current)
                || previousPhase != warmNavigation.Phase
                || previousPendingOperation != warmNavigation.Pending?.OperationId
            )
                return;
            _intent++;
            var intent = _intent;
            var operation = warmNavigation.Activate(envelope.EscapedPathAndQuery);
            _latestWarmOperation = operation.Completion;
            if (operation.Completion.IsCompletedSuccessfully)
            {
                CompleteWarm(envelope, operation.Completion.Result, intent);
                return;
            }
            _ = operation.Completion.ContinueWith(
                task =>
                {
                    if (task.IsCompletedSuccessfully)
                        _ = session.Scope.Post(() => CompleteWarm(envelope, task.Result, intent));
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }

        private void CompleteWarm(
            ActivationEnvelope envelope,
            NavigationOutcome outcome,
            long intent
        )
        {
            try
            {
                CompleteWarmCore(envelope, outcome, intent);
            }
            catch
            {
                Report(ActivationBindingStatus.Rejected);
            }
        }

        private void CompleteWarmCore(
            ActivationEnvelope envelope,
            NavigationOutcome outcome,
            long intent
        )
        {
            var session = _session;
            if (session is null || session.IsCloseRequested || inbox.IsClosing)
                return;
            if (intent != _intent || !IsCurrent(envelope))
            {
                Report(ActivationBindingStatus.Superseded);
                return;
            }
            if (outcome.IsCommitted)
                Report(ActivationBindingStatus.Committed, Attention(envelope));
            else if (outcome.Kind == NavigationOutcomeKind.Superseded)
                Report(ActivationBindingStatus.Superseded);
            else
                Report(ActivationBindingStatus.NavigationStayed);
        }

        private WindowsAttentionResult? Attention(ActivationEnvelope envelope)
        {
            if (requestAttention is null || !IsCurrent(envelope))
                return null;
            try
            {
                var currentNavigation = navigation();
                var before = currentNavigation.Current;
                var phase = currentNavigation.Phase;
                var pendingOperation = currentNavigation.Pending?.OperationId;
                if (
                    !Allows(policy.CanRequestAttention, envelope)
                    || !IsCurrent(envelope)
                    || !ReferenceEquals(before, currentNavigation.Current)
                    || phase != currentNavigation.Phase
                    || pendingOperation != currentNavigation.Pending?.OperationId
                )
                    return null;
                return requestAttention();
            }
            catch
            {
                return null;
            }
        }

        private bool IsCurrent(ActivationEnvelope envelope) =>
            _session is { IsCloseRequested: false, IsCompleted: false }
            && !inbox.IsClosing
            && envelope.Sequence
                == (
                    envelope.Kind == ActivationKind.Launch
                        ? inbox.LaunchVersion
                        : inbox.ProtocolVersion
                );

        private static bool Allows(
            Func<ActivationEnvelope, bool> check,
            ActivationEnvelope envelope
        )
        {
            try
            {
                return check(envelope);
            }
            catch
            {
                return false;
            }
        }

        private void Report(
            ActivationBindingStatus status,
            WindowsAttentionResult? attention = null
        )
        {
            try
            {
                observe?.Invoke(new(status, attention));
            }
            catch
            { /* Diagnostics must not change navigation or close behavior. */
            }
        }
    }
}
