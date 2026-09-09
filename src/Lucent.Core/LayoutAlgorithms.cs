namespace Lucent.Core;

/// <summary>A finite nonnegative logical size used by custom container layout.</summary>
public readonly record struct LayoutSize
{
    /// <summary>Creates a validated logical size.</summary>
    public LayoutSize(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width == 0 ? 0 : width;
        Height = height == 0 ? 0 : height;
    }

    /// <summary>Gets the logical width.</summary>
    public float Width { get; }

    /// <summary>Gets the logical height.</summary>
    public float Height { get; }
}

/// <summary>Finite or unbounded logical constraints supplied to a custom layout algorithm.</summary>
public readonly record struct LayoutConstraints(LayoutConstraint Width, LayoutConstraint Height);

/// <summary>A validated placement for one participating direct child.</summary>
public readonly record struct LayoutChildPlacement
{
    /// <summary>Creates a child placement in container-content coordinates.</summary>
    public LayoutChildPlacement(
        int childIndex,
        LayoutRect bounds,
        bool widthAssigned = true,
        bool heightAssigned = true
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(childIndex);
        if (
            !float.IsFinite(bounds.X)
            || !float.IsFinite(bounds.Y)
            || !float.IsFinite(bounds.Width)
            || !float.IsFinite(bounds.Height)
            || bounds.Width < 0
            || bounds.Height < 0
        )
            throw new ArgumentOutOfRangeException(nameof(bounds));
        ChildIndex = childIndex;
        Bounds = bounds;
        WidthAssigned = widthAssigned;
        HeightAssigned = heightAssigned;
    }

    /// <summary>Gets the child's index in the participating-child sequence.</summary>
    public int ChildIndex { get; }

    /// <summary>Gets the child bounds relative to the container content origin.</summary>
    public LayoutRect Bounds { get; }

    /// <summary>Gets whether the algorithm assigned the child's width.</summary>
    public bool WidthAssigned { get; }

    /// <summary>Gets whether the algorithm assigned the child's height.</summary>
    public bool HeightAssigned { get; }
}

/// <summary>The desired size and complete direct-child placement set returned by an algorithm.</summary>
public sealed class LayoutAlgorithmResult
{
    private readonly LayoutChildPlacement[] _placements;
    private readonly IReadOnlyList<LayoutChildPlacement> _placementsView;

    /// <summary>Copies an algorithm result.</summary>
    public LayoutAlgorithmResult(LayoutSize desiredSize, params LayoutChildPlacement[] placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        DesiredSize = desiredSize;
        _placements = placements.ToArray();
        _placementsView = Array.AsReadOnly(_placements);
    }

    /// <summary>Gets the container's desired content size.</summary>
    public LayoutSize DesiredSize { get; }

    /// <summary>Gets the copied ordered child placements.</summary>
    public IReadOnlyList<LayoutChildPlacement> Placements => _placementsView;
}

/// <summary>Read-only metadata for one mounted participating direct child.</summary>
public sealed class LayoutChild
{
    private readonly LayoutAlgorithmContext _context;
    private readonly Element _element;

    internal LayoutChild(
        LayoutAlgorithmContext context,
        Element element,
        int index,
        LayoutSize desiredSize
    )
    {
        _context = context;
        _element = element;
        Index = index;
        DesiredSize = desiredSize;
    }

    /// <summary>Gets the index in the participating direct-child sequence.</summary>
    public int Index { get; }

    /// <summary>Gets the child's initial unconstrained desired size.</summary>
    public LayoutSize DesiredSize
    {
        get
        {
            _context.CheckActive();
            return field;
        }
    }

    /// <summary>Reads typed resolved metadata without exposing mutable element structure.</summary>
    public T Read<T>(Property<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        _context.CheckActive();
        return _element.Composition.ResumeProjectionTracking(() =>
            _element.Resolve(property).Value
        );
    }

    internal Element Element => _element;
}

/// <summary>A bounded owner-thread invocation surface for a custom nonvirtualizing layout algorithm.</summary>
public sealed class LayoutAlgorithmContext
{
    private readonly Element _container;
    private readonly LayoutAlgorithm _algorithm;
    private readonly Func<Element, LayoutConstraints, LayoutSize> _measure;
    private readonly Dictionary<
        (int Index, LayoutConstraints Constraints),
        LayoutSize
    > _measurements = [];
    private readonly Dictionary<int, HashSet<LayoutConstraints>> _distinctConstraints = [];
    private bool _active = true;

