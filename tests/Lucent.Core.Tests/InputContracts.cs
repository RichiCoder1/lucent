using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class InputContracts
{
    [TestMethod]
    public void RoutingFocusCaptureAndAvailability()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "input");
        var enabled = new Token<bool>("input-enabled", true);
        var theme = new ThemeContext(composition.Root.Scope, new Theme("input").Set(enabled, true));
        Present(composition.Root, theme, 100, 100, true);
        var parent = composition.Child(composition.Root, "parent");
        Present(parent, theme, 100, 100, true);
        var child = composition.Child(parent, "child");
        Present(child, theme, 100, 20, true, enabled);
        var calls = new List<string>();
        var lost = 0;
        composition.Root.AttachBehaviors(
            new Probe(
                "root",
                BehaviorOwnership.Action | BehaviorOwnership.Semantics,
                pointer: route => calls.Add("root"),
                key: route => calls.Add("root-key")
            )
        );
        parent.AttachBehaviors(
            new Probe(
                "parent",
                BehaviorOwnership.Action | BehaviorOwnership.Semantics,
                pointer: route => calls.Add("parent")
            )
        );
        child.AttachBehaviors(
            new Probe(
                "child",
                BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics,
                pointer: route =>
                {
                    calls.Add("child");
                    if (route.Command.Kind == PointerCommandKind.Down)
                    {
                        route.Focus();
                        Assert(route.Capture(), "First capture claim failed.");
                    }
                },
                key: route => calls.Add(route.Command.IsRepeat ? "repeat" : "key"),
                lost: _ => lost++
            )
        );
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper())),
            "Current scene was rejected."
        );
        router.DispatchPointer(new(PointerCommandKind.Down, 1, 1, 1, PointerButton.Primary));
        Assert(
            calls.SequenceEqual(["child", "parent", "root"])
                && router.FocusedElement?.ElementId == child.Id
                && router
                    .Dump()
                    .Contains("capture pointer=1 owner=" + child.Id, StringComparison.Ordinal),
            "Pointer did not target/capture/focus and bubble deterministically."
        );
        calls.Clear();
        router.DispatchPointer(new(PointerCommandKind.Move, 1, 99, 99));
        Assert(
            calls.SequenceEqual(["child", "parent", "root"]),
            "Capture did not route moves through the original path."
        );
        router.DispatchPointer(new(PointerCommandKind.Up, 1, 99, 99));
        Assert(
            lost == 1 && !router.Dump().Contains("capture pointer=1", StringComparison.Ordinal),
            "Pointer release did not lose capture exactly once."
        );
        calls.Clear();
        router.DispatchKey(new(KeyCommandKind.Down, Key.Enter));
        router.DispatchKey(new(KeyCommandKind.Down, Key.Enter, IsRepeat: true));
        Assert(
            calls.SequenceEqual(["key", "root-key", "repeat", "root-key"]),
            "Keyboard routing or repeat identity changed."
        );
        theme.Theme = theme.Theme.Set(enabled, false);
        Assert(
            !router.SetScene(SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper()))
                && router.FocusedElement is null
                && router.SetScene(
                    SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper())
                )
                && router
                    .DispatchPointer(new(PointerCommandKind.Down, 2, 1, 1, PointerButton.Primary))
                    .Target?.ElementId != child.Id,
            "Disabled element retained focus, accepted stale paint, or became a hit target."
        );
    }

    [TestMethod]
    public void SnapshotFailureAndReorderSafety()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1, 2 }, "rows");
        using var composition = new Composition(graph, "reorder");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("reorder"));
        Present(composition.Root, theme, 100, 100, true);
        composition.Root.AttachBehaviors(
            new Probe(
                "root",
                BehaviorOwnership.Action | BehaviorOwnership.Semantics,
                pointer: _ => throw new InvalidOperationException("second")
            )
        );
        var failures = 0;
        var region = composition.ForEach(
            composition.Root,
            "rows",
            () => rows.Value,
            value => value,
            (value, context) =>
            {
                var row = context.Element("row");
                Present(row, theme, 100, 20, true);
                row.AttachBehaviors(
                    new Probe(
                        "row-" + value,
                        BehaviorOwnership.Action
                            | BehaviorOwnership.Focus
                            | BehaviorOwnership.Semantics,
                        pointer: route =>
                        {
                            route.Focus();
                            if (value == 1)
                                throw new InvalidOperationException("first");
                        },
                        lost: _ => failures++
                    )
                );
                return row;
            }
        );
        graph.Drain();
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper())),
            "Initial keyed scene rejected."
        );
        var first = region.Items[0];
        try
        {
            router.DispatchPointer(new(PointerCommandKind.Down, 3, 1, 1, PointerButton.Primary));
            throw new InvalidOperationException("Callback failure was not aggregated.");
        }
        catch (AggregateException error)
        {
            Assert(
                error
                    .InnerExceptions.Select(item => item.Message)
                    .SequenceEqual(["first", "second"]),
                "Callback failure aggregation changed."
            );
        }
        Assert(
            router.FocusedElement?.ElementId == first.Id,
            "Callback failure rolled back completed focus state."
        );
        rows.Value = [2, 1];
        graph.Drain();
        Assert(
            router.DispatchPointer(new(PointerCommandKind.Move, 3, 1, 1)).Rejection
                == InputRejection.StaleScene
                && router.FocusedElement is null,
            "Stale reordered scene routed input or retained focus."
        );
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper())),
            "Reordered scene rejected."
        );
        Assert(
            router.MoveFocus(FocusTraversalDirection.Next)
                && router.FocusedElement?.ElementId == region.Items[0].Id,
            "Traversal did not use retained reordered composition order."
        );
        var focused = region.Items[0];
        rows.Value = [1, 2];
        graph.Drain();
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper()))
                && router.FocusedElement?.ElementId == focused.Id,
            "A current keyed row lost focus when its order changed in a replacement scene."
        );
        focused.Dispose();
        Assert(
            router.FocusedElement is null && failures == 0,
            "Disposed focus owner remained active or invoked unrelated capture loss."
        );
    }

    [TestMethod]
    public void DisposalDuringRouteUsesSnapshot()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "dispatch-disposal");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("dispatch-disposal"));
        Present(composition.Root, theme, 40, 40, true);
        var child = composition.Child(composition.Root, "child");
        Present(child, theme, 40, 20, true);
        var calls = new List<string>();
        composition.Root.AttachBehaviors(
            new Probe(
                "root",
                BehaviorOwnership.Action | BehaviorOwnership.Semantics,
                pointer: _ => calls.Add("root")
            )
        );
        child.AttachBehaviors(
            new Probe(
                "child",
                BehaviorOwnership.Action | BehaviorOwnership.Semantics,
                pointer: _ =>
                {
                    calls.Add("child");
                    child.Dispose();
                }
            )
        );
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper())),
            "Disposal scene rejected."
        );
        router.DispatchPointer(new(PointerCommandKind.Down, 9, 1, 1, PointerButton.Primary));
        Assert(
            calls.SequenceEqual(["child", "root"]) && child.IsDisposed,
            "Disposal during dispatch changed its stable ancestor route."
        );
    }

    [TestMethod]
    public void FreshnessEscapesAndRegistrationCleanup()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "fresh");
        var visible = new Token<bool>("visible", true);
        var theme = new ThemeContext(composition.Root.Scope, new Theme("fresh").Set(visible, true));
        Present(composition.Root, theme, 40, 40, true);
        var child = composition.Child(composition.Root, "child");
        child.Present(
            theme,
            author: Style
                .Empty.Set(InputProperties.Visible, visible)
                .Set(LayoutProperties.Width, 40f)
                .Set(LayoutProperties.Height, 20f)
                .Set(LayoutProperties.Clip, true)
        );
        PointerRoute? escaped = null;
        child.AttachBehaviors(
            new Probe(
                "escape",
                BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics,
                pointer: route => escaped = route
            )
        );
        var router = composition.Input;
        var first = SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper());
        Assert(router.SetScene(first), "Fresh scene rejected.");
        var result = router.DispatchPointer(
            new(PointerCommandKind.Down, 4, 1, 1, PointerButton.Primary)
        );
        Expect<InvalidOperationException>(() => escaped!.Handled = true);
        Expect<NotSupportedException>(() => ((IList<ElementIdentity>)result.Route).Clear());
        theme.Theme = theme.Theme.Set(visible, false);
        Assert(
            router.DispatchPointer(new(PointerCommandKind.Move, 4, 1, 1)).Rejection
                == InputRejection.StaleScene,
            "Property-only scene change was not stale."
        );
        var second = SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper());
        var third = SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper());
        Assert(
            !router.SetScene(second)
                && !router.SetScene(third)
                && router.SetScene(
                    SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper())
                )
                && router
                    .DispatchPointer(new(PointerCommandKind.Down, 5, 1, 1, PointerButton.Primary))
                    .Target?.ElementId != child.Id
                && router.Dump().Contains("kind=Pointer/Down", StringComparison.Ordinal),
            "Router accepted a superseded or unreconciled scene, or used hidden retained input."
        );

        var retry = composition.Child(composition.Root, "retry");
        Present(retry, theme, 1, 1, true);
        Expect<InvalidOperationException>(() => retry.AttachBehaviors(new FailingFocusable()));
        Assert(
            router.Dump().Contains("focusables=1", StringComparison.Ordinal),
            "Failed focusable attachment leaked router ownership."
        );
        retry.AttachBehaviors(
            new Probe("retry", BehaviorOwnership.Focus | BehaviorOwnership.Semantics)
        );
        Assert(
            router.Dump().Contains("focusables=2", StringComparison.Ordinal),
            "Focusable retry did not recover after rollback."
        );
        retry.Dispose();
        child.Dispose();
        Assert(
            router
                .Dump()
                .Contains(
                    "registrations pointer=0 key=0 focus=0 captureLoss=0 focusables=0",
                    StringComparison.Ordinal
                ),
            "Scope disposal did not remove exact input registrations."
        );
        var probe = ReleasedRegistrationPayload();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert(!probe.Payload.IsAlive, "Router retained disposed callback/context payload.");
        GC.KeepAlive(probe.Root);
    }

    [TestMethod]
    public void ReentrancyAndCombinedFailures()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "failures");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("failures"));
        Present(composition.Root, theme, 40, 40, true);
        var child = composition.Child(composition.Root, "child");
        Present(child, theme, 40, 20, true);
        var router = composition.Input;
        child.AttachBehaviors(
            new Probe(
                "failures",
                BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics,
                pointer: route =>
                {
                    if (route.Command.Kind == PointerCommandKind.Down)
                        route.Capture();
                    else if (route.Command.Kind == PointerCommandKind.Up)
                        throw new InvalidOperationException("route");
                },
                lost: _ => throw new InvalidOperationException("loss")
            )
        );
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper())),
            "Failure scene rejected."
        );
        router.DispatchPointer(new(PointerCommandKind.Down, 1, 1, 1, PointerButton.Primary));
        router.DispatchPointer(new(PointerCommandKind.Down, 2, 1, 1, PointerButton.Primary));
        try
        {
            router.DispatchPointer(new(PointerCommandKind.Up, 1, 1, 1));
            throw new InvalidOperationException("Expected route/loss aggregate.");
        }
        catch (AggregateException error)
        {
            Assert(
                error.InnerExceptions.Select(item => item.Message).SequenceEqual(["route", "loss"]),
                "Up release masked route/capture failures."
            );
        }
        try
        {
            router.DispatchPointer(new(PointerCommandKind.Cancel, 2, 1, 1));
            throw new InvalidOperationException("Expected cancellation loss aggregate.");
        }
        catch (AggregateException error)
        {
            Assert(
                error.InnerExceptions.Single().Message == "loss",
                "Cancel did not release capture once."
            );
        }

        var nested = composition.Child(composition.Root, "nested");
        Present(nested, theme, 1, 1, true);
        nested.AttachBehaviors(
            new Probe(
                "nested",
                BehaviorOwnership.Action | BehaviorOwnership.Semantics,
                pointer: _ => router.DispatchKey(new(KeyCommandKind.Down, Key.Enter))
            )
        );
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper())),
            "Nested scene rejected."
        );
        try
        {
            router.DispatchPointer(
                new(PointerCommandKind.Down, 3, .5f, 20.5f, PointerButton.Primary)
            );
            throw new InvalidOperationException("Expected nested public dispatch rejection.");
        }
        catch (AggregateException error)
        {
            Assert(
                error.InnerExceptions.Single() is InvalidOperationException,
                "Nested public dispatch did not fail closed."
            );
        }
        Expect<ArgumentException>(() => router.MoveFocus((FocusTraversalDirection)99));
    }

    [TestMethod]
    public void RetainedHitRules()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "hit");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("hit"));
        Present(composition.Root, theme, 40, 40, true);
        var unclip = composition.Child(composition.Root, "unclip");
        Present(unclip, theme, 40, 20, false);
        var overflow = composition.Child(unclip, "overflow");
        Present(overflow, theme, 40, 25, true);
        var top = composition.Child(composition.Root, "top");
        Present(top, theme, 40, 20, true);
        var zero = composition.Child(composition.Root, "zero");
        Present(zero, theme, 0, 0, true);
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(40, 60, 1), new EmptyShaper())),
            "Hit scene rejected."
        );
        Assert(
            router.DispatchPointer(new(PointerCommandKind.Move, 8, 1, 21)).Target?.ElementId
                == top.Id,
            "Topmost retained sibling did not beat an overflowing lower sibling."
        );
        Assert(
            router.DispatchPointer(new(PointerCommandKind.Move, 8, 1, 45)).Target?.ElementId
                == composition.Root.Id,
            "Zero-bound element or paint-independent hit testing changed target."
        );
        var clipGraph = new ReactiveGraph();
        using var clipped = new Composition(clipGraph, "clip");
        var clipTheme = new ThemeContext(clipped.Root.Scope, new Theme("clip"));
        Present(clipped.Root, clipTheme, 40, 40, true);
        var parent = clipped.Child(clipped.Root, "parent");
        Present(parent, clipTheme, 40, 20, true);
        var child = clipped.Child(parent, "child");
        Present(child, clipTheme, 40, 25, true);
        var clippedRouter = clipped.Input;
        Assert(
            clippedRouter.SetScene(SceneLayout.Project(clipped, new(40, 40, 1), new EmptyShaper()))
                && clippedRouter
                    .DispatchPointer(new(PointerCommandKind.Move, 8, 1, 21))
                    .Target?.ElementId == clipped.Root.Id,
            "Retained clip did not reject an overflowing child hit."
        );

        var paddedGraph = new ReactiveGraph();
        using var padded = new Composition(paddedGraph, "padded-hit");
        var paddedTheme = new ThemeContext(padded.Root.Scope, new Theme("padded-hit"));
        Present(padded.Root, paddedTheme, 40, 40, false);
        var paddedParent = padded.Child(padded.Root, "padded-parent");
        paddedParent.Present(
            paddedTheme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 40f)
                .Set(LayoutProperties.Height, 20f)
                .Set(LayoutProperties.Padding, Insets.Uniform(5))
                .Set(LayoutProperties.Clip, true)
        );
        var paddedChild = padded.Child(paddedParent, "padded-child");
        Present(paddedChild, paddedTheme, 40, 20, true);
        var paddedRouter = padded.Input;
        Assert(
            paddedRouter.SetScene(SceneLayout.Project(padded, new(40, 40, 1), new EmptyShaper())),
            "Padded hit scene rejected."
        );
        Assert(
            paddedRouter.DispatchPointer(new(PointerCommandKind.Move, 9, 2, 2)).Target?.ElementId
                == paddedParent.Id
                && paddedRouter
                    .DispatchPointer(new(PointerCommandKind.Move, 9, 34, 10))
                    .Target?.ElementId == paddedChild.Id
                && paddedRouter
                    .DispatchPointer(new(PointerCommandKind.Move, 9, 37, 10))
                    .Target?.ElementId == paddedParent.Id,
            "Own padded outer hit or descendant inner clip behavior changed."
        );
    }

    [TestMethod]
    public void FocusLossDisposalIsIterative()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "focus-loss");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("focus-loss"));
        Present(composition.Root, theme, 40, 40, true);
        var first = composition.Child(composition.Root, "first");
        Present(first, theme, 40, 20, true);
        var second = composition.Child(composition.Root, "second");
        Present(second, theme, 40, 20, true);
        first.AttachBehaviors(
            new Probe(
                "first",
                BehaviorOwnership.Focus | BehaviorOwnership.Semantics,
                focus: route =>
                {
                    if (route.Command.Kind == FocusCommandKind.Lost)
                    {
                        first.Dispose();
                        throw new InvalidOperationException("lost");
                    }
                }
            )
        );
        second.AttachBehaviors(
            new Probe("second", BehaviorOwnership.Focus | BehaviorOwnership.Semantics)
        );
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper()))
                && router.MoveFocus(FocusTraversalDirection.Next),
            "Initial focus was not assigned."
        );
        try
        {
            router.MoveFocus(FocusTraversalDirection.Next);
            throw new InvalidOperationException("Expected focus-loss callback aggregate.");
        }
        catch (AggregateException error)
        {
            Assert(
                error.InnerExceptions.Single().Message == "lost"
                    && first.IsDisposed
                    && router.FocusedElement?.ElementId == second.Id,
                "Focus loss did not commit, dispose, and advance exactly once."
            );
        }
    }

    [TestMethod]
    public void SignatureSemanticsLifetimeAndDiagnostics()
    {
        var geometryGraph = new ReactiveGraph();
        using var geometry = new Composition(geometryGraph, "geometry-signature");
        var geometryText = new Token<string?>("geometry-text", "A");
        var geometryTheme = new ThemeContext(
            geometry.Root.Scope,
            new Theme("geometry-signature").Set(geometryText, "A")
        );
        Present(geometry.Root, geometryTheme, 40, 40, true);
        var geometryChild = geometry.Child(geometry.Root, "child");
        geometryChild.Present(
            geometryTheme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 40f)
                .Set(LayoutProperties.Height, 20f)
                .Set(ProjectionProperties.Text, geometryText)
        );
        var geometryRouter = geometry.Input;
        Assert(
            geometryRouter.SetScene(
                SceneLayout.Project(geometry, new(40, 40, 1), new MetricShaper())
            ),
            "Geometry signature scene rejected."
        );
        geometryTheme.Theme = geometryTheme.Theme.Set(geometryText, "AAAA");
        Assert(
            geometryChild.Resolve(InputProperties.Enabled).Value
                && geometryChild.Resolve(InputProperties.Visible).Value
                && geometryRouter.DispatchPointer(new(PointerCommandKind.Move, 30, 1, 1)).Rejection
                    == InputRejection.StaleScene,
            "Shaping-only geometry change did not stale the retained input scene while availability stayed unchanged."
        );

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "signature");
        var text = new Token<string?>("text", "A");
        var font = new Token<string>("font", "B;C");
        var enabled = new Token<bool>("enabled", true);
        var theme = new ThemeContext(
            composition.Root.Scope,
            new Theme("signature").Set(text, "A").Set(font, "B;C").Set(enabled, true)
        );
        Present(composition.Root, theme, 40, 40, true);
        var child = composition.Child(composition.Root, "child");
        child.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 40f)
                .Set(LayoutProperties.Height, 20f)
                .Set(ProjectionProperties.Text, text)
                .Set(TypographyProperties.FontFamily, font)
                .Set(InputProperties.Enabled, enabled)
        );
        child.AttachBehaviors(
            new RowActionBehavior(
                "row",
                new(SemanticRole.ListItem, "row", actions: SemanticAction.Select)
            )
        );
        var router = composition.Input;
        var first = SceneLayout.Project(composition, new(40, 40, 1), new MetricShaper());
        Assert(router.SetScene(first), "Initial collision scene rejected.");
        router.DispatchPointer(new(PointerCommandKind.Down, 10, 1, 1, PointerButton.Primary));
        router.DispatchPointer(new(PointerCommandKind.Up, 10, 1, 1));
        var initial = composition.SemanticSnapshot()!.Children.Single();
        Assert(
            initial.Focused
                && initial.Selected
                && composition
                    .Dump()
                    .Contains("focused=true selected=true", StringComparison.Ordinal),
            "Effective focus/selection dump and snapshot diverged."
        );
        theme.Theme = theme.Theme.Set(text, "A;B").Set(font, "C").Set(enabled, false);
        Assert(
            router.DispatchPointer(new(PointerCommandKind.Move, 11, 1, 1)).Rejection
                == InputRejection.StaleScene
                && !composition.IsCurrent(initial.Identity),
            "Delimiter collision retained stale input/semantic identity."
        );
        var second = SceneLayout.Project(composition, new(40, 40, 1), new MetricShaper());
        Assert(
            second.Boxes.Single(box => box.Identity.ElementId == child.Id).Text!.Width
                != first.Boxes.Single(box => box.Identity.ElementId == child.Id).Text!.Width
                && !router.SetScene(second)
                && router.SetScene(
                    SceneLayout.Project(composition, new(40, 40, 1), new MetricShaper())
                ),
            "Collision repro did not change geometry and reject old scene."
        );
        var disabled = composition.SemanticSnapshot()!.Children.Single();
        Assert(
            !disabled.Enabled && composition.IsCurrent(disabled.Identity),
            "Effective disabled semantic state was not reconciled."
        );
        _ = router.DispatchPointer(new(PointerCommandKind.Move, 11, 1, 1));
        Assert(
            composition.IsCurrent(disabled.Identity),
            "No-op dispatch changed semantic identity."
        );
        Assert(
            second.Dump().Contains("scene generation=", StringComparison.Ordinal)
                && second.Dump().Contains("input epoch=", StringComparison.Ordinal)
                && router.Dump().Contains("registration kind=pointer", StringComparison.Ordinal)
                && router.Dump().Contains("focusable owner=", StringComparison.Ordinal),
            "Input diagnostics omitted retained metadata or registrations."
        );

        var cleanupGraph = new ReactiveGraph();
        var cleanup = new Composition(cleanupGraph, "cleanup");
        var cleanupTheme = new ThemeContext(cleanup.Root.Scope, new Theme("cleanup"));
        Present(cleanup.Root, cleanupTheme, 20, 20, true);
        var owner = cleanup.Child(cleanup.Root, "owner");
        Present(owner, cleanupTheme, 20, 20, true);
        var losses = 0;
        var focusLosses = 0;
        owner.AttachBehaviors(
            new Probe(
                "owner",
                BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics,
                pointer: route =>
                {
                    if (route.Command.Kind == PointerCommandKind.Down)
                    {
                        route.Focus();
                        route.Capture();
                    }
                },
                focus: route =>
                {
                    if (route.Command.Kind == FocusCommandKind.Lost)
                        focusLosses++;
                },
                lost: _ => losses++
            )
        );
        var cleanupRouter = cleanup.Input;
        Assert(
            cleanupRouter.SetScene(SceneLayout.Project(cleanup, new(20, 20, 1), new EmptyShaper())),
            "Cleanup scene rejected."
        );
        cleanupRouter.DispatchPointer(
            new(PointerCommandKind.Down, 12, 1, 1, PointerButton.Primary)
        );
        cleanup.Dispose();
        Assert(
            losses == 1 && focusLosses == 1,
            "Composition cleanup did not release capture and focus while behavior state was live."
        );
        Expect<ObjectDisposedException>(() => _ = cleanupRouter.Modality);
        var threadGraph = new ReactiveGraph();
        using var threadComposition = new Composition(threadGraph, "thread");
        var threadRouter = threadComposition.Input;
        Exception? threadFailure = null;
        var worker = new Thread(() =>
        {
            try
            {
                _ = threadRouter.FocusedElement;
            }
            catch (Exception error)
            {
                threadFailure = error;
            }
        })
        {
            IsBackground = true,
        };
        worker.Start();
        if (!worker.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Foreign-thread input ownership proof did not finish.");
        Assert(
            threadFailure is InvalidOperationException,
            "Input router accepted a read from a foreign thread."
        );
    }

    [TestMethod]
    public void FinalRouterSeal()
    {
        var neverGraph = new ReactiveGraph();
        using var never = new Composition(neverGraph, "never");
        var neverRouter = never.Input;
        Assert(
            neverRouter.DispatchPointer(new(PointerCommandKind.Move, 20, 0, 0)).Rejection
                == InputRejection.NoScene
                && neverRouter.Dump().Contains("reason=NoScene", StringComparison.Ordinal),
            "Never-installed scene did not reject as NoScene."
        );

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "seal");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("seal"));
        Present(composition.Root, theme, 20, 20, true);
        var child = composition.Child(composition.Root, "child");
        Present(child, theme, 20, 20, true);
        var capture = false;
        child.AttachBehaviors(
            new Probe(
                "dispose-capture",
                BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics,
                pointer: route =>
                {
                    if (route.Command.Kind == PointerCommandKind.Down)
                    {
                        child.Dispose();
                        capture = route.Capture();
                    }
                }
            )
        );
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(20, 20, 1), new EmptyShaper())),
            "Dispose-capture scene rejected."
        );
        router.DispatchPointer(new(PointerCommandKind.Down, 21, 1, 1, PointerButton.Primary));
        Assert(
            !capture && !router.Dump().Contains("capture pointer=21", StringComparison.Ordinal),
            "Disposed callback owner acquired capture."
        );

        var staleGraph = new ReactiveGraph();
        using var stale = new Composition(staleGraph, "stale");
        var staleTheme = new ThemeContext(stale.Root.Scope, new Theme("stale"));
        Present(stale.Root, staleTheme, 20, 20, true);
        var owner = stale.Child(stale.Root, "owner");
        Present(owner, staleTheme, 20, 20, true);
        var releases = 0;
        owner.AttachBehaviors(
            new Probe(
                "owner",
                BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics,
                pointer: route =>
                {
                    if (route.Command.Kind == PointerCommandKind.Down)
                    {
                        route.Focus();
                        route.Capture();
                    }
                },
                lost: _ => releases++
            )
        );
        var staleRouter = stale.Input;
        var first = SceneLayout.Project(stale, new(20, 20, 1), new EmptyShaper());
        Assert(staleRouter.SetScene(first), "Initial stale scene rejected.");
        staleRouter.DispatchPointer(new(PointerCommandKind.Down, 22, 1, 1, PointerButton.Primary));
        _ = SceneLayout.Project(stale, new(20, 20, 1), new EmptyShaper());
        Assert(
            !staleRouter.SetScene(first)
                && releases == 1
                && staleRouter.FocusedElement is null
                && staleRouter.DispatchPointer(new(PointerCommandKind.Move, 22, 1, 1)).Rejection
                    == InputRejection.NoScene,
            "Rejected older scene did not fail-close stale installed input."
        );

        AssertAncestorLossReason(false, PointerCaptureLossReason.Hidden);
        AssertAncestorLossReason(true, PointerCaptureLossReason.Disabled);
    }

    private static void AssertAncestorLossReason(bool disabled, PointerCaptureLossReason expected)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "ancestor-" + disabled);
        var flag = new Token<bool>("flag", true);
        var theme = new ThemeContext(composition.Root.Scope, new Theme("ancestor").Set(flag, true));
        Present(composition.Root, theme, 20, 20, true);
        var parent = composition.Child(composition.Root, "parent");
        parent.Present(
            theme,
            author: (
                disabled
                    ? Style.Empty.Set(InputProperties.Enabled, flag)
                    : Style.Empty.Set(InputProperties.Visible, flag)
            )
                .Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
        );
        var child = composition.Child(parent, "child");
        Present(child, theme, 20, 20, true);
        PointerCaptureLossReason? reason = null;
        child.AttachBehaviors(
            new Probe(
                "child",
                BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics,
                pointer: route =>
                {
                    if (route.Command.Kind == PointerCommandKind.Down)
                    {
                        route.Focus();
                        route.Capture();
                    }
                },
                lost: loss => reason = loss.Reason
            )
        );
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(20, 20, 1), new EmptyShaper())),
            "Ancestor scene rejected."
        );
        router.DispatchPointer(new(PointerCommandKind.Down, 23, 1, 1, PointerButton.Primary));
        theme.Theme = theme.Theme.Set(flag, false);
        Assert(
            router.DispatchPointer(new(PointerCommandKind.Move, 23, 1, 1)).Rejection
                == InputRejection.StaleScene
                && reason == expected
                && router.Dump().Contains("captureLoss=" + expected, StringComparison.Ordinal),
            "Ancestor availability loss reason was not deterministic."
        );
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static PayloadProbe ReleasedRegistrationPayload()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "release-input");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("release-input"));
        Present(composition.Root, theme, 1, 1, true);
        var child = composition.Child(composition.Root, "child");
        Present(child, theme, 1, 1, true);
        var payload = new Payload();
        var weak = new WeakReference(payload);
        child.AttachBehaviors(
            new Probe(
                "release",
                BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics,
                pointer: _ => GC.KeepAlive(payload),
                key: _ => GC.KeepAlive(payload),
                focus: _ => GC.KeepAlive(payload),
                lost: _ => GC.KeepAlive(payload)
            )
        );
        _ = composition.Input;
        child.Dispose();
        return new(weak, composition);
    }

    private static void Present(
        Element element,
        ThemeContext theme,
        float width,
        float height,
        bool clip,
        Token<bool>? enabled = null
    ) =>
        element.Present(
            theme,
            author: (
                enabled is null ? Style.Empty : Style.Empty.Set(InputProperties.Enabled, enabled)
            )
                .Set(LayoutProperties.Width, width)
                .Set(LayoutProperties.Height, height)
                .Set(LayoutProperties.Axis, LayoutAxis.Column)
                .Set(LayoutProperties.Clip, clip)
        );

    private static void Assert(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private static void Expect<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class Probe(
        string name,
        BehaviorOwnership ownership,
        Action<PointerRoute>? pointer = null,
        Action<KeyRoute>? key = null,
        Action<FocusRoute>? focus = null,
        Action<PointerCaptureLoss>? lost = null
    ) : Behavior
    {
        public override string Name => name;
        public override BehaviorOwnership Ownership => ownership;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(new(SemanticRole.Group, name));
            if (ownership.HasFlag(BehaviorOwnership.Focus))
                context.MakeFocusable();
            if (pointer is not null)
                context.OnPointer(pointer);
            if (key is not null)
                context.OnKey(key);
            if (focus is not null)
                context.OnFocus(focus);
            if (lost is not null)
                context.OnCaptureLost(lost);
        }
    }

    private sealed class FailingFocusable : Behavior
    {
        public override string Name => "failing";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(new(SemanticRole.Group, "failing"));
            context.MakeFocusable();
            throw new InvalidOperationException("attach");
        }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "metric",
                "metric",
                400,
                5,
                0,
                "metric",
                0,
                "metric#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                request.Text.Length,
                [glyph]
            );
            return new("metric", request.Text.Length, request.FontSize, [run]);
        }
    }

    private sealed class Payload;

    private sealed record PayloadProbe(WeakReference Payload, object Root);
}
