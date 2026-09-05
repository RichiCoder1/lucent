using System.Runtime.InteropServices;
using Lucent.Core;
using Lucent.Platform.Windows;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void ApplicationNameAndClippedPointLookup()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA geometry) failed.");
        var window = CreateWindow("Links and notes");
        try
        {
            var hwnd = Hwnd(window);
            using var composition = new Composition(new ReactiveGraph(), "uia-geometry");
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            Controls.Column(composition.Root, theme, "Root", Size(200, 200));
            var clipper = composition.Child(composition.Root, "presentation-clip");
            clipper.Present(theme, author: Size(40, 20).Set(LayoutProperties.Clip, true));
            var partial = composition.Child(clipper, "partial");
            Controls.Button(partial, theme, "Partly clipped", () => { }, Size(60, 30));
            var full = composition.Child(clipper, "overscan");
            Controls.Button(full, theme, "Fully clipped", () => { }, Size(60, 30));
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                hwnd,
                composition,
                dispatcher,
                "Links and notes"
            );
            using var renderer = new SkiaSceneRenderer();
            Assert(
                ReadName(provider.InterfacePointer) == "Links and notes",
                "Root used another application's name."
            );
            var origin = new GeometryPoint();
            Assert(ClientToScreen(hwnd, ref origin), "Could not resolve UIA client origin.");
            var deviceScale = GetDpiForWindow(hwnd) / 96d;
            Assert(deviceScale > 0, "Window DPI was unavailable.");
            foreach (var layoutScale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                var scene = SceneLayout.Project(composition, new(200, 200, layoutScale), renderer);
                Assert(composition.Input.SetScene(scene), "UIA geometry scene was rejected.");
                provider.Refresh(scene);
                var partialBounds = scene
                    .Boxes.Single(box => box.Identity.ElementId == partial.Id)
                    .Bounds;
                var fullBounds = scene
                    .Boxes.Single(box => box.Identity.ElementId == full.Id)
                    .Bounds;
                var clip = scene
                    .Input.Single(item => item.Identity.ElementId == clipper.Id)
                    .ChildClipBounds!.Value;
                Assert(
                    partialBounds.Y < clip.Y + clip.Height
                        && partialBounds.Y + partialBounds.Height > clip.Y + clip.Height,
                    "Fixture did not produce a partially clipped child."
                );
                Assert(
                    fullBounds.Y >= clip.Y + clip.Height,
                    "Fixture did not produce a fully clipped child."
                );
                Assert(
                    At(clip.X + 5, clip.Y + 5) == "Partly clipped",
                    "Visible child intersection was not hittable."
                );
                Assert(
                    At(clip.X + 5, clip.Y + clip.Height + 2) != "Partly clipped",
                    "Point lookup hit below an ancestor clip."
                );
                Assert(
                    At(clip.X + clip.Width + 2, clip.Y + 5) != "Partly clipped",
                    "Point lookup hit beyond the horizontal clip."
                );
                Assert(
                    At(fullBounds.X + 5, fullBounds.Y + 5) != "Fully clipped",
                    "Point lookup hit a fully clipped overscan child."
                );
            }

            string? At(double x, double y)
            {
                Assert(
                    provider.Point(
                        origin.X + x * deviceScale,
                        origin.Y + y * deviceScale,
                        out var fragment
                    ) == WindowsUiaProvider.Ok,
                    "UIA point query failed."
                );
                if (fragment == 0)
                    return null;
                var simple = Query(fragment, UiaWrappers.Simple);
                try
                {
                    return ReadName(simple);
                }
                finally
                {
                    Release(simple);
                    Release(fragment);
                }
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static Style Size(float width, float height) =>
        Style.Empty.Set(LayoutProperties.Width, width).Set(LayoutProperties.Height, height);

    private static string? ReadName(nint simple)
    {
        WindowsUiaProvider.RawVariant value = default;
        Assert(
            Simple(simple, 5, 30005, &value) == WindowsUiaProvider.Ok && value.Type == 8,
            "UIA Name was not a BSTR."
        );
        try
        {
            return Marshal.PtrToStringBSTR(value.Value);
        }
        finally
        {
            Marshal.FreeBSTR(value.Value);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GeometryPoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint window, ref GeometryPoint point);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);
}
