namespace Lucent.Core;

/// <summary>A hoistable logical scroll position with an explicit reactive lifetime.</summary>
public sealed class ViewportState : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<ScrollOffset> _offset;
    private readonly HashSet<MountLease> _mounts = [];

    /// <summary>Creates viewport state owned by <paramref name="owner"/>.</summary>
    public ViewportState(
        ReactiveScope owner,
        ScrollOffset initialOffset = default,
        string name = "viewport-state"
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        initialOffset.Validate();
        _scope = owner.CreateChild(name);
        _offset = _scope.Signal(initialOffset, name + ".offset");
    }

    /// <summary>Gets or sets the retained logical scroll position.</summary>
    public ScrollOffset Offset
    {
        get
        {
            CheckRead();
            return _offset.Value;
        }
        set
        {
            CheckMutation();
            value.Validate();
            _offset.Value = value;
        }
    }

    /// <summary>Gets whether this state has released its reactive lifetime.</summary>
    public bool IsDisposed => _scope.IsDisposed;

    /// <summary>Releases this viewport state and its reactive resources.</summary>
    public void Dispose() => _scope.Dispose();

    internal IDisposable AcquireMount(ReactiveScope mountScope)
    {
        ArgumentNullException.ThrowIfNull(mountScope);
        CheckRead();
        mountScope.Graph.CheckThread();
        if (!ReferenceEquals(_scope.Graph, mountScope.Graph))
            throw new ArgumentException(
                "Viewport state and its mounted component must belong to the same reactive graph.",
                nameof(mountScope)
            );
        var lease = new MountLease(this);
        _mounts.Add(lease);
        mountScope.OnDispose(lease.Dispose);
        _ = mountScope.Effect(
            () =>
            {
                if (_mounts.Count > 1)
                    throw new InvalidOperationException(
                        "Viewport state can have one established scroll-viewport mount."
                    );
            },
            "viewport-state.mount-lease"
        );
        return lease;
    }

    private void CheckRead()
    {
        _scope.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
    }

    private void CheckMutation()
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
    }

    private sealed class MountLease(ViewportState owner) : IDisposable
    {
        private ViewportState? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            current?._mounts.Remove(this);
        }
    }
}
