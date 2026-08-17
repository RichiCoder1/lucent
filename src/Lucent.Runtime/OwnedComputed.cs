namespace Lucent.Runtime;

using System.Runtime.ExceptionServices;

public sealed class OwnedComputed<T> : IDisposable
{
    private readonly object _gate = new();
    private readonly ComponentOwner _owner;
    private readonly Func<CancellationToken, Task<T>> _factory;
    private readonly Action _invalidate;
    private readonly Action<Exception>? _reportUnhandled;
    private CancellationTokenSource? _currentCancellation;
    private T _value;
    private Exception? _error;
    private bool _hasCommittedValue;
    private bool _pending;
    private long _generation;
    private bool _disposed;

    public OwnedComputed(
        ComponentOwner owner,
        T initialValue,
        Func<CancellationToken, Task<T>> factory,
        Action invalidate,
        Action<Exception>? reportUnhandled = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _invalidate = invalidate ?? throw new ArgumentNullException(nameof(invalidate));
        _reportUnhandled = reportUnhandled;
        _value = initialValue;
        _owner.OnDispose(Dispose);
    }

    public T Value { get { lock (_gate) return _value; } }
    public bool HasCommittedValue { get { lock (_gate) return _hasCommittedValue; } }
    public bool IsPending { get { lock (_gate) return _pending; } }
    public Exception? Error { get { lock (_gate) return _error; } }
    public string? ErrorMessage => Error?.Message;

    public void Refresh()
    {
        CancellationTokenSource? old = null;
        Exception? cancellationFailure = null;
        CancellationTokenSource? replacement;
        long generation;
        lock (_gate)
        {
            if (_disposed || _owner.IsDisposed) return;
            generation = ++_generation;
            old = _currentCancellation;
            replacement = CancellationTokenSource.CreateLinkedTokenSource(_owner.CancellationToken);
            _currentCancellation = replacement;
            _error = null;
            _pending = true;
        }

        if (old is not null)
        {
            try { old.Cancel(); }
            catch (Exception ex) { cancellationFailure = ex; }
            finally { old.Dispose(); }
        }

        Task<T> task;
        try
        {
            task = _factory(replacement.Token) ?? Task.FromException<T>(
                new InvalidOperationException("The computed factory returned null."));
        }
        catch (Exception ex)
        {
            task = Task.FromException<T>(ex);
        }

        _ = CompleteAsync(task, generation, replacement);
        Exception? invalidationFailure = null;
        try { _invalidate(); }
        catch (Exception ex) { invalidationFailure = ex; }
        if (invalidationFailure is not null) ReportDistinct(invalidationFailure);
        if (cancellationFailure is not null) ReportDistinct(cancellationFailure);
    }

    private async Task CompleteAsync(Task<T> task, long generation, CancellationTokenSource cancellation)
    {
        T value = default!;
        Exception? failure = null;
        try { value = await task.ConfigureAwait(false); }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellation.IsCancellationRequested) return;
            failure = ex;
        }

        _owner.Dispatch(() => Commit(generation, cancellation, value, failure));
    }

    private void Commit(long generation, CancellationTokenSource cancellation, T value, Exception? failure)
    {
        lock (_gate)
        {
            if (_disposed || _owner.IsDisposed || generation != _generation ||
                !ReferenceEquals(cancellation, _currentCancellation) || cancellation.IsCancellationRequested) return;
            if (failure is null)
            {
                _value = value;
                _hasCommittedValue = true;
                _error = null;
            }
            else
            {
                _error = failure;
            }
            _pending = false;
        }

        Exception? updateFailure = null;
        try { _invalidate(); }
        catch (Exception ex) { updateFailure = ex; }
        List<Exception>? reportFailures = null;
        void AttemptReport(Action<Exception> reporter, Exception error)
        {
            try { reporter(error); }
            catch (Exception ex) { (reportFailures ??= []).Add(ex); }
        }

        if (updateFailure is not null)
            AttemptReport(_owner.ReportUnhandled, updateFailure);
        if (failure is not null)
            AttemptReport(_reportUnhandled ?? _owner.ReportUnhandled, failure);
        if (reportFailures is { Count: 1 })
            ExceptionDispatchInfo.Capture(reportFailures[0]).Throw();
        if (reportFailures is { Count: > 1 })
            throw new AggregateException(reportFailures);
    }

    private void ReportDistinct(Exception error)
    {
        _owner.ReportUnhandled(error);
    }

    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = false;
            current = _currentCancellation;
            _currentCancellation = null;
        }
        try { current?.Cancel(); }
        finally { current?.Dispose(); }
    }
}
