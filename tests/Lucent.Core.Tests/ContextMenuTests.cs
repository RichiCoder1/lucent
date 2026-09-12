using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ContextMenuTests
{
    [TestMethod]
    public void NestedEditorOwnsCursorAndMenuBeforeItsSelectableAncestor()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "nested-editor");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        Element? editor = null;
        var row = owner.Mount(
            owner.Root,
            theme,
            Components.ContextMenu(
                [
                    Components.Selectable(
                        [
                            ComponentRecipe.Create(
                                "editor",
                                (_, root) =>
                                {
                                    editor = root;
                                    Controls.TextField(
                                        root,
                                        theme,
                                        "Editor",
                                        style: Style.Empty.Height(40)
                                    );
                                }
                            ),
                        ],
                        () => "Row",
                        () => false
                    ),
                ],
                () => Components.Menu([Components.MenuItem("Outer action", () => { })])
            )
        );
        ContextMenuRequest? request = null;
        owner.Input.ContextMenuRequested += value => request = value;
        Install(owner);
        var scene = SceneLayout.Project(owner, new(800, 600, 1), new EmptyShaper());
        Assert.IsTrue(owner.Input.SetScene(scene));
        Assert.IsNotNull(editor);
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == editor.Id).Bounds;
        var x = bounds.X + 2;
        var y = bounds.Y + 2;
        Assert.AreEqual(CursorIntent.Text, owner.Input.CursorAt(x, y));
        owner.Input.DispatchPointer(new(PointerCommandKind.Down, 1, x, y, PointerButton.Secondary));
        owner.Input.DispatchPointer(new(PointerCommandKind.Up, 1, x, y));
        Assert.IsNotNull(request);
        using var popupRequest = request;
        Assert.IsTrue(
            Nodes(request.CreateComposition().SemanticSnapshot()!).Any(node => node.Name == "Copy")
        );
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 1)]
    public void DisabledChildBlocksParentActivationUnlessPointerTransparent(
        bool transparent,
        int expected
    )
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "disabled-child");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var activations = 0;
        owner.Mount(
            owner.Root,
            theme,
            Components.Selectable(
                [
                    Components.Button(
                        "Disabled",
                        () => Assert.Fail("Disabled child invoked."),
                        Style
                            .Empty.Height(40)
                            .Set(InputProperties.Enabled, false)
                            .Set(InputProperties.PointerTransparent, transparent)
                    ),
                ],
                () => "Row",
                () => false,
                () => activations++
            )
        );
        Install(owner);
        owner.Input.DispatchPointer(new(PointerCommandKind.Down, 1, 20, 20, PointerButton.Primary));
        owner.Input.DispatchPointer(new(PointerCommandKind.Up, 1, 20, 20));
        Assert.AreEqual(
            expected,
            activations,
            "Disabled and pointer-transparent hit behavior disagreed."
        );
    }

    [TestMethod]
    public void KeyboardInvocationDismissalAndTargetDisposalAreBounded()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "keyboard-menu");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var target = owner.Mount(
            owner.Root,
            theme,
            Components.ContextMenu(
                [Components.Button("Target", () => { }, Style.Empty.Height(40))],
                () => Components.Menu([Components.MenuItem("Run", () => { })])
            )
        );
        ContextMenuRequest? request = null;
        owner.Input.ContextMenuRequested += value => request = value;
        Install(owner);
        owner.Input.MoveFocus(FocusTraversalDirection.Next);
        Install(owner);
        var original = owner.Input.FocusedElement;
        Assert.IsTrue(
            owner.Input.DispatchKey(new(KeyCommandKind.Down, Key.F10, KeyModifiers.Shift)).Handled
        );
        Assert.IsNotNull(request);
        using var popupRequest = request;
        var popup = request.CreateComposition();
        Install(popup);
        popup.Input.MoveFocus(FocusTraversalDirection.Next);
        Install(popup);
        popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape));
        Assert.IsTrue(request.IsDismissed);
        Install(owner);
        Assert.IsTrue(request.RestoreFocus());
        Assert.AreEqual(original, owner.Input.FocusedElement);
        Install(owner);
        owner.Input.DispatchKey(new(KeyCommandKind.Down, Key.ContextMenu));
        Assert.IsNotNull(request);
        using var next = request;
        target.Dispose();
        Assert.IsFalse(request.IsValid);
        Assert.IsTrue(request.IsDismissed);
    }

    [TestMethod]
    public void CursorUsesEligibleControlAndAuthorOverride()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "cursor");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var cursor = graph.Signal(CursorIntent.Auto, "cursor-intent");
        var enabled = graph.Signal(true, "cursor-enabled");
        _ = owner.Mount(
            owner.Root,
            theme,
            Components.Button(
                "Action",
                () => { },
                Style
                    .Empty.Height(40)
                    .Bind(InputProperties.Cursor, () => cursor.Value)
                    .Bind(InputProperties.Enabled, () => enabled.Value)
            )
        );
        Install(owner);
        Assert.AreEqual(CursorIntent.Pointer, owner.Input.CursorAt(10, 10));
        Assert.AreEqual(CursorIntent.Default, owner.Input.CursorAt(float.NaN, 10));
        cursor.Value = CursorIntent.Default;
        Install(owner);
        Assert.AreEqual(CursorIntent.Default, owner.Input.CursorAt(10, 10));
        cursor.Value = CursorIntent.Auto;
        enabled.Value = false;
        Install(owner);
        Assert.AreEqual(CursorIntent.Default, owner.Input.CursorAt(10, 10));
    }

    [TestMethod]
    public void StandardTextMenuUsesOriginalSelectionAndClipboardContract()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "text-menu");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var field = owner.Child(owner.Root, "editor");
        var state = Controls.TextField(field, theme, "Editor", style: Style.Empty.Height(40));
        state.Value = "draft text";
        state.SelectAll();
        ContextMenuRequest? request = null;
        owner.Input.ContextMenuRequested += value => request = value;
        Install(owner);
        owner.Input.MoveFocus(FocusTraversalDirection.Next);
        Install(owner);
        owner.Input.DispatchKey(new(KeyCommandKind.Down, Key.ContextMenu));
        Assert.IsNotNull(request);
        using var popupRequest = request;
        var popup = request.CreateComposition();
        Install(popup);
        var cut = Nodes(popup.SemanticSnapshot()!).Single(node => node.Name == "Cut");
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            popup.ExecuteSemanticCommand(cut.Identity, new(SemanticCommandKind.Invoke))
        );
        Assert.IsTrue(request.IsDismissed);
        Assert.IsTrue(owner.Input.TryTakeClipboardRequest(out var clipboard));
        Assert.AreEqual("draft text", clipboard.Text);
        Assert.IsTrue(owner.Input.CompleteClipboardRequest(clipboard, true));
        Assert.AreEqual("", state.Value);
        Assert.IsTrue(request.RestoreFocus());
        Assert.AreEqual(field.Id, owner.Input.FocusedElement!.Value.ElementId);
    }

    [TestMethod]
    public void ShortStockMenuDoesNotReserveUnusedScrollbarGutter()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "short-menu-owner");
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
            Components.Menu([Components.MenuItem("Run command", () => { })]),
            theme
        );
        var popup = request.CreateComposition();
        var available = new LayoutViewport(240, 200, 1);
        var measured = request.Measure(new EmptyShaper(), available);
        using var scene = SceneLayout.Project(
            popup,
            new(measured.Width, measured.Height, 1),
            new EmptyShaper()
        );
        var nodes = Nodes(popup.SemanticSnapshot()!).ToArray();
        var menu = nodes.Single(node => node.Role == SemanticRole.Menu);
        var item = nodes.Single(node => node.Role == SemanticRole.MenuItem);
        var menuBounds = scene
            .Boxes.Single(box => box.Identity.ElementId == menu.Identity.ElementId)
            .Bounds;
        var itemBounds = scene
            .Boxes.Single(box => box.Identity.ElementId == item.Identity.ElementId)
            .Bounds;

        Assert.AreEqual(6f, itemBounds.X - menuBounds.X);
        Assert.AreEqual(
            6f,
            menuBounds.X + menuBounds.Width - itemBounds.X - itemBounds.Width,
            0.001f,
            "A short stock menu retained an unused scrollbar gutter on its right edge."
        );
        Assert.AreEqual(0, scene.ScrollBars.Count);

        var stableRevision = graph.MutationRevision;
        _ = request.Measure(new EmptyShaper(), available);
        Assert.AreEqual(
            stableRevision,
            graph.MutationRevision,
            "Remeasuring unchanged stock-menu content churned its input projection."
        );
    }

    [TestMethod]
    public void StockMenuRemeasuresWhenConditionalRowParticipationChanges()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "conditional-menu-owner");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var target = owner.Mount(
            owner.Root,
            theme,
            Components.Button("Target", () => { }, Style.Empty.Height(40))
        );
        var showExtra = graph.Signal(false, "show-extra-menu-row");
        using var request = new ContextMenuRequest(
            owner,
            new(owner.Epoch, target.Id),
            new(12, 12, 1, 1),
            Components.Menu([
                Components.MenuItem("Run", () => { }),
                ContentRecipe.When(
                    "extra-menu-row",
                    () => showExtra.Value,
                    Components.MenuItem("Inspect", () => { })
                ),
            ]),
            theme
        );
        var popup = request.CreateComposition();
        var available = new LayoutViewport(240, 200, 1);
        var compact = request.Measure(new EmptyShaper(), available);
        var compactRevision = graph.MutationRevision;

        showExtra.Value = true;
        graph.Drain();
        var expanded = request.Measure(new EmptyShaper(), available);

        Assert.IsTrue(expanded.Height > compact.Height, "The visible row did not resize the menu.");
        Assert.AreNotEqual(
            compactRevision,
            graph.MutationRevision,
            "Conditional row participation did not invalidate adaptive menu measurement."
        );
    }

    [TestMethod]
    public void AuthoredMenuScrollbarVisibilityRemainsLiveAfterMeasurement()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "authored-menu-owner");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var target = owner.Mount(
            owner.Root,
            theme,
            Components.Button("Target", () => { }, Style.Empty.Height(40))
        );
        var visibility = graph.Signal(ScrollBarVisibility.Auto, "menu-scrollbar-visibility");
        using var request = new ContextMenuRequest(
            owner,
            new(owner.Epoch, target.Id),
            new(12, 12, 1, 1),
            Components.Menu(
                [Components.MenuItem("Run command", () => { })],
                Style.Empty.Bind(ScrollBarProperties.Visibility, () => visibility.Value)
            ),
            theme
        );
        var popup = request.CreateComposition();
        var menu = popup.Root.Children.Single();
        var available = new LayoutViewport(240, 200, 1);

        _ = request.Measure(new EmptyShaper(), available);
        Assert.AreEqual(
            ScrollBarVisibility.Auto,
            menu.Resolve(ScrollBarProperties.Visibility).Value
        );
        Assert.AreEqual("author", menu.Resolve(ScrollBarProperties.Visibility).Winner.Source);

        visibility.Value = ScrollBarVisibility.Hidden;
        graph.Drain();
        _ = request.Measure(new EmptyShaper(), available);
        Assert.AreEqual(
            ScrollBarVisibility.Hidden,
            menu.Resolve(ScrollBarProperties.Visibility).Value,
            "Menu measurement replaced a live authored scrollbar binding."
        );
        Assert.AreEqual("author", menu.Resolve(ScrollBarProperties.Visibility).Winner.Source);
    }

    [TestMethod]
    public void OversizedMenuIsBoundedAndSemanticallyScrollable()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "long-menu-owner");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var target = owner.Mount(
            owner.Root,
            theme,
            Components.Button("Target", () => { }, Style.Empty.Height(40))
        );
        var items = Enumerable
            .Range(0, 20)
            .Select(index => (ContentRecipe)Components.MenuItem($"Action {index}", () => { }))
            .ToArray();
        using var request = new ContextMenuRequest(
            owner,
            new(owner.Epoch, target.Id),
            new(12, 12, 1, 1),
            Components.Menu(ComponentContent.Create(items)),
            theme
        );
        var popup = request.CreateComposition();
        var available = new LayoutViewport(240, 100, 1);
        var measured = request.Measure(new EmptyShaper(), available);
        Assert.IsTrue(
            measured.Height <= available.Height,
            $"Menu height {measured.Height} exceeded available height {available.Height}."
        );

        graph.Drain();
        var scene = SceneLayout.Project(popup, available, new EmptyShaper());
        Assert.IsTrue(popup.Input.SetScene(scene), "Long-menu scene was rejected.");
        var menu = Nodes(popup.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.Menu);
        var menuElement = new ElementIdentity(
            menu.Identity.CompositionEpoch,
            menu.Identity.ElementId
        );
        Assert.IsTrue(
            menu.Actions.HasFlag(SemanticAction.Scroll),
            "The retained menu did not expose semantic scrolling."
        );
        var scrollbar = scene.ScrollBars.SingleOrDefault(bar =>
            bar.Viewport.ElementId == menu.Identity.ElementId
        );
        Assert.IsTrue(
            scrollbar.Maximum.Y > 0,
            "An oversized menu did not project a scrollable retained viewport."
        );
        var preScrollRevision = graph.MutationRevision;
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            popup.ExecuteSemanticCommand(
                menu.Identity,
                new(SemanticCommandKind.Scroll, Endpoint: SemanticScrollEndpoint.End)
            )
        );
        var scroll = popup.Input.GetSemanticScroll(menuElement);
        Assert.IsTrue(
            scroll.HasValue
                && scroll.Value.Offset.Y > 0
                && scroll.Value.Offset.Y <= scroll.Value.Maximum.Y,
            "Semantic End did not move the bounded menu viewport."
        );

        measured = request.Measure(new EmptyShaper(), available);
        using var remeasured = SceneLayout.Project(popup, available, new EmptyShaper());
        Assert.AreEqual(
            1,
            remeasured.ScrollBars.Count,
            "Remeasuring a menu scrolled to its end hid its overflow scrollbar."
        );
        Assert.IsTrue(measured.Height <= available.Height);
        Assert.AreNotEqual(
            preScrollRevision,
            graph.MutationRevision,
            "Scrolling did not invalidate the cached adaptive menu measurement."
        );
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

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
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
