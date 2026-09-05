using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lucent.Core;

/// <summary>Layout settings for size, direction, spacing, alignment, padding, clipping, and scrolling.</summary>
public static class LayoutProperties
{
    /// <summary>Chooses whether children flow horizontally or vertically. The default is <see cref="LayoutAxis.Column"/>.</summary>
    public static readonly Property<LayoutAxis> Axis = new("layout-axis", LayoutAxis.Column);

    /// <summary>Sets an explicit width. Leave it unset to let the container determine the width.</summary>
    public static readonly Property<float?> Width = new("layout-width", null);

    /// <summary>Sets an explicit height. Leave it unset to let the container determine the height.</summary>
    public static readonly Property<float?> Height = new("layout-height", null);

    /// <summary>Sets the smallest width this element may have.</summary>
    public static readonly Property<float> MinWidth = new("layout-min-width", 0);

    /// <summary>Sets the smallest height this element may have.</summary>
    public static readonly Property<float> MinHeight = new("layout-min-height", 0);

    /// <summary>Sets the largest width this element may have. The default leaves width unrestricted.</summary>
    public static readonly Property<float> MaxWidth = new(
        "layout-max-width",
        float.PositiveInfinity
    );

    /// <summary>Sets the largest height this element may have. The default leaves height unrestricted.</summary>
    public static readonly Property<float> MaxHeight = new(
        "layout-max-height",
        float.PositiveInfinity
    );

    /// <summary>Sets the gap between adjacent children.</summary>
    public static readonly Property<float> Spacing = new("layout-spacing", 0);

    /// <summary>Sets the proportional share of positive unused space this element receives along its parent's main axis.</summary>
    public static readonly Property<float> MainGrow = new("layout-main-grow", 0);

    /// <summary>Places children at the start, center, or end of the direction selected by <see cref="Axis"/>.</summary>
    public static readonly Property<LayoutAlignment> MainAlignment = new(
        "layout-main-alignment",
        LayoutAlignment.Start
    );

    /// <summary>Controls how children fit across the direction selected by <see cref="Axis"/>.</summary>
    public static readonly Property<LayoutAlignment> CrossAlignment = new(
        "layout-cross-alignment",
        LayoutAlignment.Stretch
    );

    /// <summary>Adds space inside the element around its children.</summary>
    public static readonly Property<Insets> Padding = new("layout-padding", Insets.Zero);

    /// <summary>When true, hides content that extends outside this element's bounds.</summary>
    public static readonly Property<bool> Clip = new("layout-clip", false);

    /// <summary>Moves the content horizontally or vertically inside the element.</summary>
    public static readonly Property<ScrollOffset> Scroll = new("layout-scroll", default);
    internal static readonly Property<float?> VirtualRowHeight = new(
        "layout-virtual-row-height",
        null
    );
    internal static readonly Property<int> VirtualItemCount = new("layout-virtual-item-count", 0);
    internal static readonly Property<int> VirtualRowIndex = new("layout-virtual-row-index", 0);
}

/// <summary>Controls retained participation independently of routing-only visibility.</summary>
public enum ElementParticipation
{
    /// <summary>Participates in layout, painting, input, focus, and accessibility.</summary>
    Visible,

    /// <summary>Reserves layout space but omits the subtree from painting, input, focus, and accessibility.</summary>
    Hidden,

    /// <summary>Retains ownership and state but consumes no layout space and is otherwise hidden.</summary>
    Collapsed,
}

/// <summary>Visual settings for an element's background, transparency, and participation.</summary>
public static class VisualProperties
{
    /// <summary>Controls subtree layout, painting, input, focus, and accessibility without disposing its state.</summary>
    public static readonly Property<ElementParticipation> Participation = new(
        "visual-participation",
        ElementParticipation.Visible
    );

    /// <summary>Paints the element's background with the supplied brush.</summary>
    public static readonly Property<Brush> Background = new(
        "visual-background",
        Brush.Solid(default)
    );

    /// <summary>Sets transparency from 0 (invisible) to 1 (fully opaque).</summary>
    public static readonly Property<float> Opacity = new(
        "visual-opacity",
        1,
        transition: TransitionKind.Opacity
    );
}

/// <summary>Text settings inherited by an element's text and its descendants.</summary>
public static class TypographyProperties
{
    /// <summary>Sets the color used to draw text.</summary>
    public static readonly Property<Color> TextColor = new(
        "typography-text-color",
        Color.FromRgb(0, 0, 0),
        inherits: true,
        transition: TransitionKind.Color
    );