    internal LayoutAlgorithmContext(
        Element container,
        LayoutAlgorithm algorithm,
        LayoutConstraints constraints,
        IReadOnlyList<(Element Element, LayoutSize Desired)> children,
        Func<Element, LayoutConstraints, LayoutSize> measure
    )
    {
        _container = container;
        _algorithm = algorithm;
        _measure = measure;
        Constraints = constraints;
        Children = Array.AsReadOnly(
            children
                .Select(
                    (child, index) => new LayoutChild(this, child.Element, index, child.Desired)
                )
                .ToArray()
        );
        for (var index = 0; index < Children.Count; index++)
        {
            var unbounded = new LayoutConstraints(
                LayoutConstraint.Unbounded,
                LayoutConstraint.Unbounded
            );
            _measurements[(index, unbounded)] = Children[index].DesiredSize;
            _distinctConstraints[index] = [unbounded];
        }
    }

    /// <summary>Gets the container-content constraints for this invocation.</summary>
    public LayoutConstraints Constraints
    {
        get
        {
            CheckActive();
            return field;
        }
    }

    /// <summary>Gets mounted participating direct children in retained order.</summary>
    public IReadOnlyList<LayoutChild> Children
    {
        get
        {
            CheckActive();
            return field;
        }
    }

    /// <summary>Reads a resolved container property and tracks it as an input to this projection.</summary>
    /// <remarks>The context expires when Layout returns; this does not expose the mutable container.</remarks>
    public T Read<T>(Property<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        CheckActive();
        return _container.Composition.ResumeProjectionTracking(() =>
            _container.Resolve(property).Value
        );
    }

    /// <summary>Measures a child with at most one constraint distinct from its initial unconstrained size.</summary>
    public LayoutSize MeasureChild(LayoutChild child, LayoutConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(child);
        CheckActive();
        if (child.Index >= Children.Count || !ReferenceEquals(Children[child.Index], child))
            throw new ArgumentException(
                "The child does not belong to this layout invocation.",
                nameof(child)
            );
        var key = (child.Index, constraints);
        if (_measurements.TryGetValue(key, out var cached))
            return cached;
        var distinct = _distinctConstraints[child.Index];
        if (distinct.Count >= 2)
            throw new InvalidOperationException(
                "A layout algorithm can use at most two distinct measurements per child and invocation."
            );
        distinct.Add(constraints);
        var measured = _measure(child.Element, constraints);
        _measurements.Add(key, measured);
        return measured;
    }

    /// <summary>Gets retained per-container state scoped to this algorithm instance and state type.</summary>
    /// <remarks>
    /// Disposable state is released when another algorithm is activated or the container is disposed.
    /// A collapsed container defers algorithm activation until it participates in layout again;
    /// component-local state has its own retained lifetime.
    /// </remarks>
    public T GetOrCreateState<T>(Func<T> create)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(create);
        CheckActive();
        return _container.GetOrCreateLayoutAlgorithmState(_algorithm, create);
    }

    internal void CheckActive()
    {
        _container.Composition.CheckThread();
        if (!_active)
            throw new InvalidOperationException(
                "A layout algorithm context and its children cannot be used after Layout returns."
            );
    }

    internal void Complete() => _active = false;
}

/// <summary>A reusable owner-thread nonvirtualizing container layout policy.</summary>
public abstract class LayoutAlgorithm
{
    /// <summary>Creates an algorithm with a stable diagnostic name.</summary>
    protected LayoutAlgorithm(string name)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        Name = name;
    }

    /// <summary>Gets the stable diagnostic name.</summary>
    public string Name { get; }

    /// <summary>Measures and places all participating direct children exactly once.</summary>
    public abstract LayoutAlgorithmResult Layout(LayoutAlgorithmContext context);

    internal virtual LayoutMode? BuiltInMode => null;
}

/// <summary>Reusable markers for the existing bounded built-in strategies.</summary>
public static class LayoutAlgorithms
{
    /// <summary>Uses the existing bounded flex strategy and its typed properties.</summary>
    public static LayoutAlgorithm Flex { get; } =
        new BuiltInLayoutAlgorithm("flex", LayoutMode.Flex);

    /// <summary>Uses the existing bounded explicit Grid strategy and its typed properties.</summary>
    public static LayoutAlgorithm Grid { get; } =
        new BuiltInLayoutAlgorithm("grid", LayoutMode.Grid);

    private sealed class BuiltInLayoutAlgorithm(string name, LayoutMode mode)
        : LayoutAlgorithm(name)
    {
        public override LayoutAlgorithmResult Layout(LayoutAlgorithmContext context) =>
            throw new InvalidOperationException(
                "Built-in layout strategies are evaluated by SceneLayout."
            );

        internal override LayoutMode? BuiltInMode => mode;
    }
}
