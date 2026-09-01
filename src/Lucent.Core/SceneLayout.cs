using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lucent.Core;

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
        var input = composition.Elements().Select((element, order) => new RetainedInputElement(new(composition.Epoch, element.Id), element.Parent is null ? null : new(composition.Epoch, element.Parent.Id), byId[element.Id].Bounds,
            element.Resolve(LayoutProperties.Clip).Value ? ContentBounds(byId[element.Id].Bounds, element.Resolve(LayoutProperties.Padding).Value, viewport.Scale) : null, order,
            element.Resolve(InputProperties.Enabled).Value, element.Resolve(InputProperties.Visible).Value, InputSignature(element))).ToArray();
        return new RetainedScene(composition.NextSceneGeneration(), viewport, boxes, nodes, input);
    }

    private static IReadOnlyList<SceneNode> Layout(Element element, LayoutRect allotted, LayoutViewport viewport, ITextShaper shaper, List<LayoutBox> boxes, Dictionary<long, ShapedText?> shapes, LayoutAxis? parentAxis, bool crossAllotted)
    {
        var style = Read(element); var text = Shape(element, style, viewport.Scale, shaper, shapes);
        var width = Constrain(style.Width ?? (text is null ? allotted.Width : Finite(text.Width + style.Padding.Horizontal)), style.MinWidth, style.MaxWidth);
        var height = Constrain(style.Height ?? (text is null ? allotted.Height : Finite(text.Height + style.Padding.Vertical)), style.MinHeight, style.MaxHeight);
        if (crossAllotted && parentAxis == LayoutAxis.Row && style.Height is null) height = Constrain(allotted.Height, style.MinHeight, style.MaxHeight);
        if (crossAllotted && parentAxis == LayoutAxis.Column && style.Width is null) width = Constrain(allotted.Width, style.MinWidth, style.MaxWidth);
        if (element.Parent is null) { width = allotted.Width; height = allotted.Height; }
        var bounds = LayoutRect.Round(allotted.X, allotted.Y, width, height, viewport.Scale);
        var inner = ContentBounds(bounds, style.Padding, viewport.Scale);
        boxes.Add(new(new(element.Composition.Epoch, element.Id), bounds, text));
        var childNodes = new List<SceneNode>();
        if (element.Children.Count != 0)
        {
            var axis = style.Axis;
            var mainLimit = axis == LayoutAxis.Row ? inner.Width : inner.Height;
            var crossLimit = axis == LayoutAxis.Row ? inner.Height : inner.Width;
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
                var x = Finite(Finite(inner.X + (axis == LayoutAxis.Row ? main : crossOffset)) - style.Scroll.X);
                var y = Finite(Finite(inner.Y + (axis == LayoutAxis.Row ? crossOffset : main)) - style.Scroll.Y);
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
            var viewOffset = style.Caret is { } caretOffset ? TextViewOffset(text, style.Text!, caretOffset, inner.Width) : 0;
            var textBounds = new LayoutRect(inner.X - viewOffset, inner.Y, inner.Width + viewOffset, inner.Height);
            if (style.SelectionStart is { } start && style.SelectionEnd is { } end && start >= 0 && end > start && end <= style.Text!.Length)
            {
                var left = TextPosition(text, style.Text!, start); var right = TextPosition(text, style.Text!, end);
                result.Add(new PaintSceneNode(new(identity, SceneNodeKind.Selection), new(textBounds.X + Math.Min(left, right), inner.Y, MathF.Abs(right - left), inner.Height), Color.FromArgb(0x66, 0x3b, 0x82, 0xf6)));
            }
            result.Add(new TextSceneNode(new(identity, SceneNodeKind.Text), textBounds, style.TextColor, text));
            if (style.Caret is { } caret && caret >= 0 && caret <= style.Text!.Length)
                result.Add(new PaintSceneNode(new(identity, SceneNodeKind.Caret), new(textBounds.X + TextPosition(text, style.Text!, caret), inner.Y, 1 / viewport.Scale, inner.Height), style.TextColor));
        }
        else if (style.Text is "" && style.Caret == 0)
            result.Add(new PaintSceneNode(new(identity, SceneNodeKind.Caret), new(inner.X, inner.Y, 1 / viewport.Scale, inner.Height), style.TextColor));
        result.AddRange(childNodes);
        if (style.Clip)
        {
            var clipped = new List<SceneNode>();
            if (result.FirstOrDefault() is PaintSceneNode { Identity.Kind: SceneNodeKind.Paint } background && background.Identity.Element == identity) { clipped.Add(background); result.RemoveAt(0); }
            clipped.Add(new ClipSceneNode(new(identity, SceneNodeKind.Clip), inner, result));
            result = clipped;
        }
        return style.Opacity == 1 || result.Count == 0 ? result : [new OpacitySceneNode(new(identity, SceneNodeKind.Opacity), VisibleBounds(result, viewport), style.Opacity, result)];
    }

    private static LayoutRect VisibleBounds(IReadOnlyList<SceneNode> nodes, LayoutViewport viewport)
    {
        var left = nodes.Min(node => node.Bounds.X); var top = nodes.Min(node => node.Bounds.Y);
        var right = nodes.Max(node => Finite(node.Bounds.X + node.Bounds.Width)); var bottom = nodes.Max(node => Finite(node.Bounds.Y + node.Bounds.Height));
        var visibleRight = Math.Min(right, viewport.Width); var visibleBottom = Math.Min(bottom, viewport.Height);
        left = Math.Clamp(left, 0, viewport.Width); top = Math.Clamp(top, 0, viewport.Height);
        return new(left, top, Math.Max(0, visibleRight - left), Math.Max(0, visibleBottom - top));
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
        if (element.Children.Count == 0) return Outer(style, style.VirtualRowHeight is { } emptyRowHeight ? (width, Finite(emptyRowHeight * style.VirtualItemCount)) : (width, height));
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
        return Outer(style, (Finite(width), Finite(height)));
    }

    private static (float Width, float Height) Outer(Values style, (float Width, float Height) content)
        => (Finite(content.Width + style.Padding.Horizontal), Finite(content.Height + style.Padding.Vertical));

    internal static LayoutRect ContentBounds(LayoutRect outer, Insets padding, float scale)
    {
        static float Edge(float value, float scale) => MathF.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
        var outerRight = Finite(outer.X + outer.Width); var outerBottom = Finite(outer.Y + outer.Height);
        var left = Math.Min(outerRight, Edge(Finite(outer.X + padding.Left), scale)); var top = Math.Min(outerBottom, Edge(Finite(outer.Y + padding.Top), scale));
        var right = Math.Max(left, Math.Min(outerRight, Edge(Finite(outerRight - padding.Right), scale))); var bottom = Math.Max(top, Math.Min(outerBottom, Edge(Finite(outerBottom - padding.Bottom), scale)));
        return new(left, top, right - left, bottom - top);
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
            element.Resolve(LayoutProperties.Padding).Value, element.Resolve(LayoutProperties.Scroll).Value, element.Resolve(LayoutProperties.VirtualRowHeight).Value, element.Resolve(LayoutProperties.VirtualItemCount).Value, element.Resolve(LayoutProperties.VirtualRowIndex).Value, element.Resolve(VisualProperties.Background).Value, element.Resolve(VisualProperties.Opacity).Value, element.Resolve(TypographyProperties.TextColor).Value, element.Resolve(ProjectionProperties.Text).Value,
            element.Resolve(TypographyProperties.FontFamily).Value, element.Resolve(TypographyProperties.FontSize).Value, element.Resolve(TypographyProperties.Language).Value, element.Resolve(TypographyProperties.Direction).Value,
            element.Resolve(ProjectionProperties.TextSelectionStart).Value, element.Resolve(ProjectionProperties.TextSelectionEnd).Value, element.Resolve(ProjectionProperties.TextCaret).Value);
        if (!Enum.IsDefined(values.Axis) || !Enum.IsDefined(values.MainAlignment) || !Enum.IsDefined(values.CrossAlignment) || !Enum.IsDefined(values.Direction) || !float.IsFinite(values.Spacing) || values.Spacing < 0 || !float.IsFinite(values.Opacity) || values.Opacity < 0 || values.Opacity > 1 || values.Width is { } width && (!float.IsFinite(width) || width < 0) || values.Height is { } height && (!float.IsFinite(height) || height < 0) || values.VirtualRowHeight is { } rowHeight && (!float.IsFinite(rowHeight) || rowHeight <= 0 || values.VirtualItemCount < 0 || values.VirtualRowIndex < 0))
            throw new ArgumentOutOfRangeException(nameof(element), "LayoutProperties values must be finite and nonnegative.");
        values.Scroll.Validate(); return values;
    }

    private readonly record struct Values(LayoutAxis Axis, float? Width, float? Height, float MinWidth, float MinHeight, float MaxWidth, float MaxHeight, float Spacing, LayoutAlignment MainAlignment, LayoutAlignment CrossAlignment, bool Clip, Insets Padding, ScrollOffset Scroll, float? VirtualRowHeight, int VirtualItemCount, int VirtualRowIndex, Brush Background, float Opacity, Color TextColor, string? Text, string FontFamily, float FontSize, string Language, TextDirection Direction, int? SelectionStart, int? SelectionEnd, int? Caret);

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
            writer.Write(value.MinWidth); writer.Write(value.MinHeight); writer.Write(value.MaxWidth); writer.Write(value.MaxHeight); writer.Write(value.Spacing); writer.Write((int)value.MainAlignment); writer.Write((int)value.CrossAlignment); writer.Write(value.Clip); writer.Write(value.Padding.Left); writer.Write(value.Padding.Top); writer.Write(value.Padding.Right); writer.Write(value.Padding.Bottom); writer.Write(value.Scroll.X); writer.Write(value.Scroll.Y);
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
