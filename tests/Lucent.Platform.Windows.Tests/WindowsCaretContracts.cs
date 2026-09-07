using Lucent.Core;
using Lucent.Renderer.Skia;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsCaretContracts
{
    [TestMethod]
    public void CaretClockBlinksOnlyAnActiveEditorAndResetsAfterTyping()
    {
        var clock = new WindowsCaretBlink(500);
        Assert.AreEqual(-1, clock.WaitMilliseconds(0));
        clock.UpdateActivity(true, 0, 0);
        clock.SetTarget(new ElementIdentity(1, 2), new LayoutRect(1, 2, 1, 20), 0);
        Assert.IsTrue(clock.Visible);
        Assert.AreEqual(500, clock.WaitMilliseconds(0));
        Assert.IsFalse(clock.Tick(499));
        Assert.IsTrue(clock.Tick(500));
        Assert.IsFalse(clock.Visible);
        Assert.IsTrue(clock.UpdateActivity(true, 1, 600));
        Assert.IsTrue(clock.Visible);
        Assert.AreEqual(500, clock.WaitMilliseconds(600));
        Assert.IsTrue(clock.UpdateActivity(false, 1, 650));
        Assert.IsFalse(clock.Visible);
        Assert.AreEqual(-1, clock.WaitMilliseconds(5000));
        clock.UpdateActivity(true, 2, 6000);
        clock.SetTarget(null, default, 6000);
        Assert.AreEqual(-1, clock.WaitMilliseconds(6000));
        Assert.IsFalse(clock.Visible);
    }

    [TestMethod]
    public void WindowsDisabledBlinkKeepsCaretSteadyWithoutWakeups()
    {
        var clock = new WindowsCaretBlink(uint.MaxValue);
        clock.UpdateActivity(true, 0, 0);
        clock.SetTarget(new ElementIdentity(1, 2), new LayoutRect(1, 2, 1, 20), 0);
        Assert.IsTrue(clock.Visible);
        Assert.AreEqual(-1, clock.WaitMilliseconds(5000));
        Assert.IsFalse(clock.Tick(5000));
    }

    [TestMethod]
    public void DisablingBlinkDuringHiddenPhaseRestoresVisibleCaret()
    {
        var clock = new WindowsCaretBlink(500);
        clock.UpdateActivity(true, 0, 0);
        clock.SetTarget(new ElementIdentity(1, 2), new LayoutRect(1, 2, 1, 20), 0);
        clock.Tick(500);
        Assert.IsFalse(clock.Visible);
        Assert.IsTrue(clock.SetInterval(uint.MaxValue, 600));
        Assert.IsTrue(clock.Visible);
        Assert.AreEqual(-1, clock.WaitMilliseconds(600));
    }

    [TestMethod]
    public void TextCursorHitUsesCurrentEnabledAndClippedEditor()
    {
        using var composition = new Composition(new ReactiveGraph(), "cursor-hit");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var renderer = new SkiaSceneRenderer();
        var enabled = composition.Root.Scope.Signal(true, "enabled");
        composition.Root.Present(
            theme,
            author: Style.Empty.Width(100).Height(50).Set(LayoutProperties.Clip, true)
        );
        var field = composition.Child(composition.Root, "editor");
        Controls.TextField(
            field,
            theme,
            "Text",
            style: Style.Empty.Width(200).Height(40).Enabled(() => enabled.Value)
        );
        void Install()
        {
            for (var attempt = 0; attempt < 3; attempt++)
                if (
                    composition.Input.SetScene(
                        SceneLayout.Project(composition, new(100, 50, 1), renderer)
                    )
                )
                    return;
            Assert.Fail("Editor enabled-state reconciliation did not settle.");
        }
        Install();
        Assert.IsTrue(composition.Input.IsTextInputAt(20, 20));
        Assert.IsFalse(composition.Input.IsTextInputAt(150, 20));
        Assert.IsFalse(composition.Input.IsTextInputAt(float.NaN, 20));
        enabled.Value = false;
        Install();
        Assert.IsFalse(composition.Input.IsTextInputAt(20, 20));
    }

    [TestMethod]
    public void CaretSchedulingReusesOnlyIdleFramesAndResumesAfterRestore()
    {
        var scheduler = new WindowsFrameScheduler();
        var viewport = new WindowsViewport(100, 50, 1);
        var clock = new WindowsCaretBlink(500);
        Assert.IsFalse(scheduler.RequestCaretFrame(clock, true, 0, 0, false));
        Assert.IsTrue(scheduler.TryBegin(viewport));
        clock.SetTarget(new ElementIdentity(1, 2), new LayoutRect(1, 2, 1, 20), 0);
        clock.BeforePresent(0);
        Assert.AreEqual(500, clock.WaitMilliseconds(0));
        Assert.IsFalse(scheduler.RequestCaretFrame(clock, true, 0, 499, true));
        Assert.IsFalse(scheduler.TryBegin(viewport));
        Assert.IsTrue(scheduler.RequestCaretFrame(clock, true, 0, 500, true));
        Assert.IsTrue(scheduler.TryBegin(viewport));
        clock.BeforePresent(500);
        scheduler.Observe(WindowsFrameEvent.Minimized);
        scheduler.RequestCaretFrame(clock, true, 0, 600, true);
        Assert.AreEqual(-1, clock.WaitMilliseconds(600));
        Assert.IsFalse(scheduler.TryBegin(viewport));
        scheduler.Observe(WindowsFrameEvent.Restored);
        Assert.IsFalse(
            scheduler.RequestCaretFrame(clock, true, 0, 1000, true),
            "Restore must project before reusing a scene."
        );
        Assert.IsTrue(scheduler.TryBegin(viewport));
        clock.BeforePresent(1000);
        scheduler.Request(FrameOperation.Input);
        Assert.IsFalse(scheduler.RequestCaretFrame(clock, false, 0, 1100, true));
        Assert.IsTrue(scheduler.TryBegin(viewport));
        clock.BeforePresent(1100);
        Assert.IsFalse(clock.Visible);
        Assert.AreEqual(-1, clock.WaitMilliseconds(9000));
    }

    [TestMethod]
    public void SlowProjectionAndRasterDoNotFlashTheCaretOrRequestImmediateUploads()
    {
        var clock = new WindowsCaretBlink(500);
        clock.UpdateActivity(true, 1, 0);
        clock.SetTarget(new ElementIdentity(1, 2), new LayoutRect(1, 2, 1, 20), 0);
        clock.BeforePresent(800);
        Assert.IsTrue(
            clock.Visible,
            "Typing must start with a visible caret even after a slow projection."
        );
        Assert.AreEqual(500, clock.WaitMilliseconds(800));
        clock.Presented(1500);
        Assert.AreEqual(500, clock.WaitMilliseconds(1500));
    }

    [TestMethod]
    public void CursorCleanupAttemptsEveryHandleAfterOneDestroyFails()
    {
        var destroyed = new List<nint>();
        var cursor = new WindowsCursor(
            text => text ? 2 : 1,
            _ => true,
            handle =>
            {
                destroyed.Add(handle);
                if (handle == 1)
                    throw new InvalidOperationException("destroy failed");
            },
            () => "error"
        );
        cursor.Activate();
        cursor.Activate(true);
        Assert.ThrowsExactly<AggregateException>(cursor.Dispose);
        Assert.IsTrue(destroyed.SequenceEqual([(nint)1, 2]));
        cursor.Dispose();
        Assert.AreEqual(2, destroyed.Count);
    }

    [TestMethod]
    public void CursorChangesShapeAndReusesOwnedHandles()
    {
        var created = new List<bool>();
        var activated = new List<nint>();
        var destroyed = new List<nint>();
        using (
            var cursor = new WindowsCursor(
                text =>
                {
                    created.Add(text);
                    return text ? 2 : 1;
                },
                handle =>
                {
                    activated.Add(handle);
                    return true;
                },
                destroyed.Add,
                () => "error"
            )
        )
        {
            cursor.Activate();
            cursor.Activate(true);
            cursor.Activate(true);
            cursor.Activate();
        }
        Assert.IsTrue(created.SequenceEqual([false, true]));
        Assert.IsTrue(activated.SequenceEqual([(nint)1, 2, 1]));
        Assert.IsTrue(destroyed.Order().SequenceEqual([(nint)1, 2]));
    }
}
