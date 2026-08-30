using Lucent.Core;

internal static class CompositionContracts
{
    public static int Run()
    {
        try
        {
            ConditionalIdentityAndCleanup();
            KeyedIdentityRollbackAndCleanup();
            DepartedFacetsAndLateAsync();
            FailureAndDisposalSafety();
            FactoryGuardsAndJointFailures();
            PublicFactoryStructuralGuards();
            ManualScopeDisposalRetiresEntries();
            KeyedFactoryTransactionsAndReentrancy();
            var first = EquivalentDump();
            Assert(first == EquivalentDump(), "Composition dumps differ for equivalent active trees.");
            Assert(!first.Contains("secret", StringComparison.OrdinalIgnoreCase) && !first.Contains("value=", StringComparison.OrdinalIgnoreCase), "Composition dump exposed application values.");
            ReleasedPayload();
            ReleasedOwnershipIdentities();
            UnrelatedSemanticRefreshKeepsIdentityCurrent();
            Console.WriteLine("Lucent.Core composition contracts: PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Lucent.Core composition contracts: FAIL: " + exception.Message);
            return 1;
        }
    }

    private static void ConditionalIdentityAndCleanup()
    {
        var graph = new ReactiveGraph();
        var active = graph.Signal(false, "conditional-active");
        using var composition = new Composition(graph, "conditional-root");
        var cleanup = 0;
        var region = composition.When(composition.Root, "conditional-region", () => active.Value, context =>
        {
            var child = context.Element("conditional-child");
            _ = context.Child(child, "conditional-grandchild");
            child.Scope.OnDispose(() => cleanup++);
            return child;
        });
        graph.Drain();
        Assert(region.Active is null, "Inactive conditional created content.");
        active.Value = true; graph.Drain();
        var child = region.Active!;
        var scope = child.Scope;
        region.Update(true);
        Assert(ReferenceEquals(child, region.Active) && ReferenceEquals(scope, region.Active!.Scope) && child.Children.Count == 1, "Unchanged conditional branch replaced identity or provisional structure.");
        active.Value = false; graph.Drain();
        Assert(region.Active is null && child.IsDisposed && cleanup == 1, "Conditional departure did not dispose exactly once.");
        active.Value = true; graph.Drain();
        Assert(region.Active is not null && !ReferenceEquals(child, region.Active), "Conditional remount reused departed identity.");
    }

