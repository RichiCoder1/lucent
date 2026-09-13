namespace Lucent.Core;

/// <summary>A reusable local-coordinate drawing description prepared independently for each mount.</summary>
public sealed class DrawingDescriptor
{
    private readonly Action<DrawingRecorder> _record;

    /// <summary>Creates a bounded drawing description with a finite positive local coordinate size.</summary>
    public DrawingDescriptor(LayoutSize coordinateSize, Action<DrawingRecorder> record)
    {
        if (
            coordinateSize.Width <= 0
            || coordinateSize.Height <= 0
            || coordinateSize.Width > DrawingRecorder.MaximumMagnitude
            || coordinateSize.Height > DrawingRecorder.MaximumMagnitude
        )
            throw new ArgumentOutOfRangeException(
                nameof(coordinateSize),
                "Drawing coordinate dimensions must be positive and within the drawing magnitude limit."
            );
        CoordinateSize = coordinateSize;
        _record = record ?? throw new ArgumentNullException(nameof(record));
    }

    /// <summary>Gets the local coordinate size mapped into the arranged content box.</summary>
    public LayoutSize CoordinateSize { get; }

    internal FrozenDrawing Freeze()
    {
        var recorder = new DrawingRecorder();
        try
        {
            _record(recorder);
            return recorder.Freeze();
        }
        catch
        {
            recorder.Expire();
            throw;
        }
    }
}

public static partial class Components
{
    /// <summary>Creates one styled retained drawing root with optional ordinary child content.</summary>
    [LucentComponent]
    public static AuthorRecipe<StyledCapability> Drawing(
        DrawingDescriptor drawing,
        [DefaultContent] ComponentContent? content = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(drawing);
        content ??= ComponentContent.Create([]);
        var mountedContent = content;
        return StockRecipe.Styled(
            "drawing",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Width, drawing.CoordinateSize.Width)
                        .Set(LayoutProperties.Height, drawing.CoordinateSize.Height),
                    author: style
                );
                DrawingBinding.Attach(root, drawing);
                context.Mount(root, mountedContent);
            }
        );
    }
}

internal sealed class DrawingBinding : IDisposable
{
    private readonly Element _element;
    private readonly DrawingDescriptor _descriptor;
    private readonly ReactiveEffect _effect;
    private DrawingLease? _lease;
    private bool _disposed;
    private bool _initialized;

    private DrawingBinding(Element element, DrawingDescriptor descriptor)
    {
        _element = element;
        _descriptor = descriptor;
        _effect = element.Scope.Effect(Update, element.Name + ".drawing");
        _effect.Run();
    }

    internal static DrawingBinding Attach(Element element, DrawingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (element.Drawing is not null)
            throw new InvalidOperationException("An element can own only one drawing controller.");
        var binding = element.Scope.Own(new DrawingBinding(element, descriptor));
        element.Drawing = binding;
        return binding;
    }

    internal DrawingSceneNode? Resolve(SceneNodeIdentity identity, LayoutRect bounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _lease is null
            ? null
            : new DrawingSceneNode(identity, bounds, _descriptor.CoordinateSize, _lease);
    }

    internal FrozenDrawing? Current => _lease?.Drawing;

    private void Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var next = new DrawingLease(new DrawingResource(_descriptor.Freeze()));
        if (_lease is { } current && current.Drawing.ContentEquals(next.Drawing))
        {
            next.Dispose();
            _initialized = true;
            return;
        }
        var previous = _lease;
        _lease = next;
        previous?.Dispose();
        if (_initialized)
            _element.Composition.InvalidateInteractionVisuals();
        _initialized = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lease?.Dispose();
        _lease = null;
    }
}

/// <summary>One retained replay of immutable local-coordinate drawing commands.</summary>
public sealed class DrawingSceneNode : SceneNode
{
    internal DrawingSceneNode(
        SceneNodeIdentity identity,
        LayoutRect bounds,
        LayoutSize coordinateSize,
        DrawingLease drawing
    )
        : base(identity, bounds)
    {
        CoordinateSize = coordinateSize;
        SourceLease = drawing ?? throw new ArgumentNullException(nameof(drawing));
        Drawing = drawing.Drawing;
    }

    /// <summary>Gets the local coordinate size mapped into <see cref="SceneNode.Bounds"/>.</summary>
    public LayoutSize CoordinateSize { get; }

    /// <summary>Gets the immutable commands retained by the enclosing scene.</summary>
    public FrozenDrawing Drawing { get; }

    internal DrawingLease SourceLease { get; }
}
