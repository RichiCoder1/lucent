namespace Lucent.Core;

/// <summary>Controls whether collapsed retained fields continue to participate in form submission.</summary>
public enum FieldParticipation
{
    /// <summary>Participates while visible or hidden, and is excluded while collapsed.</summary>
    WhenNotCollapsed,

    /// <summary>Participates even while its retained presentation is collapsed.</summary>
    Always,
}

/// <summary>Declares how submission handles fields with pending validation.</summary>
public enum PendingValidationPolicy
{
    /// <summary>Rejects the submit attempt while any participating field is pending.</summary>
    Reject,

    /// <summary>Awaits declared pending completions once, then re-reads every field.</summary>
    Await,
}

/// <summary>The bounded outcome of coordinating one form submit attempt.</summary>
public enum FormSubmitStatus
{
    /// <summary>Every participating field is valid.</summary>
    Valid,

    /// <summary>At least one participating field is invalid.</summary>
    Invalid,

    /// <summary>Submission was rejected because validation remains pending.</summary>
    Pending,
}

/// <summary>Immutable result of one coordinated form submit attempt.</summary>
public sealed record FormSubmitResult(FormSubmitStatus Status, IReadOnlyList<string> InvalidFields);

/// <summary>One current form error and the field identity its summary action focuses.</summary>
public sealed record FormFieldError(string FieldId, IReadOnlyList<string> Messages);

