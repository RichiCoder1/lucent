using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lucent.Core;

/// <summary>The finite arrangement values resolved by the existing typed presentation model.</summary>
public static class LayoutProperties
{
    public static readonly Property<LayoutAxis> Axis = new("layout-axis", LayoutAxis.Column);
    public static readonly Property<float?> Width = new("layout-width", null);
    public static readonly Property<float?> Height = new("layout-height", null);
    public static readonly Property<float> MinWidth = new("layout-min-width", 0);
    public static readonly Property<float> MinHeight = new("layout-min-height", 0);
    public static readonly Property<float> MaxWidth = new("layout-max-width", float.PositiveInfinity);
    public static readonly Property<float> MaxHeight = new("layout-max-height", float.PositiveInfinity);
    public static readonly Property<float> Spacing = new("layout-spacing", 0);
    public static readonly Property<LayoutAlignment> MainAlignment = new("layout-main-alignment", LayoutAlignment.Start);
    public static readonly Property<LayoutAlignment> CrossAlignment = new("layout-cross-alignment", LayoutAlignment.Stretch);
    public static readonly Property<bool> Clip = new("layout-clip", false);
    public static readonly Property<ScrollOffset> Scroll = new("layout-scroll", default);
    internal static readonly Property<float?> VirtualRowHeight = new("layout-virtual-row-height", null);
    internal static readonly Property<int> VirtualItemCount = new("layout-virtual-item-count", 0);
    internal static readonly Property<int> VirtualRowIndex = new("layout-virtual-row-index", 0);
}

/// <summary>The minimal renderer-facing visual values; they are ordinary typed properties, not a second style model.</summary>
public static class VisualProperties
{
    public static readonly Property<Brush> Background = new("visual-background", Brush.Solid(default));
}

public static class TypographyProperties
{
    public static readonly Property<Color> TextColor = new("typography-text-color", Color.FromRgb(0, 0, 0), inherits: true, transition: TransitionKind.Color);
    public static readonly Property<string> FontFamily = new("typography-font-family", "Segoe UI", inherits: true);
    public static readonly Property<float> FontSize = new("typography-font-size", 14, inherits: true);
    public static readonly Property<string> Language = new("typography-language", "en", inherits: true);
    public static readonly Property<TextDirection> Direction = new("typography-direction", global::Lucent.Core.TextDirection.LeftToRight, inherits: true);
}

internal static class ProjectionProperties
{
    internal static readonly Property<string?> Text = new("projection-text", null);
    internal static readonly Property<int?> TextSelectionStart = new("projection-text-selection-start", null);
    internal static readonly Property<int?> TextSelectionEnd = new("projection-text-selection-end", null);
    internal static readonly Property<int?> TextCaret = new("projection-text-caret", null);
}

public enum LayoutAxis { Row, Column }
public enum LayoutAlignment { Start, Center, End, Stretch }
public enum TextDirection { LeftToRight, RightToLeft }

public readonly record struct LayoutViewport(float Width, float Height, float Scale)
{
    public void Validate()
    {
        if (!float.IsFinite(Width) || !float.IsFinite(Height) || !float.IsFinite(Scale) || Width <= 0 || Height <= 0 || Scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(LayoutViewport), "Viewport and scale must be finite and positive.");
    }
}

public readonly record struct ScrollOffset(float X, float Y)
{
    internal void Validate() { if (!float.IsFinite(X) || !float.IsFinite(Y) || X < 0 || Y < 0) throw new ArgumentOutOfRangeException(nameof(ScrollOffset)); }
}