    /// <summary>Sets the font family used for text.</summary>
    public static readonly Property<string> FontFamily = new(
        "typography-font-family",
        "Segoe UI",
        inherits: true
    );

    /// <summary>Sets the font size used for text.</summary>
    public static readonly Property<float> FontSize = new(
        "typography-font-size",
        14,
        inherits: true
    );

    /// <summary>Sets the language tag used when laying out text.</summary>
    public static readonly Property<string> Language = new(
        "typography-language",
        "en",
        inherits: true
    );

    /// <summary>Sets whether text flows from left to right or right to left.</summary>
    public static readonly Property<TextDirection> Direction = new(
        "typography-direction",
        global::Lucent.Core.TextDirection.LeftToRight,
        inherits: true
    );
}

internal static class ProjectionProperties
{
    internal static readonly Property<string?> Text = new("projection-text", null);
    internal static readonly Property<string?> TextMeasure = new("projection-text-measure", null);
    internal static readonly Property<int?> TextSelectionStart = new(
        "projection-text-selection-start",
        null
    );
    internal static readonly Property<int?> TextSelectionEnd = new(
        "projection-text-selection-end",
        null
    );
    internal static readonly Property<int?> TextCaret = new("projection-text-caret", null);
}

/// <summary>The direction in which a container places its children.</summary>
public enum LayoutAxis
{
    /// <summary>Places children from left to right.</summary>
    Row,

    /// <summary>Places children from top to bottom.</summary>
    Column,
}

/// <summary>How a child is positioned when its available space is larger than its size.</summary>
public enum LayoutAlignment
{
    /// <summary>Places content at the leading edge.</summary>
    Start,

    /// <summary>Centers content in available space.</summary>
    Center,

    /// <summary>Places content at the trailing edge.</summary>
    End,

    /// <summary>Expands eligible children across available space.</summary>
    Stretch,
}

/// <summary>The direction in which text is read and laid out.</summary>
public enum TextDirection
{
    /// <summary>Lays out text from left to right.</summary>
    LeftToRight,

    /// <summary>Lays out text from right to left.</summary>
    RightToLeft,
}

/// <summary>Space added inside an element on its four edges.</summary>
public readonly struct Insets : IEquatable<Insets>
{
    /// <summary>Creates edge insets. Every value must be finite and nonnegative.</summary>
    public Insets(float left, float top, float right, float bottom)
    {
        if (
            !float.IsFinite(left)
            || !float.IsFinite(top)
            || !float.IsFinite(right)
            || !float.IsFinite(bottom)
            || left < 0
            || top < 0
            || right < 0
            || bottom < 0
        )
            throw new ArgumentOutOfRangeException(
                nameof(left),
                "Insets must be finite and nonnegative."
            );
        Left = left == 0 ? 0 : left;
        Top = top == 0 ? 0 : top;
        Right = right == 0 ? 0 : right;
        Bottom = bottom == 0 ? 0 : bottom;
    }

    /// <summary>Gets the space added along the left edge.</summary>
    public float Left { get; }

    /// <summary>Gets the space added along the top edge.</summary>
    public float Top { get; }

    /// <summary>Gets the space added along the right edge.</summary>
    public float Right { get; }

    /// <summary>Gets the space added along the bottom edge.</summary>
    public float Bottom { get; }

    /// <summary>Gets zero inset on every edge.</summary>
    public static Insets Zero => default;

    /// <summary>Creates equal finite, nonnegative insets on all edges.</summary>
    public static Insets Uniform(float value) => new(value, value, value, value);

    /// <summary>Creates insets with one value for the horizontal edges and one for the vertical edges.</summary>
    public static Insets Symmetric(float horizontal, float vertical) =>
        new(horizontal, vertical, horizontal, vertical);

    /// <summary>Compares the complete value representation for equality.</summary>
    public bool Equals(Insets other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    /// <summary>Compares the complete value representation for equality.</summary>
    public override bool Equals(object? obj) => obj is Insets other && Equals(other);

    /// <summary>Returns a hash code based on the complete value representation.</summary>
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    /// <summary>Compares the complete value representation for equality.</summary>
    public static bool operator ==(Insets left, Insets right) => left.Equals(right);

    /// <summary>Compares the complete value representation for equality.</summary>
    public static bool operator !=(Insets left, Insets right) => !left.Equals(right);

    /// <summary>Returns the canonical diagnostic representation of this value.</summary>
    public override string ToString() =>
        "insets("
        + Format(Left)
        + ","
        + Format(Top)
        + ","
        + Format(Right)
        + ","
        + Format(Bottom)
        + ")";

    internal float Horizontal => Finite(Left + Right);
    internal float Vertical => Finite(Top + Bottom);

    private static float Finite(float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Inset arithmetic overflow.");
        return value;
    }

    private static string Format(float value) =>
        value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>The logical viewport presented to layout. Width and height are logical pixels; scale is device pixels per logical pixel.</summary>
public readonly record struct LayoutViewport(float Width, float Height, float Scale)
{
    /// <summary>Validates the value and throws when its fields are outside the supported contract.</summary>
    public void Validate()
    {
        if (
            !float.IsFinite(Width)
            || !float.IsFinite(Height)
            || !float.IsFinite(Scale)
            || Width <= 0
            || Height <= 0
            || Scale <= 0
        )
            throw new ArgumentOutOfRangeException(
                nameof(LayoutViewport),
                "Viewport and scale must be finite and positive."
            );
    }
}

/// <summary>An immutable logical-pixel scroll offset in the horizontal and vertical axes.</summary>
public readonly record struct ScrollOffset(float X, float Y)
{
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || X < 0 || Y < 0)
            throw new ArgumentOutOfRangeException(nameof(ScrollOffset));
    }
}