/// <summary>
/// Scope-owned coordinator for touched state, submit attempts, and focus-first-invalid.
/// Participating fields follow registration order: an unmounted field is removed, and a later
/// remount is appended after the fields that remained mounted.
/// </summary>
public sealed class FormSession : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<long> _submitAttempts;
    private readonly Dictionary<string, Registration> _fields = new(StringComparer.Ordinal);
    private readonly List<Registration> _registrationOrder = [];
    private readonly HashSet<AwaitedSubmit> _awaited = [];
    private bool _disposed;

    /// <summary>Creates a form session whose state cannot outlive <paramref name="owner"/>.</summary>
    public FormSession(ReactiveScope owner, string name = "form")
    {
        _scope = owner?.CreateChild(name) ?? throw new ArgumentNullException(nameof(owner));
        _submitAttempts = _scope.Signal(0L, name + ".submit-attempts");
        _scope.OnDispose(ReleaseOwnedState);
    }

    /// <summary>Gets the number of submit attempts observed by mounted fields.</summary>
    public long SubmitAttempts => _submitAttempts.Value;

    /// <summary>Gets current participating errors after the first submit attempt.</summary>
    public IReadOnlyList<FormFieldError> Errors
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_submitAttempts.Value == 0)
                return Array.Empty<FormFieldError>();
            return Array.AsReadOnly(
                Participating()
                    .Select(entry => (entry, validation: entry.Validation()))
                    .Where(item => item.validation.IsInvalid)
                    .Select(item => new FormFieldError(item.entry.Id, item.validation.Messages))
                    .ToArray()
            );
        }
    }

    /// <summary>Requests focus for a currently registered participating field.</summary>
    public bool Focus(string fieldId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldId);
        var field = Participating().FirstOrDefault(field => field.Id == fieldId);
        if (field is null)
            return false;
        field.Focus.Request();
        return true;
    }

    /// <summary>Marks a submit attempt and coordinates current participating field validation.</summary>
    public ValueTask<FormSubmitResult> SubmitAsync(
        PendingValidationPolicy pendingPolicy = PendingValidationPolicy.Reject,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(pendingPolicy))
            throw new ArgumentException(
                "Pending validation policy must be finite.",
                nameof(pendingPolicy)
            );
        _submitAttempts.Value = checked(_submitAttempts.Value + 1);

        var snapshot = Participating();
        var pending = snapshot.Where(field => field.Validation().IsPending).ToArray();
        if (pending.Length != 0 && pendingPolicy == PendingValidationPolicy.Await)
        {
            var completions = pending.Select(field => field.Validation().Completion).ToArray();
            if (completions.All(static completion => completion is not null))
                return AwaitPending(completions!, cancellationToken);
        }
        return ValueTask.FromResult(Evaluate(snapshot, pending));
    }

    private static FormSubmitResult Evaluate(Registration[] snapshot, Registration[] pending)
    {
        if (pending.Length != 0)
            return new(FormSubmitStatus.Pending, Array.Empty<string>());

        var invalid = snapshot.Where(field => field.Validation().IsInvalid).ToArray();
        if (invalid.Length == 0)
            return new(FormSubmitStatus.Valid, Array.Empty<string>());
        invalid[0].Focus.Request();
        return new(
            FormSubmitStatus.Invalid,
            Array.AsReadOnly(invalid.Select(field => field.Id).ToArray())
        );
    }

    private async ValueTask<FormSubmitResult> AwaitPending(
        Task[] completions,
        CancellationToken cancellationToken
    )
    {
        var awaited = new AwaitedSubmit();
        _awaited.Add(awaited);
        awaited.RegisterCancellation(
            () => _ = _scope.Post(() => CompleteAwaited(awaited, recheck: false)),
            cancellationToken
        );
        _ = Task.WhenAll(completions)
            .ContinueWith(
                completion =>
                {
                    if (completion.IsFaulted)
                        awaited.Fail(completion.Exception!.InnerExceptions);
                    else if (completion.IsCanceled)
                        awaited.Cancel();
                    _ = _scope.Post(() =>
                        CompleteAwaited(awaited, completion.IsCompletedSuccessfully)
                    );
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        return await awaited.Task.ConfigureAwait(false);
    }

    private void CompleteAwaited(AwaitedSubmit awaited, bool recheck)
    {
        if (!_awaited.Remove(awaited))
            return;
        awaited.ReleaseCancellation();
        if (!recheck || awaited.IsCompleted)
            return;
        var snapshot = Participating();
        var pending = snapshot.Where(field => field.Validation().IsPending).ToArray();
        awaited.Complete(Evaluate(snapshot, pending));
    }

    /// <summary>Releases coordinator state. Mounted field registrations release with their own scopes.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        ReleaseOwnedState();
        _scope.Dispose();
    }

    private void ReleaseOwnedState()
    {
        _disposed = true;
        _fields.Clear();
        _registrationOrder.Clear();
        foreach (var awaited in _awaited)
            awaited.DisposeSession();
        _awaited.Clear();
    }

    internal void Register(
        string id,
        Element root,
        Func<ValidationState> validation,
        FocusTarget focus,
        FieldParticipation participation
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(participation))
            throw new ArgumentException(
                "Field participation must be finite.",
                nameof(participation)
            );
        if (_fields.ContainsKey(id))
            throw new InvalidOperationException(
                "A mounted form field identity must be unique: " + id
            );
        var registration = new Registration(id, root, validation, focus, participation);
        _fields.Add(id, registration);
        _registrationOrder.Add(registration);
        root.Scope.OnDispose(() => RemoveRegistration(registration));
    }

    private void RemoveRegistration(Registration registration)
    {
        if (
            _fields.TryGetValue(registration.Id, out var current)
            && ReferenceEquals(current, registration)
        )
            _fields.Remove(registration.Id);
        _registrationOrder.RemoveAll(item => ReferenceEquals(item, registration));
    }

    private Registration[] Participating() =>
        _registrationOrder
            .Where(field =>
                !field.Root.IsDisposed
                && (
                    field.Participation == FieldParticipation.Always
                    || field.Root.Participation != ElementParticipation.Collapsed
                )
            )
            .ToArray();

    private sealed record Registration(
        string Id,
        Element Root,
        Func<ValidationState> Validation,
        FocusTarget Focus,
        FieldParticipation Participation
    );

    private sealed class AwaitedSubmit
    {
        private readonly TaskCompletionSource<FormSubmitResult> _source = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private CancellationTokenRegistration _cancellation;
        private Action? _cancelled;

        internal void RegisterCancellation(Action cancelled, CancellationToken cancellationToken)
        {
            _cancelled = cancelled;
            if (cancellationToken.CanBeCanceled)
                _cancellation = cancellationToken.Register(
                    static state => ((AwaitedSubmit)state!).CancelFromToken(),
                    this
                );
        }

        internal Task<FormSubmitResult> Task => _source.Task;
        internal bool IsCompleted => _source.Task.IsCompleted;

        internal void Complete(FormSubmitResult result) => _source.TrySetResult(result);

        internal void Cancel() => _source.TrySetCanceled();

        internal void Fail(IEnumerable<Exception> errors) => _source.TrySetException(errors);

        internal void DisposeSession()
        {
            _source.TrySetException(new ObjectDisposedException(nameof(FormSession)));
            ReleaseCancellation();
        }

        internal void ReleaseCancellation()
        {
            _cancelled = null;
            _cancellation.Dispose();
        }

        private void CancelFromToken()
        {
            if (_source.TrySetCanceled())
                _cancelled?.Invoke();
        }
    }
}
