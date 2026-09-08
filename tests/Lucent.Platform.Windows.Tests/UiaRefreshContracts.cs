using System.Diagnostics;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void WideSemanticRefreshPreservesNavigationAndProviderIdentity()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA refresh) failed.");
        var window = CreateWindow("Lucent UIA refresh");
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "uia-refresh");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var label = graph.Signal("First", "first-label");
            var recipes = new ContentRecipe[2000];
            recipes[0] = Components.Button(() => label.Value);
            for (var index = 1; index < recipes.Length; index++)
                recipes[index] = Components.Button("Item " + index);
            composition.Mount(
                composition.Root,
                theme,
                Components.Column(ComponentContent.Create(recipes))
            );
            using var renderer = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(
                composition,
                new(500, 800, 1),
                renderer,
                maximumWorkItems: 100_000
            );
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Refresh"
            );
            provider.Refresh(scene);
            var root = Query(provider.InterfacePointer, UiaWrappers.Fragment);
            nint first = 0;
            try
            {
                Assert(
                    Fragment(root, 3, 3, &first) == WindowsUiaProvider.Ok && first != 0,
                    "First semantic child was unavailable."
                );
                // The composition's intentional semantic root may precede the buttons.
                nint nested = 0;
                if (Fragment(first, 3, 3, &nested) == WindowsUiaProvider.Ok && nested != 0)
                {
                    Release(first);
                    first = nested;
                }
                var structureChanges = provider.DetectedStructureChanges;
                var propertyChanges = provider.DetectedPropertyChanges;
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                for (var pass = 0; pass < 5; pass++)
                    provider.Refresh(scene);
                stopwatch.Stop();
                Console.WriteLine(
                    $"UIA refresh 2000 siblings x5: {stopwatch.Elapsed.TotalMilliseconds:F3} ms; {GC.GetAllocatedBytesForCurrentThread() - allocated} bytes"
                );
                Assert(
                    provider.DetectedStructureChanges == structureChanges
                        && provider.DetectedPropertyChanges == propertyChanges,
                    "Unchanged refresh produced redundant semantic notifications."
                );
                nint next = 0,
                    previous = 0;
                try
                {
                    Assert(
                        Fragment(first, 3, 1, &next) == WindowsUiaProvider.Ok && next != 0,
                        "Next sibling was unavailable after refresh."
                    );
                    Assert(
                        Fragment(next, 3, 2, &previous) == WindowsUiaProvider.Ok
                            && previous == first,
                        "Unchanged refresh replaced provider identity or sibling order."
                    );
                    label.Value = "Changed";
                    graph.Drain();
                    scene = SceneLayout.Project(
                        composition,
                        new(500, 800, 1),
                        renderer,
                        maximumWorkItems: 100_000
                    );
                    provider.Refresh(scene);
                    Assert(
                        provider.DetectedStructureChanges == structureChanges
                            && provider.DetectedPropertyChanges == propertyChanges + 1,
                        "Changed label did not produce exactly one property change."
                    );
                    Assert(
                        provider.StaleCount == 0,
                        "A changed label invalidated retained providers."
                    );
                    var simple = Query(first, UiaWrappers.Simple);
                    try
                    {
                        WindowsUiaProvider.RawVariant value = default;
                        var hr = (
                            (delegate* unmanaged[Stdcall]<
                                nint,
                                int,
                                WindowsUiaProvider.RawVariant*,
                                int>)
                                (*(nint**)simple)[5]
                        )(simple, 30005, &value);
                        Assert(
                            hr == WindowsUiaProvider.Ok
                                && System.Runtime.InteropServices.Marshal.PtrToStringBSTR(
                                    value.Value
                                ) == "Changed",
                            "Retained provider did not expose changed semantic payload."
                        );
                        System.Runtime.InteropServices.Marshal.FreeBSTR(value.Value);
                    }
                    finally
                    {
                        Release(simple);
                    }
                }
                finally
                {
                    Release(previous);
                    Release(next);
                }
            }
            finally
            {
                Release(first);
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
