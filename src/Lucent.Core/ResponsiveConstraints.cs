namespace Lucent.Core;

/// <summary>The finite logical content size assigned to a responsive container.</summary>
public readonly record struct ContainerConstraints
{
    /// <summary>Creates finite nonnegative logical constraints.</summary>
    public ContainerConstraints(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width == 0 ? 0 : width;
        Height = height == 0 ? 0 : height;
    }

    /// <summary>Gets the assigned logical content width.</summary>
    public float Width { get; }

    /// <summary>Gets the assigned logical content height.</summary>
    public float Height { get; }
}

/// <summary>A hoistable reactive reader for one responsive container's assigned logical constraints.</summary>
public sealed class ResponsiveConstraints : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<ContainerConstraints> _current;
    private int _mounts;

    /// <summary>Creates state owned by the supplied reactive scope.</summary>
    public ResponsiveConstraints(ReactiveScope owner, string name = "responsive-constraints")
    {
        ArgumentNullException.ThrowIfNull(owner);
        _scope = owner.CreateChild(name);
        _current = _scope.Signal(default(ContainerConstraints), name + ".current");
    }

    /// <summary>Gets the latest assigned container constraints and registers a reactive read.</summary>
    public ContainerConstraints Current
    {
        get
        {
            _scope.Graph.CheckThread();
            ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
            return _current.Value;
        }
    }

    /// <summary>Gets whether this state has released its reactive lifetime.</summary>
    public bool IsDisposed => _scope.IsDisposed;

    /// <summary>Releases this state and its reactive resources.</summary>
    public void Dispose() => _scope.Dispose();

    internal void AcquireMount(ReactiveScope mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        _scope.Graph.CheckThread();
        if (!ReferenceEquals(_scope.Graph, mount.Graph))
            throw new ArgumentException(
                "Responsive constraints and container must use the same reactive graph.",
                nameof(mount)
            );
        if (_scope.IsDisposed)
            ObjectDisposedException.ThrowIf(true, this);
        _mounts++;
        mount.OnDispose(() => _mounts--);
        _ = mount.Effect(
            () =>
            {
                if (_mounts > 1)
                    throw new InvalidOperationException(
                        "Responsive constraints can have one established container mount."
                    );
            },
            "responsive-constraints.mount"
        );
    }

    internal bool Assign(ContainerConstraints value)
    {
        _scope.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
        if (_current.Value == value)
            return false;
        _current.Value = value;
        return true;
    }
}
