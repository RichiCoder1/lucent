using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using MAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void SplitterExposesFiniteRangeValuePatternAndBoundedMutation()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA range) failed.");
        var window = CreateWindow("Lucent UIA range");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "uia-range");
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            Controls.Column(
                composition.Root,
                theme,
                "Root",
                Style.Empty.Set(LayoutProperties.Width, 800).Set(LayoutProperties.Height, 400)
            );
            using var state = new SplitPaneState(
                composition.Root.Scope,
                initialExtent: 320,
                minimumFirst: 160,
                minimumSecond: 160
            );
            composition.Mount(
                composition.Root,
                theme,
                Components.SplitPane(
                    [Components.Text("First", Style.Empty)],
                    [Components.Text("Second", Style.Empty)],
                    state,
                    label: "Resize panes"
                )
            );
            composition.Flush();
            using var renderer = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(800, 400, 1), renderer);
            Assert(composition.Input.SetScene(scene), "UIA range scene was rejected.");
            MAssert.AreEqual(
                320f,
                state.EffectiveExtent,
                0.001f,
                "SplitPane constraints changed the preferred extent unexpectedly."
            );
            var semantics = composition.SemanticSnapshot()!;
            var splitterSemantic = FindSemantic(semantics);
            MAssert.AreEqual(320d, splitterSemantic.Range!.Value, 0.001);
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Lucent UIA range"
            );
            provider.Refresh(scene);

            var rangeProvider = FindPattern(provider.InterfacePointer, 10003);
            MAssert.AreNotEqual(
                nint.Zero,
                rangeProvider,
                "Splitter did not expose RangeValuePattern."
            );
            try
            {
                MAssert.AreEqual(320d, ReadDouble(rangeProvider, 4), 0.001);
                MAssert.AreEqual(160d, ReadDouble(rangeProvider, 7), 0.001);
                MAssert.AreEqual(632d, ReadDouble(rangeProvider, 6), 0.001);
                MAssert.AreEqual(8d, ReadDouble(rangeProvider, 9), 0.001);
                MAssert.AreEqual(40d, ReadDouble(rangeProvider, 8), 0.001);
                MAssert.AreEqual(
                    0,
                    ReadInt(rangeProvider, 5),
                    "The splitter range was unexpectedly read-only."
                );

                MAssert.AreEqual(
                    WindowsUiaProvider.Ok,
                    RangeSet(rangeProvider, 3, 400),
                    "RangeValue.SetValue did not dispatch to Core."
                );
                MAssert.AreEqual(400f, state.EffectiveExtent, 0.001f);
                MAssert.AreEqual(
                    WindowsUiaProvider.InvalidArgument,
                    RangeSet(rangeProvider, 3, double.NaN),
                    "RangeValue accepted a non-finite value."
                );
                MAssert.AreEqual(
                    WindowsUiaProvider.InvalidOperation,
                    RangeSet(rangeProvider, 3, 700),
                    "RangeValue accepted a value outside the semantic range."
                );
            }
            finally
            {
                Release(rangeProvider);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static nint FindPattern(nint root, int patternId)
    {
        var fragment = Query(root, UiaWrappers.Fragment);
        try
        {
            return Find(fragment);
        }
        finally
        {
            Release(fragment);
        }

        nint Find(nint current)
        {
            var simple = Query(current, UiaWrappers.Simple);
            try
            {
                nint pattern = 0;
                if (Simple(simple, 4, patternId, &pattern) == WindowsUiaProvider.Ok && pattern != 0)
                    return pattern;
            }
            finally
            {
                Release(simple);
            }

            nint child = 0;
            if (Fragment(current, 3, 3, &child) != WindowsUiaProvider.Ok || child == 0)
                return 0;
            try
            {
                while (child != 0)
                {
                    var found = Find(child);
                    if (found != 0)
                        return found;
                    nint next = 0;
                    if (Fragment(child, 3, 1, &next) != WindowsUiaProvider.Ok || next == 0)
                        break;
                    Release(child);
                    child = next;
                }
                return 0;
            }
            finally
            {
                if (child != 0)
                    Release(child);
            }
        }
    }

    private static SemanticSnapshot FindSemantic(SemanticSnapshot root)
    {
        if (root.Role == SemanticRole.Splitter)
            return root;
        foreach (var child in root.Children)
        {
            var found = FindSemanticOrNull(child);
            if (found is not null)
                return found;
        }
        throw new InvalidOperationException(
            "The SplitPane semantic snapshot omitted its splitter."
        );
    }

    private static SemanticSnapshot? FindSemanticOrNull(SemanticSnapshot root)
    {
        if (root.Role == SemanticRole.Splitter)
            return root;
        foreach (var child in root.Children)
            if (FindSemanticOrNull(child) is { } found)
                return found;
        return null;
    }

    private static double ReadDouble(nint pointer, int slot)
    {
        var value = 0d;
        MAssert.AreEqual(
            WindowsUiaProvider.Ok,
            ((delegate* unmanaged[Stdcall]<nint, double*, int>)(*(nint**)pointer)[slot])(
                pointer,
                &value
            )
        );
        return value;
    }

    private static int ReadInt(nint pointer, int slot)
    {
        var value = 0;
        MAssert.AreEqual(
            WindowsUiaProvider.Ok,
            ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)pointer)[slot])(
                pointer,
                &value
            )
        );
        return value;
    }

    private static int RangeSet(nint pointer, int slot, double value) =>
        ((delegate* unmanaged[Stdcall]<nint, double, int>)(*(nint**)pointer)[slot])(pointer, value);
}
