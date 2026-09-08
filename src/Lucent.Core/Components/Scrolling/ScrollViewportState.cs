namespace Lucent.Core;

/// <summary>Mount adapter for a hoistable bounded viewport.</summary>
internal sealed class ScrollViewportState
{
    private readonly ReactiveScope _scope;
    private readonly ViewportState _viewport;

    internal ScrollViewportState(
        ReactiveScope scope,
        string name,
        ScrollOffset offset,
        ViewportState? viewport = null
    )
    {
        _scope = scope;
        _viewport = viewport ?? new ViewportState(scope, offset, name + ".state");
        _ = _viewport.AcquireMount(scope);
    }

    public ScrollOffset Offset
    {
        get
        {
            CheckRead();
            return _viewport.Offset;
        }
        set
        {
            CheckMutation();
            _viewport.Offset = value;
        }
    }

    private void CheckRead()
    {
        _scope.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, typeof(ScrollViewportState));
    }

    private void CheckMutation()
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, typeof(ScrollViewportState));
    }
}
