using System.Numerics;

namespace Lucent.Core;

/// <summary>Endpoint treatment for a bounded drawing stroke.</summary>
public enum DrawingStrokeCap
{
    /// <summary>Ends the stroke at its endpoint.</summary>
    Butt,

    /// <summary>Ends the stroke with a semicircle.</summary>
    Round,

    /// <summary>Extends the stroke by half its width.</summary>
    Square,
}

/// <summary>Join treatment for a bounded path stroke.</summary>
public enum DrawingStrokeJoin
{
    /// <summary>Joins segments with a bounded miter.</summary>
    Miter,

    /// <summary>Rounds the outside of the join.</summary>
    Round,

    /// <summary>Bevels the outside of the join.</summary>
    Bevel,
}

/// <summary>Interior winding rule for a filled path.</summary>
public enum DrawingFillRule
{
    /// <summary>Uses nonzero winding.</summary>
    NonZero,

    /// <summary>Alternates filled regions at every crossing.</summary>
    EvenOdd,
}

/// <summary>Finite shape understood by the portable drawing replay contract.</summary>
public enum DrawingShapeKind
{
    /// <summary>An axis-aligned rectangle.</summary>
    Rectangle,

    /// <summary>An axis-aligned rounded rectangle.</summary>
    RoundedRectangle,

    /// <summary>An ellipse inscribed in an axis-aligned rectangle.</summary>
    Ellipse,
}

/// <summary>Kind of one immutable path verb.</summary>
public enum DrawingPathVerbKind
{
    /// <summary>Begins a contour at one point.</summary>
    Move,

    /// <summary>Adds a straight segment.</summary>
    Line,

    /// <summary>Adds a quadratic curve.</summary>
    Quadratic,

    /// <summary>Adds a cubic curve.</summary>
    Cubic,

    /// <summary>Closes the current contour.</summary>
    Close,
}

/// <summary>One immutable local-coordinate path verb.</summary>
public readonly record struct DrawingPathVerb(
    DrawingPathVerbKind Kind,
    Vector2 First,
    Vector2 Second,
    Vector2 Third
);

/// <summary>An immutable bounded local-coordinate path.</summary>
public sealed class DrawingPath
{
    private readonly DrawingPathVerb[] _verbs;
    private readonly IReadOnlyList<DrawingPathVerb> _view;

    internal DrawingPath(IReadOnlyList<DrawingPathVerb> verbs)
    {
        _verbs = [.. verbs];
        _view = Array.AsReadOnly(_verbs);
    }

    /// <summary>Gets the copied path verbs in replay order.</summary>
    public IReadOnlyList<DrawingPathVerb> Verbs => _view;

    internal bool ContentEquals(DrawingPath other) => _verbs.SequenceEqual(other._verbs);
}

/// <summary>Base for the closed immutable drawing command set.</summary>
public abstract class DrawingCommand
{
    private protected DrawingCommand() { }
}

/// <summary>Paints one local-coordinate line.</summary>
public sealed class DrawingLineCommand : DrawingCommand
{
    internal DrawingLineCommand(
        Vector2 start,
        Vector2 end,
        Brush brush,
        float width,
        DrawingStrokeCap cap
    ) => (Start, End, Brush, Width, Cap) = (start, end, brush, width, cap);

    /// <summary>Gets the first endpoint.</summary>
    public Vector2 Start { get; }

    /// <summary>Gets the second endpoint.</summary>
    public Vector2 End { get; }

    /// <summary>Gets the immutable stroke brush.</summary>
    public Brush Brush { get; }

    /// <summary>Gets the stroke width in local DIPs.</summary>
    public float Width { get; }

    /// <summary>Gets the stroke endpoint treatment.</summary>
    public DrawingStrokeCap Cap { get; }
}

/// <summary>Paints one local-coordinate ellipse arc.</summary>
public sealed class DrawingArcCommand : DrawingCommand
{
    internal DrawingArcCommand(
        LayoutRect oval,
        float startDegrees,
        float sweepDegrees,
        Brush brush,
        float width,
        DrawingStrokeCap cap
    ) =>
        (Oval, StartDegrees, SweepDegrees, Brush, Width, Cap) = (
            oval,
            startDegrees,
            sweepDegrees,
            brush,
            width,
            cap
        );

