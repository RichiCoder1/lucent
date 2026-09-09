namespace Lucent.Core;

/// <summary>Describes the terminal outcome of one typed dialog session.</summary>
public enum DialogResultKind
{
    /// <summary>The caller accepted the dialog and supplied a value.</summary>
    Accepted,

    /// <summary>The dialog was dismissed without applying a value.</summary>
    Canceled,
}

/// <summary>Immutable typed result returned by a dialog controller.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The typed factories keep accepted and canceled dialog outcomes explicit."
)]
public sealed record DialogResult<T>(DialogResultKind Kind, T? Value)
{
    /// <summary>Gets whether the result contains an accepted value.</summary>
    public bool IsAccepted => Kind == DialogResultKind.Accepted;

    /// <summary>Gets whether the dialog was canceled.</summary>
    public bool IsCanceled => Kind == DialogResultKind.Canceled;

    /// <summary>Creates an accepted result.</summary>
    public static DialogResult<T> Accepted(T value) => new(DialogResultKind.Accepted, value);

    /// <summary>Creates a canceled result.</summary>
    public static DialogResult<T> Canceled() => new(DialogResultKind.Canceled, default);
}

/// <summary>Reports the outcome of an asynchronous dialog accept action.</summary>
public enum DialogSubmissionStatus
{
    /// <summary>The application action completed and the dialog closed.</summary>
    Accepted,

    /// <summary>The application action failed and the dialog remains open.</summary>
    Failed,

    /// <summary>An accept action is already running for this dialog.</summary>
    AlreadyPending,

    /// <summary>The dialog is no longer open.</summary>
    NotOpen,

    /// <summary>The action was canceled before it completed.</summary>
    Canceled,

    /// <summary>The controller owner has already been disposed.</summary>
    Disposed,
}

/// <summary>Immutable status returned by an asynchronous accept action.</summary>
public sealed record DialogSubmissionResult(
    DialogSubmissionStatus Status,
    string? FailureMessage = null
)
{
    /// <summary>Gets whether the application action committed successfully.</summary>
    public bool IsAccepted => Status == DialogSubmissionStatus.Accepted;

    /// <summary>Gets whether the application action failed while the dialog remained open.</summary>
    public bool IsFailed => Status == DialogSubmissionStatus.Failed;
}

/// <summary>Small public state surface used by stock dialog content and accessibility adapters.</summary>
public interface IDialogController
{
    /// <summary>Gets whether this controller currently owns a visible dialog session.</summary>
    bool IsOpen { get; }

    /// <summary>Gets whether an accepted application action is still running.</summary>
    bool IsPending { get; }

    /// <summary>Gets the current user-facing action failure, if any.</summary>
    string? FailureMessage { get; }

    /// <summary>Attempts to cancel the current session.</summary>
    bool TryCancel();
}

/// <summary>Non-generic controller seam consumed by the retained dialog component.</summary>
public abstract class DialogControllerBase : IDialogController, IDisposable
{
    /// <inheritdoc />
    public abstract bool IsOpen { get; }

    /// <inheritdoc />
    public abstract bool IsPending { get; }

    /// <inheritdoc />
    public abstract string? FailureMessage { get; }

    /// <inheritdoc />
    public abstract bool TryCancel();

    internal abstract DialogSurfaceRequest CreateSurface(
        Element target,
        ThemeContext theme,
        string title,
        ComponentContent body,
        ComponentContent actions,
        bool destructive,
        Func<Composition, bool>? initialFocus,
        Action? defaultAccept,
        Func<ValueTask>? defaultAcceptAsync,
        Style? style
    );

    internal abstract void SurfaceDismissed(DialogSurfaceRequest request);

    internal abstract void OwnerDetached();

    internal abstract void ReportFailure(Exception error);

    /// <summary>Releases the controller's owner-scoped state.</summary>
    public abstract void Dispose();
}

/// <summary>
/// Scope-owned coordinator for a typed modal session. Application writes run to completion even
/// when the visual dialog is dismissed or its owner composition is released.
/// </summary>
public sealed class DialogController<T> : DialogControllerBase
{
    private const string GenericFailureMessage = "The action could not be completed.";
    private readonly ReactiveScope _scope;
    private readonly Signal<bool> _open;
    private readonly Signal<bool> _pending;
    private readonly Signal<string?> _failure;
    private Session? _session;
    private bool _disposed;

    /// <summary>Creates a controller whose state is owned by <paramref name="owner"/>.</summary>
    public DialogController(ReactiveScope owner, string name = "dialog")
    {
        ArgumentNullException.ThrowIfNull(owner);
        _scope = owner.CreateChild(name);
        _open = _scope.Signal(false, name + ".open");
        _pending = _scope.Signal(false, name + ".pending");
        _failure = _scope.Signal<string?>(null, name + ".failure");
        _scope.OnDispose(OwnerDisposed);
    }

    /// <inheritdoc />
    public override bool IsOpen => !_disposed && _open.Value;

    /// <inheritdoc />
    public override bool IsPending => !_disposed && _pending.Value;

    /// <inheritdoc />
    public override string? FailureMessage => !_disposed ? _failure.Value : null;

