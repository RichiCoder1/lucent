using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using MAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void GaugeTransportsReadOnlyRangeAndCannotBeInvokedOrEdited()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA Gauge) failed.");
        var window = CreateWindow("Lucent UIA Gauge");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "uia-gauge");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Mount(
                composition.Root,
                theme,
                Components
                    .Gauge("Capacity", () => 37, new(unit: "%"))
                    .Aria.Name("Disk capacity")
                    .Description("Available capacity")
                    .End
            );
            composition.Flush();
            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(composition, new(150, 150, 1.5f), renderer);
            Assert(composition.Input.SetScene(scene), "UIA Gauge scene was rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Lucent UIA Gauge"
            );
            provider.Refresh(scene);
            var gauge = FindStockProviderByName(provider.InterfacePointer, "Disk capacity");
            MAssert.AreNotEqual(nint.Zero, gauge, "Gauge semantic node was unavailable.");
            try
            {
                var simple = Query(gauge, UiaWrappers.Simple);
                try
                {
                    MAssert.AreEqual("Disk capacity", ReadName(simple));
                    MAssert.AreEqual("Available capacity", ReadFieldString(simple, 30013));
                }
                finally
                {
                    Release(simple);
                }
            }
            finally
            {
                Release(gauge);
            }
            var range = FindPattern(provider.InterfacePointer, 10003);
            MAssert.AreNotEqual(nint.Zero, range, "Gauge did not expose RangeValue.");
            try
            {
                MAssert.AreEqual(37d, ReadDouble(range, 4));
                MAssert.AreEqual(0d, ReadDouble(range, 7));
                MAssert.AreEqual(100d, ReadDouble(range, 6));
                MAssert.AreEqual(1, ReadInt(range, 5), "Gauge was not read-only.");
                MAssert.AreEqual(WindowsUiaProvider.InvalidOperation, RangeSet(range, 3, 40));
            }
            finally
            {
                Release(range);
            }
            MAssert.AreEqual(
                nint.Zero,
                FindPattern(provider.InterfacePointer, 10000),
                "Gauge exposed Invoke."
            );
            MAssert.AreEqual(
                nint.Zero,
                FindPattern(provider.InterfacePointer, 10002),
                "Gauge exposed editable Value."
            );
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}
