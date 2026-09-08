using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using MAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void ExpandCollapseStateUsesWindowsLeafNodeValue()
    {
        MAssert.AreEqual(
            0,
            WindowsUiaProvider.ExpandCollapseStateValue(false),
            "Collapsed did not use the UI Automation enum value."
        );
        MAssert.AreEqual(
            1,
            WindowsUiaProvider.ExpandCollapseStateValue(true),
            "Expanded did not use the UI Automation enum value."
        );
        MAssert.AreEqual(
            3,
            WindowsUiaProvider.ExpandCollapseStateValue(null),
            "LeafNode must use the UI Automation value 3; value 2 is PartiallyExpanded."
        );
    }

    [TestMethod]
    public void MenuSubmenuExposesExpandCollapsePatternAndTracksBranchLifetime()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA expand) failed.");
        var window = CreateWindow("Lucent UIA expand");
        try
        {
            using var renderer = new SkiaSceneRenderer();
            using var owner = new Composition(new ReactiveGraph(), "uia-expand-owner");
            using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
            _ = owner.Mount(
                owner.Root,
                theme,
                Components.ContextMenu(
                    [Components.Button("Target", () => { }, Style.Empty.Height(40))],
                    () =>
                        Components.Menu([
                            Components.MenuSubmenu(
                                "More",
                                () => Components.Menu([Components.MenuItem("Nested", () => { })])
                            ),
                        ])
                )
            );
            ContextMenuRequest? request = null;
            owner.Input.ContextMenuRequested += value => request = value;
            Install(owner, renderer);
            owner.Input.MoveFocus(FocusTraversalDirection.Next);
            Install(owner, renderer);
            Assert(
                owner
                    .Input.DispatchKey(new(KeyCommandKind.Down, Key.F10, KeyModifiers.Shift))
                    .Handled,
                "The focused target did not request its context menu."
            );
            if (request is null)
                throw new InvalidOperationException("The context-menu request was not raised.");
            using var popupRequest = request;
            var popup = request.CreateComposition();
            Install(popup, renderer);
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                popup,
                dispatcher,
                "Lucent UIA expand"
            );
            var scene = SceneLayout.Project(popup, new(420, 240, 1), renderer);
            Assert(popup.Input.SetScene(scene), "UIA expand scene was rejected.");
            provider.Refresh(scene);

            var expandProvider = FindPattern(provider.InterfacePointer, 10005);
            if (expandProvider == 0)
                throw new InvalidOperationException(
                    "MenuSubmenu did not expose ExpandCollapsePattern."
                );
            try
            {
                MAssert.AreEqual(
                    0,
                    ExpandState(expandProvider, 5),
                    "Submenu did not start collapsed."
                );
                MAssert.AreEqual(
                    WindowsUiaProvider.Ok,
                    ExpandCall(expandProvider, 3),
                    "Expand did not dispatch through the semantic command surface."
                );
            }
            finally
            {
                Release(expandProvider);
            }

            popup.Flush();
            scene = SceneLayout.Project(popup, new(420, 240, 1), renderer);
            Assert(popup.Input.SetScene(scene), "Expanded UIA scene was rejected.");
            provider.Refresh(scene);
            MAssert.AreEqual(
                2,
                request.ActiveLevels.Count,
                "Expand did not create a child menu level."
            );

            expandProvider = FindPattern(provider.InterfacePointer, 10005);
            try
            {
                MAssert.AreEqual(
                    1,
                    ExpandState(expandProvider, 5),
                    "Expanded state was not published to UIA."
                );
                MAssert.AreEqual(
                    WindowsUiaProvider.Ok,
                    ExpandCall(expandProvider, 4),
                    "Collapse did not dispatch through the semantic command surface."
                );
            }
            finally
            {
                Release(expandProvider);
            }
            MAssert.AreEqual(
                1,
                request.ActiveLevels.Count,
                "Collapse did not close descendant menu levels."
            );
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static void Install(Composition composition, SkiaSceneRenderer renderer)
    {
        for (var attempt = 0; attempt < 3; attempt++)
            if (
                composition.Input.SetScene(
                    SceneLayout.Project(composition, new(420, 240, 1), renderer)
                )
            )
                return;
        throw new InvalidOperationException("The UIA expand fixture did not install a scene.");
    }

    private static int ExpandCall(nint pointer, int slot) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)pointer)[slot])(pointer);

    private static int ExpandState(nint pointer, int slot)
    {
        var state = 0;
        MAssert.AreEqual(
            WindowsUiaProvider.Ok,
            ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)pointer)[slot])(
                pointer,
                &state
            )
        );
        return state;
    }
}
