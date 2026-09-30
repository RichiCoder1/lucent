namespace Lucent.Core;

/// <summary>A hoistable logical scroll position with an explicit reactive lifetime.</summary>
public sealed class ViewportState : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<ScrollOffset> _offset;
    private readonly Signal<long> _restorationGeneration;
    private (long Generation, ScrollOffset Offset)? _restoration;
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
        _restorationGeneration = _scope.Signal(0L, name + ".restoration");
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
            // Equal writes still express newer application intent while an imported
            // route target is waiting for its first reactive mount.
            OffsetWriteRevision = _scope.Graph.RecordViewportWrite();
            _restoration = null;
            _offset.Value = value;
        }
    }

    /// <summary>Gets whether this state has released its reactive lifetime.</summary>
    public bool IsDisposed => _scope.IsDisposed;

    /// <summary>Releases this viewport state and its reactive resources.</summary>
    public void Dispose() => _scope.Dispose();

    // Imported coordinates remain separate from live geometry until an installed
    // scene supplies the extent. An application write always wins, even at zero.
    internal long RequestRestoration(ScrollOffset offset)
    {
        CheckMutation();
        offset.Validate();
        var generation = checked(_restorationGeneration.Value + 1);
        _restoration = (generation, offset);
        _restorationGeneration.Value = generation;
        return generation;
    }

    internal long RestorationGeneration => _restorationGeneration.Value;
    internal ReactiveGraph Graph => _scope.Graph;
    internal long OffsetWriteRevision { get; private set; }

    internal bool TryGetRestoration(long generation, out ScrollOffset offset)
    {
        CheckRead();
        if (_restoration is { } pending && pending.Generation == generation)
        {
            offset = pending.Offset;
            return true;
        }
        offset = default;
        return false;
    }

    internal ScrollOffset? TakeRestoration()
    {
        CheckMutation();
        var result = _restoration?.Offset;
        _restoration = null;
        return result;
    }

    internal void CancelRestoration(long generation)
    {
        if (_scope.IsDisposed)
            return;
        CheckMutation();
        if (_restoration?.Generation == generation)
            _restoration = null;
    }

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