/// <summary>An immutable logical-pixel rectangle used consistently for layout, input, and retained scene bounds.</summary>
public readonly record struct LayoutRect(float X, float Y, float Width, float Height)
{
    internal static LayoutRect Round(float x, float y, float width, float height, float scale)
    {
        static float Edge(float value, float deviceScale)
        {
            var scaled = value * deviceScale;
            if (!float.IsFinite(scaled))
                throw new ArgumentOutOfRangeException(nameof(value), "Device rounding overflow.");
            var rounded = MathF.Round(scaled, MidpointRounding.AwayFromZero) / deviceScale;
            if (!float.IsFinite(rounded))
                throw new ArgumentOutOfRangeException(nameof(value), "Device rounding overflow.");
            return rounded;
        }
        if (
            !float.IsFinite(x)
            || !float.IsFinite(y)
            || !float.IsFinite(width)
            || !float.IsFinite(height)
            || width < 0
            || height < 0
        )
            throw new ArgumentOutOfRangeException(nameof(width));
        var left = Edge(x, scale);
        var top = Edge(y, scale);
        var right = Edge(x + width, scale);
        var bottom = Edge(y + height, scale);
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

/// <summary>Composition-local identity for a retained element.</summary>
public readonly record struct ElementIdentity(long CompositionEpoch, long ElementId);

/// <summary>Complete portable text-shaping input; font size and scale must be positive.</summary>
public readonly record struct TextMeasureRequest(
    string Text,
    string FontFamily,
    float FontSize,
    string Language,
    TextDirection Direction,
    float Scale
)
{
    /// <summary>Validates the value and throws when its fields are outside the supported contract.</summary>
    public void Validate()
    {
        if (
            Text is null
            || string.IsNullOrWhiteSpace(FontFamily)
            || string.IsNullOrWhiteSpace(Language)
            || !float.IsFinite(FontSize)
            || FontSize <= 0
            || !float.IsFinite(Scale)
            || Scale <= 0
            || !Enum.IsDefined(Direction)
        )
            throw new ArgumentException(
                "Text requests require finite font/scale and explicit language/direction."
            );
    }
}

/// <summary>One shaped glyph with logical-pixel position, advance, and offsets.</summary>
public readonly record struct ShapedGlyph(
    uint GlyphId,
    uint Cluster,
    float X,
    float Y,
    float XAdvance,
    float XOffset,
    float YOffset
);

/// <summary>One immutable face/direction run. Positions are the measured HarfBuzz positions painted by every renderer.</summary>
public sealed class ShapedRun
{
    private readonly IReadOnlyList<ShapedGlyph> _glyphs;

    /// <summary>Initializes an immutable validated shaped-font run in logical pixels.</summary>
    public ShapedRun(
        string identity,
        string family,
        int weight,
        int width,
        int slant,
        string fingerprint,
        int collectionIndex,
        string sourceIdentity,
        TextDirection direction,
        string language,
        float fontSize,
        float originX,
        float baseline,
        float ascent,
        float descent,
        float runWidth,
        IReadOnlyList<ShapedGlyph> glyphs
    )
    {
        if (
            string.IsNullOrWhiteSpace(identity)
            || string.IsNullOrWhiteSpace(family)
            || string.IsNullOrWhiteSpace(fingerprint)
            || string.IsNullOrWhiteSpace(sourceIdentity)
            || collectionIndex < 0
            || string.IsNullOrWhiteSpace(language)
            || !Enum.IsDefined(direction)
            || !float.IsFinite(fontSize)
            || fontSize <= 0
            || !float.IsFinite(originX)
            || !float.IsFinite(baseline)
            || !float.IsFinite(ascent)
            || !float.IsFinite(descent)
            || !float.IsFinite(runWidth)
            || runWidth < 0
            || ascent > descent
        )
            throw new ArgumentException("Shaped run fields must be finite and explicit.");
        ArgumentNullException.ThrowIfNull(glyphs);
        var copy = glyphs.ToArray();
        if (
            copy.Length == 0
            || copy.Any(glyph =>
                glyph.GlyphId == 0
                || glyph.GlyphId > ushort.MaxValue
                || !float.IsFinite(glyph.X)
                || !float.IsFinite(glyph.Y)
                || !float.IsFinite(glyph.XAdvance)
                || !float.IsFinite(glyph.XOffset)
                || !float.IsFinite(glyph.YOffset)
            )
            || MathF.Abs(copy.Sum(glyph => glyph.XAdvance) - runWidth) > .001f
        )
            throw new ArgumentException(
                "A shaped run requires finite non-missing glyphs.",
                nameof(glyphs)
            );
        Identity = identity;
        Family = family;
        Weight = weight;
        Width = width;
        Slant = slant;
        Fingerprint = fingerprint;
        CollectionIndex = collectionIndex;
        SourceIdentity = sourceIdentity;
        Direction = direction;
        Language = language;
        FontSize = fontSize;
        OriginX = originX;
        Baseline = baseline;
        Ascent = ascent;
        Descent = descent;
        RunWidth = runWidth;
        _glyphs = Array.AsReadOnly(copy);
    }

    /// <summary>Gets the stable identity supplied by the producer.</summary>
    public string Identity { get; }

    /// <summary>Gets the resolved font family.</summary>
    public string Family { get; }

    /// <summary>Gets the resolved font weight.</summary>
    public int Weight { get; }

    /// <summary>Gets the logical-pixel width.</summary>
    public int Width { get; }

    /// <summary>Gets the resolved font slant.</summary>
    public int Slant { get; }

    /// <summary>Gets the shaper-provided font fingerprint.</summary>
    public string Fingerprint { get; }

    /// <summary>Gets the face index in the font collection.</summary>
    public int CollectionIndex { get; }

    /// <summary>Gets the shaper-provided font source identity.</summary>
    public string SourceIdentity { get; }

    /// <summary>Gets the family, weight, width, and slant diagnostic identity.</summary>
    public string TypefaceIdentity =>
        Family
        + ":"
        + Weight.ToString(CultureInfo.InvariantCulture)
        + ":"
        + Width.ToString(CultureInfo.InvariantCulture)
        + ":"
        + Slant.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the shaping direction used for this run.</summary>
    public TextDirection Direction { get; }

    /// <summary>Gets the language tag used for shaping.</summary>
    public string Language { get; }

    /// <summary>Gets the positive logical-pixel font size.</summary>
    public float FontSize { get; }

    /// <summary>Gets the logical-pixel horizontal run origin.</summary>
    public float OriginX { get; }

    /// <summary>Gets the logical-pixel baseline.</summary>
    public float Baseline { get; }

    /// <summary>Gets the logical-pixel ascent.</summary>
    public float Ascent { get; }

    /// <summary>Gets the logical-pixel descent.</summary>
    public float Descent { get; }

    /// <summary>Gets the nonnegative logical-pixel advance width.</summary>
    public float RunWidth { get; }

    /// <summary>Gets the immutable ordered glyph sequence.</summary>
    public IReadOnlyList<ShapedGlyph> Glyphs => _glyphs;
}

/// <summary>An immutable text-shaping result shared by layout, scene projection, and renderer adapters.</summary>
public sealed class ShapedText
{
    private readonly IReadOnlyList<ShapedRun> _runs;

    /// <summary>Initializes immutable shaped text metrics and ordered runs.</summary>
    public ShapedText(string identity, float width, float height, IReadOnlyList<ShapedRun> runs)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(runs);
        var copy = runs.ToArray();
        if (
            string.IsNullOrWhiteSpace(identity)
            || !float.IsFinite(width)
            || !float.IsFinite(height)
            || width < 0
            || height < 0
            || copy.Any(run => run is null)
        )
            throw new ArgumentException("Shaped text must be finite and immutable.");
        Identity = identity;
        Width = width;
        Height = height;
        _runs = Array.AsReadOnly(copy);
    }

    /// <summary>Gets the stable identity supplied by the producer.</summary>
    public string Identity { get; }

    /// <summary>Gets the logical-pixel width.</summary>
    public float Width { get; }

    /// <summary>Gets the logical-pixel height.</summary>
    public float Height { get; }

    /// <summary>Gets the immutable ordered shaped runs.</summary>
    public IReadOnlyList<ShapedRun> Runs => _runs;

    /// <summary>Validates the value and throws when its fields are outside the supported contract.</summary>
    public void Validate(TextMeasureRequest request)
    {
        if (
            request.Text.Length == 0
                ? Runs.Count != 0 || Width != 0 || Height != 0
                : Runs.Count == 0
        )
            throw new InvalidOperationException("The text shaper returned an invalid run set.");
        if (Runs.Count == 0)
            return;
        var first = Runs[0];
        if (
            MathF.Abs(first.OriginX) > .001f
            || Runs.Any(run =>
                run.Language != request.Language
                || run.FontSize != request.FontSize
                || MathF.Abs(run.Baseline - first.Baseline) > .001f
                || MathF.Abs(run.Ascent - first.Ascent) > .001f
                || MathF.Abs(run.Descent - first.Descent) > .001f
                || !float.IsFinite(run.OriginX + run.RunWidth)
            )
            || MathF.Abs(first.Baseline + first.Ascent) > .001f
            || MathF.Abs(first.Descent - first.Ascent - Height) > .001f
        )
            throw new InvalidOperationException("The shaped text metrics are inconsistent.");
        var end = 0f;
        foreach (var run in Runs)
        {
            if (MathF.Abs(run.OriginX - end) > .001f)
                throw new InvalidOperationException("The shaped run origins are not contiguous.");
            var cursor = 0f;
            uint? priorCluster = null;
            foreach (var glyph in run.Glyphs)
            {
                if (
                    glyph.Cluster >= request.Text.Length
                    || MathF.Abs(glyph.X - (cursor + glyph.XOffset)) > .001f
                    || MathF.Abs(glyph.Y + glyph.YOffset) > .001f
                )
                    throw new InvalidOperationException(
                        "The shaped glyph positions are inconsistent."
                    );
                if (
                    priorCluster is { } prior
                    && (
                        run.Direction == TextDirection.LeftToRight
                            ? glyph.Cluster < prior
                            : glyph.Cluster > prior
                    )
                )
                    throw new InvalidOperationException(
                        "The shaped glyph cluster order is inconsistent with its run direction."
                    );
                priorCluster = glyph.Cluster;
                cursor += glyph.XAdvance;
            }
            if (MathF.Abs(cursor - run.RunWidth) > .001f)
                throw new InvalidOperationException(
                    "The shaped run advances do not match its width."
                );
            end += run.RunWidth;
            if (!float.IsFinite(end))
                throw new InvalidOperationException("The shaped run origins overflow.");
        }
        if (MathF.Abs(end - Width) > .001f)
            throw new InvalidOperationException("The shaped text width does not match its runs.");
    }
}

/// <summary>Implemented by the renderer; Core owns only this portable request/result contract.</summary>
public interface ITextShaper
{
    /// <summary>Shapes a validated request into an immutable result whose metrics and glyph coordinates are logical pixels.</summary>
    ShapedText Shape(TextMeasureRequest request);
}

/// <summary>Laid-out bounds and optional shaped text for one retained element.</summary>
public readonly record struct LayoutBox(
    ElementIdentity Identity,
    LayoutRect Bounds,
    ShapedText? Text
);

/// <summary>Immutable input projection; it contains no platform event or application values.</summary>
public readonly record struct RetainedInputElement(
    ElementIdentity Identity,
    ElementIdentity? Parent,
    LayoutRect Bounds,
    LayoutRect? ChildClipBounds,
    int Order,
    bool Enabled,
    bool Visible,
    string Signature
);

/// <summary>The finite renderer operation represented by a retained scene node.</summary>
public enum SceneNodeKind
{
    /// <summary>Paints a box-local brush.</summary>
    Paint,

    /// <summary>Paints shaped text.</summary>
    Text,

    /// <summary>Paints a selection highlight.</summary>
    Selection,

    /// <summary>Paints a text caret.</summary>
    Caret,

    /// <summary>Clips a child scene group.</summary>
    Clip,

    /// <summary>Composites a child group with opacity.</summary>
    Opacity,
}

/// <summary>Identity of a renderer operation belonging to a retained element.</summary>
public readonly record struct SceneNodeIdentity(ElementIdentity Element, SceneNodeKind Kind);

/// <summary>The portable renderer-facing base for one retained scene operation.</summary>
public abstract class SceneNode(SceneNodeIdentity identity, LayoutRect bounds)
{
    /// <summary>Gets the stable identity supplied by the producer.</summary>
    public SceneNodeIdentity Identity { get; } = identity;

    /// <summary>Gets the logical-pixel bounds of this scene operation.</summary>
    public LayoutRect Bounds { get; } = bounds;
}

/// <summary>A box-local brush paint operation in a retained scene.</summary>
public sealed class PaintSceneNode(SceneNodeIdentity identity, LayoutRect bounds, Brush brush)
    : SceneNode(identity, bounds)
{
    /// <summary>Gets the box-local brush to paint.</summary>
    public Brush Brush { get; } = brush ?? throw new ArgumentNullException(nameof(brush));
}

/// <summary>A shaped text paint operation in a retained scene.</summary>
public sealed class TextSceneNode(
    SceneNodeIdentity identity,
    LayoutRect bounds,
    Color color,
    ShapedText text
) : SceneNode(identity, bounds)
{
    /// <summary>Gets the text color used for painting.</summary>
    public Color Color { get; } = color;

    /// <summary>Gets the shaped text to paint.</summary>
    public ShapedText Text { get; } = text ?? throw new ArgumentNullException(nameof(text));
}

/// <summary>A retained group clipped once to its bounds before its children are painted.</summary>
public sealed class ClipSceneNode : SceneNode
{
    private readonly IReadOnlyList<SceneNode> _children;

    /// <summary>Initializes an immutable clipped scene group with copied children.</summary>
    public ClipSceneNode(
        SceneNodeIdentity identity,
        LayoutRect bounds,
        IReadOnlyList<SceneNode> children
    )
        : base(identity, bounds)
    {
        _children = Array.AsReadOnly(
            children?.Select(Clone).ToArray() ?? throw new ArgumentNullException(nameof(children))
        );
    }

    /// <summary>Gets the immutable copied child scene nodes.</summary>
    public IReadOnlyList<SceneNode> Children => _children;

    internal static SceneNode Clone(SceneNode node) =>
        node switch
        {
            PaintSceneNode paint => new PaintSceneNode(paint.Identity, paint.Bounds, paint.Brush),
            TextSceneNode text => new TextSceneNode(
                text.Identity,
                text.Bounds,
                text.Color,
                text.Text
            ),
            ClipSceneNode clip => new ClipSceneNode(clip.Identity, clip.Bounds, clip.Children),
            OpacitySceneNode opacity => new OpacitySceneNode(
                opacity.Identity,
                opacity.Bounds,
                opacity.Opacity,
                opacity.Children
            ),
            _ => throw new ArgumentException("Unknown scene node."),
        };
}

/// <summary>An immutable compositing group; its opacity applies once after all children have painted.</summary>
public sealed class OpacitySceneNode : SceneNode
{
    private readonly IReadOnlyList<SceneNode> _children;

    /// <summary>Initializes an immutable opacity group; opacity must be in [0,1].</summary>
    public OpacitySceneNode(
        SceneNodeIdentity identity,
        LayoutRect bounds,
        float opacity,
        IReadOnlyList<SceneNode> children
    )
        : base(identity, bounds)
    {
        if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1)
            throw new ArgumentOutOfRangeException(
                nameof(opacity),
                "Opacity must be finite and in [0,1]."
            );
        Opacity = opacity;
        _children = Array.AsReadOnly(
            children?.Select(ClipSceneNode.Clone).ToArray()
                ?? throw new ArgumentNullException(nameof(children))
        );
    }

    /// <summary>Gets the group opacity in the inclusive zero-to-one range.</summary>
    public float Opacity { get; }

    /// <summary>Gets the immutable copied child scene nodes.</summary>
    public IReadOnlyList<SceneNode> Children => _children;
}

