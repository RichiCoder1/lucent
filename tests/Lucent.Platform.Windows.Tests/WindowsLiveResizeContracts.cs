using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsLiveResizeContracts
{
    [TestMethod]
    public void ExposedWatchRendersOnlyItsOwnerWindowAndBlocksReentry()
    {
        SDL.EventFilter? watch = null;
        var renders = 0;
        using var liveResize = new WindowsLiveResize(
            42,
            () =>
            {
                renders++;
                var nested = WindowEvent(SDL.EventType.WindowExposed, 42);
                Assert.IsTrue(watch!(0, ref nested));
            },
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

    private static SDL.Event WindowEvent(SDL.EventType type, uint windowId)
    {
        var value = new SDL.Event { Type = (uint)type };
        value.Window.WindowID = windowId;
        return value;
    }
}
