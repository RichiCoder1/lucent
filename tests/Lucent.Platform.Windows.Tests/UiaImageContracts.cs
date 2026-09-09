using Lucent.Core;
using Lucent.Renderer.Skia;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void ImageSemanticMapsToNonInteractiveUiaImageControlType()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA image) failed.");
        var window = CreateWindow("Lucent UIA image");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "uia-image");
            composition.ConfigureImages(new ImageCache(new ImmediateImagePreparer()));
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Mount(
                composition.Root,
                theme,
                Components.Image(ImageSourceForTest(), "Preview")
            );
            composition.Flush();
            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(composition, new(80, 40, 1), renderer);
            Assert(composition.Input.SetScene(scene), "UIA image scene was rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Lucent UIA image"
            );
            provider.Refresh(scene);

            Assert(
                ContainsControlType(provider.InterfacePointer, 50006),
                "The image semantic did not map to UIA Image control type 50006."
            );
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static ImageSource ImageSourceForTest() =>
        ImageSource.FromAsset(
            new AssetReference(
                new AssetId("uia", "preview.png"),
                new string('0', 64),
                1,
                AssetFormat.Png,
                static () => new MemoryStream(new byte[] { 0 }),
                new AssetImageMetadata(1, 1)
            )
        );

    private static bool ContainsControlType(nint provider, int expected)
    {
        var fragment = Query(provider, UiaWrappers.Fragment);
        try
        {
            return ContainsControlTypeInFragment(fragment, expected);
        }
        finally
        {
            Release(fragment);
        }
    }

    private static bool ContainsControlTypeInFragment(nint fragment, int expected)
    {
        var simple = Query(fragment, UiaWrappers.Simple);
        try
        {
            WindowsUiaProvider.RawVariant value = default;
            if (
                Simple(simple, 5, 30003, &value) == WindowsUiaProvider.Ok
                && value.Type == 3
                && value.Value == expected
            )
                return true;
        }
        finally
        {
            Release(simple);
        }

        nint child = 0;
        if (Fragment(fragment, 3, 3, &child) != WindowsUiaProvider.Ok || child == 0)
            return false;
        try
        {
            while (child != 0)
            {
                if (ContainsControlTypeInFragment(child, expected))
                    return true;
                nint next = 0;
                if (Fragment(child, 3, 1, &next) != WindowsUiaProvider.Ok || next == 0)
                    break;
                Release(child);
                child = next;
            }
            return false;
        }
        finally
        {
            Release(child);
        }
    }

    private sealed class ImmediateImagePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, new byte[] { 0, 0, 0, 0 }));
    }
}
