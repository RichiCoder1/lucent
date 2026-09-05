using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lucent.Core;

/// <summary>Projects a retained composition into immutable layout, scene, and input data.</summary>
public static class SceneLayout
{
    /// <summary>Lays out the current composition and returns a fresh retained scene; callers install it in the input router.</summary>
    public static RetainedScene Project(
        Composition composition,
        LayoutViewport viewport,
        ITextShaper shaper
    )
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(shaper);
        viewport.Validate();
        composition.Flush();
        var responsive = ResponsiveElements(composition);
        Dictionary<long, LayoutRect>? realizedViewportBounds = null;
        if (composition.HasVirtualizedRegions || responsive.Length != 0)
        {
            var assignedBoxes = new List<LayoutBox>();
            _ = composition.WithoutProjectionTracking(() =>
                Layout(
                    composition.Root,
                    new LayoutRect(0, 0, viewport.Width, viewport.Height),
                    viewport,
                    shaper,
                    assignedBoxes,
                    new ProjectionCache(),
                    null,
                    false,
                    false
                )
            );
            var assignedById = assignedBoxes.ToDictionary(
                box => box.Identity.ElementId,
                box => box.Bounds
            );
            foreach (var item in responsive)
            {
                var inner = ContentBounds(
                    assignedById[item.Element.Id],
                    item.Element.Resolve(LayoutProperties.Padding).Value,
                    viewport.Scale
                );
                item.State!.Assign(new(inner.Width, inner.Height));
            }
            composition.Flush();
            EnsureResponsiveElementsUnchanged(composition, responsive);
            if (responsive.Length != 0)
            {
                assignedBoxes.Clear();
                _ = composition.WithoutProjectionTracking(() =>
                    Layout(
                        composition.Root,
                        new LayoutRect(0, 0, viewport.Width, viewport.Height),
                        viewport,
                        shaper,
                        assignedBoxes,
                        new ProjectionCache(),
                        null,
                        false,
                        false
                    )
                );
                assignedById = assignedBoxes.ToDictionary(
                    box => box.Identity.ElementId,
                    box => box.Bounds
                );
            }

            var virtualizedViewportIds = composition.VirtualizedViewportIds;
            composition.RealizeVirtualized(viewport, assignedById);
            realizedViewportBounds = virtualizedViewportIds.ToDictionary(
                id => id,
                id =>
                    assignedById.TryGetValue(id, out var bounds)
                        ? bounds
                        : throw new InvalidOperationException(
                            "A virtualized region has no assigned viewport after responsive layout."
                        )
            );
            composition.Flush(); // Responsive branches and newly realized rows commit before the final projection.
            EnsureResponsiveElementsUnchanged(composition, responsive);
            if (!composition.VirtualizedViewportIds.SequenceEqual(virtualizedViewportIds))
                throw new InvalidOperationException(
                    "A virtualized region was mounted or removed during its realization flush."
                );
        }
        var projected = composition.CaptureInputProjection(() =>
        {
            var elements = composition.Elements().ToArray();
            var signatures = elements.Select(InputSignature).ToArray();
            var boxes = new List<LayoutBox>();
            var cache = new ProjectionCache();
            var nodes = composition.WithoutProjectionTracking(() =>
                Layout(
                    composition.Root,
                    new LayoutRect(0, 0, viewport.Width, viewport.Height),
                    viewport,
                    shaper,
                    boxes,
                    cache,
                    null,
                    false,
                    false
                )
            );
            var currentElements = composition.Elements().ToArray();
            if (!currentElements.SequenceEqual(elements))
                throw new InvalidOperationException(
                    "Composition structure changed while the scene was being produced."
                );
            var byId = boxes.ToDictionary(box => box.Identity.ElementId);
            var input = elements
                .Select(
                    (element, order) =>
                    {
                        var signature = InputSignature(element);
                        if (!StringComparer.Ordinal.Equals(signature, signatures[order]))
                            throw new InvalidOperationException(
                                "Input projection state changed while the scene was being produced."
                            );
                        return new RetainedInputElement(
                            new(composition.Epoch, element.Id),
                            element.Parent is null
                                ? null
                                : new(composition.Epoch, element.Parent.Id),
                            byId[element.Id].Bounds,
                            element.Resolve(LayoutProperties.Clip).Value
                                ? ContentBounds(
                                    byId[element.Id].Bounds,
                                    element.Resolve(LayoutProperties.Padding).Value,
                                    viewport.Scale
                                )
                                : null,
                            order,
                            element.Resolve(InputProperties.Enabled).Value,
                            element.Resolve(InputProperties.Visible).Value
                                && element.ParticipatesInInput(),
                            signature
                        );
                    }
                )
                .ToArray();
            var collapsed = elements.Where(IsCollapsed).Select(element => element.Id).ToArray();
            return (Boxes: boxes, Nodes: nodes, Input: input, Collapsed: collapsed);
        });
        var finalById = projected.Boxes.ToDictionary(
            box => box.Identity.ElementId,
            box => box.Bounds
        );
        if (realizedViewportBounds is not null)
            foreach (var expected in realizedViewportBounds)
                if (
                    !finalById.TryGetValue(expected.Key, out var actual)
                    || actual != expected.Value
                )
                    throw new InvalidOperationException(
                        "Virtualized viewport geometry changed after its assigned realization pass."
                    );
        foreach (var item in responsive)
        {
            if (!finalById.TryGetValue(item.Element.Id, out var outer))
                throw new InvalidOperationException(
                    "Responsive container was removed during its constraint pass."
                );
            var inner = ContentBounds(
                outer,
                item.Element.Resolve(LayoutProperties.Padding).Value,
                viewport.Scale
            );
            if (item.State!.Current != new ContainerConstraints(inner.Width, inner.Height))
                throw new InvalidOperationException(
                    "Responsive container feedback changed its own assigned constraints after the bounded correction pass."
                );
        }
        return new RetainedScene(
            composition.NextSceneGeneration(),
            viewport,
            projected.Boxes,
            projected.Nodes,
            projected.Input,
            composition.InputProjectionRevision,
            projected.Collapsed
        );
    }

    private static (Element Element, ResponsiveConstraints State)[] ResponsiveElements(
        Composition composition
    ) =>
        composition
            .Elements()
            .Select(element =>
                (
                    Element: element,
                    State: element.Resolve(ProjectionProperties.ResponsiveConstraints).Value
                )
            )
            .Where(value => value.State is not null)
            .Select(value => (value.Element, value.State!))
            .ToArray();

    private static void EnsureResponsiveElementsUnchanged(
        Composition composition,
        IReadOnlyList<(Element Element, ResponsiveConstraints State)> expected
    )
    {
        if (!ResponsiveElements(composition).SequenceEqual(expected))
            throw new InvalidOperationException(
                "A responsive container was mounted or removed during the bounded constraint pass."
            );
    }

    private static IReadOnlyList<SceneNode> Layout(
        Element element,
        LayoutRect allotted,
        LayoutViewport viewport,
        ITextShaper shaper,
        List<LayoutBox> boxes,
        ProjectionCache cache,
        LayoutAxis? parentAxis,
        bool crossAllotted,
        bool mainAllotted
    )
    {
        if (element.Participation == ElementParticipation.Collapsed)
        {
            AddCollapsedBoxes(element, allotted.X, allotted.Y, boxes);
            return [];
        }
        var style = cache.Read(element);
        var text =
            style.TextWrap == TextWrap.NoWrap
                ? Shape(element, style, viewport.Scale, shaper, cache)
                : Shape(
                    element,
                    style,
                    viewport.Scale,
                    shaper,
                    cache,
                    new LayoutConstraint(
                        Math.Max(0, (style.Width ?? allotted.Width) - style.Padding.Horizontal)
                    ),
                    new LayoutConstraint(
                        Math.Max(0, (style.Height ?? allotted.Height) - style.Padding.Vertical)
                    )
                );
        var textMetrics = IntrinsicTextMetrics(style, text, viewport.Scale, shaper, cache);
        var width = Constrain(
            style.Width
                ?? (
                    textMetrics is null
                        ? allotted.Width
                        : Finite(textMetrics.Value.Width + style.Padding.Horizontal)
                ),
            style.MinWidth,
            style.MaxWidth
        );
        var height = Constrain(
            style.Height
                ?? (
                    textMetrics is null
                        ? allotted.Height
                        : Finite(textMetrics.Value.Height + style.Padding.Vertical)
                ),
            style.MinHeight,
            style.MaxHeight
        );
        if (crossAllotted && parentAxis == LayoutAxis.Row && style.Height is null)
            height = Constrain(allotted.Height, style.MinHeight, style.MaxHeight);
        if (crossAllotted && parentAxis == LayoutAxis.Column && style.Width is null)
            width = Constrain(allotted.Width, style.MinWidth, style.MaxWidth);
        if (mainAllotted && parentAxis == LayoutAxis.Row)
            width = Constrain(allotted.Width, style.MinWidth, style.MaxWidth);
        if (mainAllotted && parentAxis == LayoutAxis.Column)
            height = Constrain(allotted.Height, style.MinHeight, style.MaxHeight);
        if (element.Parent is null)
        {
            width = allotted.Width;
            height = allotted.Height;
        }
        var bounds = LayoutRect.Round(allotted.X, allotted.Y, width, height, viewport.Scale);
        var inner = ContentBounds(bounds, style.Padding, viewport.Scale);
        boxes.Add(new(new(element.Composition.Epoch, element.Id), bounds, text));
        var childNodes = new List<SceneNode>();
        if (element.Children.Count != 0)
        {
            var participating = element
                .Children.Select((child, index) => (Child: child, Index: index))
                .Where(value => value.Child.Participation != ElementParticipation.Collapsed)
                .ToArray();
            var specs = participating
                .Select(value =>
                {
                    var childStyle = cache.Read(value.Child);
                    var childText = Shape(value.Child, childStyle, viewport.Scale, shaper, cache);
                    var intrinsic = Intrinsic(
                        value.Child,
                        childStyle,
                        childText,
                        viewport.Scale,
                        shaper,
                        cache
                    );
                    var childWidth = Constrain(
                        childStyle.Width ?? intrinsic.Width,
                        childStyle.MinWidth,
                        childStyle.MaxWidth
                    );
                    var childHeight = Constrain(
                        childStyle.Height ?? intrinsic.Height,
                        childStyle.MinHeight,
                        childStyle.MaxHeight
                    );
                    return new LayoutItemSpec(
                        value.Index,
                        childWidth,
                        childHeight,
                        childStyle.MinWidth,
                        childStyle.MinHeight,
                        childStyle.MaxWidth,
                        childStyle.MaxHeight,
                        childStyle.Width is null,
                        childStyle.Height is null,
                        childStyle.MainBasis,
                        childStyle.MainGrow,
                        childStyle.MainShrink,
                        childStyle.GridPlacement
                    );
                })
                .ToArray();

            LayoutAssignment[] assignments;
            if (style.VirtualRowHeight is { } virtualRowHeight)
            {
                assignments = participating
                    .Select(
                        (value, itemIndex) =>
                        {
                            var childStyle = cache.Read(value.Child);
                            var virtualIndex = childStyle.VirtualRowIndex;
                            return new LayoutAssignment(
                                value.Index,
                                new LayoutRect(
                                    0,
                                    virtualIndex * virtualRowHeight,
                                    inner.Width,
                                    virtualRowHeight
                                ),
                                true,
                                true
                            );
                        }
                    )
                    .ToArray();
            }
            else if (style.Mode == LayoutMode.Grid)
            {
                assignments = ManagedLayout.ArrangeGrid(
                    specs,
                    style.Columns,
                    style.Rows,
                    inner.Width,
                    inner.Height,
                    style.ColumnGap,
                    style.RowGap,
                    style.CrossAlignment
                );
                var assignmentsByIndex = assignments.ToDictionary(value => value.Index);
                var corrected = specs
                    .Select(spec =>
                    {
                        var assignment = assignmentsByIndex[spec.Index];
                        var child = element.Children[spec.Index];
                        var childStyle = cache.Read(child);
                        var constrained =
                            childStyle.TextWrap == TextWrap.NoWrap
                                ? Shape(child, childStyle, viewport.Scale, shaper, cache)
                                : Shape(
                                    child,
                                    childStyle,
                                    viewport.Scale,
                                    shaper,
                                    cache,
                                    new LayoutConstraint(
                                        Math.Max(
                                            0,
                                            assignment.Bounds.Width - childStyle.Padding.Horizontal
                                        )
                                    ),
                                    new LayoutConstraint(
                                        Math.Max(
                                            0,
                                            assignment.Bounds.Height - childStyle.Padding.Vertical
                                        )
                                    )
                                );
                        if (constrained is null || !spec.AutoHeight)
                            return spec;
                        return spec with
                        {
                            Height = Constrain(
                                Finite(constrained.Height + childStyle.Padding.Vertical),
                                childStyle.MinHeight,
                                childStyle.MaxHeight
                            ),
                        };
                    })
                    .ToArray();
                assignments = ManagedLayout.ArrangeGrid(
                    corrected,
                    style.Columns,
                    style.Rows,
                    inner.Width,
                    inner.Height,
                    style.ColumnGap,
                    style.RowGap,
                    style.CrossAlignment
                );
            }
            else
            {
                assignments = ManagedLayout.ArrangeFlex(
                    specs,
                    style.Axis,
                    inner.Width,
                    inner.Height,
                    style.Spacing,
                    style.RowGap,
                    style.Wrap,
                    style.MainAlignment,
                    style.CrossAlignment
                );
            }

            var byIndex = assignments.ToDictionary(value => value.Index);
            for (var index = 0; index < element.Children.Count; index++)
            {
                var child = element.Children[index];
                if (child.Participation == ElementParticipation.Collapsed)
                {
                    AddCollapsedBoxes(child, inner.X, inner.Y, boxes);
                    continue;
                }
                var assignment = byIndex[index];
                var relative = assignment.Bounds;
                var childBounds = new LayoutRect(
                    Finite(inner.X + relative.X - style.Scroll.X),
                    Finite(inner.Y + relative.Y - style.Scroll.Y),
                    relative.Width,
                    relative.Height
                );
                childNodes.AddRange(
                    Layout(
                        child,
                        childBounds,
                        viewport,
                        shaper,
                        boxes,
                        cache,
                        style.Axis,
                        style.Axis == LayoutAxis.Row
                            ? assignment.HeightAssigned
                            : assignment.WidthAssigned,
                        style.Axis == LayoutAxis.Row
                            ? assignment.WidthAssigned
                            : assignment.HeightAssigned
                    )
                );
            }
        }
        if (element.Participation == ElementParticipation.Hidden)
            return [];
        var identity = new ElementIdentity(element.Composition.Epoch, element.Id);
        var result = new List<SceneNode>();
        if (style.Background.Color is not { A: 0 })
            result.Add(
                new PaintSceneNode(new(identity, SceneNodeKind.Paint), bounds, style.Background)
            );
        if (text is not null)
        {
            var viewOffset = style.Caret is { } caretOffset
                ? TextViewOffset(text, style.Text!, caretOffset, inner.Width)
                : 0;
            var textBounds = new LayoutRect(
                inner.X - viewOffset - style.Scroll.X,
                inner.Y - style.Scroll.Y,
                inner.Width + viewOffset + style.Scroll.X,
                Math.Max(inner.Height, text.Height)
            );
            if (
                style.SelectionStart is { } start
                && style.SelectionEnd is { } end
                && start >= 0
                && end > start
                && end <= style.Text!.Length
            )
            {
                if (style.TextWrap == TextWrap.NoWrap)
                {
                    var left = TextPosition(text, style.Text!, start);
                    var right = TextPosition(text, style.Text!, end);
                    result.Add(
                        new PaintSceneNode(
                            new(identity, SceneNodeKind.Selection),
                            new(
                                textBounds.X + Math.Min(left, right),
                                inner.Y,
                                MathF.Abs(right - left),
                                inner.Height
                            ),
                            Color.FromArgb(0x66, 0x3b, 0x82, 0xf6)
                        )
                    );
                }
                else
                {
                    var selectionRects = text.SelectionBounds(
                        style.Text!,
                        Math.Min(start, end),
                        Math.Max(start, end)
                    );
                    foreach (var selection in selectionRects)
                        result.Add(
                            new PaintSceneNode(
                                new(identity, SceneNodeKind.Selection),
                                new(
                                    textBounds.X + selection.X,
                                    textBounds.Y + selection.Y,
                                    selection.Width,
                                    selection.Height
                                ),
                                Color.FromArgb(0x66, 0x3b, 0x82, 0xf6)
                            )
                        );
                }
            }
            result.Add(
                new TextSceneNode(
                    new(identity, SceneNodeKind.Text),
                    textBounds,
                    style.TextColor,
                    text
                )
            );
            if (style.Caret is { } caret && caret >= 0 && caret <= style.Text!.Length)
            {
                if (style.TextWrap == TextWrap.NoWrap)
                {
                    result.Add(
                        new PaintSceneNode(
                            new(identity, SceneNodeKind.Caret),
                            new(
                                textBounds.X + TextPosition(text, style.Text!, caret),
                                inner.Y,
                                1 / viewport.Scale,
                                inner.Height
                            ),
                            style.TextColor
                        )
                    );
                }
                else
                {
                    var caretBounds = text.CaretBounds(
                        style.Text!,
                        caret,
                        style.CaretAffinity,
                        1 / viewport.Scale
                    );
                    result.Add(
                        new PaintSceneNode(
                            new(identity, SceneNodeKind.Caret),
                            new(
                                textBounds.X + caretBounds.X,
                                textBounds.Y + caretBounds.Y,
                                caretBounds.Width,
                                Math.Max(caretBounds.Height, 1 / viewport.Scale)
                            ),
                            style.TextColor
                        )
                    );
                }
            }
        }
        else if (style.Text is "" && style.Caret == 0)
            result.Add(
                new PaintSceneNode(
                    new(identity, SceneNodeKind.Caret),
                    new(inner.X, inner.Y, 1 / viewport.Scale, inner.Height),
                    style.TextColor
                )
            );
        result.AddRange(childNodes);
        if (style.Clip)
        {
            var clipped = new List<SceneNode>();
            if (
                result.FirstOrDefault()
                    is PaintSceneNode { Identity.Kind: SceneNodeKind.Paint } background
                && background.Identity.Element == identity
            )
            {
                clipped.Add(background);
                result.RemoveAt(0);
            }
            clipped.Add(new ClipSceneNode(new(identity, SceneNodeKind.Clip), inner, result));
            result = clipped;
        }
        return style.Opacity == 1 || result.Count == 0
            ? result
            :
            [
                new OpacitySceneNode(
                    new(identity, SceneNodeKind.Opacity),
                    VisibleBounds(result, viewport),
                    style.Opacity,
                    result
                ),
            ];
    }

    private static void AddCollapsedBoxes(Element element, float x, float y, List<LayoutBox> boxes)
    {
        boxes.Add(
            new(
                new ElementIdentity(element.Composition.Epoch, element.Id),
                new LayoutRect(x, y, 0, 0),
                null
            )
        );
        foreach (var child in element.Children)
            AddCollapsedBoxes(child, x, y, boxes);
    }

    private static bool IsCollapsed(Element element)
    {
        for (Element? current = element; current is not null; current = current.Parent)
            if (current.Participation == ElementParticipation.Collapsed)
                return true;
        return false;
    }

    private static LayoutRect VisibleBounds(IReadOnlyList<SceneNode> nodes, LayoutViewport viewport)
    {
        var left = nodes.Min(node => node.Bounds.X);
        var top = nodes.Min(node => node.Bounds.Y);
        var right = nodes.Max(node => Finite(node.Bounds.X + node.Bounds.Width));
        var bottom = nodes.Max(node => Finite(node.Bounds.Y + node.Bounds.Height));
        var visibleRight = Math.Min(right, viewport.Width);
        var visibleBottom = Math.Min(bottom, viewport.Height);
        left = Math.Clamp(left, 0, viewport.Width);
        top = Math.Clamp(top, 0, viewport.Height);
        return new(left, top, Math.Max(0, visibleRight - left), Math.Max(0, visibleBottom - top));
    }

    private static Measurement Measure(
        Element element,
        LayoutAxis parentAxis,
        float crossLimit,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache
    )
    {
        var style = cache.Read(element);
        var text = Shape(element, style, scale, shaper, cache);
        var intrinsic = Intrinsic(element, style, text, scale, shaper, cache);
        var width = Constrain(style.Width ?? intrinsic.Width, style.MinWidth, style.MaxWidth);
        var height = Constrain(style.Height ?? intrinsic.Height, style.MinHeight, style.MaxHeight);
        return parentAxis == LayoutAxis.Row
            ? new(
                element,
                width,
                height,
                style.Height is null,
                style.MainGrow,
                style.MinWidth,
                style.MaxWidth,
                style.MainShrink
            )
            : new(
                element,
                height,
                width,
                style.Width is null,
                style.MainGrow,
                style.MinHeight,
                style.MaxHeight,
                style.MainShrink
            );
    }

    private static void GrowMain(Measurement[] measured, float available)
    {
        if (available <= 0 || !measured.Any(item => item.MainGrow > 0))
            return;
        for (var pass = 0; pass <= measured.Length && available > 0; pass++)
        {
            var eligible = measured
                .Select((item, index) => (Item: item, Index: index))
                .Where(value => value.Item.MainGrow > 0 && value.Item.Main < value.Item.MaxMain)
                .ToArray();
            if (eligible.Length == 0)
                return;
            var scale = eligible.Max(value => value.Item.MainGrow);
            var normalizedTotal = eligible.Sum(value => value.Item.MainGrow / scale);
            var consumed = 0f;
            foreach (var value in eligible)
            {
                var share = Finite(available * (value.Item.MainGrow / scale) / normalizedTotal);
                var next = Constrain(
                    Finite(value.Item.Main + share),
                    value.Item.MinMain,
                    value.Item.MaxMain
                );
                consumed = Finite(consumed + next - value.Item.Main);
                measured[value.Index] = value.Item with { Main = next };
            }
            if (consumed <= 0)
                return;
            available = Math.Max(0, Finite(available - consumed));
        }
    }

    private static (float Width, float Height) Intrinsic(
        Element element,
        Values style,
        ShapedText? text,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache
    )
    {
        var textMetrics = IntrinsicTextMetrics(style, text, scale, shaper, cache);
        var width = textMetrics?.Width ?? 0f;
        var height = textMetrics?.Height ?? 0f;
        var participatingChildren = element
            .Children.Where(child => child.Participation != ElementParticipation.Collapsed)
            .ToArray();
        if (participatingChildren.Length == 0)
            return Outer(
                style,
                style.VirtualRowHeight is { } emptyRowHeight
                    ? (width, Finite(emptyRowHeight * style.VirtualItemCount))
                    : (width, height)
            );
        var children = participatingChildren
            .Select(child => Measure(child, style.Axis, float.MaxValue, scale, shaper, cache))
            .ToArray();
        if (style.Axis == LayoutAxis.Row)
        {
            foreach (var child in children)
                width = Finite(width + child.Main);
            width = Finite(width + Finite(style.Spacing * Math.Max(0, children.Length - 1)));
            foreach (var child in children)
                height = Math.Max(height, child.Cross);
        }
        else
        {
            foreach (var child in children)
                height = Finite(height + child.Main);
            height = Finite(height + Finite(style.Spacing * Math.Max(0, children.Length - 1)));
            foreach (var child in children)
                width = Math.Max(width, child.Cross);
        }
        if (style.VirtualRowHeight is { } rowHeight)
            height = Finite(rowHeight * style.VirtualItemCount);
        return Outer(style, (Finite(width), Finite(height)));
    }

    private static (float Width, float Height) Outer(
        Values style,
        (float Width, float Height) content
    ) =>
        (
            Finite(content.Width + style.Padding.Horizontal),
            Finite(content.Height + style.Padding.Vertical)
        );

    internal static LayoutRect ContentBounds(LayoutRect outer, Insets padding, float scale)
    {
        static float Edge(float value, float scale) =>
            MathF.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
        var outerRight = Finite(outer.X + outer.Width);
        var outerBottom = Finite(outer.Y + outer.Height);
        var left = Math.Min(outerRight, Edge(Finite(outer.X + padding.Left), scale));
        var top = Math.Min(outerBottom, Edge(Finite(outer.Y + padding.Top), scale));
        var right = Math.Max(
            left,
            Math.Min(outerRight, Edge(Finite(outerRight - padding.Right), scale))
        );
        var bottom = Math.Max(
            top,
            Math.Min(outerBottom, Edge(Finite(outerBottom - padding.Bottom), scale))
        );
        return new(left, top, right - left, bottom - top);
    }

    private static ShapedText? Shape(
        Element element,
        Values style,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache,
        LayoutConstraint inlineConstraint = default,
        LayoutConstraint blockConstraint = default
    )
    {
        if (string.IsNullOrEmpty(style.Text))
            return null;
        return ShapeText(
            style.Text,
            style,
            scale,
            shaper,
            cache,
            inlineConstraint,
            blockConstraint
        );
    }

    private static ShapedText? ShapeText(
        string? text,
        Values style,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache,
        LayoutConstraint inlineConstraint = default,
        LayoutConstraint blockConstraint = default
    )
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var request = new TextMeasureRequest(
            text,
            style.FontFamily,
            style.FontSize,
            style.Language,
            style.Direction,
            scale,
            inlineConstraint,
            style.Multiline ? default : blockConstraint,
            style.TextWrap,
            style.MaxLines,
            style.TextOverflow
        );
        request.Validate();
        if (cache.Shapes.TryGetValue(request, out var cached))
            return cached;
        var shaped = shaper.Shape(request);
        shaped.Validate(request);
        cache.Shapes.Add(request, shaped);
        return shaped;
    }

    private static (float Width, float Height)? IntrinsicTextMetrics(
        Values style,
        ShapedText? text,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache
    )
    {
        var measuredText =
            style.TextMeasure == style.Text
                ? text
                : ShapeText(style.TextMeasure, style, scale, shaper, cache);
        if (text is null && measuredText is null)
            return null;
        return (
            Math.Max(text?.Width ?? 0f, measuredText?.Width ?? 0f),
            Math.Max(text?.Height ?? 0f, measuredText?.Height ?? 0f)
        );
    }

    private static float Constrain(float value, float min, float max)
    {
        if (
            !float.IsFinite(value)
            || !float.IsFinite(min)
            || (!float.IsFinite(max) && !float.IsPositiveInfinity(max))
            || value < 0
            || min < 0
            || max < min
        )
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "LayoutProperties sizes must be finite/nonnegative and min must not exceed max."
            );
        return Math.Clamp(value, min, max);
    }

    private static float Finite(float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Layout arithmetic overflow.");
        return value;
    }

    private static Values ReadResolved(Element element)
    {
        var values = new Values(
            element.Resolve(LayoutProperties.Mode).Value,
            element.Resolve(LayoutProperties.Axis).Value,
            element.Resolve(LayoutProperties.Columns).Value,
            element.Resolve(LayoutProperties.Rows).Value,
            element.Resolve(LayoutProperties.ColumnGap).Value,
            element.Resolve(LayoutProperties.RowGap).Value,
            element.Resolve(LayoutProperties.GridPlacement).Value,
            element.Resolve(LayoutProperties.Width).Value,
            element.Resolve(LayoutProperties.Height).Value,
            element.Resolve(LayoutProperties.MinWidth).Value,
            element.Resolve(LayoutProperties.MinHeight).Value,
            element.Resolve(LayoutProperties.MaxWidth).Value,
            element.Resolve(LayoutProperties.MaxHeight).Value,
            element.Resolve(LayoutProperties.Spacing).Value,
            element.Resolve(LayoutProperties.MainGrow).Value,
            element.Resolve(LayoutProperties.MainBasis).Value,
            element.Resolve(LayoutProperties.MainShrink).Value,
            element.Resolve(LayoutProperties.Wrap).Value,
            element.Resolve(LayoutProperties.MainAlignment).Value,
            element.Resolve(LayoutProperties.CrossAlignment).Value,
            element.Resolve(LayoutProperties.Clip).Value,
            element.Resolve(LayoutProperties.Padding).Value,
            element.Resolve(LayoutProperties.Scroll).Value,
            element.Resolve(LayoutProperties.VirtualRowHeight).Value,
            element.Resolve(LayoutProperties.VirtualItemCount).Value,
            element.Resolve(LayoutProperties.VirtualRowIndex).Value,
            element.Resolve(VisualProperties.Background).Value,
            element.Resolve(VisualProperties.Opacity).Value,
            element.Resolve(TypographyProperties.TextColor).Value,
            element.Resolve(ProjectionProperties.Text).Value,
            element.Resolve(ProjectionProperties.TextMeasure).Value,
            element.Resolve(TypographyProperties.FontFamily).Value,
            element.Resolve(TypographyProperties.FontSize).Value,
            element.Resolve(TypographyProperties.Language).Value,
            element.Resolve(TypographyProperties.Direction).Value,
            element.Resolve(TypographyProperties.TextWrap).Value,
            element.Resolve(TypographyProperties.MaxLines).Value,
            element.Resolve(TypographyProperties.Overflow).Value,
            element.Resolve(ProjectionProperties.TextSelectionStart).Value,
            element.Resolve(ProjectionProperties.TextSelectionEnd).Value,
            element.Resolve(ProjectionProperties.TextCaret).Value,
            element.Resolve(ProjectionProperties.TextMultiline).Value,
            element.Resolve(ProjectionProperties.TextCaretAffinity).Value
        );
        if (
            !Enum.IsDefined(values.Mode)
            || !Enum.IsDefined(values.Axis)
            || !Enum.IsDefined(values.TextWrap)
            || !Enum.IsDefined(values.TextOverflow)
            || !Enum.IsDefined(values.MainAlignment)
            || !Enum.IsDefined(values.CrossAlignment)
            || !Enum.IsDefined(values.Direction)
            || !float.IsFinite(values.Spacing)
            || values.Spacing < 0
            || !float.IsFinite(values.MainGrow)
            || values.MainGrow < 0
            || values.MainBasis is { } basis && (!float.IsFinite(basis) || basis < 0)
            || !float.IsFinite(values.MainShrink)
            || values.MainShrink < 0
            || !float.IsFinite(values.ColumnGap)
            || values.ColumnGap < 0
            || !float.IsFinite(values.RowGap)
            || values.RowGap < 0
            || values.MaxLines is <= 0
            || values.Columns is null
            || values.Rows is null
            || values.Mode == LayoutMode.Grid
                && (values.Columns.Count == 0 || values.Rows.Count == 0)
            || !float.IsFinite(values.Opacity)
            || values.Opacity < 0
            || values.Opacity > 1
            || values.Width is { } width && (!float.IsFinite(width) || width < 0)
            || values.Height is { } height && (!float.IsFinite(height) || height < 0)
            || values.VirtualRowHeight is { } rowHeight
                && (
                    !float.IsFinite(rowHeight)
                    || rowHeight <= 0
                    || values.VirtualItemCount < 0
                    || values.VirtualRowIndex < 0
                )
        )
            throw new ArgumentOutOfRangeException(
                nameof(element),
                "LayoutProperties values must be finite and nonnegative."
            );
        values.Scroll.Validate();
        return values;
    }

    private readonly record struct Measurement(
        Element Element,
        float Main,
        float Cross,
        bool AutoCross,
        float MainGrow,
        float MinMain,
        float MaxMain,
        float MainShrink
    );

    private sealed class ProjectionCache
    {
        private readonly Dictionary<long, Values> _styles = [];

        internal Dictionary<TextMeasureRequest, ShapedText> Shapes { get; } = [];

        internal Values Read(Element element)
        {
            if (_styles.TryGetValue(element.Id, out var cached))
                return cached;
            var resolved = ReadResolved(element);
            _styles.Add(element.Id, resolved);
            return resolved;
        }
    }

    private readonly record struct Values(
        LayoutMode Mode,
        LayoutAxis Axis,
        GridTracks Columns,
        GridTracks Rows,
        float ColumnGap,
        float RowGap,
        GridPlacement? GridPlacement,
        float? Width,
        float? Height,
        float MinWidth,
        float MinHeight,
        float MaxWidth,
        float MaxHeight,
        float Spacing,
        float MainGrow,
        float? MainBasis,
        float MainShrink,
        bool Wrap,
        LayoutAlignment MainAlignment,
        LayoutAlignment CrossAlignment,
        bool Clip,
        Insets Padding,
        ScrollOffset Scroll,
        float? VirtualRowHeight,
        int VirtualItemCount,
        int VirtualRowIndex,
        Brush Background,
        float Opacity,
        Color TextColor,
        string? Text,
        string? TextMeasure,
        string FontFamily,
        float FontSize,
        string Language,
        TextDirection Direction,
        TextWrap TextWrap,
        int? MaxLines,
        TextOverflow TextOverflow,
        int? SelectionStart,
        int? SelectionEnd,
        int? Caret,
        bool Multiline,
        TextAffinity CaretAffinity
    );

    /// <summary>Returns a bounded LTR caret position; it interpolates grapheme boundaries inside one ligature cluster and does not implement full bidi caret ordering.</summary>
    internal static float TextPosition(ShapedText text, string source, int utf16Offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(source);
        if (utf16Offset < 0 || utf16Offset > source.Length)
            throw new ArgumentOutOfRangeException(nameof(utf16Offset));
        if (utf16Offset == source.Length)
            return text.Width;
        var glyphs = text
            .Runs.SelectMany(run => run.Glyphs.Select(glyph => (Run: run, Glyph: glyph)))
            .ToArray();
        var current = glyphs
            .Where(value => value.Glyph.Cluster <= utf16Offset)
            .OrderByDescending(value => value.Glyph.Cluster)
            .FirstOrDefault();
        if (current.Glyph.GlyphId != 0 && current.Run.Direction == TextDirection.LeftToRight)
        {
            var cluster = (int)current.Glyph.Cluster;
            var end = glyphs
                .Where(value => value.Glyph.Cluster > current.Glyph.Cluster)
                .Select(value => (int)value.Glyph.Cluster)
                .DefaultIfEmpty(source.Length)
                .Min();
            var boundaries = StringInfo
                .ParseCombiningCharacters(source)
                .Append(source.Length)
                .Where(value => value >= cluster && value <= end)
                .ToArray();
            var position = Array.IndexOf(boundaries, utf16Offset);
            if (boundaries.Length > 2 && position >= 0)
            {
                var clusterGlyphs = glyphs
                    .Where(value => value.Glyph.Cluster == current.Glyph.Cluster)
                    .ToArray();
                var left = clusterGlyphs.Min(value => value.Run.OriginX + value.Glyph.X);
                var right = clusterGlyphs.Max(value =>
                    value.Run.OriginX + value.Glyph.X + value.Glyph.XAdvance
                );
                return left + (right - left) * position / (boundaries.Length - 1);
            }
        }
        var next = glyphs
            .Where(value => value.Glyph.Cluster >= utf16Offset)
            .OrderBy(value => value.Glyph.Cluster)
            .FirstOrDefault();
        return next.Glyph.GlyphId == 0 ? text.Width : next.Run.OriginX + next.Glyph.X;
    }

    internal static float TextViewOffset(ShapedText text, string source, int caret, float width)
    {
        if (!float.IsFinite(width) || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return Math.Clamp(
            TextPosition(text, source, caret) - Math.Max(0, width - 1),
            0,
            Math.Max(0, text.Width - width + 1)
        );
    }

    internal static string InputSignature(Element element)
    {
        var mode = element.Resolve(LayoutProperties.Mode).Value;
        var axis = element.Resolve(LayoutProperties.Axis).Value;
        var columns = element.Resolve(LayoutProperties.Columns).Value;
        var rows = element.Resolve(LayoutProperties.Rows).Value;
        if (columns is null || rows is null)
            throw new ArgumentOutOfRangeException(
                nameof(element),
                "Grid track collections must not be null."
            );
        var columnGap = element.Resolve(LayoutProperties.ColumnGap).Value;
        var rowGap = element.Resolve(LayoutProperties.RowGap).Value;
        var placement = element.Resolve(LayoutProperties.GridPlacement).Value;
        var width = element.Resolve(LayoutProperties.Width).Value;
        var height = element.Resolve(LayoutProperties.Height).Value;
        var minWidth = element.Resolve(LayoutProperties.MinWidth).Value;
        var minHeight = element.Resolve(LayoutProperties.MinHeight).Value;
        var maxWidth = element.Resolve(LayoutProperties.MaxWidth).Value;
        var maxHeight = element.Resolve(LayoutProperties.MaxHeight).Value;
        var spacing = element.Resolve(LayoutProperties.Spacing).Value;
        var mainGrow = element.Resolve(LayoutProperties.MainGrow).Value;
        var mainBasis = element.Resolve(LayoutProperties.MainBasis).Value;
        var mainShrink = element.Resolve(LayoutProperties.MainShrink).Value;
        var wrap = element.Resolve(LayoutProperties.Wrap).Value;
        var mainAlignment = element.Resolve(LayoutProperties.MainAlignment).Value;
        var crossAlignment = element.Resolve(LayoutProperties.CrossAlignment).Value;
        var clip = element.Resolve(LayoutProperties.Clip).Value;
        var padding = element.Resolve(LayoutProperties.Padding).Value;
        var scroll = element.Resolve(LayoutProperties.Scroll).Value;
        var virtualRowHeight = element.Resolve(LayoutProperties.VirtualRowHeight).Value;
        var virtualItemCount = element.Resolve(LayoutProperties.VirtualItemCount).Value;
        var virtualRowIndex = element.Resolve(LayoutProperties.VirtualRowIndex).Value;
        var text = element.Resolve(ProjectionProperties.Text).Value;
        var textMeasure = element.Resolve(ProjectionProperties.TextMeasure).Value;
        var fontFamily = element.Resolve(TypographyProperties.FontFamily).Value;
        var fontSize = element.Resolve(TypographyProperties.FontSize).Value;
        var language = element.Resolve(TypographyProperties.Language).Value;
        var direction = element.Resolve(TypographyProperties.Direction).Value;
        var textWrap = element.Resolve(TypographyProperties.TextWrap).Value;
        var maxLines = element.Resolve(TypographyProperties.MaxLines).Value;
        var textOverflow = element.Resolve(TypographyProperties.Overflow).Value;
        var selectionStart = element.Resolve(ProjectionProperties.TextSelectionStart).Value;
        var selectionEnd = element.Resolve(ProjectionProperties.TextSelectionEnd).Value;
        var caret = element.Resolve(ProjectionProperties.TextCaret).Value;
        var multiline = element.Resolve(ProjectionProperties.TextMultiline).Value;
        var caretAffinity = element.Resolve(ProjectionProperties.TextCaretAffinity).Value;
        var enabled = element.Resolve(InputProperties.Enabled).Value;
        var visible = element.Resolve(InputProperties.Visible).Value;
        var participation = element.Participation;
        return Hash(writer =>
        {
            writer.Write((int)mode);
            writer.Write((int)axis);
            WriteTracks(writer, columns);
            WriteTracks(writer, rows);
            writer.Write(columnGap);
            writer.Write(rowGap);
            writer.Write(placement.HasValue);
            if (placement is { } grid)
            {
                writer.Write(grid.Row);
                writer.Write(grid.Column);
                writer.Write(grid.RowSpan);
                writer.Write(grid.ColumnSpan);
            }
            writer.Write(width.HasValue);
            if (width is { } resolvedWidth)
                writer.Write(resolvedWidth);
            writer.Write(height.HasValue);
            if (height is { } resolvedHeight)
                writer.Write(resolvedHeight);
            writer.Write(minWidth);
            writer.Write(minHeight);
            writer.Write(maxWidth);
            writer.Write(maxHeight);
            writer.Write(spacing);
            writer.Write(mainGrow);
            writer.Write(mainBasis.HasValue);
            if (mainBasis is { } basis)
                writer.Write(basis);
            writer.Write(mainShrink);
            writer.Write(wrap);
            writer.Write((int)mainAlignment);
            writer.Write((int)crossAlignment);
            writer.Write(clip);
            writer.Write(padding.Left);
            writer.Write(padding.Top);
            writer.Write(padding.Right);
            writer.Write(padding.Bottom);
            writer.Write(scroll.X);
            writer.Write(scroll.Y);
            writer.Write(virtualRowHeight.HasValue);
            if (virtualRowHeight is { } rowHeight)
                writer.Write(rowHeight);
            writer.Write(virtualItemCount);
            writer.Write(virtualRowIndex);
            writer.Write(text is not null);
            if (text is { } resolvedText)
                writer.Write(resolvedText);
            writer.Write(textMeasure is not null);
            if (textMeasure is { } resolvedTextMeasure)
                writer.Write(resolvedTextMeasure);
            writer.Write(fontFamily);
            writer.Write(fontSize);
            writer.Write(language);
            writer.Write((int)direction);
            writer.Write((int)textWrap);
            writer.Write(maxLines.HasValue);
            if (maxLines is { } resolvedMaxLines)
                writer.Write(resolvedMaxLines);
            writer.Write((int)textOverflow);
            writer.Write(selectionStart.HasValue);
            if (selectionStart is { } resolvedSelectionStart)
                writer.Write(resolvedSelectionStart);
            writer.Write(selectionEnd.HasValue);
            if (selectionEnd is { } resolvedSelectionEnd)
                writer.Write(resolvedSelectionEnd);
            writer.Write(multiline);
            writer.Write((int)caretAffinity);
            writer.Write(caret.HasValue);
            if (caret is { } resolvedCaret)
                writer.Write(resolvedCaret);
            writer.Write(enabled);
            writer.Write(visible);
            writer.Write((int)participation);
        });
    }

    private static void WriteTracks(BinaryWriter writer, GridTracks tracks)
    {
        writer.Write(tracks.Count);
        foreach (var track in tracks)
        {
            writer.Write((int)track.Kind);
            writer.Write(track.Minimum);
            writer.Write(track.Maximum);
            writer.Write(track.FractionWeight);
        }
    }

    private static string Hash(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