public readonly record struct LayoutRect(float X, float Y, float Width, float Height)
{
    internal static LayoutRect Round(float x, float y, float width, float height, float scale)
    {
        static float Edge(float value, float deviceScale) { var scaled = value * deviceScale; if (!float.IsFinite(scaled)) throw new ArgumentOutOfRangeException(nameof(value), "Device rounding overflow."); var rounded = MathF.Round(scaled, MidpointRounding.AwayFromZero) / deviceScale; if (!float.IsFinite(rounded)) throw new ArgumentOutOfRangeException(nameof(value), "Device rounding overflow."); return rounded; }
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height) || width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
        var left = Edge(x, scale); var top = Edge(y, scale); var right = Edge(x + width, scale); var bottom = Edge(y + height, scale);
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

public readonly record struct ElementIdentity(long CompositionEpoch, long ElementId);
public readonly record struct TextMeasureRequest(string Text, string FontFamily, float FontSize, string Language, TextDirection Direction, float Scale)
{
    public void Validate()
    {
        if (Text is null || string.IsNullOrWhiteSpace(FontFamily) || string.IsNullOrWhiteSpace(Language) || !float.IsFinite(FontSize) || FontSize <= 0 || !float.IsFinite(Scale) || Scale <= 0 || !Enum.IsDefined(Direction))
            throw new ArgumentException("Text requests require finite font/scale and explicit language/direction.");
    }
}

public readonly record struct ShapedGlyph(uint GlyphId, uint Cluster, float X, float Y, float XAdvance, float XOffset, float YOffset);

/// <summary>One immutable face/direction run. Positions are the measured HarfBuzz positions painted by every renderer.</summary>
public sealed class ShapedRun
{
    private readonly IReadOnlyList<ShapedGlyph> _glyphs;
    public ShapedRun(string identity, string family, int weight, int width, int slant, string fingerprint, int collectionIndex, string sourceIdentity, TextDirection direction, string language, float fontSize, float originX, float baseline, float ascent, float descent, float runWidth, IReadOnlyList<ShapedGlyph> glyphs)
    {
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(fingerprint) || string.IsNullOrWhiteSpace(sourceIdentity) || collectionIndex < 0 || string.IsNullOrWhiteSpace(language) || !Enum.IsDefined(direction) || !float.IsFinite(fontSize) || fontSize <= 0 || !float.IsFinite(originX) || !float.IsFinite(baseline) || !float.IsFinite(ascent) || !float.IsFinite(descent) || !float.IsFinite(runWidth) || runWidth < 0 || ascent > descent)
            throw new ArgumentException("Shaped run fields must be finite and explicit.");
        ArgumentNullException.ThrowIfNull(glyphs);
        var copy = glyphs.ToArray();
        if (copy.Length == 0 || copy.Any(glyph => glyph.GlyphId == 0 || glyph.GlyphId > ushort.MaxValue || !float.IsFinite(glyph.X) || !float.IsFinite(glyph.Y) || !float.IsFinite(glyph.XAdvance) || !float.IsFinite(glyph.XOffset) || !float.IsFinite(glyph.YOffset)) || MathF.Abs(copy.Sum(glyph => glyph.XAdvance) - runWidth) > .001f)
            throw new ArgumentException("A shaped run requires finite non-missing glyphs.", nameof(glyphs));
        Identity = identity; Family = family; Weight = weight; Width = width; Slant = slant; Fingerprint = fingerprint; CollectionIndex = collectionIndex; SourceIdentity = sourceIdentity; Direction = direction; Language = language; FontSize = fontSize; OriginX = originX; Baseline = baseline; Ascent = ascent; Descent = descent; RunWidth = runWidth;
        _glyphs = Array.AsReadOnly(copy);
    }
    public string Identity { get; } public string Family { get; } public int Weight { get; } public int Width { get; } public int Slant { get; } public string Fingerprint { get; } public int CollectionIndex { get; } public string SourceIdentity { get; }
    public string TypefaceIdentity => Family + ":" + Weight.ToString(CultureInfo.InvariantCulture) + ":" + Width.ToString(CultureInfo.InvariantCulture) + ":" + Slant.ToString(CultureInfo.InvariantCulture);
    public TextDirection Direction { get; } public string Language { get; } public float FontSize { get; } public float OriginX { get; } public float Baseline { get; } public float Ascent { get; } public float Descent { get; } public float RunWidth { get; }
    public IReadOnlyList<ShapedGlyph> Glyphs => _glyphs;
}

