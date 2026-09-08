using System.Diagnostics;
using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsPopupChainContracts
{
    [TestMethod]
    public void RetainedCompositionCanBeHostedAgainWithoutSecondPresentation()
    {
        using var composition = new Composition(new ReactiveGraph(), "retained-popup");

        WindowsPopupHost.EnsureHostPadding(composition);

        // Closing a popup retains its composition so a branch can reopen without
        // rebuilding semantic identities. Hosting it again must reuse the one
        // presentation model attached to the retained root.
        WindowsPopupHost.EnsureHostPadding(composition);
    }

    [TestMethod]
    public void SubmenuPlacementOpensLeftWhenRightSideDoesNotFit()
    {
        var trigger = new PopupScreenRect(900, 120, 940, 160);
        var parent = new PopupScreenRect(700, 80, 940, 420);
        var placement = WindowsPopupPlacement.Submenu(
            trigger,
            new LayoutRect(0, 0, 240, 160),
            parent,
            new SDL.Rect
            {
                X = 0,
                Y = 0,
                W = 1000,
                H = 700,
            },
            scale: 1,
            density: 1
        );

        Assert.IsTrue(placement.OpensLeft, "The submenu did not choose the available left side.");
        Assert.AreEqual(
            900 - 240 - 2 * WindowsPopupHost.ShadowMargin - parent.Left,
            placement.OffsetX
        );
        Assert.AreEqual(trigger.Top - parent.Top, placement.OffsetY);
    }

    [TestMethod]
    public void SubmenuPlacementClampsWithinUsableBoundsWhenNeitherSideFits()
    {
        var trigger = new PopupScreenRect(4, 650, 24, 680);
        var parent = new PopupScreenRect(0, 600, 300, 700);
        var placement = WindowsPopupPlacement.Submenu(
            trigger,
            new LayoutRect(0, 0, 600, 160),
            parent,
            new SDL.Rect
            {
                X = 0,
                Y = 0,
                W = 500,
                H = 700,
            },
            scale: 1,
            density: 1
        );

        Assert.IsFalse(placement.OpensLeft);
        Assert.AreEqual(0 - parent.Left, placement.OffsetX);
        Assert.AreEqual(700 - 192 - parent.Top, placement.OffsetY);
    }

    [TestMethod]
    public void SafeIntentDefersDiagonalParentMotionOnlyWithinBoundedGrace()
    {
        var safe = new WindowsPopupSafeIntent();
        var parent = new PopupScreenRect(100, 100, 300, 400);
        var child = new PopupScreenRect(300, 130, 520, 350);
        var start = new PopupScreenPoint(290, 180);
        safe.Arm(parent, child, opensLeft: false, start, now: 0);

        Assert.IsTrue(
            safe.Observe(new PopupScreenPoint(295, 190), Stopwatch.Frequency / 10),
            "Diagonal travel toward the child was not deferred."
        );
        Assert.IsFalse(
            safe.Observe(new PopupScreenPoint(295, 200), Stopwatch.Frequency / 2),
            "Safe intent exceeded its 300 ms grace bound."
        );

        safe.Arm(parent, child, opensLeft: false, start, now: 0);
        Assert.IsFalse(
            safe.Observe(new PopupScreenPoint(310, 200), Stopwatch.Frequency / 10),
            "Pointer entry into the child did not cancel deferral."
        );
    }

    [TestMethod]
    public void SafeIntentPublishesBoundedReplayDeadlineWhenPointerStops()
    {
        var safe = new WindowsPopupSafeIntent();
        var parent = new PopupScreenRect(100, 100, 300, 400);
        var child = new PopupScreenRect(300, 130, 520, 350);
        const long start = 1_000_000;
        safe.Arm(parent, child, opensLeft: false, new PopupScreenPoint(290, 180), start);

        Assert.IsTrue(
            safe.WaitMilliseconds(start) > 0,
            "An active safe-intent corridor had no deadline."
        );
        Assert.IsFalse(
            safe.IsExpired(start + Stopwatch.Frequency * 299 / 1000),
            "Safe intent expired before its 300 ms bound."
        );
        Assert.IsTrue(
            safe.IsExpired(start + Stopwatch.Frequency * 300 / 1000),
            "A stopped pointer did not become eligible for replay at the deadline."
        );
        safe.Cancel();
        Assert.AreEqual(-1, safe.WaitMilliseconds(start + Stopwatch.Frequency));
    }

    [TestMethod]
    public void SafeIntentSetCancelsIncomingBoundaryButKeepsDeeperBoundary()
    {
        var intents = new WindowsPopupSafeIntentSet();
        var parent = new PopupScreenRect(100, 100, 300, 400);
        var child = new PopupScreenRect(300, 130, 520, 350);
        var grandchild = new PopupScreenRect(520, 150, 740, 370);
        const long now = 1_000_000;
        intents.Arm(1, 2, parent, child, opensLeft: false, new(290, 180), now);
        intents.Arm(2, 3, child, grandchild, opensLeft: false, new(510, 200), now);

        Assert.IsTrue(
            intents.Observe(1, new(295, 190), now + Stopwatch.Frequency / 10),
            "The first parent-child corridor did not defer diagonal travel."
        );
        intents.CancelForChild(2);
        Assert.IsFalse(
            intents.Observe(1, new(295, 195), now + Stopwatch.Frequency / 10),
            "Entering the child left its incoming safe-intent corridor active."
        );
        Assert.IsTrue(
            intents.Observe(2, new(515, 205), now + Stopwatch.Frequency / 10),
            "Canceling an incoming corridor also canceled the child-to-grandchild corridor."
        );
    }

    [TestMethod]
    public void SafeIntentReconcilePreservesExistingDeadlineWhenAddingDeeperLevel()
    {
        var intents = new WindowsPopupSafeIntentSet();
        var parent = new PopupScreenRect(100, 100, 300, 400);
        var child = new PopupScreenRect(300, 130, 520, 350);
        var grandchild = new PopupScreenRect(520, 150, 740, 370);
        const long start = 1_000_000;

        intents.Arm(1, 2, parent, child, opensLeft: false, new(290, 180), start);
        intents.Reconcile(
            [
                (
                    ParentWindowId: 1U,
                    ChildWindowId: 2U,
                    Parent: parent,
                    Child: child,
                    OpensLeft: false,
                    Pointer: new PopupScreenPoint(290, 180)
                ),
                (
                    ParentWindowId: 2U,
                    ChildWindowId: 3U,
                    Parent: child,
                    Child: grandchild,
                    OpensLeft: false,
                    Pointer: new PopupScreenPoint(510, 200)
                ),
            ],
            start + Stopwatch.Frequency * 250 / 1000
        );

        Assert.IsTrue(
            intents.IsExpired(1, start + Stopwatch.Frequency * 300 / 1000),
            "Adding a deeper level re-armed the unchanged parent-child grace period."
        );
        Assert.IsTrue(
            intents.Observe(2, new(515, 205), start + Stopwatch.Frequency * 260 / 1000),
            "The newly added child-grandchild boundary was not armed."
        );
    }

    [TestMethod]
    public void KeyboardRoutingPreservesHoverParentUntilFocusedChildPromotion()
    {
        Assert.AreEqual(
            0,
            WindowsPopupKeyboardRouting.SelectOwnerIndex(
                currentIndex: 0,
                levelCount: 2,
                deepestFocusedIndex: -1,
                promoteFocusedChild: false
            ),
            "An unfocused hover-open child stole owner keyboard input."
        );
        Assert.AreEqual(
            1,
            WindowsPopupKeyboardRouting.SelectOwnerIndex(
                currentIndex: 0,
                levelCount: 2,
                deepestFocusedIndex: 1,
                promoteFocusedChild: true
            ),
            "A child that gained focus was not promoted as the keyboard target."
        );
        Assert.AreEqual(
            0,
            WindowsPopupKeyboardRouting.SelectOwnerIndex(
                currentIndex: 0,
                levelCount: 3,
                deepestFocusedIndex: 2,
                promoteFocusedChild: false
            ),
            "A deeper hover-open branch changed the keyboard target without focus."
        );
    }

    [TestMethod]
    public void FocusGateKeepsInternalPopupFocusChangesAndDismissesExternalLoss()
    {
        var gate = new WindowsPopupFocusGate();
        gate.LostFocus();
        Assert.IsFalse(
            gate.ShouldDismiss(focusWithinChain: true),
            "Moving focus to another owned popup dismissed the chain."
        );

        gate.LostFocus();
        Assert.IsTrue(
            gate.ShouldDismiss(focusWithinChain: false),
            "External focus loss did not dismiss the chain."
        );
    }
}