    /// <summary>Gets the ellipse bounds.</summary>
    public LayoutRect Oval { get; }

    /// <summary>Gets the clockwise starting angle in degrees.</summary>
    public float StartDegrees { get; }

    /// <summary>Gets the signed clockwise sweep in degrees.</summary>
    public float SweepDegrees { get; }

    /// <summary>Gets the immutable stroke brush.</summary>
    public Brush Brush { get; }

    /// <summary>Gets the stroke width in local DIPs.</summary>
    public float Width { get; }

    /// <summary>Gets the stroke endpoint treatment.</summary>
    public DrawingStrokeCap Cap { get; }
}

/// <summary>Fills or strokes one finite local-coordinate shape.</summary>
public sealed class DrawingShapeCommand : DrawingCommand
{
    internal DrawingShapeCommand(
        DrawingShapeKind shape,
        LayoutRect bounds,
        float cornerRadius,
        Brush brush,
        bool fill,
        float width,
        DrawingStrokeCap cap
    ) =>
        (Shape, Bounds, CornerRadius, Brush, Fill, Width, Cap) = (
            shape,
            bounds,
            cornerRadius,
            brush,
            fill,
            width,
            cap
        );

    /// <summary>Gets the closed shape kind.</summary>
    public DrawingShapeKind Shape { get; }

    /// <summary>Gets its local-coordinate bounds.</summary>
    public LayoutRect Bounds { get; }

    /// <summary>Gets the rounded-rectangle radius, or zero for other shapes.</summary>
    public float CornerRadius { get; }

    /// <summary>Gets the immutable fill or stroke brush.</summary>
    public Brush Brush { get; }

    /// <summary>Gets whether this command fills rather than strokes.</summary>
    public bool Fill { get; }

    /// <summary>Gets the stroke width; fills use zero.</summary>
    public float Width { get; }

    /// <summary>Gets the stroke endpoint treatment.</summary>
    public DrawingStrokeCap Cap { get; }
}

/// <summary>Fills or strokes one immutable local-coordinate path.</summary>
public sealed class DrawingPathCommand : DrawingCommand
{
    internal DrawingPathCommand(
        DrawingPath path,
        Brush brush,
        bool fill,
        float width,
        DrawingStrokeCap cap,
        DrawingStrokeJoin join,
        DrawingFillRule fillRule
    ) =>
        (Path, Brush, Fill, Width, Cap, Join, FillRule) = (
            path,
            brush,
            fill,
            width,
            cap,
            join,
            fillRule
        );

    /// <summary>Gets the immutable path.</summary>
    public DrawingPath Path { get; }

    /// <summary>Gets the immutable fill or stroke brush.</summary>
    public Brush Brush { get; }

    /// <summary>Gets whether this command fills rather than strokes.</summary>
    public bool Fill { get; }

    /// <summary>Gets the stroke width; fills use zero.</summary>
    public float Width { get; }

    /// <summary>Gets the stroke endpoint treatment.</summary>
    public DrawingStrokeCap Cap { get; }

    /// <summary>Gets the path join treatment.</summary>
    public DrawingStrokeJoin Join { get; }

    /// <summary>Gets the path fill rule.</summary>
    public DrawingFillRule FillRule { get; }
}

/// <summary>Pushes one local-coordinate rectangular clip.</summary>
public sealed class DrawingPushClipCommand : DrawingCommand
{
    internal DrawingPushClipCommand(LayoutRect bounds) => Bounds = bounds;

    /// <summary>Gets the clip bounds.</summary>
    public LayoutRect Bounds { get; }
}

/// <summary>Pushes one local-coordinate affine transform.</summary>
public sealed class DrawingPushTransformCommand : DrawingCommand
{
    internal DrawingPushTransformCommand(Matrix3x2 transform) => Transform = transform;

    /// <summary>Gets the affine transform.</summary>
    public Matrix3x2 Transform { get; }
}

/// <summary>Pops the most recent drawing clip or transform.</summary>
public sealed class DrawingPopCommand : DrawingCommand
{
    internal DrawingPopCommand() { }
}

/// <summary>Builds one immutable bounded path during a drawing recording.</summary>
public sealed class DrawingPathRecorder
{
    private readonly List<DrawingPathVerb> _verbs = [];
    private bool _active = true;
    private bool _hasContour;