/// <summary>A renderer-facing retained snapshot. It owns no platform or renderer resources.</summary>
public sealed class RetainedScene
{
    internal RetainedScene(
        long generation,
        LayoutViewport viewport,
        IReadOnlyList<LayoutBox> boxes,
        IReadOnlyList<SceneNode> nodes,
        IReadOnlyList<RetainedInputElement> input,
        long inputProjectionRevision = 0,
        IReadOnlyCollection<long>? collapsedElementIds = null
    )
    {
        Generation = generation;
        Viewport = viewport;
        Boxes = Array.AsReadOnly(boxes.ToArray());
        Nodes = Array.AsReadOnly(nodes.Select(ClipSceneNode.Clone).ToArray());
        Input = Array.AsReadOnly(input.ToArray());
        InputProjectionRevision = inputProjectionRevision;
        _collapsedElementIds = collapsedElementIds is null ? [] : [.. collapsedElementIds];
        InputSignature = Signature(Input);
    }

    /// <summary>Monotonic composition-local identity; routers reject older snapshots.</summary>
    public long Generation { get; }

    /// <summary>Gets the logical viewport used to produce the scene.</summary>
    public LayoutViewport Viewport { get; }

    /// <summary>Gets the immutable layout boxes for the scene generation.</summary>
    public IReadOnlyList<LayoutBox> Boxes { get; }

