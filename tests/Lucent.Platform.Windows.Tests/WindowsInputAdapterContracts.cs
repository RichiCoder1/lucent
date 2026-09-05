using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsInputAdapterContracts
{
    [TestMethod]
    public void SdlWheelPreservesFractionsPositionAndFlippedDirection()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "windows-wheel");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("windows-wheel"));
        Present(composition.Root, theme, 100, 100, true);
        var viewport = composition.Child(composition.Root, "viewport");
        var state = Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 100f)
        );
        var content = composition.Child(viewport, "content");
        content.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 200f)
                .Set(LayoutProperties.Height, 200f)
                .Set(LayoutProperties.MainShrink, 0f)
        );
        Assert.IsTrue(
            composition.Input.SetScene(
                SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper())
            )
        );
        using var adapter = new WindowsInputAdapter(composition);

        var precise = new SDL.Event
        {
            Type = (uint)SDL.EventType.MouseWheel,
            Wheel = new SDL.MouseWheelEvent
            {
                Type = SDL.EventType.MouseWheel,
                X = 0.5f,
                Y = -0.25f,
                MouseX = 10,
                MouseY = 10,
                Direction = SDL.MouseWheelDirection.Normal,
            },
        };
        Assert.IsTrue(adapter.Dispatch(precise));
        Assert.AreEqual(20f, state.Offset.X);
        Assert.AreEqual(10f, state.Offset.Y);
        Assert.IsTrue(adapter.ConsumeRepaintRequest());

        var flipped = precise;
        flipped.Wheel.X = -0.25f;
        flipped.Wheel.Y = 0.5f;
        flipped.Wheel.Direction = SDL.MouseWheelDirection.Flipped;
        Assert.IsTrue(adapter.Dispatch(flipped));
        Assert.AreEqual(30f, state.Offset.X);
        Assert.AreEqual(30f, state.Offset.Y);

        precise.Wheel.X = float.NaN;
        Assert.IsFalse(adapter.Dispatch(precise), "Non-finite native wheel input was accepted.");
    }

    [TestMethod]
    public void ShortcutMappingAddsApplicationKeysWithoutStealingAltGr()
    {
        var control = KeyModifiers.Control;
        Assert.AreEqual(Key.F, WindowsInputAdapter.MapShortcut(SDL.Keycode.F, control));
        Assert.AreEqual(Key.N, WindowsInputAdapter.MapShortcut(SDL.Keycode.N, control));
        Assert.AreEqual(Key.S, WindowsInputAdapter.MapShortcut(SDL.Keycode.S, control));
        Assert.AreEqual(Key.S, WindowsInputAdapter.MapShortcut(SDL.Keycode.S, KeyModifiers.Meta));
        Assert.IsNull(
            WindowsInputAdapter.MapShortcut(SDL.Keycode.S, KeyModifiers.Control | KeyModifiers.Alt),
            "Control+Alt AltGr-like input was mapped as an application shortcut."
        );
    }

    private static void Present(
        Element element,
        ThemeContext theme,
        float width,
        float height,
        bool clip
    ) =>
        element.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, width)
                .Set(LayoutProperties.Height, height)
                .Set(LayoutProperties.Axis, LayoutAxis.Column)
                .Set(LayoutProperties.Clip, clip)
        );

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
