using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>A UI-thread-owned reactive graph for all Lucent authoring surfaces.</summary>
/// <remarks>Read and mutation operations belong to the creating thread. Worker completion is queued and becomes observable only when the owner calls <see cref="Drain"/>.</remarks>
public sealed class ReactiveGraph
{
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly List<ReactiveNode> _nodes = [];
    private readonly List<ReactiveScope> _scopes = [];
    private readonly LinkedList<ReactiveEffect> _effects = [];
    private readonly ConcurrentQueue<IPosted> _posted = new();
    private readonly object _postedGate = new();
    private readonly List<ReactiveNode> _evaluating = [];
    private ReactiveCollector? _collecting;
    private int _batchDepth;
    private int _nextNodeId;
    private int _nextScopeId;

    /// <summary>Raised once when worker-posted work changes from empty to nonempty.</summary>
    public event Action? WorkAvailable;

    /// <summary>Creates a root lifetime scope on the UI thread. Disposing it releases every owned node and child scope.</summary>
    public ReactiveScope CreateScope(string name)
    {
        CheckThread();
        ValidateName(name, nameof(name));
        return new ReactiveScope(this, null, name);
    }

    /// <summary>Creates writable graph state that notifies dependent nodes when its value changes.</summary>
    public Signal<T> Signal<T>(T value, string name)
    {
        CheckThread();
        ValidateName(name, nameof(name));
        return new Signal<T>(this, value, name, null);
    }