    internal IReadOnlyList<DrawingPathVerb> Finish()
    {
        Check();
        _active = false;
        if (_verbs.Count == 0)
            throw new InvalidOperationException("A drawing path must contain at least one verb.");
        return _verbs;
    }

    internal void Expire() => _active = false;

    /// <summary>Begins a contour.</summary>
    public void MoveTo(Vector2 point)
    {
        Add(new(DrawingPathVerbKind.Move, DrawingRecorder.Point(point), default, default));
        _hasContour = true;
    }

    /// <summary>Adds a straight segment to the current contour.</summary>
    public void LineTo(Vector2 point)
    {
        RequireContour();
        Add(new(DrawingPathVerbKind.Line, DrawingRecorder.Point(point), default, default));
    }

    /// <summary>Adds a quadratic segment to the current contour.</summary>
    public void QuadraticTo(Vector2 control, Vector2 end)
    {
        RequireContour();
        Add(
            new(
                DrawingPathVerbKind.Quadratic,
                DrawingRecorder.Point(control),
                DrawingRecorder.Point(end),
                default
            )
        );
    }

    /// <summary>Adds a cubic segment to the current contour.</summary>
    public void CubicTo(Vector2 firstControl, Vector2 secondControl, Vector2 end)
    {
        RequireContour();
        Add(
            new(
                DrawingPathVerbKind.Cubic,
                DrawingRecorder.Point(firstControl),
                DrawingRecorder.Point(secondControl),
                DrawingRecorder.Point(end)
            )
        );
    }

    /// <summary>Closes the current contour.</summary>
    public void Close()
    {
        RequireContour();
        Add(new(DrawingPathVerbKind.Close, default, default, default));
        _hasContour = false;
    }

    private void Add(DrawingPathVerb verb)
    {
        Check();
        if (_verbs.Count >= DrawingRecorder.MaximumPathVerbs)
            throw new InvalidOperationException(
                $"A drawing path cannot exceed {DrawingRecorder.MaximumPathVerbs} verbs."
            );
        _verbs.Add(verb);
    }

    private void RequireContour()
    {
        Check();
        if (!_hasContour)
            throw new InvalidOperationException("Begin a path contour with MoveTo.");
    }

    private void Check()
    {
        if (!_active)
            throw new InvalidOperationException("The drawing path recorder has expired.");
    }
}

/// <summary>Records one finite immutable drawing command list on a component owner.</summary>
public sealed class DrawingRecorder
{
    /// <summary>Maximum number of replay operations in one recording.</summary>
    public const int MaximumOperations = 4096;

    /// <summary>Maximum total path verbs referenced by one recording.</summary>
    public const int MaximumPathVerbs = 16384;

    /// <summary>Maximum nested clip and transform depth.</summary>
    public const int MaximumScopeDepth = 32;

    /// <summary>Maximum absolute coordinate, stroke width and transform component.</summary>
    public const float MaximumMagnitude = 1_000_000;

    private readonly List<DrawingCommand> _commands = [];
    private readonly Stack<DrawingScope> _scopes = [];
    private int _pathVerbs;
    private bool _active = true;

    internal FrozenDrawing Freeze()
    {
        Check();
        _active = false;
        if (_scopes.Count != 0)
            throw new InvalidOperationException(
                "Every drawing clip and transform must be disposed."
            );
        return new FrozenDrawing(_commands);
    }

    internal void Expire() => _active = false;

    /// <summary>Paints one straight line.</summary>
    public void Line(
        Vector2 start,
        Vector2 end,
        Brush stroke,
        float width,
        DrawingStrokeCap cap = DrawingStrokeCap.Butt
    ) =>
        Add(
            new DrawingLineCommand(Point(start), Point(end), Brush(stroke), Width(width), Cap(cap))
        );

    /// <summary>Paints one ellipse arc using clockwise degrees.</summary>
    public void Arc(
        LayoutRect oval,
        float startDegrees,
        float sweepDegrees,
        Brush stroke,
        float width,
        DrawingStrokeCap cap = DrawingStrokeCap.Butt
    )
    {
        Rect(oval);
        Magnitude(startDegrees, nameof(startDegrees));
        if (!float.IsFinite(sweepDegrees) || sweepDegrees is < -360 or > 360)
            throw new ArgumentOutOfRangeException(
                nameof(sweepDegrees),
                "Arc sweep must be finite and between -360 and 360 degrees."
            );
        Add(
            new DrawingArcCommand(
                oval,
                startDegrees,
                sweepDegrees,
                Brush(stroke),
                Width(width),
                Cap(cap)
            )
        );
    }