public sealed class ShapedText
{
    private readonly IReadOnlyList<ShapedRun> _runs;
    public ShapedText(string identity, float width, float height, IReadOnlyList<ShapedRun> runs)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(runs);
        var copy = runs.ToArray();
        if (string.IsNullOrWhiteSpace(identity) || !float.IsFinite(width) || !float.IsFinite(height) || width < 0 || height < 0 || copy.Any(run => run is null)) throw new ArgumentException("Shaped text must be finite and immutable.");
        Identity = identity; Width = width; Height = height; _runs = Array.AsReadOnly(copy);
    }
    public string Identity { get; } public float Width { get; } public float Height { get; } public IReadOnlyList<ShapedRun> Runs => _runs;
    public void Validate(TextMeasureRequest request)
    {
        if (request.Text.Length == 0 ? Runs.Count != 0 || Width != 0 || Height != 0 : Runs.Count == 0) throw new InvalidOperationException("The text shaper returned an invalid run set.");
        if (Runs.Count == 0) return;
        var first = Runs[0];
        if (MathF.Abs(first.OriginX) > .001f || Runs.Any(run => run.Language != request.Language || run.FontSize != request.FontSize || MathF.Abs(run.Baseline - first.Baseline) > .001f || MathF.Abs(run.Ascent - first.Ascent) > .001f || MathF.Abs(run.Descent - first.Descent) > .001f || !float.IsFinite(run.OriginX + run.RunWidth)) || MathF.Abs(first.Baseline + first.Ascent) > .001f || MathF.Abs(first.Descent - first.Ascent - Height) > .001f)
            throw new InvalidOperationException("The shaped text metrics are inconsistent.");
        var end = 0f;
        foreach (var run in Runs)
        {
            if (MathF.Abs(run.OriginX - end) > .001f) throw new InvalidOperationException("The shaped run origins are not contiguous.");
            var cursor = 0f; uint? priorCluster = null;
            foreach (var glyph in run.Glyphs)
            {
                if (glyph.Cluster >= request.Text.Length || MathF.Abs(glyph.X - (cursor + glyph.XOffset)) > .001f || MathF.Abs(glyph.Y + glyph.YOffset) > .001f) throw new InvalidOperationException("The shaped glyph positions are inconsistent.");
                if (priorCluster is { } prior && (run.Direction == TextDirection.LeftToRight ? glyph.Cluster < prior : glyph.Cluster > prior)) throw new InvalidOperationException("The shaped glyph cluster order is inconsistent with its run direction.");
                priorCluster = glyph.Cluster; cursor += glyph.XAdvance;
            }
            if (MathF.Abs(cursor - run.RunWidth) > .001f) throw new InvalidOperationException("The shaped run advances do not match its width.");
            end += run.RunWidth;
            if (!float.IsFinite(end)) throw new InvalidOperationException("The shaped run origins overflow.");
        }
        if (MathF.Abs(end - Width) > .001f) throw new InvalidOperationException("The shaped text width does not match its runs.");
    }
}

/// <summary>Implemented by the renderer; Core owns only this portable request/result contract.</summary>
public interface ITextShaper { ShapedText Shape(TextMeasureRequest request); }

public readonly record struct LayoutBox(ElementIdentity Identity, LayoutRect Bounds, ShapedText? Text);
/// <summary>Immutable input projection; it contains no platform event or application values.</summary>
public readonly record struct RetainedInputElement(ElementIdentity Identity, ElementIdentity? Parent, LayoutRect Bounds, int Order, bool Clip, bool Enabled, bool Visible, string Signature);
public enum SceneNodeKind { Paint, Text, Selection, Caret, Clip }
public readonly record struct SceneNodeIdentity(ElementIdentity Element, SceneNodeKind Kind);
public abstract class SceneNode(SceneNodeIdentity identity, LayoutRect bounds) { public SceneNodeIdentity Identity { get; } = identity; public LayoutRect Bounds { get; } = bounds; }
public sealed class PaintSceneNode(SceneNodeIdentity identity, LayoutRect bounds, Brush brush) : SceneNode(identity, bounds) { public Brush Brush { get; } = brush ?? throw new ArgumentNullException(nameof(brush)); }
public sealed class TextSceneNode(SceneNodeIdentity identity, LayoutRect bounds, Color color, ShapedText text) : SceneNode(identity, bounds) { public Color Color { get; } = color; public ShapedText Text { get; } = text ?? throw new ArgumentNullException(nameof(text)); }
public sealed class ClipSceneNode : SceneNode
{
    private readonly IReadOnlyList<SceneNode> _children;
    public ClipSceneNode(SceneNodeIdentity identity, LayoutRect bounds, IReadOnlyList<SceneNode> children) : base(identity, bounds) { _children = Array.AsReadOnly(children?.Select(Clone).ToArray() ?? throw new ArgumentNullException(nameof(children))); }
    public IReadOnlyList<SceneNode> Children => _children;
    internal static SceneNode Clone(SceneNode node) => node switch { PaintSceneNode paint => new PaintSceneNode(paint.Identity, paint.Bounds, paint.Brush), TextSceneNode text => new TextSceneNode(text.Identity, text.Bounds, text.Color, text.Text), ClipSceneNode clip => new ClipSceneNode(clip.Identity, clip.Bounds, clip.Children), _ => throw new ArgumentException("Unknown scene node.") };
}