    /// <summary>Creates a lazy memoized value whose dependencies are discovered during each evaluation.</summary>
    public Derived<T> Derived<T>(Func<T> compute, string name)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(compute);
        ValidateName(name, nameof(name));
        return new Derived<T>(this, compute, name, null);
    }

    /// <summary>Creates an immediately scheduled callback that reruns after values it reads change.</summary>
    public ReactiveEffect Effect(Action callback, string name)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(callback);
        ValidateName(name, nameof(name));
        return new ReactiveEffect(this, callback, name, null);
    }

    /// <summary>Creates latest-generation asynchronous state; only the current generation may commit on <see cref="Drain"/>.</summary>
    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, string name)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(load);
        ValidateName(name, nameof(name));
        return new AsyncValue<T>(this, load, default!, false, name, null);
    }

    /// <summary>Creates latest-generation asynchronous state with a value retained until its replacement commits.</summary>
    public AsyncValue<T> Async<T>(Func<CancellationToken, Task<T>> load, T staleValue, string name)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(load);
        ValidateName(name, nameof(name));
        return new AsyncValue<T>(this, load, staleValue, true, name, null);
    }

    /// <summary>Defers effect execution until the outermost batch exits.</summary>
    public void Batch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        CheckThread();
        _batchDepth++;
        Exception? bodyError = null;
        try
        {
            action();
        }
        catch (Exception exception)
        {
            bodyError = exception;
        }
        finally
        {
            _batchDepth--;
        }

        if (_batchDepth != 0)
        {
            if (bodyError is not null)
                ExceptionDispatchInfo.Capture(bodyError).Throw();
            return;
        }

        Exception? drainError = null;
        try
        {
            Drain();
        }
        catch (Exception exception)
        {
            drainError = exception;
        }
        ThrowCombined(bodyError, drainError, "Reactive batch failed.");
    }

    /// <summary>Commits posted async completions and scheduled effects on the owning UI thread.</summary>
    public void Drain() => DrainPosted();

    internal bool DrainPosted()
    {
        CheckThread();
        if (_batchDepth != 0)
            return false;

        List<Exception>? errors = null;
        var posted = false;
        while (true)
        {
            while (TakePosted(out var post))
            {
                try
                {
                    posted |= post.Commit();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }

            var effect = _effects.First;
            if (effect is null)
                break;
            _effects.RemoveFirst();
            effect.Value.Dequeue();
            if (effect.Value.IsDisposed)
                continue;
            try
            {
                effect.Value.Run();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        if (errors is { Count: > 0 })
            throw new AggregateException("Reactive callbacks failed.", errors);
        return posted;
    }

    /// <summary>Returns a deterministic snapshot of active graph topology without values or exception messages.</summary>
    public string Dump()
    {
        CheckThread();
        var dump = new StringBuilder("reactive-graph\n");
        foreach (var scope in _scopes.OrderBy(scope => scope.Id))
            dump.Append("scope ")
                .Append(scope.Id.ToString(CultureInfo.InvariantCulture))
                .Append(" name=")
                .Append(Quote(scope.Name))
                .Append(" parent=")
                .Append(scope.Parent?.Id.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append('\n');
        foreach (var node in _nodes.OrderBy(node => node.Id))
        {
            dump.Append("node ")
                .Append(node.Id.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(node.Kind)
                .Append(" name=")
                .Append(Quote(node.Name))
                .Append(" scope=")
                .Append(node.Scope?.Id.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(" deps=[")
                .Append(
                    string.Join(
                        ',',
                        node.Dependencies.Select(dependency =>
                            dependency.Id.ToString(CultureInfo.InvariantCulture)
                        )
                    )
                )
                .Append(']');
            node.AppendDump(dump);
            dump.Append('\n');
        }
        return dump.ToString();
    }

    internal int Register(ReactiveNode node)
    {
        _nodes.Add(node);
        return ++_nextNodeId;
    }

    internal int Register(ReactiveScope scope)
    {
        _scopes.Add(scope);
        return ++_nextScopeId;
    }

    internal void Unregister(ReactiveNode node) => _nodes.Remove(node);

    internal void Unregister(ReactiveScope scope) => _scopes.Remove(scope);

    internal void Track(ReactiveNode node)
    {
        CheckThread();
        _collecting?.Add(node);
    }

    internal Evaluation<T> Evaluate<T>(ReactiveNode node, Func<T> callback)
    {
        CheckThread();
        var cycleStart = _evaluating.IndexOf(node);
        if (cycleStart >= 0)
            throw new ReactiveCycleException(
                _evaluating.Skip(cycleStart).Append(node).Select(current => current.Name)
            );

        var prior = _collecting;
        var collector = _collecting = new ReactiveCollector();
        _evaluating.Add(node);
        try
        {
            var value = callback();
            node.ApplyDependencies(collector, true);
            return new Evaluation<T>(value, collector.Changed());
        }
        catch
        {
            node.ApplyDependencies(collector, false);
            node.EvaluationFailed();
            throw;
        }
        finally
        {
            _evaluating.RemoveAt(_evaluating.Count - 1);
            _collecting = prior;
        }
    }

    internal bool Collect(ReactiveNode node, Action callback)
    {
        var prior = _collecting;
        var collector = _collecting = new ReactiveCollector();
        try
        {
            callback();
            node.ApplyDependencies(collector, true);
            return collector.Changed();
        }
        catch
        {
            node.ApplyDependencies(collector, false);
            if (collector.Changed() && node is ReactiveEffect effect)
                Schedule(effect);
            throw;
        }
        finally
        {
            _collecting = prior;
        }
    }

    internal void Schedule(ReactiveEffect effect)
    {
        if (!effect.IsDisposed && effect.QueueNode is null)
            effect.QueueNode = _effects.AddLast(effect);
    }

    internal void Unschedule(ReactiveEffect effect)
    {
        if (effect.QueueNode is null)
            return;
        _effects.Remove(effect.QueueNode);
        effect.QueueNode = null;
    }

    internal void Post(IPosted post)
    {
        ArgumentNullException.ThrowIfNull(post);
        Action? available = null;
        lock (_postedGate)
        {
            var wasEmpty = _posted.IsEmpty;
            _posted.Enqueue(post);
            if (wasEmpty)
                available = WorkAvailable;
        }
        available?.Invoke();
    }

    private bool TakePosted(out IPosted post)
    {
        lock (_postedGate)
            return _posted.TryDequeue(out post!);
    }

    internal void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _uiThread)
            throw new InvalidOperationException(
                "Reactive graph access must occur on its owning UI thread."
            );
    }

    internal static void ValidateName(string name, string parameter)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A reactive name is required.", parameter);
    }

    internal static void ThrowCombined(Exception? first, Exception? second, string message)
    {
        if (first is null && second is null)
            return;
        if (first is null)
        {
            ExceptionDispatchInfo.Capture(second!).Throw();
            return;
        }
        if (second is null)
        {
            ExceptionDispatchInfo.Capture(first).Throw();
            return;
        }
        throw new AggregateException(message, first, second);
    }

    private static string Quote(string value) =>
        '"'
        + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
        + '"';
}