    /// <summary>Fills one rectangle.</summary>
    public void FillRectangle(LayoutRect bounds, Brush fill) =>
        Shape(DrawingShapeKind.Rectangle, bounds, 0, fill, true, 0, DrawingStrokeCap.Butt);

    /// <summary>Strokes one rectangle.</summary>
    public void StrokeRectangle(LayoutRect bounds, Brush stroke, float width) =>
        Shape(DrawingShapeKind.Rectangle, bounds, 0, stroke, false, width, DrawingStrokeCap.Butt);

    /// <summary>Fills one rounded rectangle.</summary>
    public void FillRoundedRectangle(LayoutRect bounds, float radius, Brush fill) =>
        Shape(
            DrawingShapeKind.RoundedRectangle,
            bounds,
            Radius(radius),
            fill,
            true,
            0,
            DrawingStrokeCap.Butt
        );

    /// <summary>Strokes one rounded rectangle.</summary>
    public void StrokeRoundedRectangle(
        LayoutRect bounds,
        float radius,
        Brush stroke,
        float width
    ) =>
        Shape(
            DrawingShapeKind.RoundedRectangle,
            bounds,
            Radius(radius),
            stroke,
            false,
            width,
            DrawingStrokeCap.Butt
        );

    /// <summary>Fills one ellipse.</summary>
    public void FillEllipse(LayoutRect bounds, Brush fill) =>
        Shape(DrawingShapeKind.Ellipse, bounds, 0, fill, true, 0, DrawingStrokeCap.Butt);

    /// <summary>Strokes one ellipse.</summary>
    public void StrokeEllipse(
        LayoutRect bounds,
        Brush stroke,
        float width,
        DrawingStrokeCap cap = DrawingStrokeCap.Butt
    ) => Shape(DrawingShapeKind.Ellipse, bounds, 0, stroke, false, width, cap);

    /// <summary>Builds and freezes one bounded path immediately.</summary>
    public DrawingPath Path(Action<DrawingPathRecorder> record)
    {
        Check();
        ArgumentNullException.ThrowIfNull(record);
        var path = new DrawingPathRecorder();
        try
        {
            record(path);
            return new(path.Finish());
        }
        finally
        {
            path.Expire();
        }
    }

    /// <summary>Fills one immutable path.</summary>
    public void FillPath(
        DrawingPath path,
        Brush fill,
        DrawingFillRule fillRule = DrawingFillRule.NonZero
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        Rule(fillRule);
        AddPath(
            path,
            new(
                path,
                Brush(fill),
                true,
                0,
                DrawingStrokeCap.Butt,
                DrawingStrokeJoin.Miter,
                fillRule
            )
        );
    }

    /// <summary>Strokes one immutable path.</summary>
    public void StrokePath(
        DrawingPath path,
        Brush stroke,
        float width,
        DrawingStrokeCap cap = DrawingStrokeCap.Butt,
        DrawingStrokeJoin join = DrawingStrokeJoin.Miter
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        Join(join);
        AddPath(
            path,
            new(path, Brush(stroke), false, Width(width), Cap(cap), join, DrawingFillRule.NonZero)
        );
    }

    /// <summary>Pushes a rectangular clip until the returned scope is disposed.</summary>
    public IDisposable Clip(LayoutRect bounds)
    {
        Rect(bounds);
        return Push(new DrawingPushClipCommand(bounds));
    }

    /// <summary>Pushes an affine transform until the returned scope is disposed.</summary>
    public IDisposable Transform(Matrix3x2 transform)
    {
        Matrix(transform, nameof(transform));
        var composed =
            _scopes.Count == 0
                ? transform
                : Matrix3x2.Multiply(transform, _scopes.Peek().CompositeTransform);
        Matrix(composed, nameof(transform));
        return Push(new DrawingPushTransformCommand(transform), composed);
    }

