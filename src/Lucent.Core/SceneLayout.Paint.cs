namespace Lucent.Core;

public static partial class SceneLayout
{
    /// <summary>Projects a frame, reusing prior geometry and shaping when only presentation samples changed.</summary>
    /// <remarks>The caller retains ownership of both scenes. Pass no prior scene after a renderer resource reset.</remarks>
    public static RetainedScene ProjectFrame(
        Composition composition,
        LayoutViewport viewport,
        ITextShaper shaper,
        RetainedScene? previousScene,
        int maximumWorkItems = ReactiveGraph.DefaultMaximumWorkItems
    )
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(shaper);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWorkItems);
        composition.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(composition.IsDisposed, composition);
        viewport.Validate();
        if (
            previousScene is not { IsDisposed: false, PaintSnapshot: { } snapshot }
            || !snapshot.CanReuse(composition, viewport, shaper)
        )
            return Project(composition, viewport, shaper, maximumWorkItems);

        var nodes = snapshot.Paint(composition, viewport);
        var scene = previousScene.WithPaint(
            composition.NextSceneGeneration(),
            InsertScrollBars(nodes, previousScene.ScrollBars)
        );
        composition.CapturePresentationFrame(scene.Generation);
        return scene;
    }

    private static (Brush Background, float Opacity, Color TextColor) ReadPresentedPaint(
        Element element
    ) =>
        (
            element.Composition.ReadPresentedValue(element, VisualProperties.Background),
            element.Composition.ReadPresentedValue(element, VisualProperties.Opacity),
            element.Composition.ReadPresentedValue(element, TypographyProperties.TextColor)
        );

    internal sealed class PaintSnapshot
    {
        private readonly long _epoch;
        private readonly long _mutationRevision;
        private readonly long _inputRevision;
        private readonly long _interactionRevision;
        private readonly LayoutViewport _viewport;
        private readonly ITextShaper _shaper;
        private readonly ElementPaintPlan? _root;

        internal PaintSnapshot(
            Composition composition,
            ITextShaper shaper,
            ElementPaintPlan? root,
            LayoutViewport viewport
        )
        {
            _epoch = composition.Epoch;
            _mutationRevision = composition.Graph.MutationRevision;
            _inputRevision = composition.InputProjectionRevision;
            _interactionRevision = composition.InteractionVisualGeneration;
            _viewport = viewport;
            _shaper = shaper;
            // Freeze borrowed image slots only after the final assigned demand.
            // The retained scene owns the resulting prepared-resource leases.
            _root = Freeze(root, viewport.Scale);
        }

        internal bool CanReuse(
            Composition composition,
            LayoutViewport viewport,
            ITextShaper shaper
        ) =>
            _viewport == viewport && ReferenceEquals(_shaper, shaper) && CanReuseInput(composition);

        internal bool CanReuseInput(Composition composition) =>
            _epoch == composition.Epoch
            && _mutationRevision == composition.Graph.MutationRevision
            && _inputRevision == composition.InputProjectionRevision
            && _interactionRevision == composition.InteractionVisualGeneration
            && !composition.Graph.HasPendingProjectionWork;

        internal IReadOnlyList<SceneNode> Paint(Composition composition, LayoutViewport viewport) =>
            _root is null ? [] : Replay(_root, composition, viewport);

        private static ElementPaintPlan? Freeze(ElementPaintPlan? plan, float scale) =>
            plan is null
                ? null
                : plan with
                {
                    OwnNodes = ResolveImages(plan.OwnNodes, scale).ToArray(),
                    Children = plan.Children.Select(child => Freeze(child, scale)!).ToArray(),
                };

        private static List<SceneNode> Replay(
            ElementPaintPlan plan,
            Composition composition,
            LayoutViewport viewport
        )
        {
            var element =
                composition.Find(plan.Identity)
                ?? throw new InvalidOperationException(
                    "A retained paint owner is no longer mounted."
                );
            var children = new List<SceneNode>();
            foreach (var child in plan.Children)
                children.AddRange(Replay(child, composition, viewport));
            return PaintElement(plan, ReadPresentedPaint(element), children, viewport);
        }
    }

    internal sealed record ElementPaintPlan(
        ElementIdentity Identity,
        LayoutRect Bounds,
        LayoutRect Inner,
        float CornerRadius,
        bool Clip,
        SceneNode[] OwnNodes,
        SceneNode[] Decorations,
        ElementPaintPlan[] Children
    );

    private static List<SceneNode> PaintElement(
        ElementPaintPlan plan,
        (Brush Background, float Opacity, Color TextColor) paint,
        IReadOnlyList<SceneNode> children,
        LayoutViewport viewport
    )
    {
        var contents = new List<SceneNode>(plan.OwnNodes.Length + children.Count);
        foreach (var node in plan.OwnNodes)
            contents.Add(
                node switch
                {
                    TextSceneNode text when text.Color != paint.TextColor => new TextSceneNode(
                        text.Identity,
                        text.Bounds,
                        paint.TextColor,
                        text.Text
                    ),
                    PaintSceneNode { Identity.Kind: SceneNodeKind.Caret } caret =>
                        new PaintSceneNode(
                            caret.Identity,
                            caret.Bounds,
                            paint.TextColor,
                            caret.CornerRadius
                        ),
                    ImageSceneNode image => image.WithTint(paint.TextColor),
                    _ => node,
                }
            );
        contents.AddRange(children);
        var result = new List<SceneNode>();
        if (paint.Background.Color is not { A: 0 })
            result.Add(
                new PaintSceneNode(
                    new(plan.Identity, SceneNodeKind.Paint),
                    plan.Bounds,
                    paint.Background,
                    plan.CornerRadius
                )
            );
        if (plan.Clip)
            result.Add(
                new ClipSceneNode(
                    new(plan.Identity, SceneNodeKind.Clip),
                    plan.Inner,
                    contents,
                    InnerCornerRadius(plan.CornerRadius, plan.Bounds, plan.Inner)
                )
            );
        else
            result.AddRange(contents);
        result.AddRange(plan.Decorations);
        return paint.Opacity == 1 || result.Count == 0
            ? result
            :
            [
                new OpacitySceneNode(
                    new(plan.Identity, SceneNodeKind.Opacity),
                    VisibleBounds(result, viewport),
                    paint.Opacity,
                    result
                ),
            ];
    }
}
