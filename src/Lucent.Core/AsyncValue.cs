using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>Latest-generation asynchronous state. Cancellation releases Lucent-owned resources; an uncooperative producer may still retain its own task closure until it completes.</summary>
public sealed class AsyncValue<T> : ReactiveNode
{
    private Func<CancellationToken, Task<T>>? _load;
    private AsyncLease<T>? _lease;
    private T? _value;
    private Exception? _error;
    private bool _dirty = true;
    private bool _pending;
    private bool _cancelled;
    private bool _hasValue;
    private long _generation;

    internal AsyncValue(
        ReactiveGraph graph,
        Func<CancellationToken, Task<T>> load,
        T? staleValue,
        bool hasValue,
        string name,
        ReactiveScope? scope
    )
        : base(graph, name, scope)
    {
        _load = load;
        _value = staleValue;
        _hasValue = hasValue;
    }

    internal override string Kind => "async";

    /// <summary>Gets the latest successfully committed value, or the supplied stale value while a reload is pending.</summary>
    public T? Value
    {
        get
        {
            Graph.CheckThread();
            Read();
            EnsureStarted();
            return _value;
        }
    }

    /// <summary>Gets whether <see cref="Value"/> currently represents a committed or stale value.</summary>
    public bool HasValue
    {
        get
        {
            Graph.CheckThread();
            Read();
            EnsureStarted();
            return _hasValue;
        }
    }

    /// <summary>Gets whether the current generation is awaiting its producer; reading it participates in reactivity.</summary>
    public bool IsPending
    {
        get
        {
            Graph.CheckThread();
            Read();
            EnsureStarted();
            return _pending;
        }
    }

    /// <summary>Gets whether the current generation was cancelled before it could commit.</summary>
    public bool IsCancelled
    {
        get
        {
            Graph.CheckThread();
            Read();
            EnsureStarted();
            return _cancelled;
        }
    }

    /// <summary>Gets the current generation failure without throwing it; a later invalidation may replace it.</summary>
    public Exception? Error
    {
        get
        {
            Graph.CheckThread();
            Read();
            EnsureStarted();
            return _error;
        }
    }

    /// <summary>Invalidates the current load for retry or refresh. The next read starts a new generation and retains the last successful value.</summary>
    public void Refresh()
    {
        Graph.CheckThread();
        CheckScopeMutationGuard();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        DependencyChanged();
    }

    private void EnsureStarted()
    {
        ValidatePotentialDependencies();
        if (!_dirty)
            return;
        CheckScopeEvaluationGuard();
        Exception? stopError = StopCurrent();
        var lease = _lease = new AsyncLease<T>(this, ++_generation);
        _dirty = false;
        _pending = true;
        _cancelled = false;
        _error = null;
        Evaluation<Task<T>> evaluation;
        try
        {
            evaluation = Graph.Evaluate(this, () => _load!(lease.Token));
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(lease, _lease))
            {
                ReactiveGraph.ThrowCombined(stopError, exception, "Async start was invalidated.");
                return;
            }
            Exception? completeError = null;
            try
            {
                Complete(lease, default, exception, false);
            }
            catch (Exception completionException)
            {
                completeError = completionException;
            }
            ReactiveGraph.ThrowCombined(stopError, completeError, "Async start failed.");
            return;
        }

        if (evaluation.Value is null)
        {
            Exception? completeError = null;
            try
            {
                Complete(
                    lease,
                    default,
                    new InvalidOperationException("Async loader returned null task."),
                    false
                );
            }
            catch (Exception completionException)
            {
                completeError = completionException;
            }
            ReactiveGraph.ThrowCombined(stopError, completeError, "Async start failed.");
            return;
        }