/// <summary>A renderer-facing retained snapshot. It owns no platform or renderer resources.</summary>
public sealed class RetainedScene
{
    internal RetainedScene(long generation, LayoutViewport viewport, IReadOnlyList<LayoutBox> boxes, IReadOnlyList<SceneNode> nodes, IReadOnlyList<RetainedInputElement> input) { Generation = generation; Viewport = viewport; Boxes = Array.AsReadOnly(boxes.ToArray()); Nodes = Array.AsReadOnly(nodes.Select(ClipSceneNode.Clone).ToArray()); Input = Array.AsReadOnly(input.ToArray()); InputSignature = Signature(Input); }
    /// <summary>Monotonic composition-local identity; routers reject older snapshots.</summary>
    public long Generation { get; }
    public LayoutViewport Viewport { get; }
    public IReadOnlyList<LayoutBox> Boxes { get; }
    public IReadOnlyList<SceneNode> Nodes { get; }
    /// <summary>Retained hit/focus metadata matched to this scene generation.</summary>
    public IReadOnlyList<RetainedInputElement> Input { get; }
    public string InputSignature { get; }
    public string Dump()
    {
        var output = new StringBuilder("scene generation=").Append(Generation.ToString(CultureInfo.InvariantCulture)).Append(" inputSignature=").Append(InputSignature).Append(" viewport=").Append(Format(new LayoutRect(0, 0, Viewport.Width, Viewport.Height))).Append(" scale=").Append(Viewport.Scale.ToString("R", CultureInfo.InvariantCulture)).Append("\n");
        foreach (var input in Input.OrderBy(item => item.Identity.ElementId)) output.Append("input epoch=").Append(input.Identity.CompositionEpoch.ToString(CultureInfo.InvariantCulture)).Append(" element=").Append(input.Identity.ElementId.ToString(CultureInfo.InvariantCulture)).Append(" parent=").Append(input.Parent?.ElementId.ToString(CultureInfo.InvariantCulture) ?? "-").Append(" order=").Append(input.Order.ToString(CultureInfo.InvariantCulture)).Append(" bounds=").Append(Format(input.Bounds)).Append(" clip=").Append(input.Clip ? "true" : "false").Append(" enabled=").Append(input.Enabled ? "true" : "false").Append(" visible=").Append(input.Visible ? "true" : "false").Append(" signature=").Append(input.Signature).Append('\n');
        foreach (var box in Boxes.OrderBy(box => box.Identity.ElementId))
        {
            output.Append("layout epoch=").Append(box.Identity.CompositionEpoch.ToString(CultureInfo.InvariantCulture)).Append(" element=").Append(box.Identity.ElementId.ToString(CultureInfo.InvariantCulture)).Append(" bounds=")
                .Append(Format(box.Bounds));
            if (box.Text is not null) foreach (var run in box.Text.Runs) output.Append(" shape=").Append(DiagnosticText.Quote(run.Identity)).Append(" face=").Append(DiagnosticText.Quote(run.TypefaceIdentity)).Append(" fingerprint=").Append(DiagnosticText.Quote(run.Fingerprint)).Append(" collection=").Append(run.CollectionIndex.ToString(CultureInfo.InvariantCulture)).Append(" source=").Append(DiagnosticText.Quote(run.SourceIdentity)).Append(" origin=").Append(run.OriginX.ToString("R", CultureInfo.InvariantCulture)).Append(" width=").Append(run.RunWidth.ToString("R", CultureInfo.InvariantCulture)).Append(" fontSize=").Append(run.FontSize.ToString("R", CultureInfo.InvariantCulture)).Append(" direction=").Append(run.Direction).Append(" language=").Append(DiagnosticText.Quote(run.Language)).Append(" baseline=").Append(run.Baseline.ToString("R", CultureInfo.InvariantCulture)).Append(" ascent=").Append(run.Ascent.ToString("R", CultureInfo.InvariantCulture)).Append(" descent=").Append(run.Descent.ToString("R", CultureInfo.InvariantCulture)).Append(" glyphs=[").Append(string.Join(',', run.Glyphs.Select(glyph => glyph.GlyphId.ToString(CultureInfo.InvariantCulture) + ":" + glyph.Cluster.ToString(CultureInfo.InvariantCulture) + ":" + glyph.X.ToString("R", CultureInfo.InvariantCulture) + ":" + glyph.Y.ToString("R", CultureInfo.InvariantCulture) + ":" + glyph.XAdvance.ToString("R", CultureInfo.InvariantCulture) + ":" + glyph.XOffset.ToString("R", CultureInfo.InvariantCulture) + ":" + glyph.YOffset.ToString("R", CultureInfo.InvariantCulture)))).Append(']');
            output.Append('\n');
        }
        Append(Nodes, output, 0, null);
        return output.ToString();
    }
    private static void Append(IEnumerable<SceneNode> nodes, StringBuilder output, int depth, long? parent)
    {
        foreach (var node in nodes)
        {
            output.Append("node epoch=").Append(node.Identity.Element.CompositionEpoch.ToString(CultureInfo.InvariantCulture)).Append(" element=").Append(node.Identity.Element.ElementId.ToString(CultureInfo.InvariantCulture)).Append(" parent=").Append(parent?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(" depth=").Append(depth.ToString(CultureInfo.InvariantCulture)).Append(" kind=").Append(node.Identity.Kind).Append(" bounds=").Append(Format(node.Bounds));
            if (node is PaintSceneNode paint) output.Append(" brush=").Append(paint.Brush);
            if (node is TextSceneNode text) output.Append(" color=").Append(text.Color).Append(" shape=").Append(DiagnosticText.Quote(text.Text.Identity));
            output.Append('\n');
            if (node is ClipSceneNode clip) Append(clip.Children, output, depth + 1, node.Identity.Element.ElementId);
        }
    }
    private static string Format(LayoutRect value) => "[" + value.X.ToString("R", CultureInfo.InvariantCulture) + "," + value.Y.ToString("R", CultureInfo.InvariantCulture) + "," + value.Width.ToString("R", CultureInfo.InvariantCulture) + "," + value.Height.ToString("R", CultureInfo.InvariantCulture) + "]";
    private static string Signature(IEnumerable<RetainedInputElement> input) => Hash(writer =>
    {
        var copy = input.OrderBy(item => item.Order).ToArray(); writer.Write(copy.Length);
        foreach (var item in copy)
        {
            writer.Write(item.Identity.CompositionEpoch); writer.Write(item.Identity.ElementId); writer.Write(item.Parent.HasValue); if (item.Parent is { } parent) { writer.Write(parent.CompositionEpoch); writer.Write(parent.ElementId); }
            writer.Write(item.Bounds.X); writer.Write(item.Bounds.Y); writer.Write(item.Bounds.Width); writer.Write(item.Bounds.Height); writer.Write(item.Order); writer.Write(item.Clip); writer.Write(item.Enabled); writer.Write(item.Visible); writer.Write(item.Signature);
        }
    });
    private static string Hash(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true); write(writer); writer.Flush(); return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}

/// <summary>Finite row/column layout: invalid values fail; fixed over-constraint is retained and bounded by an explicit clip.</summary>
public static class SceneLayout
{
    public static RetainedScene Project(Composition composition,
LayoutViewport viewport, ITextShaper shaper)
    {
        ArgumentNullException.ThrowIfNull(composition); ArgumentNullException.ThrowIfNull(shaper); viewport.Validate();
        composition.RealizeVirtualized(viewport);
        composition.Flush(); // Virtual row factories may register effects; a projected scene must not precede their first commit.
        composition.RealizeVirtualized(viewport);
        var boxes = new List<LayoutBox>();
        var shapes = new Dictionary<long, ShapedText?>();
        var nodes = Layout(composition.Root, new LayoutRect(0, 0, viewport.Width, viewport.Height), viewport, shaper, boxes, shapes, null, false);
        var byId = boxes.ToDictionary(box => box.Identity.ElementId);
        var input = composition.Elements().Select((element, order) => new RetainedInputElement(new(composition.Epoch, element.Id), element.Parent is null ? null : new(composition.Epoch, element.Parent.Id), byId[element.Id].Bounds, order,
            element.Resolve(LayoutProperties.Clip).Value, element.Resolve(InputProperties.Enabled).Value, element.Resolve(InputProperties.Visible).Value, InputSignature(element))).ToArray();
        return new RetainedScene(composition.NextSceneGeneration(), viewport, boxes, nodes, input);
    }

