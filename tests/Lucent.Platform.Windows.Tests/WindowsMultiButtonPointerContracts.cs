using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsMultiButtonPointerContracts
{
    [TestMethod]
    public void SecondaryClickDoesNotEndActivePrimaryTextSelectionCapture()
    {
        using var composition = new Composition(new ReactiveGraph(), "windows-multi-button");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(
            theme,
            author: Style.Empty.Width(240).Height(60).Axis(LayoutAxis.Column)
        );
        var editor = composition.Child(composition.Root, "editor");
        var state = Controls.TextField(
            editor,
            theme,
            "Editor",
            "abcdefghij",
            Style.Empty.Width(220).Height(40)
        );
        using var renderer = new SkiaSceneRenderer();
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(240, 60, 1), renderer);
        Assert.IsTrue(composition.Input.SetScene(scene));
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == editor.Id).Bounds;
        using var adapter = new WindowsInputAdapter(
            composition,
            coordinateScale: new WindowsCoordinateScale(1, 1)
        );
        const uint mouse = 7;
        var y = bounds.Y + bounds.Height / 2;

        Assert.IsTrue(
            adapter.Dispatch(Button(SDL.EventType.MouseButtonDown, mouse, 1, bounds.X + 10, y))
        );
        Assert.IsTrue(adapter.Dispatch(Motion(mouse, bounds.X + 60, y)));
        var selectionAfterPrimaryDrag = Math.Abs(state.Caret - state.Anchor);
        Assert.IsTrue(
            selectionAfterPrimaryDrag > 0,
            "Primary drag did not establish text selection."
        );

        Assert.IsTrue(
            adapter.Dispatch(Button(SDL.EventType.MouseButtonDown, mouse, 3, bounds.X + 60, y))
        );
        Assert.IsTrue(
            adapter.Dispatch(Button(SDL.EventType.MouseButtonUp, mouse, 3, bounds.X + 60, y))
        );
        Assert.IsTrue(
            composition.Input.Dump().Contains("capture pointer=", StringComparison.Ordinal),
            "Secondary release ended the active primary pointer capture."
        );

        Assert.IsTrue(adapter.Dispatch(Motion(mouse, bounds.X + 110, y)));
        Assert.IsTrue(
            Math.Abs(state.Caret - state.Anchor) > selectionAfterPrimaryDrag,
            "Primary selection stopped extending after the secondary click."
        );
        Assert.IsTrue(
            adapter.Dispatch(Button(SDL.EventType.MouseButtonUp, mouse, 1, bounds.X + 110, y))
        );
        Assert.IsFalse(
            composition.Input.Dump().Contains("capture pointer=", StringComparison.Ordinal),
            "Primary release did not end its own capture."
        );
    }

    private static SDL.Event Button(
        SDL.EventType type,
        uint source,
        byte button,
        float x,
        float y
    ) =>
        new()
        {
            Button = new()
            {
                Type = type,
                Which = source,
                Button = button,
                X = x,
                Y = y,
            },
        };

    private static SDL.Event Motion(uint source, float x, float y) =>
        new()
        {
            Motion = new()
            {
                Type = SDL.EventType.MouseMotion,
                Which = source,
                X = x,
                Y = y,
            },
        };
}