    /// <summary>Gets the immutable renderer operations for the scene generation.</summary>
    public IReadOnlyList<SceneNode> Nodes { get; }

    /// <summary>Retained hit/focus metadata matched to this scene generation.</summary>
    public IReadOnlyList<RetainedInputElement> Input { get; }

    /// <summary>Gets the composition projection revision captured with this scene.</summary>
    internal long InputProjectionRevision { get; }
    private readonly HashSet<long> _collapsedElementIds;

    internal bool IsCollapsed(long elementId) => _collapsedElementIds.Contains(elementId);

    /// <summary>Gets the deterministic signature of the scene input projection.</summary>
    public string InputSignature { get; }

    /// <summary>Returns a deterministic, value-free snapshot suitable for diagnostics and contract comparison.</summary>
    public string Dump()
    {
        var output = new StringBuilder("scene generation=")
            .Append(Generation.ToString(CultureInfo.InvariantCulture))
            .Append(" inputSignature=")
            .Append(InputSignature)
            .Append(" viewport=")
            .Append(Format(new LayoutRect(0, 0, Viewport.Width, Viewport.Height)))
            .Append(" scale=")
            .Append(Viewport.Scale.ToString("R", CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (var input in Input.OrderBy(item => item.Identity.ElementId))
            output
                .Append("input epoch=")
                .Append(input.Identity.CompositionEpoch.ToString(CultureInfo.InvariantCulture))
                .Append(" element=")
                .Append(input.Identity.ElementId.ToString(CultureInfo.InvariantCulture))
                .Append(" parent=")
                .Append(input.Parent?.ElementId.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(" order=")
                .Append(input.Order.ToString(CultureInfo.InvariantCulture))
                .Append(" bounds=")
                .Append(Format(input.Bounds))
                .Append(" childClip=")
                .Append(input.ChildClipBounds is { } clip ? Format(clip) : "-")
                .Append(" enabled=")
                .Append(input.Enabled ? "true" : "false")
                .Append(" visible=")
                .Append(input.Visible ? "true" : "false")
                .Append(" signature=")
                .Append(input.Signature)
                .Append('\n');
        foreach (var box in Boxes.OrderBy(box => box.Identity.ElementId))
        {
            output
                .Append("layout epoch=")
                .Append(box.Identity.CompositionEpoch.ToString(CultureInfo.InvariantCulture))
                .Append(" element=")
                .Append(box.Identity.ElementId.ToString(CultureInfo.InvariantCulture))
                .Append(" bounds=")
                .Append(Format(box.Bounds));
            if (box.Text is not null)
                foreach (var run in box.Text.Runs)
                    output
                        .Append(" shape=")
                        .Append(DiagnosticText.Quote(run.Identity))
                        .Append(" face=")
                        .Append(DiagnosticText.Quote(run.TypefaceIdentity))
                        .Append(" fingerprint=")
                        .Append(DiagnosticText.Quote(run.Fingerprint))
                        .Append(" collection=")
                        .Append(run.CollectionIndex.ToString(CultureInfo.InvariantCulture))
                        .Append(" source=")
                        .Append(DiagnosticText.Quote(run.SourceIdentity))
                        .Append(" origin=")
                        .Append(run.OriginX.ToString("R", CultureInfo.InvariantCulture))
                        .Append(" width=")
                        .Append(run.RunWidth.ToString("R", CultureInfo.InvariantCulture))
                        .Append(" fontSize=")
                        .Append(run.FontSize.ToString("R", CultureInfo.InvariantCulture))
                        .Append(" direction=")
                        .Append(run.Direction)
                        .Append(" language=")
                        .Append(DiagnosticText.Quote(run.Language))
                        .Append(" baseline=")
                        .Append(run.Baseline.ToString("R", CultureInfo.InvariantCulture))
                        .Append(" ascent=")
                        .Append(run.Ascent.ToString("R", CultureInfo.InvariantCulture))
                        .Append(" descent=")
                        .Append(run.Descent.ToString("R", CultureInfo.InvariantCulture))
                        .Append(" glyphs=[")
                        .Append(
                            string.Join(
                                ',',
                                run.Glyphs.Select(glyph =>
                                    glyph.GlyphId.ToString(CultureInfo.InvariantCulture)
                                    + ":"
                                    + glyph.Cluster.ToString(CultureInfo.InvariantCulture)
                                    + ":"
                                    + glyph.X.ToString("R", CultureInfo.InvariantCulture)
                                    + ":"
                                    + glyph.Y.ToString("R", CultureInfo.InvariantCulture)
                                    + ":"
                                    + glyph.XAdvance.ToString("R", CultureInfo.InvariantCulture)
                                    + ":"
                                    + glyph.XOffset.ToString("R", CultureInfo.InvariantCulture)
                                    + ":"
                                    + glyph.YOffset.ToString("R", CultureInfo.InvariantCulture)
                                )
                            )
                        )
                        .Append(']');
            output.Append('\n');
        }
        Append(Nodes, output, 0, null);
        return output.ToString();
    }

    private static void Append(
        IEnumerable<SceneNode> nodes,
        StringBuilder output,
        int depth,
        long? parent
    )
    {
        foreach (var node in nodes)
        {
            output
                .Append("node epoch=")
                .Append(
                    node.Identity.Element.CompositionEpoch.ToString(CultureInfo.InvariantCulture)
                )
                .Append(" element=")
                .Append(node.Identity.Element.ElementId.ToString(CultureInfo.InvariantCulture))
                .Append(" parent=")
                .Append(parent?.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(" depth=")
                .Append(depth.ToString(CultureInfo.InvariantCulture))
                .Append(" kind=")
                .Append(node.Identity.Kind)
                .Append(" bounds=")
                .Append(Format(node.Bounds));
            if (node is PaintSceneNode paint)
                output.Append(" brush=").Append(paint.Brush);
            if (node is TextSceneNode text)
                output
                    .Append(" color=")
                    .Append(text.Color)
                    .Append(" shape=")
                    .Append(DiagnosticText.Quote(text.Text.Identity));
            if (node is OpacitySceneNode opacity)
                output
                    .Append(" opacity=")
                    .Append(opacity.Opacity.ToString("R", CultureInfo.InvariantCulture));
            output.Append('\n');
            if (node is ClipSceneNode clip)
                Append(clip.Children, output, depth + 1, node.Identity.Element.ElementId);
            else if (node is OpacitySceneNode group)
                Append(group.Children, output, depth + 1, node.Identity.Element.ElementId);
        }
    }

    private static string Format(LayoutRect value) =>
        "["
        + value.X.ToString("R", CultureInfo.InvariantCulture)
        + ","
        + value.Y.ToString("R", CultureInfo.InvariantCulture)
        + ","
        + value.Width.ToString("R", CultureInfo.InvariantCulture)
        + ","
        + value.Height.ToString("R", CultureInfo.InvariantCulture)
        + "]";

    private static string Signature(IEnumerable<RetainedInputElement> input) =>
        Hash(writer =>
        {
            var copy = input.OrderBy(item => item.Order).ToArray();
            writer.Write(copy.Length);
            foreach (var item in copy)
            {
                writer.Write(item.Identity.CompositionEpoch);
                writer.Write(item.Identity.ElementId);
                writer.Write(item.Parent.HasValue);
                if (item.Parent is { } parent)
                {
                    writer.Write(parent.CompositionEpoch);
                    writer.Write(parent.ElementId);
                }
                writer.Write(item.Bounds.X);
                writer.Write(item.Bounds.Y);
                writer.Write(item.Bounds.Width);
                writer.Write(item.Bounds.Height);
                writer.Write(item.ChildClipBounds.HasValue);
                if (item.ChildClipBounds is { } clip)
                {
                    writer.Write(clip.X);
                    writer.Write(clip.Y);
                    writer.Write(clip.Width);
                    writer.Write(clip.Height);
                }
                writer.Write(item.Order);
                writer.Write(item.Enabled);
                writer.Write(item.Visible);
                writer.Write(item.Signature);
            }
        });

    private static string Hash(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