    private DrawingScope Push(DrawingCommand command, Matrix3x2? transform = null)
    {
        Check();
        if (_scopes.Count >= MaximumScopeDepth)
            throw new InvalidOperationException(
                $"Drawing clip and transform nesting cannot exceed {MaximumScopeDepth}."
            );
        Add(command);
        var composite =
            transform
            ?? (_scopes.Count == 0 ? Matrix3x2.Identity : _scopes.Peek().CompositeTransform);
        var scope = new DrawingScope(this, composite);
        _scopes.Push(scope);
        return scope;
    }

    private void Pop(DrawingScope scope)
    {
        Check();
        if (_scopes.Count == 0 || !ReferenceEquals(_scopes.Peek(), scope))
            throw new InvalidOperationException(
                "Drawing scopes must be disposed in last-in-first-out order."
            );
        _scopes.Pop();
        Add(new DrawingPopCommand());
    }

    private void Shape(
        DrawingShapeKind shape,
        LayoutRect bounds,
        float radius,
        Brush brush,
        bool fill,
        float width,
        DrawingStrokeCap cap
    )
    {
        Rect(bounds);
        Add(
            new DrawingShapeCommand(
                shape,
                bounds,
                radius,
                Brush(brush),
                fill,
                fill ? 0 : Width(width),
                Cap(cap)
            )
        );
    }

    private void AddPath(DrawingPath path, DrawingPathCommand command)
    {
        Check();
        if (_pathVerbs > MaximumPathVerbs - path.Verbs.Count)
            throw new InvalidOperationException(
                $"A drawing cannot replay more than {MaximumPathVerbs} path verbs."
            );
        _pathVerbs += path.Verbs.Count;
        Add(command);
    }

    private void Add(DrawingCommand command)
    {
        Check();
        if (_commands.Count >= MaximumOperations)
            throw new InvalidOperationException(
                $"A drawing cannot exceed {MaximumOperations} operations."
            );
        _commands.Add(command);
    }

    private static Brush Brush(Brush brush) =>
        brush ?? throw new ArgumentNullException(nameof(brush));

    private static float Width(float width)
    {
        if (!float.IsFinite(width) || width <= 0 || width > MaximumMagnitude)
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Stroke width must be finite, positive and at most {MaximumMagnitude}."
            );
        return width;
    }

    private static float Radius(float radius)
    {
        if (!float.IsFinite(radius) || radius < 0 || radius > MaximumMagnitude)
            throw new ArgumentOutOfRangeException(nameof(radius));
        return radius;
    }

    internal static Vector2 Point(Vector2 point)
    {
        Magnitude(point.X, nameof(point));
        Magnitude(point.Y, nameof(point));
        return point;
    }

    private static void Rect(LayoutRect rect)
    {
        Magnitude(rect.X, nameof(rect));
        Magnitude(rect.Y, nameof(rect));
        Magnitude(rect.Width, nameof(rect));
        Magnitude(rect.Height, nameof(rect));
        if (rect.Width < 0 || rect.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(rect));
    }

    private static void Matrix(Matrix3x2 matrix, string name)
    {
        Magnitude(matrix.M11, name);
        Magnitude(matrix.M12, name);
        Magnitude(matrix.M21, name);
        Magnitude(matrix.M22, name);
        Magnitude(matrix.M31, name);
        Magnitude(matrix.M32, name);
    }

    private static void Magnitude(float value, string name)
    {
        if (!float.IsFinite(value) || MathF.Abs(value) > MaximumMagnitude)
            throw new ArgumentOutOfRangeException(name);
    }

    private static DrawingStrokeCap Cap(DrawingStrokeCap cap)
    {
        if (!Enum.IsDefined(cap))
            throw new ArgumentOutOfRangeException(nameof(cap));
        return cap;
    }

    private static void Join(DrawingStrokeJoin join)
    {
        if (!Enum.IsDefined(join))
            throw new ArgumentOutOfRangeException(nameof(join));
    }

    private static void Rule(DrawingFillRule rule)
    {
        if (!Enum.IsDefined(rule))
            throw new ArgumentOutOfRangeException(nameof(rule));
    }

    private void Check()
    {
        if (!_active)
            throw new InvalidOperationException("The drawing recorder has expired.");
    }

    private sealed class DrawingScope(DrawingRecorder owner, Matrix3x2 composite) : IDisposable
    {
        private DrawingRecorder? _owner = owner;

        internal Matrix3x2 CompositeTransform { get; } = composite;

        public void Dispose()
        {
            var current = _owner;
            if (current is null)
                throw new InvalidOperationException("The drawing scope has already been disposed.");
            current.Pop(this);
            _owner = null;
        }
    }
}

