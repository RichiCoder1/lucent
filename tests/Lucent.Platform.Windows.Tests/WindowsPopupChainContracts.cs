using System.Diagnostics;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsPopupChainContracts
{
    [TestMethod]
    public void OwnerFocusEventsContinueToOwnerInputWhilePopupFocusEventsAreConsumed()
    {
        Assert.IsFalse(
            WindowsPopupChain.ShouldConsumeFocusEvent(windowId: 17, ownerWindowId: 17),
            "The popup chain swallowed owner focus loss before SDL text input and IME cleanup."
        );
        Assert.IsTrue(
            WindowsPopupChain.ShouldConsumeFocusEvent(windowId: 29, ownerWindowId: 17),
            "A popup focus event fell through to owner input."
        );
    }

    [TestMethod]
    public void PopupActionsCanInvalidateTheirOwnerWithoutTurningHoverIntoOwnerFrames()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "popup-owner-invalidation");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var target = owner.Child(owner.Root, "popup-owner-target");
        target.Present(theme);
        using var request = new OwnedSurfaceRequest(
            target,
            theme,
            Components.Text("Action"),
            interactive: true,
            consumeOutsideClick: true,
            closed: null
        );
        var selected = graph.Signal("keyboard", "selected-behavior");
        var hover = graph.Signal(false, "popup-hover");
        var invalidations = 0;
        var wakes = 0;

        Assert.IsTrue(
            WindowsSurfaceManager.DispatchAndInvalidateOwner(
                request,
                () =>
                {
                    selected.Value = "pointer";
                    return true;
                },
                () => invalidations++,
                () => wakes++
            )
        );
        Assert.AreEqual("pointer", selected.Value);
        Assert.AreEqual(1, invalidations, "The popup action did not invalidate its owner.");
        Assert.AreEqual(1, wakes, "The owner frame request was not woken.");

        Assert.IsTrue(
            WindowsSurfaceManager.DispatchAndInvalidateOwner(
                request,
                () => true,
                () => invalidations++,
                () => wakes++
            )
        );
        Assert.AreEqual(1, invalidations, "Unchanged popup input invalidated the owner.");
        Assert.AreEqual(1, wakes, "Unchanged popup input woke an owner frame.");

        Assert.IsTrue(
            WindowsSurfaceManager.DispatchAndInvalidateOwner(
                request,
                () =>
                {
                    hover.Value = true;
                    return true;
                },
                () => invalidations++,
                () => wakes++
            )
        );
        Assert.IsTrue(hover.Value);
        Assert.AreEqual(2, invalidations, "Shared hover state did not invalidate the owner.");
        Assert.AreEqual(2, wakes, "Shared hover state did not wake an owner frame.");
    }

    [TestMethod]
    public void RetainedCompositionCanBeHostedAgainWithoutSecondPresentation()
    {
        using var owner = new Composition(new ReactiveGraph(), "retained-popup-owner");
        using var theme = new ThemeContext(owner.Root.Scope, new Theme("authored-popup"));
        var target = owner.Child(owner.Root, "popup-target");
        target.Present(theme);
        using var request = new OwnedSurfaceRequest(
            target,
            theme,
            Components.Text("Popup content"),
            interactive: true,
            consumeOutsideClick: true,
            closed: null
        );
        var composition = request.CreateComposition();

        WindowsPopupHost.EnsureHostPadding(composition, request);
        Assert.AreEqual(
            LayoutAlignment.Start,
            composition.Root.Resolve(LayoutProperties.CrossAlignment).Value,
            "Native host padding replaced the popup's authored root presentation."
        );
        Assert.AreEqual(
            Insets.Uniform(WindowsPopupHost.ShadowMargin),
            composition.Root.Resolve(LayoutProperties.Padding).Value
        );

        // Closing a popup retains its composition so a branch can reopen without
        // rebuilding semantic identities. Hosting it again must reuse the one
        // presentation model attached to the retained root.
        WindowsPopupHost.EnsureHostPadding(composition, request);
    }

    [TestMethod]
    public void CustomSurfaceWithoutRootPresentationReceivesNeutralHostPadding()
    {
        using var owner = new Composition(new ReactiveGraph(), "custom-popup-owner");
        using var popup = new Composition(owner.Graph, "custom-popup");
        using var request = new UnpresentedPopupRequest(owner, popup);

        WindowsPopupHost.EnsureHostPadding(popup, request);

        Assert.AreEqual(
            Insets.Uniform(WindowsPopupHost.ShadowMargin),
            popup.Root.Resolve(LayoutProperties.Padding).Value
        );
    }

    [TestMethod]
    public void RefreshStopsWhenControlledCloseDisposesPopupDuringReactiveFlush()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "closing-popup-owner");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var target = owner.Child(owner.Root, "closing-popup-target");
        target.Present(theme);
        using var request = new OwnedSurfaceRequest(
            target,
            theme,
            Components.Text("Closing popup"),
            interactive: true,
            consumeOutsideClick: true,
            closed: null
        );
        var popup = request.CreateComposition();
        var close = graph.Signal(false, "close-popup");
        _ = owner.Root.Scope.Effect(
            () =>
            {
                if (close.Value)
                    request.Dispose();
            },
            "dispose-popup"
        );
        graph.Drain();

        close.Value = true;

        Assert.IsFalse(WindowsPopupHost.PrepareRefresh(request, popup));
        Assert.IsTrue(popup.IsDisposed, "The controlled close did not release its popup.");
    }

    [TestMethod]
    public void EscapeDisposalStopsWindowsPostDispatchClipboardReconciliation()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "escape-disposal-owner");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var target = owner.Mount(
            owner.Root,
            theme,
            Components.Button("Target", () => { }, Style.Empty.Height(40))
        );
        ContextMenuRequest? request = null;
        request = new ContextMenuRequest(
            owner,
            new(owner.Epoch, target.Id),
            new(12, 12, 1, 1),
            Components.Menu([Components.MenuItem("Run", () => { })]),
            theme,
            () => request!.Dispose()
        );
        using (request)
        {
            var popup = request.CreateComposition();
            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(popup, new(240, 160, 1), renderer);
            Assert.IsTrue(popup.Input.SetScene(scene));
            Assert.IsTrue(request.FocusFirst(request.ActiveLevels.Single()));
            using var clipboard = new WindowsClipboard(
                () => "clipboard",
                _ => true,
                () => "clipboard failure"
            );
            using var adapter = new WindowsInputAdapter(popup, clipboard: clipboard);
            var escape = new SDL.Event
            {
                Key = new()
                {
                    Type = SDL.EventType.KeyDown,
                    Key = SDL.Keycode.Escape,
                    Down = true,
                },
            };

            Assert.IsTrue(adapter.Dispatch(escape));
            Assert.IsTrue(popup.IsDisposed, "Escape did not synchronously release the popup.");
            adapter.ProcessClipboardRequests();
        }
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
        Assert.AreEqual(900 - 240 - WindowsPopupHost.ShadowMargin - parent.Left, placement.OffsetX);
        Assert.AreEqual(
            trigger.Top - WindowsPopupHost.ShadowMargin - parent.Top,
            placement.OffsetY
        );
        Assert.AreEqual(
            trigger.Left,
            parent.Left + placement.OffsetX + 240 + WindowsPopupHost.ShadowMargin,
            "The left-opening submenu's visible surface was not flush with its trigger."
        );
    }

    [TestMethod]
    public void SubmenuPlacementAlignsVisibleSurfaceWithTriggerTopAndRightEdge()
    {
        var trigger = new PopupScreenRect(200, 120, 360, 152);
        var parent = new PopupScreenRect(100, 80, 400, 420);
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

        Assert.IsFalse(placement.OpensLeft);
        Assert.AreEqual(
            trigger.Right,
            parent.Left + placement.OffsetX + WindowsPopupHost.ShadowMargin,
            "The right-opening submenu's visible surface was not flush with its trigger."
        );
        Assert.AreEqual(
            trigger.Top,
            parent.Top + placement.OffsetY + WindowsPopupHost.ShadowMargin,
            "The submenu's visible surface was not aligned with the trigger row."
        );
    }

    [TestMethod]
    public void ContentResizeOccursOnlyWhenMeasuredPopupDimensionsChange()
    {
        var unchanged = new PopupHostPlacement(10, 20, 240, 132, false);
        var oneSuggestion = new PopupHostPlacement(10, 20, 240, 68, false);

        Assert.IsFalse(WindowsPopupHost.NeedsContentResize(240, 132, unchanged));
        Assert.IsTrue(WindowsPopupHost.NeedsContentResize(240, 132, oneSuggestion));
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

    private sealed class UnpresentedPopupRequest(Composition owner, Composition popup)
        : PopupSurfaceRequest
    {
        public override Composition Owner => owner;
        public override LayoutRect Anchor => default;
        public override ThemeAppearance Appearance => default;
        public override bool IsValid => true;
        public override bool IsDismissed => false;

        public override Composition CreateComposition() => popup;

        public override LayoutRect Measure(ITextShaper shaper, LayoutViewport available) =>
            new(0, 0, 1, 1);

        public override void Dismiss() { }

        public override bool RestoreFocus() => false;

        public override void Dispose() { }
    }
}
