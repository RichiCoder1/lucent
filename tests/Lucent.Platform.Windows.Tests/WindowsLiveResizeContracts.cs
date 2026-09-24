using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed partial class WindowsLiveResizeContracts
{
    [TestMethod]
    public void ExposedWatchRendersOnlyItsOwnerWindowAndBlocksReentry()
    {
        SDL.EventFilter? watch = null;
        var renders = 0;
        var nativeSizeMove = false;
        using var liveResize = new WindowsLiveResize(
            42,
            () =>
            {
                renders++;
                var nested = WindowEvent(SDL.EventType.WindowExposed, 42);
                Assert.IsTrue(watch!(0, ref nested));
            },
            () => nativeSizeMove,
            (callback, _) =>
            {
                watch = callback;
                return true;
            },
            (_, _) => { }
        );

        var otherWindow = WindowEvent(SDL.EventType.WindowExposed, 7);
        Assert.IsTrue(watch!(0, ref otherWindow));
        var resized = WindowEvent(SDL.EventType.WindowResized, 42);
        Assert.IsTrue(watch(0, ref resized));
        var exposed = WindowEvent(SDL.EventType.WindowExposed, 42);
        Assert.IsTrue(watch(0, ref exposed));
        Assert.AreEqual(0, renders, "An exposure outside SDL's explicit pump entered Core.");
        liveResize.EnterPump();
        Assert.IsTrue(watch(0, ref exposed));
        Assert.AreEqual(0, renders, "A programmatic exposure entered the native resize callback.");
        nativeSizeMove = true;
        Assert.IsTrue(watch(0, ref exposed));
        liveResize.ExitPump();

        Assert.AreEqual(1, renders);
    }

    [TestMethod]
    public void EventWatchNeverEntersRenderingFromANonOwnerThread()
    {
        SDL.EventFilter? watch = null;
        var renders = 0;
        using var liveResize = new WindowsLiveResize(
            42,
            () => renders++,
            () => true,
            (callback, _) =>
            {
                watch = callback;
                return true;
            },
            (_, _) => { }
        );

        var accepted = Task.Run(() =>
            {
                var exposed = WindowEvent(SDL.EventType.WindowExposed, 42);
                return watch!(0, ref exposed);
            })
            .GetAwaiter()
            .GetResult();

        Assert.IsTrue(accepted);
        Assert.AreEqual(0, renders);
    }

    [TestMethod]
    public void CallbackFailuresStayManagedAndWatchIsRemovedBeforeDependencies()
    {
        SDL.EventFilter? watch = null;
        var removed = 0;
        var liveResize = new WindowsLiveResize(
            42,
            () => throw new InvalidOperationException("live frame failed"),
            () => true,
            (callback, _) =>
            {
                watch = callback;
                return true;
            },
            (callback, _) =>
            {
                Assert.AreSame(watch, callback);
                removed++;
            }
        );

        var exposed = WindowEvent(SDL.EventType.WindowExposed, 42);
        liveResize.EnterPump();
        Assert.IsTrue(watch!(0, ref exposed));
        liveResize.ExitPump();
        var error = Assert.ThrowsExactly<InvalidOperationException>(liveResize.ThrowIfFailed);
        Assert.AreEqual("live frame failed", error.Message);

        liveResize.Dispose();
        liveResize.Dispose();
        Assert.AreEqual(1, removed);
    }

    [TestMethod]
    public void NativeMoveSizeMessagesGateExposureAndWindowDestroyReleasesSubclass()
    {
        Assert.IsTrue(SDL.Init(SDL.InitFlags.Video), SDL.GetError());
        WeakReference owner;
        try
        {
            owner = ExerciseNativeMoveSizeLifecycle();
        }
        finally
        {
            SDL.Quit();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsFalse(
            owner.IsAlive,
            "The native subclass retained its managed owner after window destruction and disposal."
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExerciseNativeMoveSizeLifecycle()
    {
        nint window = 0;
        WindowsLiveResize? liveResize = null;
        try
        {
            window = SDL.CreateWindow("Lucent native resize gate", 32, 32, SDL.WindowFlags.Hidden);
            Assert.AreNotEqual((nint)0, window, SDL.GetError());
            var hwnd = SDL.GetPointerProperty(
                SDL.GetWindowProperties(window),
                SDL.Props.WindowWin32HWNDPointer,
                0
            );
            Assert.AreNotEqual((nint)0, hwnd);
            SDL.EventFilter? watch = null;
            var renders = 0;
            var removals = 0;
            liveResize = new WindowsLiveResize(
                SDL.GetWindowID(window),
                hwnd,
                () => renders++,
                (callback, _) =>
                {
                    watch = callback;
                    return true;
                },
                (_, _) => removals++
            );
            var exposed = WindowEvent(SDL.EventType.WindowExposed, SDL.GetWindowID(window));
            liveResize.EnterPump();
            try
            {
                Assert.IsTrue(watch!(0, ref exposed));
                Assert.AreEqual(0, renders);
                _ = SendMessage(hwnd, 0x0231, 0, 0);
                Assert.IsTrue(watch(0, ref exposed));
                Assert.AreEqual(1, renders);
                _ = SendMessage(hwnd, 0x0232, 0, 0);
                Assert.IsTrue(watch(0, ref exposed));
                Assert.AreEqual(1, renders);
            }
            finally
            {
                liveResize.ExitPump();
            }
            SDL.DestroyWindow(window);
            window = 0;
            var owner = new WeakReference(liveResize);
            liveResize.Dispose();
            liveResize = null;
            watch = null;
            Assert.AreEqual(1, removals);
            return owner;
        }
        finally
        {
            liveResize?.Dispose();
            if (window != 0)
                SDL.DestroyWindow(window);
        }
    }

    private static SDL.Event WindowEvent(SDL.EventType type, uint windowId)
    {
        var value = new SDL.Event { Type = (uint)type };
        value.Window.WindowID = windowId;
        return value;
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
