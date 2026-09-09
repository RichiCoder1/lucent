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
