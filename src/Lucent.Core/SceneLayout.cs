using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lucent.Core;

/// <summary>Projects a retained composition into immutable layout, scene, and input data.</summary>
public static partial class SceneLayout
{
    private const int MaxStructuralDiscoveryRounds = 8;

    /// <summary>Lays out the current composition and returns a fresh retained scene; callers install it in the input router.</summary>
    public static RetainedScene Project(
        Composition composition,
        LayoutViewport viewport,
        ITextShaper shaper
    ) => Project(composition, viewport, shaper, ReactiveGraph.DefaultMaximumWorkItems);

    /// <summary>Lays out the current composition while bounding reactive work performed by the projection.</summary>
    public static RetainedScene Project(
        Composition composition,
        LayoutViewport viewport,
        ITextShaper shaper,
        int maximumWorkItems
    )
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(shaper);
        if (maximumWorkItems <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumWorkItems),
                "The projection drain limit must be positive."
            );
        viewport.Validate();
        var structuralDiscoveryRounds = 0;
        FlushAndSynchronizeAvailability(
            composition,
            maximumWorkItems,
            ref structuralDiscoveryRounds
        );
        var windowBreakpointDiscovery = DiscoverWindowBreakpoints(
            composition,
            viewport.Width,
            maximumWorkItems,
            ref structuralDiscoveryRounds
        );
        var windowBreakpoints = windowBreakpointDiscovery.Elements;
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
                    false,
                    false
                )
            );
            var assignedById = assignedBoxes.ToDictionary(
                box => box.Identity.ElementId,
                box => box.Bounds
            );
            // A responsive branch may introduce another responsive control (for
            // example a SplitPane). Discover that finite tree before realizing rows.
            // Stable containers still get one correction pass; geometry feedback is
            // rejected below rather than iterated until an arbitrary layout settles.
            for (; ; )
            {
                foreach (var item in responsive)
                {
                    var inner = ContentBounds(
                        item.Element,
                        assignedById[item.Element.Id],
                        viewport.Scale
                    );
                    item.State.Assign(new(inner.Width, inner.Height));
                }
                FlushAndSynchronizeAvailability(
                    composition,
                    maximumWorkItems,
                    ref structuralDiscoveryRounds
                );
                var previousWindowBreakpoints = windowBreakpoints;
                var discoveredWindows = DiscoverWindowBreakpoints(
                    composition,
                    viewport.Width,
                    maximumWorkItems,
                    ref structuralDiscoveryRounds
                );
                windowBreakpoints = discoveredWindows.Elements;
                var windowRegistrationChanged =
                    discoveredWindows.RegistrationChanged
                    || !windowBreakpoints.SequenceEqual(previousWindowBreakpoints);
                if (
                    windowRegistrationChanged
                    && !discoveredWindows.RegistrationChanged
                    && !TryConsumeStructuralDiscoveryRound(ref structuralDiscoveryRounds)
                )
                    throw new InvalidOperationException(
                        "Window breakpoint registration did not stabilize within eight passes."
                    );
                var current = ResponsiveElements(composition);
                var responsiveRegistrationChanged = !current.SequenceEqual(responsive);
                if (!responsiveRegistrationChanged && !windowRegistrationChanged)
                    break;
                if (
                    responsiveRegistrationChanged
                    && !windowRegistrationChanged
                    && !TryConsumeStructuralDiscoveryRound(ref structuralDiscoveryRounds)
                )
                    throw new InvalidOperationException(
                        "Responsive container discovery did not stabilize within eight passes."
                    );
                responsive = current;
                assignedBoxes.Clear();
                _ = composition.WithoutProjectionTracking(() =>
                    Layout(
                        composition.Root,
                        new LayoutRect(0, 0, viewport.Width, viewport.Height),
                        viewport,
                        shaper,
                        assignedBoxes,
                        new ProjectionCache(),
                        false,
                        false
                    )
                );
                assignedById = assignedBoxes.ToDictionary(
                    box => box.Identity.ElementId,
                    box => box.Bounds
                );
            }
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
            // Newly realized rows must have their disabled styles before final geometry is captured.
            FlushAndSynchronizeAvailability(
                composition,
                maximumWorkItems,
                ref structuralDiscoveryRounds
            );
            EnsureResponsiveElementsUnchanged(composition, responsive);
            if (!composition.VirtualizedViewportIds.SequenceEqual(virtualizedViewportIds))
                throw new InvalidOperationException(
                    "A virtualized region was mounted or removed during its realization flush."
                );
        }
        EnsureWindowBreakpointElementsUnchanged(composition, windowBreakpoints);
        composition.CommitPresentationTargets();
        var projected = composition.CaptureInputProjection(() =>
        {
            var elements = composition.Elements().ToArray();
            // The mutation guard already resolves every input-relevant style before
            // layout. Reuse that pass-local snapshot for participating elements;
            // the fresh signatures below still reject changes made during layout.
            var resolvedStyles = elements
                .Where(element => element.Participation != ElementParticipation.Collapsed)
                .ToDictionary(element => element.Id, ReadInputTrackedResolved);
            var signatures = elements
                .Select(element =>
                    resolvedStyles.TryGetValue(element.Id, out var resolved)
                        ? InputSignature(element, resolved)
                        : InputSignature(element)
                )
                .ToArray();
            var boxes = new List<LayoutBox>();
            var cache = new ProjectionCache(resolvedStyles);
            var nodes = composition.WithoutProjectionTracking(() =>
                Layout(
                    composition.Root,
                    new LayoutRect(0, 0, viewport.Width, viewport.Height),
                    viewport,
                    shaper,
                    boxes,
                    cache,
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
                        var bounds = byId[element.Id].Bounds;
                        var clip = element.ResolveValue(LayoutProperties.Clip);
                        LayoutRect? childClipBounds = clip
                            ? ContentBounds(element, bounds, viewport.Scale)
                            : null;
                        var childClipCornerRadius = childClipBounds is { } childClip
                            ? InnerCornerRadius(
                                element.ResolveValue(VisualProperties.CornerRadius),
                                bounds,
                                childClip
                            )
                            : 0;
                        return new RetainedInputElement(
                            new(composition.Epoch, element.Id),
                            element.Parent is null
                                ? null
                                : new(composition.Epoch, element.Parent.Id),
                            bounds,
                            childClipBounds,
                            order,
                            element.ResolveValue(InputProperties.Enabled),
                            element.ResolveValue(InputProperties.Visible)
                                && element.ParticipatesInInput(),
                            signature,
                            childClipCornerRadius,
                            element.ResolveValue(InputProperties.PointerTransparent)
                        );
                    }
                )
                .ToArray();
            var collapsed = elements.Where(IsCollapsed).Select(element => element.Id).ToArray();
            return (Boxes: boxes, Nodes: nodes, Input: input, Collapsed: collapsed, Cache: cache);
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
            var inner = ContentBounds(item.Element, outer, viewport.Scale);
            if (item.State!.Current != new ContainerConstraints(inner.Width, inner.Height))
                throw new InvalidOperationException(
                    "Responsive container feedback changed its own assigned constraints after the bounded correction pass."
                );
        }
        var scrollBars = BuildScrollBars(
            composition,
            projected.Boxes,
            projected.Collapsed,
            viewport
        );
        // Only the final assigned boxes establish image demand. Discovery layouts
        // create borrowed slots, never leases or IO, and may be discarded.
        foreach (var box in projected.Boxes)
        {
            var element = composition.Find(box.Identity);
            if (element?.Image is not { } image)
                continue;
            image.Request(
                ContentBounds(element, box.Bounds, viewport.Scale),
                viewport.Scale,
                element.ResolveValue(ImageProperties.Fit),
                element.ResolveValue(ImageProperties.ImageZoom)
            );
        }
        var nodes = ResolveImages(InsertScrollBars(projected.Nodes, scrollBars), viewport.Scale);
        var scene = new RetainedScene(
            composition.NextSceneGeneration(),
            viewport,
            projected.Boxes,
            nodes,
            projected.Input,
            composition.InputProjectionRevision,
            projected.Collapsed,
            scrollBars
        );
        scene.PaintSnapshot = new PaintSnapshot(
            composition,
            shaper,
            projected.Cache.PaintPlans.GetValueOrDefault(composition.Root.Id),
            viewport
        );
        composition.CapturePresentationFrame(scene.Generation);
        return scene;
    }

    private static IReadOnlyList<SceneNode> ResolveImages(
        IReadOnlyList<SceneNode> nodes,
        float scale
    )
    {
        List<SceneNode>? result = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var resolved = node switch
            {
                ImageSlotSceneNode image => image.Resolve(scale),
                ClipSceneNode clip => ResolveClipImages(clip, scale),
                OpacitySceneNode opacity => ResolveOpacityImages(opacity, scale),
                _ => node,
            };
            if (result is null && !ReferenceEquals(node, resolved))
                result = [.. nodes.Take(index)];
            result?.Add(resolved);
        }
        return result ?? nodes;
    }

    private static SceneNode ResolveClipImages(ClipSceneNode clip, float scale)
    {
        var children = ResolveImages(clip.Children, scale);
        return ReferenceEquals(children, clip.Children)
            ? clip
            : new ClipSceneNode(clip.Identity, clip.Bounds, children, clip.CornerRadius);
    }

    private static SceneNode ResolveOpacityImages(OpacitySceneNode opacity, float scale)
    {
        var children = ResolveImages(opacity.Children, scale);
        return ReferenceEquals(children, opacity.Children)
            ? opacity
            : new OpacitySceneNode(opacity.Identity, opacity.Bounds, opacity.Opacity, children);
    }

    private static IReadOnlyList<SceneNode> InsertScrollBars(
        IReadOnlyList<SceneNode> nodes,
        IReadOnlyList<RetainedScrollBar> scrollBars
    )
    {
        if (scrollBars.Count == 0)
            return nodes;
        var byViewport = scrollBars
            .GroupBy(scrollBar => scrollBar.Viewport.ElementId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        return InsertScrollBars(nodes, byViewport);
    }

    private static IReadOnlyList<SceneNode> InsertScrollBars(
        IReadOnlyList<SceneNode> nodes,
        IReadOnlyDictionary<long, RetainedScrollBar[]> byViewport
    )
    {
        var result = new List<SceneNode>(nodes.Count);
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ClipSceneNode clip:
                {
                    result.Add(
                        new ClipSceneNode(
                            clip.Identity,
                            clip.Bounds,
                            InsertScrollBars(clip.Children, byViewport),
                            clip.CornerRadius
                        )
                    );
                    // The gutter is outside this viewport's content clip, but remains
                    // inside the ancestor's clip and opacity group.
                    if (byViewport.TryGetValue(clip.Identity.Element.ElementId, out var bars))
                        foreach (var bar in bars)
                        {
                            result.Add(
                                new PaintSceneNode(
                                    new(bar.Viewport, SceneNodeKind.ScrollBarTrack),
                                    bar.Track,
                                    bar.TrackBrush,
                                    bar.CornerRadius
                                )
                            );
                            result.Add(
                                new PaintSceneNode(
                                    new(bar.Viewport, SceneNodeKind.ScrollBarThumb),
                                    bar.Thumb,
                                    bar.ThumbBrush,
                                    bar.CornerRadius
                                )
                            );
                        }
                    break;
                }
                case OpacitySceneNode opacity:
                    result.Add(
                        new OpacitySceneNode(
                            opacity.Identity,
                            opacity.Bounds,
                            opacity.Opacity,
                            InsertScrollBars(opacity.Children, byViewport)
                        )
                    );
                    break;
                default:
                    result.Add(node);
                    break;
            }
        }
        return result;
    }

    private static RetainedScrollBar[] BuildScrollBars(
        Composition composition,
        IReadOnlyList<LayoutBox> boxes,
        IReadOnlyCollection<long> collapsed,
        LayoutViewport viewport
    )
    {
        var input = composition.InputIfCreated;
        if (input is null)
            return [];
        var elements = composition.Elements().ToArray();
        var scrollableIds = elements
            .Where(element =>
                input.IsScrollable(new ElementIdentity(composition.Epoch, element.Id))
            )
            .Select(element => element.Id)
            .ToHashSet();
        if (scrollableIds.Count == 0)
            return [];
        var collapsedIds = collapsed.ToHashSet();
        var boxesById = boxes.ToDictionary(box => box.Identity.ElementId);
        var bars = new List<RetainedScrollBar>();
        foreach (var element in elements)
        {
            if (
                !scrollableIds.Contains(element.Id)
                || collapsedIds.Contains(element.Id)
                || !boxesById.TryGetValue(element.Id, out var box)
            )
                continue;
            var visibility = element.ResolveValue(ScrollBarProperties.Visibility);
            if (!Enum.IsDefined(visibility) || visibility == ScrollBarVisibility.Hidden)
                continue;
            var thickness = element.ResolveValue(ScrollBarProperties.Thickness);
            var minimumThumb = element.ResolveValue(ScrollBarProperties.MinimumThumbLength);
            var cornerRadius = element.ResolveValue(ScrollBarProperties.ThumbCornerRadius);
            var trackBrush = element.ResolveValue(ScrollBarProperties.TrackBrush);
            var thumbBrush = element.ResolveValue(ScrollBarProperties.ThumbBrush);
            var hoverThumbBrush = element.ResolveValue(ScrollBarProperties.HoverThumbBrush);
            var pressedThumbBrush = element.ResolveValue(ScrollBarProperties.PressedThumbBrush);
            if (
                !float.IsFinite(thickness)
                || thickness < 0
                || !float.IsFinite(minimumThumb)
                || minimumThumb <= 0
                || !float.IsFinite(cornerRadius)
                || cornerRadius < 0
            )
                throw new ArgumentOutOfRangeException(
                    nameof(boxes),
                    "Scrollbar dimensions must be finite and nonnegative."
                );
            var padded = ContentBounds(
                box.Bounds,
                element.ResolveValue(LayoutProperties.Padding),
                viewport.Scale
            );
            if (thickness <= 0 || padded.Width <= thickness)
                continue;
            var content = ContentBounds(element, box.Bounds, viewport.Scale);
            if (thickness <= 0 || content.Width <= 0 || content.Height <= 0)
                continue;
            var offset = element.ResolveValue(LayoutProperties.Scroll);
            var right = content.X;
            var bottom = content.Y;
            foreach (var candidate in boxes)
            {
                if (
                    candidate.Identity.ElementId == element.Id
                    || collapsedIds.Contains(candidate.Identity.ElementId)
                    || !boxesById.ContainsKey(candidate.Identity.ElementId)
                    || composition.Find(candidate.Identity) is not { } child
                    || !IsScrollContentDescendant(child, element, scrollableIds)
                )
                    continue;
                right = Math.Max(right, candidate.Bounds.X + candidate.Bounds.Width + offset.X);
                bottom = Math.Max(bottom, candidate.Bounds.Y + candidate.Bounds.Height + offset.Y);
            }
            if (element.ResolveValue(ProjectionProperties.TextMultiline) && box.Text is { } text)
            {
                right = Math.Max(right, content.X + text.Width);
                bottom = Math.Max(bottom, content.Y + text.Height);
            }
            var maximum = new ScrollOffset(
                Math.Max(0, right - content.X - content.Width),
                Math.Max(0, bottom - content.Y - content.Height)
            );
            if (visibility == ScrollBarVisibility.Auto && maximum.Y <= 0)
                continue;
            var track = LayoutRect.Round(
                content.X + content.Width,
                content.Y,
                thickness,
                content.Height,
                viewport.Scale
            );
            var thumbHeight =
                maximum.Y <= 0
                    ? track.Height
                    : Math.Clamp(
                        track.Height * content.Height / (content.Height + maximum.Y),
                        Math.Min(minimumThumb, track.Height),
                        track.Height
                    );
            var travel = Math.Max(0, track.Height - thumbHeight);
            var thumbTop =
                track.Y + travel * (maximum.Y <= 0 ? 0 : Math.Clamp(offset.Y / maximum.Y, 0, 1));
            var thumb = LayoutRect.Round(
                track.X,
                thumbTop,
                track.Width,
                thumbHeight,
                viewport.Scale
            );
            bars.Add(
                new(
                    new(composition.Epoch, element.Id),
                    track,
                    thumb,
                    maximum,
                    trackBrush,
                    input.IsScrollbarPressed(new(composition.Epoch, element.Id)) ? pressedThumbBrush
                        : input.IsScrollbarHovered(new(composition.Epoch, element.Id))
                            ? hoverThumbBrush
                        : thumbBrush,
                    hoverThumbBrush,
                    pressedThumbBrush,
                    Math.Min(cornerRadius, Math.Min(track.Width, track.Height) / 2)
                )
            );
        }
        return bars.ToArray();
    }

    private static bool IsScrollContentDescendant(
        Element candidate,
        Element ancestor,
        IReadOnlySet<long> scrollableIds
    )
    {
        for (var parent = candidate.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Id == ancestor.Id)
                return true;
            if (scrollableIds.Contains(parent.Id))
                return false;
        }
        return false;
    }

    private static (Element Element, ResponsiveConstraints State)[] ResponsiveElements(
        Composition composition
    ) =>
        composition
            .Elements()
            .Select(element =>
                (
                    Element: element,
                    State: element.ResolveValue(ProjectionProperties.ResponsiveConstraints)
                )
            )
            .Where(value => value.State is not null)
            .Select(value => (value.Element, value.State!))
            .ToArray();

    private static WindowBreakpoints[] WindowBreakpointElements(Composition composition) =>
        composition
            .Elements()
            .Select(element => element.ResolveValue(ProjectionProperties.WindowBreakpoints))
            .Where(state => state is not null)
            .Select(state => state!)
            .ToArray();

    private static (
        WindowBreakpoints[] Elements,
        bool RegistrationChanged
    ) DiscoverWindowBreakpoints(
        Composition composition,
        float width,
        int maximumWorkItems,
        ref int structuralDiscoveryRounds
    )
    {
        var elements = WindowBreakpointElements(composition);
        var registrationChanged = false;
        if (elements.Length == 0)
            return (elements, registrationChanged);

        while (true)
        {
            foreach (var state in elements)
                state.Assign(composition, width);
            FlushAndSynchronizeAvailability(
                composition,
                maximumWorkItems,
                ref structuralDiscoveryRounds
            );
            var current = WindowBreakpointElements(composition);
            if (current.SequenceEqual(elements))
                return (current, registrationChanged);
            registrationChanged = true;
            if (!TryConsumeStructuralDiscoveryRound(ref structuralDiscoveryRounds))
                throw new InvalidOperationException(
                    "Window breakpoint registration did not stabilize within eight passes."
                );
            elements = current;
        }
    }

    private static bool TryConsumeStructuralDiscoveryRound(ref int rounds)
    {
        if (rounds >= MaxStructuralDiscoveryRounds)
            return false;
        rounds++;
        return true;
    }

    private static void FlushAndSynchronizeAvailability(
        Composition composition,
        int maximumWorkItems,
        ref int structuralDiscoveryRounds
    )
    {
        composition.Flush(maximumWorkItems);
        while (true)
        {
            // The router still reconciles focus/capture and rejects changes during installation.
            // Settle element-owned availability here so disabled variants participate in layout.
            var changed = composition.WithoutProjectionTracking(() =>
            {
                var visualChanged = false;
                foreach (var element in composition.Elements().ToArray())
                    visualChanged |= element.SetInputDisabledVariant(!element.InputAvailable());
                return visualChanged;
            });
            if (!changed)
                return;
            if (!TryConsumeStructuralDiscoveryRound(ref structuralDiscoveryRounds))
                throw new InvalidOperationException(
                    "Input availability did not stabilize within eight pre-layout discovery passes."
                );
            composition.Flush(maximumWorkItems);
        }
    }

    private static void EnsureWindowBreakpointElementsUnchanged(
        Composition composition,
        IReadOnlyList<WindowBreakpoints> expected
    )
    {
        if (!WindowBreakpointElements(composition).SequenceEqual(expected))
            throw new InvalidOperationException(
                "A window breakpoint registration changed after bounded pre-layout discovery."
            );
    }

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
        bool widthAllotted,
        bool heightAllotted
    )
    {
        if (element.Participation == ElementParticipation.Collapsed)
        {
            AddCollapsedBoxes(element, allotted.X, allotted.Y, boxes);
            return [];
        }
        var style = cache.Read(element);
        var shapeOuterWidth = Constrain(
            style.Width ?? allotted.Width,
            style.MinWidth,
            style.MaxWidth
        );
        if (widthAllotted)
            shapeOuterWidth = Constrain(allotted.Width, style.MinWidth, style.MaxWidth);
        if (element.Parent is null)
            shapeOuterWidth = allotted.Width;
        var shapeContentWidth = ContentWidth(element, allotted.X, shapeOuterWidth, viewport.Scale);
        var text =
            style.TextWrap == TextWrap.NoWrap
                ? Shape(element, style, viewport.Scale, shaper, cache)
                : Shape(
                    element,
                    style,
                    viewport.Scale,
                    shaper,
                    cache,
                    new LayoutConstraint(shapeContentWidth),
                    new LayoutConstraint(
                        Math.Max(0, (style.Height ?? allotted.Height) - style.Padding.Vertical)
                    )
                );
        var textMetrics = IntrinsicTextMetrics(style, text, viewport.Scale, shaper, cache);
        var emptyCaretText =
            text is null && style.Text is "" && style.Caret == 0
                ? EmptyCaretText(style, viewport.Scale, shaper, cache)
                : null;
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
        if (widthAllotted)
            width = Constrain(allotted.Width, style.MinWidth, style.MaxWidth);
        if (heightAllotted)
            height = Constrain(allotted.Height, style.MinHeight, style.MaxHeight);
        if (element.Parent is null)
        {
            width = allotted.Width;
            height = allotted.Height;
        }
        var bounds = LayoutRect.Round(allotted.X, allotted.Y, width, height, viewport.Scale);
        var inner = ContentBounds(element, bounds, viewport.Scale);
        boxes.Add(new(new(element.Composition.Epoch, element.Id), bounds, text));
        var childNodes = new List<SceneNode>();
        if (element.Children.Count != 0 || style.Algorithm is { BuiltInMode: null })
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
                        cache,
                        style.Axis == LayoutAxis.Column ? inner.Width : null
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
            if (
                element.IsConditionalRegion
                && !element.HasPresentation
                && participating is [var active]
            )
            {
                assignments =
                [
                    new(active.Index, new LayoutRect(0, 0, inner.Width, inner.Height), true, true),
                ];
            }
            else if (style.VirtualRowHeight is { } virtualRowHeight)
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
            else if (style.Algorithm is { BuiltInMode: null } algorithm)
            {
                assignments = ArrangeCustom(
                    element,
                    algorithm,
                    participating,
                    inner.Width,
                    inner.Height,
                    viewport.Scale,
                    shaper,
                    cache
                );
            }
            else if ((style.Algorithm?.BuiltInMode ?? style.Mode) == LayoutMode.Grid)
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
                        if (!spec.AutoHeight)
                            return spec;
                        var constrained = Intrinsic(
                            child,
                            childStyle,
                            Shape(child, childStyle, viewport.Scale, shaper, cache),
                            viewport.Scale,
                            shaper,
                            cache,
                            assignment.Bounds.Width
                        );
                        return spec with
                        {
                            Height = Constrain(
                                constrained.Height,
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
                var assignmentsByIndex = assignments.ToDictionary(value => value.Index);
                var corrected = specs
                    .Select(spec =>
                    {
                        if (!spec.AutoHeight)
                            return spec;
                        var assignment = assignmentsByIndex[spec.Index];
                        var child = element.Children[spec.Index];
                        var childStyle = cache.Read(child);
                        var constrained = Intrinsic(
                            child,
                            childStyle,
                            Shape(child, childStyle, viewport.Scale, shaper, cache),
                            viewport.Scale,
                            shaper,
                            cache,
                            assignment.Bounds.Width
                        );
                        return spec with
                        {
                            Height = Constrain(
                                constrained.Height,
                                childStyle.MinHeight,
                                childStyle.MaxHeight
                            ),
                        };
                    })
                    .ToArray();
                assignments = ManagedLayout.ArrangeFlex(
                    corrected,
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
            var specsByIndex = specs.ToDictionary(value => value.Index);
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
                var desired = specsByIndex[index];
                var childBounds = new LayoutRect(
                    Finite(inner.X + relative.X - style.Scroll.X),
                    Finite(inner.Y + relative.Y - style.Scroll.Y),
                    assignment.WidthAssigned ? relative.Width : desired.Width,
                    assignment.HeightAssigned ? relative.Height : desired.Height
                );
                childNodes.AddRange(
                    Layout(
                        child,
                        childBounds,
                        viewport,
                        shaper,
                        boxes,
                        cache,
                        assignment.WidthAssigned,
                        assignment.HeightAssigned
                    )
                );
            }
        }
        if (element.Participation == ElementParticipation.Hidden)
            return [];
        var identity = new ElementIdentity(element.Composition.Epoch, element.Id);
        var result = new List<SceneNode>();
        if (element.Image is { } image)
            result.Add(
                new ImageSlotSceneNode(
                    new(identity, SceneNodeKind.Image),
                    inner,
                    image,
                    element.ResolveValue(ImageProperties.Fit),
                    element.ResolveValue(ImageProperties.ColorMode),
                    element.ResolveValue(ImageProperties.ImageZoom),
                    cache.CapturePaint
                        ? element.Composition.ReadPresentedValue(
                            element,
                            TypographyProperties.TextColor
                        )
                        : element.ResolveValue(TypographyProperties.TextColor)
                )
            );
        if (text is not null)
        {
            var viewOffset = style.Caret is { } caretOffset
                ? TextViewOffset(text, style.Text!, caretOffset, inner.Width)
                : 0;
            var textOffset = LeafTextOffset(element, style, inner, text);
            var textBounds = new LayoutRect(
                inner.X + textOffset.X - viewOffset - style.Scroll.X,
                inner.Y + textOffset.Y - style.Scroll.Y,
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
                    var selection = SingleLineSelectionBounds(text, style.Text!, start, end);
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
                    var caretBounds = SingleLineCaretBounds(
                        text,
                        style.Text!,
                        caret,
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
        {
            var metrics = GetTextLineMetrics(emptyCaretText, style.FontSize);
            var verticalAlignment =
                style.Axis == LayoutAxis.Row ? style.CrossAlignment : style.MainAlignment;
            result.Add(
                new PaintSceneNode(
                    new(identity, SceneNodeKind.Caret),
                    new(
                        inner.X,
                        inner.Y + AlignmentOffset(verticalAlignment, inner.Height, metrics.Height),
                        1 / viewport.Scale,
                        Math.Max(metrics.Height, 1 / viewport.Scale)
                    ),
                    style.TextColor
                )
            );
        }
        var decorations = new List<SceneNode>();
        AddDecorations(decorations, element, identity, bounds, viewport.Scale, cache);
        var plan = new ElementPaintPlan(
            identity,
            bounds,
            inner,
            style.CornerRadius,
            style.Clip,
            result.ToArray(),
            decorations.ToArray(),
            cache.CapturePaint
                ? element
                    .Children.Where(child => cache.PaintPlans.ContainsKey(child.Id))
                    .Select(child => cache.PaintPlans[child.Id])
                    .ToArray()
                : []
        );
        if (cache.CapturePaint)
            cache.PaintPlans[element.Id] = plan;
        var paint = cache.CapturePaint
            ? ReadPresentedPaint(element)
            : (style.Background, style.Opacity, style.TextColor);
        return PaintElement(plan, paint, childNodes, viewport);
    }

    private static (float X, float Y) LeafTextOffset(
        Element element,
        in Values style,
        LayoutRect inner,
        ShapedText text
    )
    {
        if (element.Children.Count != 0)
            return default;

        var horizontalAlignment =
            style.Axis == LayoutAxis.Row ? style.MainAlignment : style.CrossAlignment;
        var verticalAlignment =
            style.Axis == LayoutAxis.Row ? style.CrossAlignment : style.MainAlignment;
        return (
            AlignmentOffset(horizontalAlignment, inner.Width, text.Width),
            AlignmentOffset(verticalAlignment, inner.Height, text.Height)
        );
    }

    private static float AlignmentOffset(LayoutAlignment alignment, float available, float size) =>
        alignment switch
        {
            LayoutAlignment.Center => Math.Max(0, available - size) / 2,
            LayoutAlignment.End => Math.Max(0, available - size),
            _ => 0,
        };

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static void AddDecorations(
        List<SceneNode> result,
        Element element,
        ElementIdentity identity,
        LayoutRect bounds,
        float scale,
        ProjectionCache cache
    )
    {
        var style = cache.ReadDecorations(element);
        result.AddRange(
            DecorationNodes(
                identity,
                SceneNodeKind.Border,
                bounds,
                style.Border.Brush,
                style.Border.Widths,
                style.Border.IsHairline,
                scale,
                style.CornerRadius
            )
        );
        result.AddRange(
            DecorationNodes(
                identity,
                SceneNodeKind.FocusRing,
                bounds,
                style.FocusRing.Brush,
                Insets.Uniform(style.FocusRing.Thickness),
                false,
                scale,
                style.CornerRadius
            )
        );
    }

    private static IReadOnlyList<SceneNode> DecorationNodes(
        ElementIdentity identity,
        SceneNodeKind kind,
        LayoutRect bounds,
        Brush? brush,
        Insets widths,
        bool hairline,
        float scale,
        float cornerRadius
    )
    {
        if (brush is null || brush.Color is { A: 0 } || widths == Insets.Zero)
            return [];
        if (hairline)
        {
            var devicePixel = 1 / scale;
            widths = new Insets(
                widths.Left > 0 ? devicePixel : 0,
                widths.Top > 0 ? devicePixel : 0,
                widths.Right > 0 ? devicePixel : 0,
                widths.Bottom > 0 ? devicePixel : 0
            );
        }

        var left = Math.Min(widths.Left, bounds.Width);
        var top = Math.Min(widths.Top, bounds.Height);
        var right = Math.Min(widths.Right, bounds.Width);
        var bottom = Math.Min(widths.Bottom, bounds.Height);
        var resolved = new Insets(left, top, right, bottom);
        return [new PaintSceneNode(new(identity, kind), bounds, brush, cornerRadius, resolved)];
    }

    private static float InnerCornerRadius(float radius, LayoutRect outer, LayoutRect inner)
    {
        var inset = Math.Max(
            Math.Max(inner.X - outer.X, inner.Y - outer.Y),
            Math.Max(
                outer.X + outer.Width - inner.X - inner.Width,
                outer.Y + outer.Height - inner.Y - inner.Height
            )
        );
        return Math.Max(0, radius - Math.Max(0, inset));
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
        float? outerWidthLimit,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache
    )
    {
        var style = cache.Read(element);
        var text = Shape(element, style, scale, shaper, cache);
        var intrinsic = Intrinsic(
            element,
            style,
            text,
            scale,
            shaper,
            cache,
            parentAxis == LayoutAxis.Column ? outerWidthLimit : null
        );
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
        in Values style,
        ShapedText? text,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache,
        float? outerWidthLimit = null
    )
    {
        var constrainedOuterWidth =
            style.Width is { } authoredWidth
                ? Constrain(authoredWidth, style.MinWidth, style.MaxWidth)
            : outerWidthLimit is { } availableWidth
                ? Constrain(availableWidth, style.MinWidth, style.MaxWidth)
            : (float?)null;
        var constrainedContentWidth = constrainedOuterWidth is { } outerWidth
            ? ContentWidth(element, 0, outerWidth, scale)
            : (float?)null;
        if (cache.TryReadIntrinsic(element.Id, constrainedOuterWidth, out var cachedIntrinsic))
            return cachedIntrinsic;
        if (style.TextWrap != TextWrap.NoWrap && constrainedContentWidth is { } textWidth)
            text = Shape(element, style, scale, shaper, cache, new LayoutConstraint(textWidth));
        var textMetrics = IntrinsicTextMetrics(style, text, scale, shaper, cache);
        var width = textMetrics?.Width ?? 0f;
        var height = textMetrics?.Height ?? 0f;
        if (
            element.Image is not null
            && element.ResolveValue(ImageProperties.Source) is { } imageSource
        )
        {
            width = imageSource.Metadata.Width;
            height = imageSource.Metadata.Height;
            if (style.Width is not null && style.Height is null)
                height = (constrainedContentWidth ?? 0) / width * height;
            else if (style.Height is { } imageHeight && style.Width is null)
                width =
                    Math.Max(
                        0,
                        Constrain(imageHeight, style.MinHeight, style.MaxHeight)
                            - style.Padding.Vertical
                    )
                    / height
                    * width;
            else if (
                style.Width is null
                && style.Height is null
                && constrainedContentWidth is { } imageLimit
                && imageLimit < width
            )
            {
                height = imageLimit / width * height;
                width = imageLimit;
            }
        }
        // A virtualized viewport is a measurement boundary. Its source extent
        // belongs to the scrolling region, never to an ancestor's desired size;
        // otherwise nested auto layout can realize the entire source as visible.
        // Explicit dimensions and flex/grid assignments are applied by callers.
        if (element.Composition.IsVirtualizedViewport(element.Id))
            return cache.WriteIntrinsic(
                element.Id,
                constrainedOuterWidth,
                Outer(style, (width, height))
            );
        var participatingChildren = element
            .Children.Where(child => child.Participation != ElementParticipation.Collapsed)
            .ToArray();
        if (style.Algorithm is { BuiltInMode: null } algorithm)
        {
            var initial = participatingChildren
                .Select(child =>
                {
                    var childStyle = cache.Read(child);
                    var intrinsic = Intrinsic(
                        child,
                        childStyle,
                        Shape(child, childStyle, scale, shaper, cache),
                        scale,
                        shaper,
                        cache
                    );
                    return (
                        Element: child,
                        Desired: new LayoutSize(
                            Constrain(
                                childStyle.Width ?? intrinsic.Width,
                                childStyle.MinWidth,
                                childStyle.MaxWidth
                            ),
                            Constrain(
                                childStyle.Height ?? intrinsic.Height,
                                childStyle.MinHeight,
                                childStyle.MaxHeight
                            )
                        )
                    );
                })
                .ToArray();
            var result = InvokeCustomAlgorithm(
                element,
                algorithm,
                new(
                    constrainedContentWidth is { } contentWidth
                        ? new LayoutConstraint(contentWidth)
                        : LayoutConstraint.Unbounded,
                    LayoutConstraint.Unbounded
                ),
                initial,
                scale,
                shaper,
                cache
            );
            return cache.WriteIntrinsic(
                element.Id,
                constrainedOuterWidth,
                Outer(style, (result.DesiredSize.Width, result.DesiredSize.Height))
            );
        }
        if (participatingChildren.Length == 0)
            return cache.WriteIntrinsic(
                element.Id,
                constrainedOuterWidth,
                Outer(
                    style,
                    style.VirtualRowHeight is { } emptyRowHeight
                        ? (width, Finite(emptyRowHeight * style.VirtualItemCount))
                        : (width, height)
                )
            );
        var children = new Measurement[participatingChildren.Length];
        for (var index = 0; index < participatingChildren.Length; index++)
            children[index] = Measure(
                participatingChildren[index],
                style.Axis,
                style.Axis == LayoutAxis.Column ? constrainedContentWidth : null,
                scale,
                shaper,
                cache
            );
        if (style.Axis == LayoutAxis.Row)
        {
            foreach (var child in children)
                width = Finite(width + child.Main);
            width = Finite(width + Finite(style.Spacing * Math.Max(0, children.Length - 1)));
            if (style.Wrap && constrainedContentWidth is { } wrapWidth)
            {
                var specs = children
                    .Select(
                        (child, index) =>
                        {
                            var childStyle = cache.Read(child.Element);
                            return new LayoutItemSpec(
                                index,
                                child.Main,
                                child.Cross,
                                child.MinMain,
                                childStyle.MinHeight,
                                child.MaxMain,
                                childStyle.MaxHeight,
                                false,
                                child.AutoCross,
                                childStyle.MainBasis,
                                child.MainGrow,
                                child.MainShrink,
                                childStyle.GridPlacement
                            );
                        }
                    )
                    .ToArray();
                var wrapped = ManagedLayout.ArrangeFlex(
                    specs,
                    LayoutAxis.Row,
                    wrapWidth,
                    0,
                    style.Spacing,
                    style.RowGap,
                    true,
                    style.MainAlignment,
                    style.CrossAlignment
                );
                var byIndex = wrapped.ToDictionary(assignment => assignment.Index);
                specs = specs
                    .Select(spec =>
                    {
                        if (!spec.AutoHeight)
                            return spec;
                        var child = participatingChildren[spec.Index];
                        var childStyle = cache.Read(child);
                        var assignedWidth = byIndex[spec.Index].Bounds.Width;
                        var childIntrinsic = Intrinsic(
                            child,
                            childStyle,
                            Shape(child, childStyle, scale, shaper, cache),
                            scale,
                            shaper,
                            cache,
                            assignedWidth
                        );
                        return spec with
                        {
                            Height = Constrain(
                                childIntrinsic.Height,
                                childStyle.MinHeight,
                                childStyle.MaxHeight
                            ),
                        };
                    })
                    .ToArray();
                wrapped = ManagedLayout.ArrangeFlex(
                    specs,
                    LayoutAxis.Row,
                    wrapWidth,
                    0,
                    style.Spacing,
                    style.RowGap,
                    true,
                    style.MainAlignment,
                    style.CrossAlignment
                );
                foreach (var assignment in wrapped)
                    height = Math.Max(
                        height,
                        Finite(assignment.Bounds.Y + assignment.Bounds.Height)
                    );
                width = Math.Min(width, wrapWidth);
            }
            else if (constrainedContentWidth is { } rowWidth)
            {
                var specs = children
                    .Select(
                        (child, index) =>
                        {
                            var childStyle = cache.Read(child.Element);
                            return new LayoutItemSpec(
                                index,
                                child.Main,
                                child.Cross,
                                child.MinMain,
                                childStyle.MinHeight,
                                child.MaxMain,
                                childStyle.MaxHeight,
                                false,
                                child.AutoCross,
                                childStyle.MainBasis,
                                child.MainGrow,
                                child.MainShrink,
                                childStyle.GridPlacement
                            );
                        }
                    )
                    .ToArray();
                var arranged = ManagedLayout.ArrangeFlex(
                    specs,
                    LayoutAxis.Row,
                    rowWidth,
                    0,
                    style.Spacing,
                    style.RowGap,
                    false,
                    style.MainAlignment,
                    style.CrossAlignment
                );
                var byIndex = arranged.ToDictionary(assignment => assignment.Index);
                for (var index = 0; index < children.Length; index++)
                {
                    var child = children[index];
                    if (!child.AutoCross)
                    {
                        height = Math.Max(height, child.Cross);
                        continue;
                    }
                    var elementChild = participatingChildren[index];
                    var childStyle = cache.Read(elementChild);
                    var constrained = Intrinsic(
                        elementChild,
                        childStyle,
                        Shape(elementChild, childStyle, scale, shaper, cache),
                        scale,
                        shaper,
                        cache,
                        byIndex[index].Bounds.Width
                    );
                    height = Math.Max(
                        height,
                        Constrain(constrained.Height, childStyle.MinHeight, childStyle.MaxHeight)
                    );
                }
            }
            else
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
        return cache.WriteIntrinsic(
            element.Id,
            constrainedOuterWidth,
            Outer(style, (Finite(width), Finite(height)))
        );
    }

    private static LayoutAssignment[] ArrangeCustom(
        Element container,
        LayoutAlgorithm algorithm,
        IReadOnlyList<(Element Child, int Index)> participating,
        float width,
        float height,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache
    )
    {
        var initial = participating
            .Select(child =>
            {
                var childStyle = cache.Read(child.Child);
                var intrinsic = Intrinsic(
                    child.Child,
                    childStyle,
                    Shape(child.Child, childStyle, scale, shaper, cache),
                    scale,
                    shaper,
                    cache
                );
                return (
                    child.Child,
                    new LayoutSize(
                        Constrain(
                            childStyle.Width ?? intrinsic.Width,
                            childStyle.MinWidth,
                            childStyle.MaxWidth
                        ),
                        Constrain(
                            childStyle.Height ?? intrinsic.Height,
                            childStyle.MinHeight,
                            childStyle.MaxHeight
                        )
                    )
                );
            })
            .ToArray();
        var result = InvokeCustomAlgorithm(
            container,
            algorithm,
            new(new LayoutConstraint(width), new LayoutConstraint(height)),
            initial,
            scale,
            shaper,
            cache
        );
        return result
            .Placements.Select(placement => new LayoutAssignment(
                participating[placement.ChildIndex].Index,
                placement.Bounds,
                placement.WidthAssigned,
                placement.HeightAssigned
            ))
            .ToArray();
    }

    private static LayoutAlgorithmResult InvokeCustomAlgorithm(
        Element container,
        LayoutAlgorithm algorithm,
        LayoutConstraints constraints,
        IReadOnlyList<(Element Element, LayoutSize Desired)> children,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache
    )
    {
        LayoutSize MeasureChild(Element child, LayoutConstraints childConstraints)
        {
            var childStyle = cache.Read(child);
            var intrinsic = Intrinsic(
                child,
                childStyle,
                Shape(child, childStyle, scale, shaper, cache),
                scale,
                shaper,
                cache,
                childConstraints.Width.Limit
            );
            return new(
                Math.Min(
                    childConstraints.Width.Or(float.PositiveInfinity),
                    Constrain(
                        childStyle.Width ?? intrinsic.Width,
                        childStyle.MinWidth,
                        childStyle.MaxWidth
                    )
                ),
                Math.Min(
                    childConstraints.Height.Or(float.PositiveInfinity),
                    Constrain(
                        childStyle.Height ?? intrinsic.Height,
                        childStyle.MinHeight,
                        childStyle.MaxHeight
                    )
                )
            );
        }

        var context = new LayoutAlgorithmContext(
            container,
            algorithm,
            constraints,
            children,
            MeasureChild
        );
        try
        {
            container.ActivateLayoutAlgorithm(algorithm);
            var result =
                container.Composition.RunLayoutCallback(() => algorithm.Layout(context))
                ?? throw new InvalidOperationException("A layout algorithm returned null.");
            var placements = result.Placements;
            if (
                placements.Count != children.Count
                || placements.Select(value => value.ChildIndex).Distinct().Count() != children.Count
                || placements.Any(value => value.ChildIndex >= children.Count)
            )
                throw new InvalidOperationException(
                    "A layout algorithm must place every participating direct child exactly once."
                );
            return result;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Layout algorithm '{algorithm.Name}' failed for container '{container.Name}' (element {container.Id}).",
                exception
            );
        }
        finally
        {
            context.Complete();
        }
    }

    private static (float Width, float Height) Outer(
        in Values style,
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

    internal static LayoutRect ContentBounds(Element element, LayoutRect outer, float scale)
    {
        ArgumentNullException.ThrowIfNull(element);
        var content = ContentBounds(outer, element.ResolveValue(LayoutProperties.Padding), scale);
        var visibility = element.ResolveValue(ScrollBarProperties.Visibility);
        if (!Enum.IsDefined(visibility) || visibility == ScrollBarVisibility.Hidden)
            return content;
        var thickness = element.ResolveValue(ScrollBarProperties.Thickness);
        if (!float.IsFinite(thickness) || thickness < 0)
            throw new ArgumentOutOfRangeException(nameof(element));
        if (thickness == 0 || content.Width <= thickness)
            return content;
        var right = Math.Max(content.X, content.X + content.Width - thickness);
        return new(content.X, content.Y, right - content.X, content.Height);
    }

    private static float ContentWidth(Element element, float x, float width, float scale)
    {
        if (!float.IsFinite(x) || !float.IsFinite(width) || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return ContentBounds(element, LayoutRect.Round(x, 0, width, 0, scale), scale).Width;
    }

    private static ShapedText? Shape(
        Element element,
        in Values style,
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
        in Values style,
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
            style.TextOverflow,
            style.FontWeight
        );
        request.Validate();
        if (cache.Shapes.TryGetValue(request, out var cached))
        {
            cached.SetSourceText(request.Text);
            return cached;
        }
        var shaped = shaper.Shape(request);
        shaped.Validate(request);
        shaped.SetSourceText(request.Text);
        cache.Shapes.Add(request, shaped);
        return shaped;
    }

    private static (float Width, float Height)? IntrinsicTextMetrics(
        in Values style,
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
        var paint = ReadPaint(element);
        return ReadResolved(element, paint.Background, paint.Opacity, paint.TextColor);
    }

    private static Values ReadInputTrackedResolved(Element element)
    {
        var paint = element.Composition.WithoutProjectionTracking(() => ReadPaint(element));
        return ReadResolved(element, paint.Background, paint.Opacity, paint.TextColor);
    }

    private static (Brush Background, float Opacity, Color TextColor) ReadPaint(Element element) =>
        (
            element.ResolveValue(VisualProperties.Background),
            element.ResolveValue(VisualProperties.Opacity),
            element.ResolveValue(TypographyProperties.TextColor)
        );

    private static Values ReadResolved(
        Element element,
        Brush background,
        float opacity,
        Color textColor
    )
    {
        var values = new Values(
            element.ResolveValue(LayoutProperties.Mode),
            element.ResolveValue(LayoutProperties.Algorithm),
            element.ResolveValue(LayoutProperties.Axis),
            element.ResolveValue(LayoutProperties.Columns),
            element.ResolveValue(LayoutProperties.Rows),
            element.ResolveValue(LayoutProperties.ColumnGap),
            element.ResolveValue(LayoutProperties.RowGap),
            element.ResolveValue(LayoutProperties.GridPlacement),
            element.ResolveValue(LayoutProperties.Width),
            element.ResolveValue(LayoutProperties.Height),
            element.ResolveValue(LayoutProperties.MinWidth),
            element.ResolveValue(LayoutProperties.MinHeight),
            element.ResolveValue(LayoutProperties.MaxWidth),
            element.ResolveValue(LayoutProperties.MaxHeight),
            element.ResolveValue(LayoutProperties.Spacing),
            element.ResolveValue(LayoutProperties.MainGrow),
            element.ResolveValue(LayoutProperties.MainBasis),
            element.ResolveValue(LayoutProperties.MainShrink),
            element.ResolveValue(LayoutProperties.Wrap),
            element.ResolveValue(LayoutProperties.MainAlignment),
            element.ResolveValue(LayoutProperties.CrossAlignment),
            element.ResolveValue(LayoutProperties.Clip),
            element.ResolveValue(LayoutProperties.Padding),
            element.ResolveValue(LayoutProperties.Scroll),
            element.ResolveValue(LayoutProperties.VirtualRowHeight),
            element.ResolveValue(LayoutProperties.VirtualItemCount),
            element.ResolveValue(LayoutProperties.VirtualRowIndex),
            background,
            element.ResolveValue(VisualProperties.CornerRadius),
            opacity,
            textColor,
            element.ResolveValue(ProjectionProperties.Text),
            element.ResolveValue(ProjectionProperties.TextMeasure),
            element.ResolveValue(TypographyProperties.FontFamily),
            element.ResolveValue(TypographyProperties.FontSize),
            element.ResolveValue(TypographyProperties.FontWeight),
            element.ResolveValue(TypographyProperties.Language),
            element.ResolveValue(TypographyProperties.Direction),
            element.ResolveValue(TypographyProperties.TextWrap),
            element.ResolveValue(TypographyProperties.MaxLines),
            element.ResolveValue(TypographyProperties.Overflow),
            element.ResolveValue(ProjectionProperties.TextSelectionStart),
            element.ResolveValue(ProjectionProperties.TextSelectionEnd),
            element.ResolveValue(ProjectionProperties.TextCaret),
            element.ResolveValue(ProjectionProperties.TextMultiline),
            element.ResolveValue(ProjectionProperties.TextCaretAffinity)
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
            || (
                values.Algorithm is null
                    ? values.Mode == LayoutMode.Grid
                    : values.Algorithm.BuiltInMode == LayoutMode.Grid
            ) && (values.Columns.Count == 0 || values.Rows.Count == 0)
            || !float.IsFinite(values.Opacity)
            || values.Opacity < 0
            || values.Opacity > 1
            || !float.IsFinite(values.CornerRadius)
            || values.CornerRadius < 0
            || !Enum.IsDefined(values.FontWeight)
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
        internal Dictionary<long, ElementPaintPlan> PaintPlans { get; } = [];
        internal bool CapturePaint => _resolvedStyles is not null;
        private readonly Dictionary<long, Values> _styles = [];
        private readonly IReadOnlyDictionary<long, Values>? _resolvedStyles;
        private readonly Dictionary<long, DecorationValues> _decorations = [];
        private readonly Dictionary<
            (long ElementId, float? Width),
            (float Width, float Height)
        > _intrinsics = [];

        internal Dictionary<TextMeasureRequest, ShapedText> Shapes { get; } = [];

        internal ProjectionCache() { }

        internal ProjectionCache(IReadOnlyDictionary<long, Values> resolvedStyles)
        {
            _resolvedStyles = resolvedStyles;
        }

        internal bool TryReadIntrinsic(
            long elementId,
            float? width,
            out (float Width, float Height) intrinsic
        ) => _intrinsics.TryGetValue((elementId, width), out intrinsic);

        internal (float Width, float Height) WriteIntrinsic(
            long elementId,
            float? width,
            (float Width, float Height) intrinsic
        )
        {
            _intrinsics.Add((elementId, width), intrinsic);
            return intrinsic;
        }

        internal Values Read(Element element)
        {
            if (_styles.TryGetValue(element.Id, out var cached))
                return cached;
            var resolved =
                _resolvedStyles is not null
                && _resolvedStyles.TryGetValue(element.Id, out var snapshot)
                    ? snapshot
                    : ReadResolved(element);
            element.ActivateLayoutAlgorithm(
                resolved.Algorithm is { BuiltInMode: null } ? resolved.Algorithm : null
            );
            if (
                element.IsConditionalRegion
                && !element.HasPresentation
                && element.Children is [var child]
                && child.Participation != ElementParticipation.Collapsed
            )
            {
                // The retained branch owner is not an authored layout container.
                // Carry its single child's parent-facing allocation through the
                // boundary without copying paint, padding, or child arrangement.
                var content = Read(child);
                var parent = element.Parent;
                while (parent is { IsConditionalRegion: true, HasPresentation: false })
                    parent = parent.Parent;
                resolved = resolved with
                {
                    Axis = parent?.ResolveValue(LayoutProperties.Axis) ?? LayoutAxis.Column,
                    Width = content.Width,
                    Height = content.Height,
                    MinWidth = content.MinWidth,
                    MinHeight = content.MinHeight,
                    MaxWidth = content.MaxWidth,
                    MaxHeight = content.MaxHeight,
                    MainBasis = content.MainBasis,
                    MainGrow = content.MainGrow,
                    MainShrink = content.MainShrink,
                    GridPlacement = content.GridPlacement,
                };
            }
            _styles.Add(element.Id, resolved);
            var decorations = new DecorationValues(
                element.ResolveValue(VisualProperties.Border),
                element.ResolveValue(VisualProperties.FocusRing),
                element.ResolveValue(VisualProperties.CornerRadius)
            );
            if (decorations != default)
                _decorations.Add(element.Id, decorations);
            return resolved;
        }

        internal DecorationValues ReadDecorations(Element element)
        {
            _ = Read(element);
            return _decorations.TryGetValue(element.Id, out var decorations)
                ? decorations
                : default;
        }
    }

    private readonly record struct DecorationValues(
        Border Border,
        FocusRing FocusRing,
        float CornerRadius
    );

    private readonly record struct Values(
        LayoutMode Mode,
        LayoutAlgorithm? Algorithm,
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
        float CornerRadius,
        float Opacity,
        Color TextColor,
        string? Text,
        string? TextMeasure,
        string FontFamily,
        float FontSize,
        FontWeight FontWeight,
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

    private static ShapedText? EmptyCaretText(
        in Values style,
        float scale,
        ITextShaper shaper,
        ProjectionCache cache
    ) =>
        string.IsNullOrEmpty(style.TextMeasure)
            ? null
            : ShapeText(style.TextMeasure, style, scale, shaper, cache);

    private static LayoutRect SingleLineCaretBounds(
        ShapedText text,
        string source,
        int utf16Offset,
        float caretWidth
    )
    {
        var metrics = GetTextLineMetrics(text, 0);
        return new(
            TextPosition(text, source, utf16Offset),
            metrics.Top,
            caretWidth,
            metrics.Height
        );
    }

    private static LayoutRect SingleLineSelectionBounds(
        ShapedText text,
        string source,
        int start,
        int end
    )
    {
        var metrics = GetTextLineMetrics(text, 0);
        var leftPosition = TextPosition(text, source, start);
        var rightPosition = TextPosition(text, source, end);
        return new(
            Math.Min(leftPosition, rightPosition),
            metrics.Top,
            MathF.Abs(rightPosition - leftPosition),
            metrics.Height
        );
    }

    private static TextLineMetrics GetTextLineMetrics(ShapedText? text, float fallbackHeight)
    {
        if (text is not null && text.Lines.Count != 0)
        {
            var line = text.Lines[0];
            return new(line.Top, Math.Max(0, line.Descent - line.Ascent + line.Leading));
        }
        if (text is null || text.Runs.Count == 0)
            return new(0, Math.Max(0, fallbackHeight));
        var ascent = text.Runs.Min(run => run.Ascent);
        var descent = text.Runs.Max(run => run.Descent);
        return new(text.Runs[0].Baseline + ascent, Math.Max(0, descent - ascent));
    }

    private readonly record struct TextLineMetrics(float Top, float Height);

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
        ArgumentNullException.ThrowIfNull(element);
        return InputSignature(element, ReadInputTrackedResolved(element));
    }

    private static string InputSignature(Element element, in Values values)
    {
        var mode = values.Mode;
        var algorithm = values.Algorithm;
        var axis = values.Axis;
        var columns = values.Columns;
        var rows = values.Rows;
        if (columns is null || rows is null)
            throw new ArgumentOutOfRangeException(
                nameof(element),
                "Grid track collections must not be null."
            );
        var columnGap = values.ColumnGap;
        var rowGap = values.RowGap;
        var placement = values.GridPlacement;
        var width = values.Width;
        var height = values.Height;
        var minWidth = values.MinWidth;
        var minHeight = values.MinHeight;
        var maxWidth = values.MaxWidth;
        var maxHeight = values.MaxHeight;
        var spacing = values.Spacing;
        var mainGrow = values.MainGrow;
        var mainBasis = values.MainBasis;
        var mainShrink = values.MainShrink;
        var wrap = values.Wrap;
        var mainAlignment = values.MainAlignment;
        var crossAlignment = values.CrossAlignment;
        var clip = values.Clip;
        var cornerRadius = values.CornerRadius;
        var padding = values.Padding;
        var scroll = values.Scroll;
        var scrollbarVisibility = element.ResolveValue(ScrollBarProperties.Visibility);
        var scrollbarThickness = element.ResolveValue(ScrollBarProperties.Thickness);
        var virtualRowHeight = values.VirtualRowHeight;
        var virtualItemCount = values.VirtualItemCount;
        var virtualRowIndex = values.VirtualRowIndex;
        var text = values.Text;
        var textMeasure = values.TextMeasure;
        var fontFamily = values.FontFamily;
        var fontSize = values.FontSize;
        var fontWeight = values.FontWeight;
        var language = values.Language;
        var direction = values.Direction;
        var textWrap = values.TextWrap;
        var maxLines = values.MaxLines;
        var textOverflow = values.TextOverflow;
        var selectionStart = values.SelectionStart;
        var selectionEnd = values.SelectionEnd;
        var caret = values.Caret;
        var multiline = values.Multiline;
        var caretAffinity = values.CaretAffinity;
        var enabled = element.ResolveValue(InputProperties.Enabled);
        var visible = element.ResolveValue(InputProperties.Visible);
        var pointerTransparent = element.ResolveValue(InputProperties.PointerTransparent);
        var participation = element.Participation;
        return Hash(writer =>
        {
            writer.Write((int)mode);
            writer.Write(algorithm is not null);
            if (algorithm is not null)
                writer.Write(algorithm.Name);
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
            writer.Write(cornerRadius);
            writer.Write(padding.Left);
            writer.Write(padding.Top);
            writer.Write(padding.Right);
            writer.Write(padding.Bottom);
            writer.Write(scroll.X);
            writer.Write(scroll.Y);
            writer.Write((int)scrollbarVisibility);
            writer.Write(scrollbarThickness);
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
            writer.Write((int)fontWeight);
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
            writer.Write(pointerTransparent);
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