        Exception? transitionError = null;
        try
        {
            evaluation.Value.ContinueWith(
                completed => lease.Post(completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
            if (evaluation.ChangedDuringRun)
                DependencyChanged();
        }
        catch (Exception exception)
        {
            transitionError = exception;
        }
        ReactiveGraph.ThrowCombined(stopError, transitionError, "Async transition failed.");
    }

    internal void Complete(AsyncLease<T> lease, T? value, Exception? error, bool cancelled)
    {
        Graph.CheckThread();
        if (IsDisposed || !ReferenceEquals(lease, _lease))
        {
            lease.ReleasePosted();
            return;
        }
        _lease = null;
        var releaseError = lease.Finish();
        _pending = false;
        if (cancelled)
            _cancelled = true;
        else if (error is null)
        {
            _value = value;
            _hasValue = true;
        }
        else
            _error = error;
        Exception? changedError = null;
        try
        {
            Changed();
        }
        catch (Exception exception)
        {
            changedError = exception;
        }
        ReactiveGraph.ThrowCombined(releaseError, changedError, "Async completion failed.");
    }

    internal override void DependencyChanged()
    {
        if (IsDisposed)
            return;
        _dirty = true;
        _pending = false;
        _cancelled = true;
        var error = StopCurrent();
        Exception? changedError = null;
        try
        {
            Changed();
        }
        catch (Exception exception)
        {
            changedError = exception;
        }
        ReactiveGraph.ThrowCombined(error, changedError, "Async invalidation failed.");
    }

    internal override void EnsureCurrent() => EnsureStarted();

    /// <summary>Cancels the active generation and releases graph ownership; late producer completion is ignored.</summary>
    public override void Dispose()
    {
        Graph.CheckThread();
        CheckScopeMutationGuard();
        if (IsDisposed)
            return;
        var error = StopCurrent();
        _load = null;
        _value = default;
        _error = null;
        _pending = false;
        _hasValue = false;
        try
        {
            base.Dispose();
        }
        catch (Exception exception)
        {
            ReactiveGraph.ThrowCombined(error, exception, "Async disposal failed.");
            return;
        }
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    internal override void AppendDump(StringBuilder dump) =>
        dump.Append(" dirty=")
            .Append(_dirty ? "true" : "false")
            .Append(" generation=")
            .Append(_generation.ToString(CultureInfo.InvariantCulture))
            .Append(" pending=")
            .Append(_pending ? "true" : "false")
            .Append(" cancelled=")
            .Append(_cancelled ? "true" : "false")
            .Append(" hasValue=")
            .Append(_hasValue ? "true" : "false")
            .Append(" hasError=")
            .Append(_error is null ? "false" : "true");

    private Exception? StopCurrent()
    {
        var lease = _lease;
        _lease = null;
        return lease?.Cancel();
    }
}

internal sealed class AsyncLease<T>
{
    private readonly object _gate = new();
    private AsyncValue<T>? _owner;
    private CancellationTokenSource? _cancellation = new();
    private AsyncPosted<T>? _posted;

    internal AsyncLease(AsyncValue<T> owner, long generation)
    {
        _owner = owner;
        Generation = generation;
    }

    internal long Generation { get; }
    internal CancellationToken Token => _cancellation?.Token ?? CancellationToken.None;

    internal void Post(Task<T> task)
    {
        AsyncPosted<T>? posted;
        ReactiveGraph graph;
        lock (_gate)
        {
            if (_owner is null)
                return;
            graph = _owner.Graph;
            var cancelled = task.IsCanceled;
            var error = cancelled ? null : task.Exception?.GetBaseException();
            var value = task.Status == TaskStatus.RanToCompletion ? task.Result : default;
            posted = _posted = new AsyncPosted<T>(this, value, error, cancelled);
        }
        graph.Post(posted);
    }

    internal Exception? Cancel()
    {
        CancellationTokenSource? cancellation;
        AsyncPosted<T>? posted;
        lock (_gate)
        {
            _owner = null;
            cancellation = _cancellation;
            _cancellation = null;
            posted = _posted;
            _posted = null;
        }
        posted?.Release();
        return CancelAndDispose(cancellation);
    }

    internal Exception? Finish()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _owner = null;
            cancellation = _cancellation;
            _cancellation = null;
            _posted = null;
        }
        if (cancellation is null)
            return null;
        try
        {
            cancellation.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    internal bool Commit(AsyncPosted<T> posted, T? value, Exception? error, bool cancelled)
    {
        AsyncValue<T>? owner;
        lock (_gate)
        {
            if (!ReferenceEquals(_posted, posted))
            {
                posted.Release();
                return false;
            }
            _posted = null;
            owner = _owner;
        }
        if (owner is null)
        {
            posted.Release();
            return false;
        }
        try
        {
            owner.Complete(this, value, error, cancelled);
            return true;
        }
        finally
        {
            posted.Release();
        }
    }

    internal void ReleasePosted()
    {
        lock (_gate)
        {
            _posted?.Release();
            _posted = null;
        }
    }

    private static Exception? CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return null;
        Exception? cancelError = null;
        try
        {
            cancellation.Cancel();
        }
        catch (Exception exception)
        {
            cancelError = exception;
        }
        try
        {
            cancellation.Dispose();
        }
        catch (Exception exception)
        {
            return cancelError is null
                ? exception
                : new AggregateException("Async cancellation failed.", cancelError, exception);
        }
        return cancelError;
    }
}

internal sealed class AsyncPosted<T>(
    AsyncLease<T> lease,
    T? value,
    Exception? error,
    bool cancelled
) : IPosted
{
    private AsyncLease<T>? _lease = lease;
    private T? _value = value;
    private Exception? _error = error;
    private readonly bool _cancelled = cancelled;

    public bool Commit()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null)
            return false;
        var value = _value;
        var error = _error;
        _value = default;
        _error = null;
        return lease.Commit(this, value, error, _cancelled);
    }

    internal void Release()
    {
        Interlocked.Exchange(ref _lease, null);
        _value = default;
        _error = null;
    }
}