    private static IReadOnlyList<SceneNode> Layout(Element element, LayoutRect allotted, LayoutViewport viewport, ITextShaper shaper, List<LayoutBox> boxes, Dictionary<long, ShapedText?> shapes, LayoutAxis? parentAxis, bool crossAllotted)
    {
        var style = Read(element); var text = Shape(element, style, viewport.Scale, shaper, shapes);
        var width = Constrain(style.Width ?? (text?.Width ?? allotted.Width), style.MinWidth, style.MaxWidth);
        var height = Constrain(style.Height ?? (text?.Height ?? allotted.Height), style.MinHeight, style.MaxHeight);
        if (crossAllotted && parentAxis == LayoutAxis.Row && style.Height is null) height = Constrain(allotted.Height, style.MinHeight, style.MaxHeight);
        if (crossAllotted && parentAxis == LayoutAxis.Column && style.Width is null) width = Constrain(allotted.Width, style.MinWidth, style.MaxWidth);
        if (element.Parent is null) { width = allotted.Width; height = allotted.Height; }
        var bounds = LayoutRect.Round(allotted.X, allotted.Y, width, height, viewport.Scale);
        boxes.Add(new(new(element.Composition.Epoch, element.Id), bounds, text));
        var childNodes = new List<SceneNode>();
        if (element.Children.Count != 0)
        {
            var axis = style.Axis;
            var mainLimit = axis == LayoutAxis.Row ? bounds.Width : bounds.Height;
            var crossLimit = axis == LayoutAxis.Row ? bounds.Height : bounds.Width;
            var measured = element.Children.Select(child => Measure(child, axis, crossLimit, viewport.Scale, shaper, shapes)).ToArray();
            var total = 0f;
            foreach (var item in measured) total = Finite(total + item.Main);
            total = Finite(total + Finite(style.Spacing * (element.Children.Count - 1)));
            var cursor = style.MainAlignment switch { LayoutAlignment.Center => Math.Max(0, (mainLimit - total) / 2), LayoutAlignment.End => Math.Max(0, mainLimit - total), _ => 0 };
            foreach (var item in measured)
            {
                var virtualIndex = style.VirtualRowHeight is null ? 0 : Read(item.Element).VirtualRowIndex;
                var cross = style.CrossAlignment == LayoutAlignment.Stretch && item.AutoCross ? crossLimit : item.Cross;
                var crossOffset = style.CrossAlignment switch { LayoutAlignment.Center => (crossLimit - cross) / 2, LayoutAlignment.End => crossLimit - cross, _ => 0 };
                var main = style.VirtualRowHeight is { } rowHeight ? virtualIndex * rowHeight : cursor;
                var x = Finite(Finite(bounds.X + (axis == LayoutAxis.Row ? main : crossOffset)) - style.Scroll.X);
                var y = Finite(Finite(bounds.Y + (axis == LayoutAxis.Row ? crossOffset : main)) - style.Scroll.Y);
                var childBounds = axis == LayoutAxis.Row ? new LayoutRect(x, y, item.Main, cross) : new LayoutRect(x, y, cross, item.Main);
                childNodes.AddRange(Layout(item.Element, childBounds, viewport, shaper, boxes, shapes, axis, style.CrossAlignment == LayoutAlignment.Stretch && item.AutoCross));
                if (style.VirtualRowHeight is null) cursor = Finite(cursor + item.Main + style.Spacing);
            }
        }
        var identity = new ElementIdentity(element.Composition.Epoch, element.Id);
        var result = new List<SceneNode>();
        if (style.Background.Color is not { A: 0 }) result.Add(new PaintSceneNode(new(identity, SceneNodeKind.Paint), bounds, style.Background));
        if (text is not null)
        {
            var viewOffset = style.Caret is { } caretOffset ? TextViewOffset(text, style.Text!, caretOffset, bounds.Width) : 0;
            var textBounds = new LayoutRect(bounds.X - viewOffset, bounds.Y, bounds.Width + viewOffset, bounds.Height);
            if (style.SelectionStart is { } start && style.SelectionEnd is { } end && start >= 0 && end > start && end <= style.Text!.Length)
            {
                var left = TextPosition(text, style.Text!, start); var right = TextPosition(text, style.Text!, end);
                result.Add(new PaintSceneNode(new(identity, SceneNodeKind.Selection), new(textBounds.X + Math.Min(left, right), bounds.Y, MathF.Abs(right - left), bounds.Height), Color.FromArgb(0x66, 0x3b, 0x82, 0xf6)));
            }
            result.Add(new TextSceneNode(new(identity, SceneNodeKind.Text), textBounds, style.TextColor, text));
            if (style.Caret is { } caret && caret >= 0 && caret <= style.Text!.Length)
                result.Add(new PaintSceneNode(new(identity, SceneNodeKind.Caret), new(textBounds.X + TextPosition(text, style.Text!, caret), bounds.Y, 1 / viewport.Scale, bounds.Height), style.TextColor));
        }
        else if (style.Text is "" && style.Caret == 0)
            result.Add(new PaintSceneNode(new(identity, SceneNodeKind.Caret), new(bounds.X, bounds.Y, 1 / viewport.Scale, bounds.Height), style.TextColor));
        result.AddRange(childNodes);
        return style.Clip ? [new ClipSceneNode(new(identity, SceneNodeKind.Clip), bounds, result)] : result;
    }

