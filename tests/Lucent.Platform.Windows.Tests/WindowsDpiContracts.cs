using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsDpiContracts
{
    [TestMethod]
    public void CoordinateScaleSeparatesContentScaleFromBackingPixelDensity()
    {
        foreach (
            var (contentScale, density) in new[]
            {
                (1f, 1f),
                (1.25f, 1f),
                (1.5f, 1f),
                (2f, 1f),
                (2f, 2f),
            }
        )
        {
            var scale = new WindowsCoordinateScale(contentScale, density);
            const float logical = 80f;
            var window = scale.LogicalToWindow(logical);

            Assert.AreEqual(logical, scale.WindowToLogical(window), 0.001f);
            Assert.AreEqual(logical * contentScale / density, window, 0.001f);
            Assert.AreEqual(
                (int)MathF.Round(logical * contentScale),
                scale.LogicalToScreenPixels(logical),
                "Screen geometry follows content scale, not backing pixel density."
            );
        }

        Assert.AreEqual(
            125,
            WindowsCoordinateScale.WindowToScreenPixels(125),
            "Windows SDL event coordinates are already physical screen pixels."
        );
    }

    [TestMethod]
    public void PointerHitTestingAndDragUseLogicalCoordinatesAtEveryDpiScale()
    {
        foreach (var contentScale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            using var composition = new Composition(
                new ReactiveGraph(),
                $"windows-pointer-{contentScale:R}"
            );
            var theme = new ThemeContext(composition.Root.Scope, new Theme("windows-pointer"));
            composition.Root.Present(
                theme,
                author: Style
                    .Empty.Set(LayoutProperties.Width, 40f)
                    .Set(LayoutProperties.Height, 40f)
                    .Set(LayoutProperties.Clip, true)
            );
            var target = composition.Child(composition.Root, "target");
            target.Present(
                theme,
                author: Style
                    .Empty.Set(LayoutProperties.Width, 20f)
                    .Set(LayoutProperties.Height, 20f)
                    .Set(VisualProperties.Background, Color.Parse("#ffffff"))
            );
            target.AttachBehaviors(
                new RowActionBehavior(
                    "target-action",
                    new(SemanticRole.Button, "target", actions: SemanticAction.Invoke),
                    static () => { }
                )
            );

            using var renderer = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(40, 40, 1), renderer);
            Assert.IsTrue(composition.Input.SetScene(scene));
            using var adapter = new WindowsInputAdapter(
                composition,
                coordinateScale: new WindowsCoordinateScale(contentScale, 1)
            );

            const float logicalDownX = 5f;
            const float logicalDownY = 6f;
            var nativeDownX = logicalDownX * contentScale;
            var nativeDownY = logicalDownY * contentScale;
            Assert.IsTrue(
                adapter.Dispatch(
                    new SDL.Event
                    {
                        Button = new()
                        {
                            Type = SDL.EventType.MouseButtonDown,
                            Which = 9,
                            Button = 1,
                            X = nativeDownX,
                            Y = nativeDownY,
                        },
                    }
                ),
                $"Pointer down was not accepted at {contentScale:R}x."
            );
            Assert.AreEqual(target.Id, composition.Input.FocusedElement?.ElementId);
            AssertLogicalPoint(adapter.PointerPosition, logicalDownX, logicalDownY);

            const float logicalDragX = 15f;
            const float logicalDragY = 16f;
            Assert.IsTrue(
                adapter.Dispatch(
                    new SDL.Event
                    {
                        Motion = new()
                        {
                            Type = SDL.EventType.MouseMotion,
                            Which = 9,
                            X = logicalDragX * contentScale,
                            Y = logicalDragY * contentScale,
                        },
                    }
                ),
                $"Captured pointer motion was not accepted at {contentScale:R}x."
            );
            AssertLogicalPoint(adapter.PointerPosition, logicalDragX, logicalDragY);
        }
    }

    [TestMethod]
    public void ImeCaretAreaUsesWindowUnitsWithoutLosingLogicalCaretGeometry()
    {
        foreach (
            var (contentScale, density) in new[]
            {
                (1f, 1f),
                (1.25f, 1f),
                (1.5f, 1f),
                (2f, 1f),
                (2f, 2f),
            }
        )
        {
            using var composition = new Composition(
                new ReactiveGraph(),
                $"windows-ime-{contentScale:R}-{density:R}"
            );
            using var renderer = new SkiaSceneRenderer();
            using var session = new EditorSession(composition.Root.Scope, "draft");
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Mount(
                composition.Root,
                theme,
                Components.TextField(session: session, style: Style.Empty.Width(180).Height(100))
            );

            void Install()
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    composition.Flush();
                    if (
                        composition.Input.SetScene(
                            SceneLayout.Project(composition, new(180, 100, 1), renderer)
                        )
                    )
                        return;
                }
                throw new InvalidOperationException("Editor scene did not settle.");
            }

            Install();
            Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
            Install();
            Assert.IsTrue(composition.Input.TryGetCaretGeometry(out var caret));

            SDL.Rect? area = null;
            using var adapter = new WindowsInputAdapter(
                composition,
                1,
                textInput: new TextInputTransport(
                    _ => false,
                    _ => true,
                    _ => true,
                    (_, value, _) =>
                    {
                        area = value;
                        return true;
                    }
                ),
                coordinateScale: new WindowsCoordinateScale(contentScale, density)
            );
            adapter.RefreshTextInput();

            var actual = area ?? throw new InvalidOperationException("SDL IME area was not set.");
            var scale = new WindowsCoordinateScale(contentScale, density);
            Assert.AreEqual(scale.LogicalToWindowRound(caret.X), actual.X);
            Assert.AreEqual(scale.LogicalToWindowRound(caret.Y), actual.Y);
            Assert.AreEqual(Math.Max(1, scale.LogicalToWindowCeiling(caret.Width)), actual.W);
            Assert.AreEqual(Math.Max(1, scale.LogicalToWindowCeiling(caret.Height)), actual.H);
        }
    }

    [TestMethod]
    public void PopupAnchorUsesWindowUnitsWhileSafeIntentAndUiaUsePhysicalScreenUnits()
    {
        foreach (
            var (contentScale, density) in new[]
            {
                (1f, 1f),
                (1.25f, 1f),
                (1.5f, 1f),
                (2f, 1f),
                (2f, 2f),
            }
        )
        {
            var anchor = new LayoutRect(100, 40, 32, 16);
            var placement = WindowsPopupPlacement.Root(
                anchor,
                new LayoutRect(0, 0, 200, 100),
                contentScale,
                density
            );
            var scale = new WindowsCoordinateScale(contentScale, density);

            Assert.AreEqual(
                scale.LogicalToWindowCeiling(anchor.X - WindowsPopupHost.ShadowMargin),
                placement.OffsetX
            );
            Assert.AreEqual(
                scale.LogicalToWindowCeiling(
                    anchor.Y + anchor.Height - WindowsPopupHost.ShadowMargin
                ),
                placement.OffsetY
            );
            Assert.AreEqual(
                scale.LogicalToScreenPixels(anchor.X),
                (int)MathF.Round(anchor.X * contentScale)
            );
        }
    }

    [TestMethod]
    public void PopupPlacementDoesNotScaleScreenWorkAreaByBackingDensity()
    {
        var placement = WindowsPopupPlacement.Submenu(
            new PopupScreenRect(780, 100, 820, 140),
            new LayoutRect(0, 0, 200, 120),
            new PopupScreenRect(580, 80, 820, 400),
            new SDL.Rect
            {
                X = 0,
                Y = 0,
                W = 1000,
                H = 700,
            },
            scale: 2,
            density: 2
        );

        Assert.IsTrue(
            placement.OpensLeft,
            "The screen work area was incorrectly enlarged by backing pixel density."
        );
        Assert.AreEqual(780 - 200 - 2 * WindowsPopupHost.ShadowMargin - 580, placement.OffsetX);
    }

    private static void AssertLogicalPoint(
        (float X, float Y)? actual,
        float expectedX,
        float expectedY
    )
    {
        Assert.IsTrue(actual.HasValue, "The adapter did not publish pointer position.");
        Assert.AreEqual(expectedX, actual.Value.X, 0.001f);
        Assert.AreEqual(expectedY, actual.Value.Y, 0.001f);
    }
}
