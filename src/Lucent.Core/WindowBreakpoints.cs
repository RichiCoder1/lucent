using System.Collections;

namespace Lucent.Core;

/// <summary>An immutable named logical window-width threshold.</summary>
public sealed class Breakpoint
{
    /// <summary>Creates a named minimum-width threshold in logical pixels.</summary>
    public Breakpoint(string name, float minimumWidth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!float.IsFinite(minimumWidth) || minimumWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        Name = name;
        MinimumWidth = minimumWidth == 0 ? 0 : minimumWidth;
    }

    /// <summary>Gets the diagnostic name.</summary>
    public string Name { get; }

    /// <summary>Gets the inclusive minimum logical window width.</summary>
    public float MinimumWidth { get; }
}

/// <summary>An immutable ordered set of uniquely named ascending window-width thresholds.</summary>
public sealed class BreakpointSet : IReadOnlyList<Breakpoint>
{
    private readonly Breakpoint[] _items;

    private BreakpointSet(Breakpoint[] items) => _items = items;

    /// <summary>Copies validated descriptors ordered from the smallest to largest threshold.</summary>
    public static BreakpointSet Create(params Breakpoint[] breakpoints)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        var copy = breakpoints.ToArray();
        if (copy.Any(item => item is null))
            throw new ArgumentException("Breakpoint entries cannot be null.", nameof(breakpoints));
        if (copy.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Breakpoint names must be unique.", nameof(breakpoints));
        for (var index = 1; index < copy.Length; index++)
            if (copy[index].MinimumWidth <= copy[index - 1].MinimumWidth)
                throw new ArgumentException(
                    "Breakpoint minimum widths must be unique and strictly ascending.",
                    nameof(breakpoints)
                );
        return new BreakpointSet(copy);
    }

    /// <summary>Gets the number of thresholds.</summary>
    public int Count => _items.Length;

    /// <summary>Gets a threshold by ascending zero-based index.</summary>
    public Breakpoint this[int index] => _items[index];

    /// <summary>Enumerates thresholds in ascending order.</summary>
    public IEnumerator<Breakpoint> GetEnumerator() =>
        ((IEnumerable<Breakpoint>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    internal int IndexOf(Breakpoint breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        for (var index = 0; index < _items.Length; index++)
            if (ReferenceEquals(_items[index], breakpoint))
                return index;
        return -1;
    }
}

/// <summary>Owner-scoped reactive state for one set of logical window-width breakpoints.</summary>
public sealed class WindowBreakpoints : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<float> _width;
    private readonly Signal<int> _bucket;
    private Composition? _composition;

    /// <summary>Creates breakpoint state owned by the supplied reactive scope.</summary>
    public WindowBreakpoints(
        ReactiveScope owner,
        BreakpointSet breakpoints,
        string name = "window-breakpoints"
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(breakpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _scope = owner.CreateChild(name);
        _scope.DiagnosticState = "window-breakpoints: unregistered";
        Breakpoints = breakpoints;
        _width = _scope.Signal(0f, name + ".width");
        _bucket = _scope.Signal(Bucket(0), name + ".bucket");
    }

    /// <summary>Gets the declared ordered threshold set.</summary>
    public BreakpointSet Breakpoints { get; }

    /// <summary>Gets the latest assigned logical window width.</summary>
    /// <remarks>Starts at zero before the first mounted projection and retains the last value after unmounting.</remarks>
    public float Width
    {
        get
        {
            CheckRead();
            return _width.Value;
        }
    }

    /// <summary>Gets whether the latest window width meets the supplied threshold from this set.</summary>
    public bool IsActive(Breakpoint breakpoint)
    {
        CheckRead();
        var index = Breakpoints.IndexOf(breakpoint);
        if (index < 0)
            throw new ArgumentException(
                "The breakpoint does not belong to this window breakpoint set.",
                nameof(breakpoint)
            );
        return _bucket.Value >= index;
    }

    /// <summary>Gets whether this state has released its reactive lifetime.</summary>
    public bool IsDisposed => _scope.IsDisposed;

    /// <summary>Releases the breakpoint state and its reactive resources.</summary>
    public void Dispose() => _scope.Dispose();

    internal void AcquireMount(Element mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        CheckRead();
        if (!ReferenceEquals(_scope.Graph, mount.Scope.Graph))
            throw new ArgumentException(
                "Window breakpoints and layout must use the same reactive graph.",
                nameof(mount)
            );
        if (_composition is not null)
            throw new InvalidOperationException(
                "Window breakpoints can have one established layout mount."
            );
        _composition = mount.Composition;
        _scope.DiagnosticState = "window-breakpoints: mounted, awaiting viewport";
        mount.Scope.OnDispose(() =>
        {
            _composition = null;
            _scope.DiagnosticState = "window-breakpoints: unregistered";
        });
    }

    internal void Assign(Composition composition, float width)
    {
        CheckRead();
        if (!ReferenceEquals(_composition, composition))
            throw new InvalidOperationException(
                "Window breakpoints were assigned by a composition other than their established mount."
            );
        if (!float.IsFinite(width) || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        _width.Value = width == 0 ? 0 : width;
        _bucket.Value = Bucket(width);
        _scope.DiagnosticState = "window-breakpoints: mounted, viewport assigned";
    }

    private int Bucket(float width)
    {
        var result = -1;
        for (var index = 0; index < Breakpoints.Count; index++)
        {
            if (width < Breakpoints[index].MinimumWidth)
                break;
            result = index;
        }
        return result;
    }

    private void CheckRead()
    {
        _scope.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
    }
}
