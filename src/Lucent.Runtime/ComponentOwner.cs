namespace Lucent.Runtime;

using System.Runtime.ExceptionServices;

public sealed class ComponentOwner : IDisposable
{
    private readonly object _gate = new();
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<Exception>? _reportUnhandled;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _cancellationToken;
    private List<Action>? _cleanups = [];
    private int _disposed;

    public ComponentOwner(IUiDispatcher dispatcher)
        : this(dispatcher, null)
    {
    }

    public ComponentOwner(IUiDispatcher dispatcher, Action<Exception>? reportUnhandled)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _reportUnhandled = reportUnhandled;
        _cancellationToken = _cancellation.Token;
    }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public CancellationToken CancellationToken => _cancellationToken;

    public ComponentOwner CreateChild()
    {
        var child = new ComponentOwner(_dispatcher, _reportUnhandled);
        try
        {
            OnDispose(child.Dispose);
            return child;
        }
        catch
        {
            child.Dispose();
            throw;
        }
    }

    public void OnDispose(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _cleanups!.Add(cleanup);
        }
    }

    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsDisposed)
        {
            return;
        }

        _dispatcher.Dispatch(() =>
        {
            if (!IsDisposed)
            {
                action();
            }
        });
    }

    public void ReportUnhandled(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (IsDisposed)
        {
            return;
        }

        if (_reportUnhandled is not null)
        {
            _reportUnhandled(error);
            return;
        }

        ExceptionDispatchInfo.Capture(error).Throw();
    }

    public void Dispose()
    {
        List<Action> cleanups;
        lock (_gate)
        {
            if (IsDisposed)
            {
                return;
            }

            Volatile.Write(ref _disposed, 1);
            cleanups = _cleanups!;
            _cleanups = null;
        }

        List<Exception>? failures = null;
        try
        {
            _cancellation.Cancel();
        }
        catch (Exception exception)
        {
            AddFailures(ref failures, exception);
        }

        for (var index = cleanups.Count - 1; index >= 0; index--)
        {
            try
            {
                cleanups[index]();
            }
            catch (Exception exception)
            {
                AddFailures(ref failures, exception);
            }
        }

        _cancellation.Dispose();
        if (failures is not null)
        {
            throw new AggregateException(failures);
        }
    }

    private static void AddFailures(ref List<Exception>? failures, Exception exception)
    {
        failures ??= [];
        if (exception is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(exception);
        }
    }
}