    private static (Element Element, float Main, float Cross, bool AutoCross) Measure(Element element, LayoutAxis parentAxis, float crossLimit, float scale, ITextShaper shaper, Dictionary<long, ShapedText?> shapes)
    {
        var style = Read(element); var text = Shape(element, style, scale, shaper, shapes);
        var intrinsic = Intrinsic(element, style, text, scale, shaper, shapes);
        var width = Constrain(style.Width ?? intrinsic.Width, style.MinWidth, style.MaxWidth);
        var height = Constrain(style.Height ?? intrinsic.Height, style.MinHeight, style.MaxHeight);
        return parentAxis == LayoutAxis.Row ? (element, width, height, style.Height is null) : (element, height, width, style.Width is null);
    }

    private static (float Width, float Height) Intrinsic(Element element, Values style, ShapedText? text, float scale, ITextShaper shaper, Dictionary<long, ShapedText?> shapes)
    {
        var width = text?.Width ?? 0f; var height = text?.Height ?? 0f;
        if (element.Children.Count == 0) return style.VirtualRowHeight is { } emptyRowHeight ? (width, Finite(emptyRowHeight * style.VirtualItemCount)) : (width, height);
        var children = element.Children.Select(child => Measure(child, style.Axis, float.MaxValue, scale, shaper, shapes)).ToArray();
        if (style.Axis == LayoutAxis.Row)
        {
            foreach (var child in children) width = Finite(width + child.Main);
            width = Finite(width + Finite(style.Spacing * Math.Max(0, children.Length - 1)));
            foreach (var child in children) height = Math.Max(height, child.Cross);
        }
        else
        {
            foreach (var child in children) height = Finite(height + child.Main);
            height = Finite(height + Finite(style.Spacing * Math.Max(0, children.Length - 1)));
            foreach (var child in children) width = Math.Max(width, child.Cross);
        }
        if (style.VirtualRowHeight is { } rowHeight) height = Finite(rowHeight * style.VirtualItemCount);
        return (Finite(width), Finite(height));
    }

