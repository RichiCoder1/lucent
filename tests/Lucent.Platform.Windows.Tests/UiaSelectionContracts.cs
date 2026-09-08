using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void ControlledSelectionReportsOnlyAppliedApplicationState()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA selection) failed.");
        var window = CreateWindow("Lucent UIA selection");
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "uia-selection");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var selected = graph.Signal(false, "selected");
            var accept = false;
            composition.Mount(
                composition.Root,
                theme,
                Components.Selectable(
                    () => "Item",
                    () => selected.Value,
                    () =>
                    {
                        if (accept)
                            selected.Value = true;
                    }
                )
            );
            using var renderer = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(160, 80, 1), renderer);
            Assert(composition.Input.SetScene(scene), "Selection scene was rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "UIA selection"
            );
            provider.Refresh(scene);
            var root = Query(provider.InterfacePointer, UiaWrappers.Fragment);
            nint item = 0,
                selection = 0;
            try
            {
                Assert(
                    Fragment(root, 3, 3, &item) == WindowsUiaProvider.Ok && item != 0,
                    "Selection item was unavailable."
                );
                nint nested = 0;
                if (Fragment(item, 3, 3, &nested) == WindowsUiaProvider.Ok && nested != 0)
                {
                    Release(item);
                    item = nested;
                }
                selection = Query(item, UiaWrappers.SelectionItem);
                Assert(
                    Fragment(selection, 3) == WindowsUiaProvider.InvalidOperation,
                    "Unapplied selection request reported UIA success."
                );
                var value = -1;
                Assert(
                    Simple(selection, 6, &value) == WindowsUiaProvider.Ok && value == 0,
                    "Declined request changed the exported selection."
                );
                accept = true;
                Assert(
                    Fragment(selection, 3) == WindowsUiaProvider.Ok,
                    "Synchronously accepted selection did not report UIA success."
                );
                scene = SceneLayout.Project(composition, new(160, 80, 1), renderer);
                provider.Refresh(scene);
                Assert(
                    Simple(selection, 6, &value) == WindowsUiaProvider.Ok && value == 1,
                    "Accepted selection did not reach the retained provider."
                );
            }
            finally
            {
                Release(selection);
                Release(item);
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
