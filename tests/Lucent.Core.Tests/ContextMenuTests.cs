using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ContextMenuTests
{
    [TestMethod]
    public void SecondaryInvocationPreservesSelectionAndOwnsOnePopup()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "menu-owner");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal(false, "selected");
        var calls = 0;
        var opening = new List<bool>();
        var enabled = graph.Signal(true, "enabled");
        _ = owner.Mount(
            owner.Root,
            theme,
            Components.ContextMenu(
                [
                    Components.Selectable(
                        () => "Target",
                        () => selected.Value,
                        () => selected.Value = true,
                        Style.Empty.Height(40)
                    ),
                ],
                () =>
                    Components.Menu([
                        Components.MenuItem("Disabled", () => calls += 10, () => false),
                        Components.MenuItem("Run", () => calls++, () => enabled.Value),
                        Components.MenuItem("Other", () => calls += 2),
                    ]),
                onOpenChanged: opening.Add
            )
        );
        ContextMenuRequest? request = null;
        owner.Input.ContextMenuRequested += value => request = value;
        Install(owner);
        owner.Input.DispatchPointer(
            new(PointerCommandKind.Down, 1, 10, 10, PointerButton.Secondary)
        );
        Install(owner);
        owner.Input.DispatchPointer(new(PointerCommandKind.Up, 1, 10, 10));
        Assert.IsNotNull(request);
        using var popupRequest = request;
        Assert.IsFalse(selected.Value);
        Assert.AreEqual(1, opening.Count);
        Assert.IsTrue(opening[0]);
        var size = request.Measure(new EmptyShaper(), new(800, 600, 1));
        Assert.AreEqual(260f, size.Width);
        Assert.IsTrue(size.Height >= 96 && size.Height < 600);
        var popup = request.CreateComposition();
        Assert.AreSame(popup, request.CreateComposition());
        Install(popup);
        Assert.IsTrue(popup.Input.MoveFocus(FocusTraversalDirection.Next));
        Install(popup);
        var first = Nodes(popup.SemanticSnapshot()!).Single(node => node.Name == "Run");
        Assert.AreEqual(SemanticRole.MenuItem, first.Role);
        Assert.AreEqual(first.Identity.ElementId, popup.Input.FocusedElement!.Value.ElementId);
        popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down));
        Install(popup);
        var other = Nodes(popup.SemanticSnapshot()!).Single(node => node.Name == "Other");
        Assert.AreEqual(other.Identity.ElementId, popup.Input.FocusedElement!.Value.ElementId);
        popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Home));
        Install(popup);
        popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter));
        Assert.AreEqual(1, calls);
        Assert.IsTrue(request.IsDismissed);
        Assert.IsFalse(selected.Value);
        request.Dispose();
        Assert.AreEqual(2, opening.Count);
        Assert.IsFalse(opening[1]);
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