    private static ShapedText? Shape(Element element, Values style, float scale, ITextShaper shaper, Dictionary<long, ShapedText?> shapes)
    {
        if (shapes.TryGetValue(element.Id, out var cached)) return cached;
        if (string.IsNullOrEmpty(style.Text)) return null;
        var request = new TextMeasureRequest(style.Text, style.FontFamily, style.FontSize, style.Language, style.Direction, scale);
        request.Validate();
        var shaped = shaper.Shape(request); shaped.Validate(request); shapes.Add(element.Id, shaped); return shaped;
    }

    private static float Constrain(float value, float min, float max)
    {
        if (!float.IsFinite(value) || !float.IsFinite(min) || (!float.IsFinite(max) && !float.IsPositiveInfinity(max)) || value < 0 || min < 0 || max < min)
            throw new ArgumentOutOfRangeException(nameof(value), "LayoutProperties sizes must be finite/nonnegative and min must not exceed max.");
        return Math.Clamp(value, min, max);
    }

    private static float Finite(float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Layout arithmetic overflow.");
        return value;
    }

    private static Values Read(Element element)
    {
        var values = new Values(element.Resolve(LayoutProperties.Axis).Value, element.Resolve(LayoutProperties.Width).Value, element.Resolve(LayoutProperties.Height).Value,
            element.Resolve(LayoutProperties.MinWidth).Value, element.Resolve(LayoutProperties.MinHeight).Value, element.Resolve(LayoutProperties.MaxWidth).Value, element.Resolve(LayoutProperties.MaxHeight).Value,
            element.Resolve(LayoutProperties.Spacing).Value, element.Resolve(LayoutProperties.MainAlignment).Value, element.Resolve(LayoutProperties.CrossAlignment).Value, element.Resolve(LayoutProperties.Clip).Value,
            element.Resolve(LayoutProperties.Scroll).Value, element.Resolve(LayoutProperties.VirtualRowHeight).Value, element.Resolve(LayoutProperties.VirtualItemCount).Value, element.Resolve(LayoutProperties.VirtualRowIndex).Value, element.Resolve(VisualProperties.Background).Value, element.Resolve(TypographyProperties.TextColor).Value, element.Resolve(ProjectionProperties.Text).Value,
            element.Resolve(TypographyProperties.FontFamily).Value, element.Resolve(TypographyProperties.FontSize).Value, element.Resolve(TypographyProperties.Language).Value, element.Resolve(TypographyProperties.Direction).Value,
            element.Resolve(ProjectionProperties.TextSelectionStart).Value, element.Resolve(ProjectionProperties.TextSelectionEnd).Value, element.Resolve(ProjectionProperties.TextCaret).Value);
        if (!Enum.IsDefined(values.Axis) || !Enum.IsDefined(values.MainAlignment) || !Enum.IsDefined(values.CrossAlignment) || !Enum.IsDefined(values.Direction) || !float.IsFinite(values.Spacing) || values.Spacing < 0 || values.Width is { } width && (!float.IsFinite(width) || width < 0) || values.Height is { } height && (!float.IsFinite(height) || height < 0) || values.VirtualRowHeight is { } rowHeight && (!float.IsFinite(rowHeight) || rowHeight <= 0 || values.VirtualItemCount < 0 || values.VirtualRowIndex < 0))
            throw new ArgumentOutOfRangeException(nameof(element), "LayoutProperties values must be finite and nonnegative.");
        values.Scroll.Validate(); return values;
    }

