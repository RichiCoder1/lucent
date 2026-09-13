using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class InputMenuWheelContracts
{
    [TestMethod]
    public void DisabledContextMenuDismissesWithEscapeWithoutEligibleItem()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "disabled-menu");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        owner.Mount(
            owner.Root,
            theme,
            Components.ContextMenu(
                [Components.Button("Target", () => { }, Style.Empty.Height(40))],
                () =>
                    Components.Menu([
                        Components.MenuItem("Unavailable A", () => { }, () => false),
                        Components.MenuItem("Unavailable B", () => { }, () => false),
                    ])
            )
        );
        ContextMenuRequest? request = null;
        owner.Input.ContextMenuRequested += value => request = value;
        Install(owner);
        Assert.IsTrue(owner.Input.MoveFocus(FocusTraversalDirection.Next));
        Install(owner);
        Assert.IsTrue(
            owner.Input.DispatchKey(new(KeyCommandKind.Down, Key.F10, KeyModifiers.Shift)).Handled
        );
        Assert.IsNotNull(request);

        var opened = request!;
        using var popupRequest = opened;
        var popup = popupRequest.CreateComposition();
        Install(popup);
        Assert.IsTrue(
            popup.Input.FocusMenuBoundary(first: true),
            "The all-disabled menu did not expose its eligible root focus target."
        );

        var escape = popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape));
        Assert.IsTrue(escape.Handled, "Escape was not routed through the mounted menu root.");
        Assert.IsTrue(opened.IsDismissed, "Escape did not dismiss the disabled popup menu.");
    }

    [TestMethod]
    public void HoveringSeparatorAndDisabledRowsPreservesMenuKeyboardRoute()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "mixed-menu-owner");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var target = owner.Mount(
            owner.Root,
            theme,
            Components.Button("Target", () => { }, Style.Empty.Height(40))
        );
        using var request = new ContextMenuRequest(
            owner,
            new(owner.Epoch, target.Id),
            new(12, 12, 1, 1),
            Components.Menu([
                Components.MenuItem("First", () => { }),
                Components.MenuSeparator(),
                Components.MenuItem("Second", () => { }),
                Components.MenuItem("Unavailable", () => { }, () => false),
                Components.MenuSubmenu(
                    "More",
                    () =>
                        Components.Menu([
                            Components.MenuItem("Nested first", () => { }),
                            Components.MenuSeparator(),
                            Components.MenuItem("Nested unavailable", () => { }, () => false),
                            Components.MenuItem("Nested second", () => { }),
                        ])
                ),
            ]),
            theme
        );
        var popup = request.CreateComposition();
        using var scene = InstallScene(popup);
        var rootLevel = request.ActiveLevels.Single();
        Assert.IsTrue(request.FocusFirst(rootLevel));
        var rootNodes = Nodes(popup.SemanticSnapshot()!).ToArray();
        var first = rootNodes.Single(node =>
            node.Role == SemanticRole.MenuItem && node.Name == "First"
        );
        var second = rootNodes.Single(node =>
            node.Role == SemanticRole.MenuItem && node.Name == "Second"
        );
        var unavailable = rootNodes.Single(node =>
            node.Role == SemanticRole.MenuItem && node.Name == "Unavailable"
        );
        var more = rootNodes.Single(node =>
            node.Role == SemanticRole.MenuItem && node.Name == "More"
        );
        Assert.AreEqual(first.Identity.ElementId, popup.Input.FocusedElement?.ElementId);

        var menuRoot = popup.Root.Children.Single();
        MovePointerOver(popup, scene, menuRoot.Children[1], 51);
        using var separatorScene = InstallScene(popup);
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled);
        Assert.AreEqual(
            second.Identity.ElementId,
            popup.Input.FocusedElement?.ElementId,
            "Hovering a separator discarded the keyboard-active menu item."
        );

        MovePointerOver(popup, separatorScene, unavailable.Identity.ElementId, 52);
        using var disabledScene = InstallScene(popup);
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled);
        Assert.AreEqual(
            more.Identity.ElementId,
            popup.Input.FocusedElement?.ElementId,
            "Hovering a disabled row discarded the keyboard-active menu item."
        );
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled);
        var nestedLevel = request.ActiveLevels.Single(level => level.Depth == 1);
        var nested = nestedLevel.Composition;
        using var nestedScene = InstallScene(nested);
        Assert.IsTrue(request.FocusFirst(nestedLevel));
        var nestedNodes = Nodes(nested.SemanticSnapshot()!).ToArray();
        var nestedFirst = nestedNodes.Single(node =>
            node.Role == SemanticRole.MenuItem && node.Name == "Nested first"
        );
        var nestedUnavailable = nestedNodes.Single(node =>
            node.Role == SemanticRole.MenuItem && node.Name == "Nested unavailable"
        );
        var nestedSecond = nestedNodes.Single(node =>
            node.Role == SemanticRole.MenuItem && node.Name == "Nested second"
        );
        Assert.AreEqual(nestedFirst.Identity.ElementId, nested.Input.FocusedElement?.ElementId);

        var nestedRoot = nested.Root.Children.Single();
        MovePointerOver(nested, nestedScene, nestedRoot.Children[1], 53);
        using var nestedSeparatorScene = InstallScene(nested);
        Assert.IsTrue(nested.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled);
        Assert.AreEqual(nestedSecond.Identity.ElementId, nested.Input.FocusedElement?.ElementId);
        MovePointerOver(nested, nestedSeparatorScene, nestedUnavailable.Identity.ElementId, 54);
        using var nestedDisabledScene = InstallScene(nested);
        Assert.IsTrue(nested.Input.DispatchKey(new(KeyCommandKind.Down, Key.Up)).Handled);
        Assert.AreEqual(
            nestedFirst.Identity.ElementId,
            nested.Input.FocusedElement?.ElementId,
            "A disabled nested row broke keyboard traversal."
        );
        Assert.IsTrue(nested.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled);
        Assert.HasCount(1, request.ActiveLevels);
        using var restoredRootScene = InstallScene(popup);
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled);
        Assert.IsTrue(request.IsDismissed);
    }

    [TestMethod]
    public void WheelOverDisabledChildChainsToAvailableScrollAncestor()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "disabled-wheel");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            composition.Root,
            theme,
            "Root",
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 40f)
                .Set(LayoutProperties.Clip, true)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        var scroll = Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 40f)
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(
            content,
            theme,
            "Content",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 120f)
        );
        var disabled = composition.Child(content, "disabled");
        Controls.Button(
            disabled,
            theme,
            "Disabled",
            () => Assert.Fail("A wheel dispatch invoked the disabled child."),
            Style
                .Empty.Set(InputProperties.Enabled, false)
                .Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 120f)
        );
        graph.Drain();
        var router = composition.Input;
        Assert.IsTrue(
            router.SetScene(SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper()))
        );

        var result = router.DispatchWheel(new(10, 10, 0, 20));

        Assert.AreEqual(InputRejection.None, result.Rejection);
        Assert.IsTrue(result.Handled, "Wheel over a disabled child did not reach its viewport.");
        Assert.AreEqual(20f, scroll.Offset.Y);
    }

    private static void Install(Composition composition)
    {
        for (var attempt = 0; attempt < 3; attempt++)
            if (
                composition.Input.SetScene(
                    SceneLayout.Project(composition, new(800, 600, 1), new EmptyShaper())
                )
            )
                return;
        Assert.Fail("Scene installation failed.");
    }

    private static RetainedScene InstallScene(Composition composition)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var scene = SceneLayout.Project(composition, new(800, 600, 1), new EmptyShaper());
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        throw new AssertFailedException("Scene installation failed.");
    }

    private static void MovePointerOver(
        Composition composition,
        RetainedScene scene,
        Element element,
        int pointerId
    ) => MovePointerOver(composition, scene, element.Id, pointerId);

    private static void MovePointerOver(
        Composition composition,
        RetainedScene scene,
        long elementId,
        int pointerId
    )
    {
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == elementId).Bounds;
        _ = composition.Input.DispatchPointer(
            new(
                PointerCommandKind.Move,
                pointerId,
                bounds.X + bounds.Width / 2,
                bounds.Y + bounds.Height / 2
            )
        );
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Nodes(child))
            yield return descendant;
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "controls",
                "controls",
                400,
                5,
                0,
                "controls",
                0,
                "controls#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                request.Text.Length,
                [glyph]
            );
            return new("controls", request.Text.Length, request.FontSize, [run]);
        }
    }
}