    private static void KeyedIdentityRollbackAndCleanup()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { "a", "b" }, "keyed-rows");
        using var composition = new Composition(graph, "keyed-root");
        var cleanup = new Dictionary<string, int>();
        var region = composition.ForEach(composition.Root, "keyed-region", () => rows.Value, value => value, (value, context) =>
        {
            var row = context.Element("keyed-row");
            row.Scope.OnDispose(() => cleanup[value] = cleanup.GetValueOrDefault(value) + 1);
            return row;
        });
        graph.Drain();
        var a = region.Items[0];
        var b = region.Items[1];
        rows.Value = ["b", "a", "c"]; graph.Drain();
        var c = region.Items[2];
        Assert(region.Items.Select(item => item.Id).SequenceEqual([b.Id, a.Id, c.Id]) && ReferenceEquals(region.Items[0].Scope, b.Scope), "Same-key reorder replaced an element or scope.");
        rows.Value = ["c", "d", "a"]; graph.Drain();
        Assert(b.IsDisposed && cleanup.GetValueOrDefault("b") == 1 && ReferenceEquals(region.Items[0], c) && ReferenceEquals(region.Items[2], a), "Keyed removal/addition was not exact.");

        var dump = composition.Dump();
        var identities = region.Items.ToArray();
        rows.Value = ["c", "c", "a"];
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == dump && region.Items.SequenceEqual(identities), "Duplicate keys mutated the live tree.");

        rows.Value = ["a"];
        graph.Drain();
        Assert(c.IsDisposed && cleanup.GetValueOrDefault("c") == 1 && cleanup.GetValueOrDefault("d") == 1, "Departed keyed entries were not cleaned once.");
    }

    private static void UnrelatedSemanticRefreshKeepsIdentityCurrent()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "semantic-current");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light); Controls.Panel(composition.Root, theme, "root");
        var first = composition.Child(composition.Root, "first"); var invoked = 0; Controls.Button(first, theme, "First", () => invoked++);
        var second = composition.Child(composition.Root, "second"); var status = Controls.Loading(second, theme, "Ready");
        graph.Drain(); var identity = composition.SemanticSnapshot()!.Children.Single(node => node.Name == "First").Identity;
        status.Label = "Changed"; graph.Drain();
        Assert(composition.ExecuteSemanticCommand(identity, new(SemanticCommandKind.Invoke)) == SemanticCommandResult.Applied && invoked == 1,
            "An unrelated semantic refresh invalidated a still-current command identity.");
    }

    private static void DepartedFacetsAndLateAsync()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1, 2 }, "facet-rows");
        var tick = graph.Signal(0, "facet-tick");
        var work = new Dictionary<int, TaskCompletionSource<int>> { [1] = new(), [2] = new() };
        using var composition = new Composition(graph, "facet-root");
        var cleanup = 0;
        var cancelled = 0;
        var region = composition.ForEach(composition.Root, "facet-region", () => rows.Value, value => value, (value, context) =>
        {
            var row = context.Element("facet-row");
            _ = row.Scope.Effect(() => _ = tick.Value, "facet-subscription");
            var pending = row.Scope.Async(token =>
            {
                token.Register(() => cancelled++);
                return work[value].Task;
            }, "facet-async");
            _ = pending.IsPending;
            row.Scope.OnDispose(() => cleanup++); // future focus
            row.Scope.OnDispose(() => cleanup++); // future capture
            row.Scope.OnDispose(() => cleanup++); // future semantics/scene
            return row;
        });
        graph.Drain();
        var departed = region.Items[0];
        var retained = region.Items[1];
        rows.Value = [2];
        graph.Drain();
        Task.Run(() => work[1].SetResult(42)).GetAwaiter().GetResult();
        graph.Drain();
        Assert(departed.IsDisposed && ReferenceEquals(region.Items.Single(), retained) && cancelled == 1 && cleanup == 3 && composition.Dump().Split('\n').Count(line => line.Contains("facet-row", StringComparison.Ordinal)) == 1, "Departed facets were retained or async work committed.");
    }

    private static void FailureAndDisposalSafety()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { "kept" }, "failure-rows");
        using var composition = new Composition(graph, "failure-root");
        var provisionalCleanup = 0;
        var throwingCleanup = 0;
        var region = composition.ForEach(composition.Root, "failure-region", () => rows.Value, value => value, (value, context) =>
        {
            var row = context.Element("failure-row");
            if (value == "broken")
            {
                row.Scope.OnDispose(() => provisionalCleanup++);
                throw new InvalidOperationException("factory");
            }
            if (value == "throwing")
            {
                row.Scope.OnDispose(() => throwingCleanup++);
                row.Scope.OnDispose(() => throw new InvalidOperationException("cleanup"));
            }
            return row;
        });
        graph.Drain();
        var kept = region.Items.Single();
        rows.Value = ["kept", "broken"];
        ExpectAggregate(graph.Drain);
        Assert(ReferenceEquals(region.Items.Single(), kept) && provisionalCleanup == 1, "Factory failure changed live keyed state or leaked provisional ownership.");

        rows.Value = ["throwing"];
        graph.Drain();
        Assert(region.Items.Single().IsDisposed == false && kept.IsDisposed && throwingCleanup == 0, "Keyed cleanup did not commit before reporting cleanup failure.");
        rows.Value = [];
        ExpectAggregate(graph.Drain);
        Assert(throwingCleanup == 1 && region.Items.Count == 0, "Throwing cleanup stopped later cleanup or left a live entry.");

        var disposalGraph = new ReactiveGraph();
        var trigger = disposalGraph.Signal(false, "dispose-during-factory");
        var disposal = new Composition(disposalGraph, "dispose-root");
        _ = disposal.When(disposal.Root, "dispose-region", () => trigger.Value, context =>
        {
            var child = context.Element("dispose-child");
            disposal.Dispose();
            return child;
        });
        disposalGraph.Drain();
        trigger.Value = true;
        ExpectAggregate(disposalGraph.Drain);
        Assert(disposal.IsDisposed, "Disposal during a factory left the composition active.");
    }

    private static void FactoryGuardsAndJointFailures()
    {
        var graph = new ReactiveGraph();
        var active = graph.Signal(false, "invalid-parent-active");
        using var composition = new Composition(graph, "invalid-parent-root");
        var unrelated = composition.Child(composition.Root, "unrelated");
        var region = composition.When(composition.Root, "invalid-parent-region", () => active.Value, context =>
        {
            var root = context.Element("provisional");
            _ = context.Child(composition.Root, "ghost");
            return root;
        });
        graph.Drain();
        var before = composition.Dump();
        active.Value = true;
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == before && unrelated.Children.Count == 0 && region.Active is null, "A factory attached an unrelated live parent before validation.");
        Assert(composition.Root.Children is not List<Element>, "Children exposed the mutable backing list.");
        var writable = (IList<Element>)composition.Root.Children;
        ExpectNotSupported(writable.Clear);
        Assert(composition.Dump() == before, "A mutable Children view bypassed ownership.");

        var disposedGraph = new ReactiveGraph();
        var disposedActive = disposedGraph.Signal(false, "disposed-context-active");
        using var disposedComposition = new Composition(disposedGraph, "disposed-context-root");
        var contextUsedAfterDispose = false;
        var disposedRegion = disposedComposition.When(disposedComposition.Root, "disposed-context-region", () => disposedActive.Value, context =>
        {
            var root = context.Element("disposed-context-child");
            context.Dispose();
            ExpectDisposed(() => context.Element("late-root"));
            ExpectDisposed(() => context.Child(root, "late-child"));
            contextUsedAfterDispose = true;
            return root;
        });
        disposedGraph.Drain();
        disposedActive.Value = true;
        ExpectAggregate(disposedGraph.Drain);
        Assert(contextUsedAfterDispose && disposedRegion.Active is null && disposedRegion.Region.Children.Count == 0, "Disposed factory context committed content.");

        var rootGraph = new ReactiveGraph();
        var rootActive = rootGraph.Signal(false, "disposed-root-active");
        using var rootComposition = new Composition(rootGraph, "disposed-root-composition");
        var rootRegion = rootComposition.When(rootComposition.Root, "disposed-root-region", () => rootActive.Value, context =>
        {
            var root = context.Element("disposed-root-child");
            root.Scope.Dispose();
            return root;
        });
        rootGraph.Drain();
        rootActive.Value = true;
        ExpectAggregate(rootGraph.Drain);
        Assert(rootRegion.Active is null && rootRegion.Region.Children.Count == 0, "Disposed conditional root committed content.");

        var keyedGraph = new ReactiveGraph();
        var rows = keyedGraph.Signal(new[] { 1 }, "disposed-keyed-rows");
        using var keyedComposition = new Composition(keyedGraph, "disposed-keyed-root");
        var keyed = keyedComposition.ForEach(keyedComposition.Root, "disposed-keyed-region", () => rows.Value, value => value, (_, context) =>
        {
            var root = context.Element("disposed-keyed-child");
            root.Scope.Dispose();
            return root;
        });
        ExpectAggregate(keyedGraph.Drain);
        Assert(keyed.Items.Count == 0, "Disposed keyed root committed content.");

        var descendantGraph = new ReactiveGraph();
        var descendantActive = descendantGraph.Signal(false, "disposed-descendant-active");
        using var descendantComposition = new Composition(descendantGraph, "disposed-descendant-root");
        var descendantRegion = descendantComposition.When(descendantComposition.Root, "disposed-descendant-region", () => descendantActive.Value, context =>
        {
            var root = context.Element("disposed-descendant-child");
            context.Child(root, "disposed-descendant-leaf").Scope.Dispose();
            return root;
        });
        descendantGraph.Drain();
        descendantActive.Value = true;
        ExpectAggregate(descendantGraph.Drain);
        Assert(descendantRegion.Active is null && descendantRegion.Region.Children.Count == 0, "Disposed factory descendants committed content.");

        var conditionalGraph = new ReactiveGraph();
        var conditionalActive = conditionalGraph.Signal(false, "joint-conditional-active");
        using var conditionalComposition = new Composition(conditionalGraph, "joint-conditional-root");
        _ = conditionalComposition.When(conditionalComposition.Root, "joint-conditional-region", () => conditionalActive.Value, context =>
        {
            var root = context.Element("joint-conditional-child");
            root.Scope.OnDispose(() => throw new InvalidOperationException("conditional-cleanup"));
            throw new InvalidOperationException("conditional-factory");
        });
        conditionalGraph.Drain();
        conditionalActive.Value = true;
        ExpectErrors(CaptureAggregate(conditionalGraph.Drain), "conditional-factory", "conditional-cleanup");

        var failureGraph = new ReactiveGraph();
        var failureRows = failureGraph.Signal(Array.Empty<int>(), "joint-keyed-rows");
        using var failureComposition = new Composition(failureGraph, "joint-keyed-root");
        var failureRegion = failureComposition.ForEach(failureComposition.Root, "joint-keyed-region", () => failureRows.Value, value => value, (value, context) =>
        {
            var root = context.Element("joint-keyed-child");
            if (value == 1)
            {
                root.Scope.OnDispose(() => throw new InvalidOperationException("keyed-provisional-cleanup"));
                return root;
            }
            root.Scope.OnDispose(() => throw new InvalidOperationException("keyed-current-cleanup"));
            throw new InvalidOperationException("keyed-factory");
        });
        failureGraph.Drain();
        failureRows.Value = [1, 2];
        ExpectErrors(CaptureAggregate(failureGraph.Drain), "keyed-factory", "keyed-provisional-cleanup", "keyed-current-cleanup");
        Assert(failureRegion.Items.Count == 0, "Failed keyed factory left provisional content live.");
    }

    private static void PublicFactoryStructuralGuards()
    {
        AssertPublicFactoryGuard(false, "child", composition => composition.Child(composition.Root, "escaped-child"));
        AssertPublicFactoryGuard(false, "when", composition => composition.When(composition.Root, "escaped-when", () => false, context => context.Element("escaped-when-child")));
        AssertPublicFactoryGuard(false, "foreach", composition => composition.ForEach(composition.Root, "escaped-foreach", Array.Empty<int>, value => value, (_, context) => context.Element("escaped-foreach-child")));
        AssertPublicFactoryGuard(true, "child", composition => composition.Child(composition.Root, "escaped-child"));
        AssertPublicFactoryGuard(true, "when", composition => composition.When(composition.Root, "escaped-when", () => false, context => context.Element("escaped-when-child")));
        AssertPublicFactoryGuard(true, "foreach", composition => composition.ForEach(composition.Root, "escaped-foreach", Array.Empty<int>, value => value, (_, context) => context.Element("escaped-foreach-child")));
    }

    private static void AssertPublicFactoryGuard(bool keyed, string api, Action<Composition> bypass)
    {
        var graph = new ReactiveGraph();
        var active = graph.Signal(false, "factory-guard-active-" + api + keyed);
        var rows = graph.Signal(Array.Empty<int>(), "factory-guard-rows-" + api + keyed);
        using var composition = new Composition(graph, "factory-guard-root-" + api + keyed);
        var cleanup = 0;
        Element region;
        Func<CompositionContext, Element> content = context =>
        {
            var root = context.Element("factory-guard-child-" + api + keyed);
            root.Scope.OnDispose(() => cleanup++);
            bypass(composition);
            return root;
        };
        ConditionalRegion? conditional = null;
        KeyedRegion<int, int>? keyedRegion = null;
        if (keyed)
            keyedRegion = composition.ForEach(composition.Root, "factory-guard-keyed-" + api, () => rows.Value, value => value, (_, context) => content(context));
        else
            conditional = composition.When(composition.Root, "factory-guard-conditional-" + api, () => active.Value, content);
        graph.Drain();
        region = keyed ? keyedRegion!.Region : conditional!.Region;
        var before = composition.Dump();
        if (keyed) rows.Value = [1]; else active.Value = true;
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == before && region.Children.Count == 0 && cleanup == 1 &&
            (keyed ? keyedRegion!.Items.Count == 0 : conditional!.Active is null), $"Captured public {api} bypassed { (keyed ? "keyed" : "conditional") } factory rollback.");
    }

    private static void ManualScopeDisposalRetiresEntries()
    {
        var graph = new ReactiveGraph();
        var active = graph.Signal(true, "manual-scope-conditional-active");
        using var composition = new Composition(graph, "manual-scope-conditional-root");
        var cleanup = 0;
        var region = composition.When(composition.Root, "manual-scope-conditional-region", () => active.Value, context =>
        {
            var root = context.Element("manual-scope-conditional-child");
            root.Scope.OnDispose(() => cleanup++);
            return root;
        });
        graph.Drain();
        var retired = region.Active!;
        retired.Scope.Dispose();
        Assert(retired.IsDisposed && region.Active is null && region.Region.Children.Count == 0 && cleanup == 1 &&
            !composition.Dump().Contains("manual-scope-conditional-child", StringComparison.Ordinal), "Disposed conditional scope left active tree or cache state.");
        region.Update(true);
        Assert(region.Active is not null && !ReferenceEquals(retired, region.Active) && !region.Active.Scope.IsDisposed, "Conditional update reused a disposed scope.");

        var keyedGraph = new ReactiveGraph();
        var rows = keyedGraph.Signal(new[] { 1 }, "manual-scope-keyed-rows");
        using var keyedComposition = new Composition(keyedGraph, "manual-scope-keyed-root");
        var keyed = keyedComposition.ForEach(keyedComposition.Root, "manual-scope-keyed-region", () => rows.Value, value => value, (_, context) => context.Element("manual-scope-keyed-child"));
        keyedGraph.Drain();
        var keyedRetired = keyed.Items.Single();
        keyedRetired.Scope.Dispose();
        Assert(keyedRetired.IsDisposed && keyed.Items.Count == 0 && keyed.Region.Children.Count == 0 &&
            !keyedComposition.Dump().Contains("manual-scope-keyed-child", StringComparison.Ordinal), "Disposed keyed scope left tree or cache state.");
        keyed.Update([1]);
        Assert(keyed.Items.Count == 1 && !ReferenceEquals(keyedRetired, keyed.Items[0]) && !keyed.Items[0].Scope.IsDisposed, "Same-key update reused a disposed keyed scope.");

        var probe = RetiredScopePayload();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(!probe.Payload.IsAlive, "Rooted composition retained a scope-disposed element.");
        GC.KeepAlive(probe.Root);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ScopeProbe RetiredScopePayload()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1 }, "manual-scope-release-rows");
        var composition = new Composition(graph, "manual-scope-release-root");
        var keyed = composition.ForEach(composition.Root, "manual-scope-release-region", () => rows.Value, value => value, (_, context) => context.Element("manual-scope-release-child"));
        graph.Drain();
        var retired = keyed.Items.Single();
        var payload = new WeakReference(retired);
        retired.Scope.Dispose();
        keyed.Update([1]);
        return new ScopeProbe(payload, composition);
    }

    private static void KeyedFactoryTransactionsAndReentrancy()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(Array.Empty<int>(), "keyed-transaction-rows");
        using var composition = new Composition(graph, "keyed-transaction-root");
        Element? firstRoot = null;
        Element? firstLeaf = null;
        var cleanup = 0;
        var region = composition.ForEach(composition.Root, "keyed-transaction-region", () => rows.Value, value => value, (value, context) =>
        {
            var root = context.Element("keyed-transaction-child");
            root.Scope.OnDispose(() => cleanup++);
            if (value == 1)
            {
                firstRoot = root;
                firstLeaf = context.Child(root, "keyed-transaction-leaf");
            }
            else if (value == 2) firstLeaf!.Scope.Dispose();
            else firstRoot!.Scope.Dispose();
            return root;
        });
        graph.Drain();
        var before = composition.Dump();
        rows.Value = [1, 2, 3];
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == before && region.Items.Count == 0 && region.Region.Children.Count == 0 && cleanup == 3,
            "Later keyed factories left a disposed provisional root or descendant, or leaked rollback cleanup.");

        var probe = RetainedEntryRollback();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(!probe.Retained.IsAlive && !probe.Provisional.IsAlive, "Failed keyed rollback retained an entry or provisional root.");
        GC.KeepAlive(probe.Root);

        var orderingProbe = KeyedOrderingRollback();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(orderingProbe.Payloads.All(payload => !payload.IsAlive), "Failed keyed ordering retained a completed provisional context.");
        GC.KeepAlive(orderingProbe.Root);

        var reentrantGraph = new ReactiveGraph();
        var reentrantRows = reentrantGraph.Signal(new[] { 1 }, "keyed-reentrant-rows");
        using var reentrantComposition = new Composition(reentrantGraph, "keyed-reentrant-root");
        var reentrant = reentrantComposition.ForEach(reentrantComposition.Root, "keyed-reentrant-region", () => reentrantRows.Value,
            value => value, (_, context) => context.Element("keyed-reentrant-child"));
        reentrantGraph.Drain();
        var departed = reentrant.Items.Single();
        departed.Scope.OnDispose(() => reentrant.Update([1]));
        departed.Scope.Dispose();
        var remounted = reentrant.Items.Single();
        Assert(!ReferenceEquals(departed, remounted) && !remounted.IsDisposed && !remounted.Scope.IsDisposed &&
            reentrant.Region.Children.SequenceEqual([remounted]) &&
            reentrantComposition.Dump().Split('\n').Count(line => line.Contains("keyed-reentrant-child", StringComparison.Ordinal)) == 1,
            "Reentrant same-key cleanup left keyed cache, tree, or dump inconsistent.");
        reentrant.Update([1]);
        Assert(ReferenceEquals(remounted, reentrant.Items.Single()), "An old disposed entry removed its same-key remount.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static KeyedRollbackProbe RetainedEntryRollback()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1 }, "keyed-retained-rows");
        var composition = new Composition(graph, "keyed-retained-root");
        Element? retained = null;
        WeakReference? retainedWeak = null;
        WeakReference? provisionalWeak = null;
        var region = composition.ForEach(composition.Root, "keyed-retained-region", () => rows.Value, value => value, (value, context) =>
        {
            var root = context.Element("keyed-retained-child");
            if (value == 1)
            {
                retained = root;
                retainedWeak = new WeakReference(root);
            }
            else
            {
                provisionalWeak = new WeakReference(root);
                retained!.Scope.Dispose();
                retained = null;
            }
            return root;
        });
        graph.Drain();
        rows.Value = [1, 2];
        ExpectAggregate(graph.Drain);
        Assert(region.Items.Count == 0 && region.Region.Children.Count == 0 &&
            !composition.Dump().Contains("keyed-retained-child", StringComparison.Ordinal), "Disposed retained entry or provisional root remained live after rollback.");
        return new KeyedRollbackProbe(retainedWeak!, provisionalWeak!, composition);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static KeyPreparationProbe KeyedOrderingRollback()
    {
        var graph = new ReactiveGraph();
        var first = new MutableKey(1);
        var second = new MutableKey(2);
        var rows = graph.Signal(new[] { first, second }, "keyed-ordering-rows");
        var weak = new List<WeakReference>();
        var cleanup = 0;
        var composition = new Composition(graph, "keyed-ordering-root");
        var region = composition.ForEach(composition.Root, "keyed-ordering-region", () => rows.Value, value => value, (value, context) =>
        {
            var root = context.Element("keyed-ordering-child");
            weak.Add(new WeakReference(root));
            root.Scope.OnDispose(() => cleanup++);
            if (ReferenceEquals(value, second)) first.Hash = 3;
            return root;
        });
        var before = composition.Dump();
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == before && region.Items.Count == 0 && region.Region.Children.Count == 0 && cleanup == 2,
            "Keyed ordering failure committed or retained provisional content.");
        return new KeyPreparationProbe(weak, composition);
    }

    private static void ReleasedPayload()
    {
        var probe = RemovedPayload();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(probe.Payloads.All(payload => !payload.IsAlive), "Departed composition ownership retained a facet payload after forced GC.");
        GC.KeepAlive(probe.Root);
    }

    private static void ReleasedOwnershipIdentities()
    {
        var probe = ReleasedIdentities();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(!probe.Element.IsAlive && !probe.Conditional.IsAlive && !probe.Keyed.IsAlive, "Active scopes retained manually disposed ownership identities.");
        GC.KeepAlive(probe.Root);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PayloadProbe RemovedPayload()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1 }, "release-rows");
        var weak = new List<WeakReference>();
        var composition = new Composition(graph, "release-root");
        _ = composition.ForEach(composition.Root, "release-region", () => rows.Value, value => value, (item, context) =>
        {
            var effectPayload = new Payload();
            weak.Add(new WeakReference(effectPayload));
            var row = context.Element("release-row");
            _ = row.Scope.Effect(() => GC.KeepAlive(effectPayload), "release-effect");
            var asyncPayload = new Payload();
            weak.Add(new WeakReference(asyncPayload));
            var pending = row.Scope.Async(_ => Task.FromResult(asyncPayload), "release-async");
            _ = pending.Value;
            var genericPayload = new Payload();
            weak.Add(new WeakReference(genericPayload));
            row.Scope.OnDispose(() => GC.KeepAlive(genericPayload));
            return row;
        });
        graph.Drain();
        rows.Value = [];
        graph.Drain();
        return new PayloadProbe(weak, composition);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static IdentityProbe ReleasedIdentities()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "identity-root");
        var element = composition.Child(composition.Root, "manual-element");
        var elementWeak = new WeakReference(element);
        element.Dispose();

        var conditional = composition.When(composition.Root, "manual-conditional", () => false, context => context.Element("unused"));
        graph.Drain();
        var conditionalWeak = new WeakReference(conditional);
        conditional.Dispose();

        var keyed = composition.ForEach(composition.Root, "manual-keyed", Array.Empty<int>, value => value, (_, context) => context.Element("unused-item"));
        graph.Drain();
        var keyedWeak = new WeakReference(keyed);
        keyed.Dispose();
        return new IdentityProbe(elementWeak, conditionalWeak, keyedWeak, composition);
    }

    private static string EquivalentDump()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "dump-root");
        var header = composition.Child(composition.Root, "dump-header");
        _ = composition.Child(header, "dump-title");
        return composition.Dump();
    }

    private static void ExpectAggregate(Action action)
    {
        try { action(); throw new InvalidOperationException("Expected aggregate failure."); }
        catch (AggregateException) { }
    }

    private static AggregateException CaptureAggregate(Action action)
    {
        try { action(); throw new InvalidOperationException("Expected aggregate failure."); }
        catch (AggregateException exception) { return exception; }
    }

    private static void ExpectErrors(AggregateException exception, params string[] messages)
    {
        var actual = exception.Flatten().InnerExceptions.Select(error => error.Message).ToArray();
        Assert(actual.SequenceEqual(messages), "Expected exact errors: " + string.Join(", ", messages) + "; actual: " + string.Join(", ", actual));
    }

    private static void ExpectDisposed(Action action)
    {
        try { action(); throw new InvalidOperationException("Expected disposed failure."); }
        catch (ObjectDisposedException) { }
    }

    private static void ExpectNotSupported(Action action)
    {
        try { action(); throw new InvalidOperationException("Expected immutable view failure."); }
        catch (NotSupportedException) { }
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed record PayloadProbe(IReadOnlyList<WeakReference> Payloads, object Root);
    private sealed record IdentityProbe(WeakReference Element, WeakReference Conditional, WeakReference Keyed, object Root);
    private sealed record ScopeProbe(WeakReference Payload, object Root);
    private sealed record KeyedRollbackProbe(WeakReference Retained, WeakReference Provisional, object Root);
    private sealed record KeyPreparationProbe(IReadOnlyList<WeakReference> Payloads, object Root);
    private sealed class MutableKey(int hash)
    {
        internal int Hash { get; set; } = hash;
        public override int GetHashCode() => Hash;
    }
    private sealed class Payload;
}
