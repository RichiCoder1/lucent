using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void AuthoredAriaNameAndDescriptionRefreshOnTheRetainedProvider()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(authored UIA metadata) failed.");
        var window = CreateWindow("Lucent authored UIA metadata");
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "uia-author-aria");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var metadata = composition.Root.Scope.Signal<AriaMetadata?>(
                new("Authored button", "Authored help"),
                "uia-author-aria-metadata"
            );
            composition.Mount(
                composition.Root,
                theme,
                Components.Button("Behavior button", aria: () => metadata.Value)
            );
            composition.Flush();
            using var renderer = new SkiaSceneRenderer();
            using var initial = SceneLayout.Project(composition, new(320, 120, 1), renderer);
            Assert(composition.Input.SetScene(initial), "Authored UIA scene rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Authored metadata"
            );
            provider.Refresh(initial);
            var root = Query(provider.InterfacePointer, UiaWrappers.Fragment);
            nint button = 0;
            try
            {
                Assert(
                    Fragment(root, 3, 3, &button) == WindowsUiaProvider.Ok && button != 0,
                    "Authored UIA semantic child was unavailable."
                );
                nint nested = 0;
                if (Fragment(button, 3, 3, &nested) == WindowsUiaProvider.Ok && nested != 0)
                {
                    Release(button);
                    button = nested;
                }
                var simple = Query(button, UiaWrappers.Simple);
                try
                {
                    Assert(ReadName(simple) == "Authored button", "UIA Name was not authored.");
                    Assert(
                        ReadFieldString(simple, 30013) == "Authored help",
                        "UIA HelpText was not authored."
                    );

                    metadata.Value = new("Updated button", "Updated help");
                    graph.Drain();
                    using var updated = WindowsBootstrap.ProjectAndInstall(
                        composition,
                        new(320, 120, 1),
                        renderer,
                        initial
                    );
                    provider.Refresh(updated);

                    Assert(
                        ReadName(simple) == "Updated button",
                        "The retained UIA provider did not refresh its authored Name."
                    );
                    Assert(
                        ReadFieldString(simple, 30013) == "Updated help",
                        "The retained UIA provider did not refresh its authored HelpText."
                    );
                }
                finally
                {
                    Release(simple);
                }
            }
            finally
            {
                if (button != 0)
                    Release(button);
                Release(root);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}
