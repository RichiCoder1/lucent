using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class PresenterResetContracts
{
    [TestMethod]
    public void ResetRecreatesResourcesAndPresentsTheSameMountedScene()
    {
        WithRenderer(native =>
        {
            using var composition = new Composition(new ReactiveGraph(), "reset-scene");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var field = composition.Child(composition.Root, "draft");
            var editor = Controls.TextField(field, theme, "Draft", "retained text");
            using var shaper = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(128, 64, 1), shaper);
            using var presenter = new CpuSkiaPresenter(native);
            presenter.Present(scene, new(128, 64, 1), shaper);
            Assert.AreEqual(1, presenter.ResourceCreationCount);
            Assert.IsTrue(presenter.HandleRendererEvent(SDL.EventType.RenderTargetsReset));
            presenter.Present(scene, new(128, 64, 1), shaper);
            Assert.AreEqual(
                1,
                presenter.ResourceCreationCount,
                "Target content reset needlessly recreated the streaming texture."
            );
            Assert.IsTrue(presenter.HandleRendererEvent(SDL.EventType.RenderDeviceReset));
            Assert.AreEqual(0, presenter.LiveSurfaceCount);
            Assert.AreEqual(0, presenter.LiveTextureCount);
            presenter.Present(scene, new(128, 64, 1), shaper);
            Assert.AreEqual(2, presenter.ResourceCreationCount);
            Assert.AreEqual("retained text", editor.Value);
            Assert.IsTrue(scene.Boxes.Any(box => box.Identity.ElementId == field.Id));
            presenter.Present(scene, new(128, 64, 1), shaper);
            Assert.AreEqual(
                2,
                presenter.ResourceCreationCount,
                "Recovery recreated resources on every frame."
            );
        });
    }

    [TestMethod]
    public void RecreationFailureReleasesPartialResourcesAndPreservesTheFailure()
    {
        WithRenderer(native =>
        {
            var attempts = 0;
            var failure = new InvalidOperationException("texture recreation failed");
            using var presenter = new CpuSkiaPresenter(
                native,
                descriptor =>
                {
                    if (++attempts > 1)
                        throw failure;
                    return SDL.CreateTexture(
                        native,
                        SDL.PixelFormat.ABGR8888,
                        SDL.TextureAccess.Streaming,
                        descriptor.Width,
                        descriptor.Height
                    );
                }
            );
            using var composition = new Composition(new ReactiveGraph(), "reset-failure");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Root.Present(theme);
            using var shaper = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(128, 64, 1), shaper);
            presenter.Present(scene, new(128, 64, 1), shaper);
            presenter.HandleRendererEvent(SDL.EventType.RenderDeviceReset);
            var actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
                presenter.Present(scene, new(128, 64, 1), shaper)
            );
            Assert.AreSame(failure, actual);
            Assert.AreEqual(2, attempts, "Presentation retried a failed allocation.");
            Assert.AreEqual(0, presenter.LiveSurfaceCount);
            Assert.AreEqual(0, presenter.LiveTextureCount);
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                presenter.HandleRendererEvent(SDL.EventType.RenderDeviceLost)
            );
        });
    }

    [TestMethod]
    public void ResetEventsAreRoutedOnlyToTheirRendererWindow()
    {
        foreach (
            var type in new[]
            {
                SDL.EventType.RenderTargetsReset,
                SDL.EventType.RenderDeviceReset,
                SDL.EventType.RenderDeviceLost,
            }
        )
        {
            var message = new SDL.Event
            {
                Render = new() { Type = type, WindowID = 17 },
            };
            Assert.AreEqual(17u, WindowsPopupHost.EventWindowId(message));
            Assert.IsFalse(WindowsBootstrap.IsForeignWindowEvent(message, 17));
            Assert.IsTrue(WindowsBootstrap.IsForeignWindowEvent(message, 18));
            Assert.IsTrue(WindowsPopupHost.TargetsPopup(message, 17, 18));
            Assert.IsFalse(WindowsPopupHost.TargetsPopup(message, 18, 17));
        }
    }

    private static void WithRenderer(Action<nint> body)
    {
        Assert.IsTrue(SDL.Init(SDL.InitFlags.Video), SDL.GetError());
        var window = SDL.CreateWindow(
            "Lucent presenter reset fixture",
            128,
            64,
            SDL.WindowFlags.Hidden
        );
        nint renderer = 0;
        try
        {
            Assert.AreNotEqual((nint)0, window, SDL.GetError());
            renderer = SDL.CreateRenderer(window, "software");
            Assert.AreNotEqual((nint)0, renderer, SDL.GetError());
            body(renderer);
        }
        finally
        {
            if (renderer != 0)
                SDL.DestroyRenderer(renderer);
            if (window != 0)
                SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}