    private readonly record struct Values(LayoutAxis Axis, float? Width, float? Height, float MinWidth, float MinHeight, float MaxWidth, float MaxHeight, float Spacing, LayoutAlignment MainAlignment, LayoutAlignment CrossAlignment, bool Clip, ScrollOffset Scroll, float? VirtualRowHeight, int VirtualItemCount, int VirtualRowIndex, Brush Background, Color TextColor, string? Text, string FontFamily, float FontSize, string Language, TextDirection Direction, int? SelectionStart, int? SelectionEnd, int? Caret);

    /// <summary>Returns a bounded LTR caret position; it interpolates grapheme boundaries inside one ligature cluster and does not implement full bidi caret ordering.</summary>
    internal static float TextPosition(ShapedText text, string source, int utf16Offset)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(source);
        if (utf16Offset < 0 || utf16Offset > source.Length) throw new ArgumentOutOfRangeException(nameof(utf16Offset));
        if (utf16Offset == source.Length) return text.Width;
        var glyphs = text.Runs.SelectMany(run => run.Glyphs.Select(glyph => (Run: run, Glyph: glyph))).ToArray();
        var current = glyphs.Where(value => value.Glyph.Cluster <= utf16Offset).OrderByDescending(value => value.Glyph.Cluster).FirstOrDefault();
        if (current.Glyph.GlyphId != 0 && current.Run.Direction == TextDirection.LeftToRight)
        {
            var cluster = (int)current.Glyph.Cluster;
            var end = glyphs.Where(value => value.Glyph.Cluster > current.Glyph.Cluster).Select(value => (int)value.Glyph.Cluster).DefaultIfEmpty(source.Length).Min();
            var boundaries = StringInfo.ParseCombiningCharacters(source).Append(source.Length).Where(value => value >= cluster && value <= end).ToArray();
            var position = Array.IndexOf(boundaries, utf16Offset);
            if (boundaries.Length > 2 && position >= 0)
            {
                var clusterGlyphs = glyphs.Where(value => value.Glyph.Cluster == current.Glyph.Cluster).ToArray();
                var left = clusterGlyphs.Min(value => value.Run.OriginX + value.Glyph.X);
                var right = clusterGlyphs.Max(value => value.Run.OriginX + value.Glyph.X + value.Glyph.XAdvance);
                return left + (right - left) * position / (boundaries.Length - 1);
            }
        }
        var next = glyphs.Where(value => value.Glyph.Cluster >= utf16Offset).OrderBy(value => value.Glyph.Cluster).FirstOrDefault();
        return next.Glyph.GlyphId == 0 ? text.Width : next.Run.OriginX + next.Glyph.X;
    }

    internal static float TextViewOffset(ShapedText text, string source, int caret, float width)
    {
        if (!float.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        return Math.Clamp(TextPosition(text, source, caret) - Math.Max(0, width - 1), 0, Math.Max(0, text.Width - width + 1));
    }

    internal static string InputSignature(Element element)
    {
        var value = Read(element);
        return Hash(writer =>
        {
            writer.Write((int)value.Axis); writer.Write(value.Width.HasValue); if (value.Width is { } width) writer.Write(width); writer.Write(value.Height.HasValue); if (value.Height is { } height) writer.Write(height);
            writer.Write(value.MinWidth); writer.Write(value.MinHeight); writer.Write(value.MaxWidth); writer.Write(value.MaxHeight); writer.Write(value.Spacing); writer.Write((int)value.MainAlignment); writer.Write((int)value.CrossAlignment); writer.Write(value.Clip); writer.Write(value.Scroll.X); writer.Write(value.Scroll.Y);
            writer.Write(value.Text is not null); if (value.Text is { } text) writer.Write(text); writer.Write(value.FontFamily); writer.Write(value.FontSize); writer.Write(value.Language); writer.Write((int)value.Direction);
            writer.Write(value.SelectionStart.HasValue); if (value.SelectionStart is { } selectionStart) writer.Write(selectionStart);
            writer.Write(value.SelectionEnd.HasValue); if (value.SelectionEnd is { } selectionEnd) writer.Write(selectionEnd);
            writer.Write(value.Caret.HasValue); if (value.Caret is { } caret) writer.Write(caret);
            writer.Write(element.Resolve(InputProperties.Enabled).Value); writer.Write(element.Resolve(InputProperties.Visible).Value);
        });
    }

    private static string Hash(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true); write(writer); writer.Flush(); return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