/// <summary>Immutable frozen commands consumed by retained renderer replay.</summary>
public sealed class FrozenDrawing
{
    private readonly DrawingCommand[] _commands;
    private readonly IReadOnlyList<DrawingCommand> _view;
    private bool _released;
    private int _releaseCount;

    internal FrozenDrawing(IReadOnlyList<DrawingCommand> commands)
    {
        _commands = [.. commands];
        _view = Array.AsReadOnly(_commands);
    }

    /// <summary>Gets the copied commands while this resource is retained.</summary>
    public IReadOnlyList<DrawingCommand> Commands
    {
        get
        {
            ObjectDisposedException.ThrowIf(_released, this);
            return _view;
        }
    }

    /// <summary>Gets whether every framework lease has released this drawing.</summary>
    public bool IsDisposed
    {
        get { return _released; }
    }

    internal int FinalReleaseCount
    {
        get { return _releaseCount; }
    }

    internal bool ContentEquals(FrozenDrawing other)
    {
        var left = Commands;
        var right = other.Commands;
        if (left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
            if (!CommandEquals(left[index], right[index]))
                return false;
        return true;
    }

    internal void Release()
    {
        if (_released)
            throw new InvalidOperationException("A frozen drawing resource was released twice.");
        _released = true;
        _releaseCount++;
    }

    private static bool CommandEquals(DrawingCommand left, DrawingCommand right) =>
        (left, right) switch
        {
            (DrawingLineCommand a, DrawingLineCommand b) => a.Start == b.Start
                && a.End == b.End
                && Equals(a.Brush, b.Brush)
                && a.Width == b.Width
                && a.Cap == b.Cap,
            (DrawingArcCommand a, DrawingArcCommand b) => a.Oval == b.Oval
                && a.StartDegrees == b.StartDegrees
                && a.SweepDegrees == b.SweepDegrees
                && Equals(a.Brush, b.Brush)
                && a.Width == b.Width
                && a.Cap == b.Cap,
            (DrawingShapeCommand a, DrawingShapeCommand b) => a.Shape == b.Shape
                && a.Bounds == b.Bounds
                && a.CornerRadius == b.CornerRadius
                && Equals(a.Brush, b.Brush)
                && a.Fill == b.Fill
                && a.Width == b.Width
                && a.Cap == b.Cap,
            (DrawingPathCommand a, DrawingPathCommand b) => a.Path.ContentEquals(b.Path)
                && Equals(a.Brush, b.Brush)
                && a.Fill == b.Fill
                && a.Width == b.Width
                && a.Cap == b.Cap
                && a.Join == b.Join
                && a.FillRule == b.FillRule,
            (DrawingPushClipCommand a, DrawingPushClipCommand b) => a.Bounds == b.Bounds,
            (DrawingPushTransformCommand a, DrawingPushTransformCommand b) => a.Transform
                == b.Transform,
            (DrawingPopCommand, DrawingPopCommand) => true,
            _ => false,
        };
}

internal sealed class DrawingResource(FrozenDrawing drawing)
{
    private readonly object _gate = new();
    private int _references = 1;

    internal FrozenDrawing Drawing { get; } = drawing;

    internal void Retain()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_references == 0, this);
            _references = checked(_references + 1);
        }
    }

    internal void Release()
    {
        lock (_gate)
        {
            if (_references == 0)
                return;
            _references--;
            if (_references == 0)
                Drawing.Release();
        }
    }
}

internal sealed class DrawingLease : IDisposable
{
    private DrawingResource? _resource;

    internal DrawingLease(DrawingResource resource) => _resource = resource;

    internal FrozenDrawing Drawing =>
        _resource?.Drawing ?? throw new ObjectDisposedException(nameof(DrawingLease));

    internal DrawingLease Retain()
    {
        var resource = _resource ?? throw new ObjectDisposedException(nameof(DrawingLease));
        resource.Retain();
        return new(resource);
    }

    public void Dispose() => Interlocked.Exchange(ref _resource, null)?.Release();
}