    /// <summary>Opens one session and returns its typed result. Duplicate opens share the active session.</summary>
    public ValueTask<DialogResult<T>> OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_session is { } existing)
            return new(existing.Completion.Task);

        var session = new Session();
        _session = session;
        _failure.Value = null;
        _pending.Value = false;
        _open.Value = true;
        if (cancellationToken.CanBeCanceled)
        {
            session.Cancellation = cancellationToken.Register(
                static state => ((CancellationState)state!).Cancel(),
                new CancellationState(this, session)
            );
        }
        if (cancellationToken.IsCancellationRequested)
            CancelFromCancellation(session);
        return new(session.Completion.Task);
    }

    /// <summary>
    /// Runs an application-owned async write for the current session. A duplicate call while the
    /// first write is pending is ignored and reports <see cref="DialogSubmissionStatus.AlreadyPending"/>.
    /// </summary>
    public ValueTask<DialogSubmissionResult> AcceptAsync(
        T value,
        Func<T, CancellationToken, ValueTask> apply,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(apply);
        ThrowIfDisposed();
        if (_session is not { } session || !_open.Value)
            return CompletedSubmission(DialogSubmissionStatus.NotOpen);
        if (_pending.Value)
            return CompletedSubmission(DialogSubmissionStatus.AlreadyPending);

        _pending.Value = true;
        _failure.Value = null;
        session.Value = value;
        ValueTask application;
        try
        {
            // The controller does not create an owner-linked cancellation source. A dialog
            // disappearing cannot silently cancel an accepted application write.
            application = apply(value, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CompleteSubmission(session, DialogSubmissionStatus.Canceled, null);
            return CompletedSubmission(DialogSubmissionStatus.Canceled);
        }
        catch (Exception error)
        {
            CompleteSubmission(session, DialogSubmissionStatus.Failed, error);
            return CompletedSubmission(DialogSubmissionStatus.Failed, GenericFailureMessage);
        }

        if (application.IsCompletedSuccessfully)
        {
            CompleteSubmission(session, DialogSubmissionStatus.Accepted, null);
            return CompletedSubmission(DialogSubmissionStatus.Accepted);
        }

        return AwaitApplication(session, application);
    }

    /// <inheritdoc />
    public override bool TryCancel()
    {
        ThrowIfDisposed();
        if (_session is not { } session || !_open.Value || _pending.Value)
            return false;
        CompleteCanceled(session);
        return true;
    }

    internal override DialogSurfaceRequest CreateSurface(
        Element target,
        ThemeContext theme,
        string title,
        ComponentContent body,
        ComponentContent actions,
        bool destructive,
        Func<Composition, bool>? initialFocus,
        Action? defaultAccept,
        Func<ValueTask>? defaultAcceptAsync,
        Style? style
    )
    {
        ThrowIfDisposed();
        if (!_open.Value)
            throw new InvalidOperationException(
                "A dialog surface requires an open controller session."
            );
        return new DialogSurfaceRequest(
            this,
            target,
            theme,
            title,
            body,
            actions,
            destructive,
            initialFocus,
            defaultAccept,
            defaultAcceptAsync,
            style
        );
    }

    internal override void SurfaceDismissed(DialogSurfaceRequest request)
    {
        if (_session is not { } session || !IsOpen)
            return;
        // A pending write owns its lifetime. The host still closes the visual surface, while
        // completion below settles the typed result without canceling that write.
        if (!_pending.Value)
            CompleteCanceled(session);
    }

    internal override void OwnerDetached()
    {
        if (_disposed)
            return;
        if (_session is { } session && !_pending.Value)
            CompleteCanceled(session);
        else
            _open.Value = false;
    }

    internal override void ReportFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (_disposed || !_open.Value)
            return;
        _failure.Value = GenericFailureMessage;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_disposed)
            return;
        _scope.Dispose();
    }

    private ValueTask<DialogSubmissionResult> AwaitApplication(
        Session session,
        ValueTask application
    )
    {
        var pending = new PendingApplication();
        session.Pending = pending;
        _ = ObserveApplication(session, pending, application);
        return new(pending.Completion.Task);
    }

    private async Task ObserveApplication(
        Session session,
        PendingApplication pending,
        ValueTask application
    )
    {
        Exception? failure = null;
        var status = DialogSubmissionStatus.Accepted;
        try
        {
            await application.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            status = DialogSubmissionStatus.Canceled;
        }
        catch (Exception error)
        {
            status = DialogSubmissionStatus.Failed;
            failure = error;
        }

        var result = SubmissionResult(status);
        // Publish the caller-visible submission before posting UI cleanup. Scope disposal cancels
        // queued callbacks, so the task cannot depend on a callback that may never drain.
        pending.TrySetResult(result);
        if (Volatile.Read(ref _disposed) || _scope.IsDisposed)
        {
            CompleteAfterOwnerDisposed(session, status);
            return;
        }

        if (status is DialogSubmissionStatus.Accepted or DialogSubmissionStatus.Canceled)
            CompleteTyped(session, status);

        // Post is deliberately a no-op after disposal. OwnerDisposed and the completion above
        // handle that race; this callback only reconciles reactive presentation state.
        _scope.Post(() => ApplyPendingResult(session, pending, status, failure));
    }

    private void ApplyPendingResult(
        Session session,
        PendingApplication pending,
        DialogSubmissionStatus status,
        Exception? error
    )
    {
        if (
            Volatile.Read(ref _disposed)
            || _scope.IsDisposed
            || !ReferenceEquals(_session, session)
            || !ReferenceEquals(session.Pending, pending)
        )
            return;
        CompleteSubmission(session, status, error);
    }

    private DialogSubmissionResult CompleteSubmission(
        Session session,
        DialogSubmissionStatus status,
        Exception? error
    )
    {
        if (
            !ReferenceEquals(_session, session)
            || Volatile.Read(ref _disposed)
            || _scope.IsDisposed
        )
            return new(DialogSubmissionStatus.Disposed);
        session.Pending = null;
        _pending.Value = false;
        if (status == DialogSubmissionStatus.Accepted)
        {
            _failure.Value = null;
            _open.Value = false;
            _session = null;
            CompleteTyped(session, DialogSubmissionStatus.Accepted);
            return new(DialogSubmissionStatus.Accepted);
        }
        if (status == DialogSubmissionStatus.Failed)
        {
            _failure.Value = GenericFailureMessage;
            return new(DialogSubmissionStatus.Failed, GenericFailureMessage);
        }
        if (status == DialogSubmissionStatus.Canceled)
        {
            _failure.Value = null;
            _open.Value = false;
            _session = null;
            CompleteTyped(session, DialogSubmissionStatus.Canceled);
            return new(DialogSubmissionStatus.Canceled);
        }
        return new(status, error is null ? null : GenericFailureMessage);
    }

    private void CompleteCanceled(Session session)
    {
        if (!ReferenceEquals(_session, session))
            return;
        session.Pending = null;
        _pending.Value = false;
        _failure.Value = null;
        _open.Value = false;
        _session = null;
        CompleteTyped(session, DialogSubmissionStatus.Canceled);
    }

    private void CancelFromCancellation(Session session)
    {
        if (Volatile.Read(ref _disposed) || _scope.IsDisposed)
            return;
        _scope.Post(() =>
        {
            if (ReferenceEquals(_session, session) && !_pending.Value)
                CompleteCanceled(session);
        });
    }

    private void OwnerDisposed()
    {
        Volatile.Write(ref _disposed, true);
        if (_session is not { } session)
            return;

        if (session.Pending is { } pending)
        {
            // A queued completion may have been canceled with the scope. If its application has
            // already returned, settle the typed caller now; otherwise the worker observer does
            // so when the application eventually returns.
            if (pending.Result is { } result)
                CompleteAfterOwnerDisposed(session, result.Status);
            return;
        }

        _session = null;
        CompleteTyped(session, DialogSubmissionStatus.Canceled);
    }

    private void CompleteAfterOwnerDisposed(Session session, DialogSubmissionStatus status)
    {
        if (status == DialogSubmissionStatus.Failed)
            status = DialogSubmissionStatus.Canceled;
        session.Pending = null;
        if (ReferenceEquals(_session, session))
            _session = null;
        CompleteTyped(session, status);
    }

    private static void CompleteTyped(Session session, DialogSubmissionStatus status)
    {
        if (status == DialogSubmissionStatus.Accepted)
            session.Completion.TrySetResult(DialogResult<T>.Accepted(session.Value));
        else if (status == DialogSubmissionStatus.Canceled)
            session.Completion.TrySetResult(DialogResult<T>.Canceled());
        else
            return;
        session.ReleaseCancellation();
    }

    private static DialogSubmissionResult SubmissionResult(DialogSubmissionStatus status) =>
        status == DialogSubmissionStatus.Failed ? new(status, GenericFailureMessage) : new(status);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed || _scope.IsDisposed,
            typeof(DialogController<T>)
        );
    }

    private static ValueTask<DialogSubmissionResult> CompletedSubmission(
        DialogSubmissionStatus status,
        string? failure = null
    ) => ValueTask.FromResult(new DialogSubmissionResult(status, failure));

    private sealed class Session
    {
        internal readonly TaskCompletionSource<DialogResult<T>> Completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal CancellationTokenRegistration Cancellation;
        internal T Value = default!;
        internal PendingApplication? Pending;
        private int _cancellationReleased;

        internal void ReleaseCancellation()
        {
            if (Interlocked.Exchange(ref _cancellationReleased, 1) == 0)
                Cancellation.Dispose();
        }
    }

    private sealed class PendingApplication
    {
        internal readonly TaskCompletionSource<DialogSubmissionResult> Completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private DialogSubmissionResult? _result;

        internal DialogSubmissionResult? Result => Volatile.Read(ref _result);

        internal void TrySetResult(DialogSubmissionResult result)
        {
            if (Interlocked.CompareExchange(ref _result, result, null) is null)
                Completion.TrySetResult(result);
        }
    }

    private sealed class CancellationState(DialogController<T> controller, Session session)
    {
        internal void Cancel() => controller.CancelFromCancellation(session);
    }
}
