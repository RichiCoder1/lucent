using System.Globalization;
using Lucent.Core;

internal static class PresentationContracts
{
    private static readonly Property<int> Value = new("value", 1);
    private static readonly Property<int> Inherited = new("inherited", 2, inherits: true);
    private static readonly Property<float> Opacity = new("opacity", 1, transition: TransitionKind.Opacity);
    private static readonly Property<int> TokenAValue = new("token-a", 0);
    private static readonly Property<int> TokenBValue = new("token-b", 0);
    private static readonly Token<int> Accent = new("accent", 10);
    private static readonly Token<int> AccentB = new("accent-b", 20);
    private static readonly Token<int> FallbackAccent = new("fallback-accent", 10);
    private static readonly Token<int> MaliciousToken = new("bad\r\n:#[]", 1);
    private static readonly Property<int> FallbackValue = new("fallback-value", 0);

    public static int Run()
    {
        try
        {
            StylesTransitionsAndDependencies();
            BindingsRespectVariantsAndControlAuthority();
            BindingRowScaleLifecycle();
            BehaviorIsolationAndRollback();
            SemanticsAndDumps();
            Console.WriteLine("Lucent.Core presentation contracts: PASS");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Lucent.Core presentation contracts: FAIL: " + error.Message); return 1; }
    }

    private static void StylesTransitionsAndDependencies()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "styles");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("light").Set(Accent, 10));
        composition.Root.Present(theme, author: Style.Empty.Set(Inherited, 9));
        var middle = composition.Child(composition.Root, "middle");
        var child = composition.Child(middle, "child");
        var nested = Style.Compose(Style.Empty.Set(Value, 2).When(VariantState.Hover, Style.Empty.Set(Value, 3)), Style.Empty.When(VariantState.Selected, Style.Empty.When(VariantState.Pressed, Style.Empty.Set(Value, 4))));
        var author = Style.Empty.Set(Value, 5).When(VariantState.Hover | VariantState.Selected, Style.Empty.Set(Value, 6));
        child.Present(theme, nested, author, Transition.For(Opacity, 100));
        child.SetVariants(VariantState.Hover | VariantState.Selected | VariantState.Pressed);
        var resolved = child.Resolve(Value);
        Assert(resolved.Value == 4 && resolved.Winner.Source == "component" && resolved.Winner.Condition == (VariantState.Selected | VariantState.Pressed), "Nested styles did not normalize by cardinality/vector/component order.");
        Assert(resolved.Overridden.Select(item => item.Source + item.Ordinal).Distinct().Count() == resolved.Overridden.Count, "Repeated candidates lost deterministic source/ordinals.");
        Assert(child.Resolve(Inherited).Value == 9, "Inheritance did not cross an unpresented intermediate element.");
        var childDump = composition.Dump(); var childStart = childDump.IndexOf("name=\"child\"", StringComparison.Ordinal); var childEnd = childDump.IndexOf("element ", childStart + 1, StringComparison.Ordinal);
        Assert(childStart >= 0 && childDump[childStart..(childEnd < 0 ? childDump.Length : childEnd)].Contains("property name=\"inherited\" winner=\"inherited\"", StringComparison.Ordinal), "Child dump omitted inherited provenance through an unpresented ancestor.");
        var frozen = composition.Child(composition.Root, "frozen");
        var states = new[] { VariantState.Hover, VariantState.FocusVisible, VariantState.Selected, VariantState.Pressed, VariantState.Invalid, VariantState.Disabled };
        var frozenStyle = states.Select((state, index) => Style.Empty.When(state, Style.Empty.Set(Value, index + 2))).Aggregate(Style.Empty, (left, right) => Style.Compose(left, right));
        frozen.Present(theme, frozenStyle); frozen.SetVariants(states.Aggregate(VariantState.None, (all, state) => all | state));
        Assert(frozen.Resolve(Value).Value == 7, "Frozen six-state order did not prefer Disabled.");
        var equal = composition.Child(composition.Root, "equal");
        equal.Present(theme, Style.Empty.When(VariantState.Hover | VariantState.Selected, Style.Empty.Set(Value, 2)).When(VariantState.Hover | VariantState.Pressed, Style.Empty.Set(Value, 3)));
        equal.SetVariants(VariantState.Hover | VariantState.Selected | VariantState.Pressed);
        Assert(equal.Resolve(Value).Value == 3, "Equal-cardinality compound vector order was not deterministic.");
        var transitionRuns = 0;
        var transition = composition.Root.Scope.Derived(() => { transitionRuns++; return child.Resolve(Opacity).Value; }, "transition-slot");
        Assert(transition.Value == 1f && transitionRuns == 1, "Transition reader did not resolve before a sample.");
        child.StartTransition(Opacity, .4f);
        Assert(transition.Value == .4f && transitionRuns == 2, "Transition start did not invalidate a prior derived reader.");
        composition.AdvanceTransitions(50);
        Assert(transition.Value == .4f && transitionRuns == 2, "Non-expiring transition advance created work.");
        composition.AdvanceTransitions(50);
        Assert(transition.Value == 1f && transitionRuns == 3, "Transition expiry did not invalidate a derived reader.");
        child.StartTransition(Opacity, .5f); theme.ReducedMotion = true;
        Assert(child.Resolve(Opacity).SuppressedTransition?.Source == "transition-suppressed" && child.Resolve(Opacity).Winner.Source == "default", "Reduced motion did not suppress the active sample.");
        var appearanceRuns = 0;
        var appearance = composition.Root.Scope.Derived(() => { appearanceRuns++; return theme.Appearance; }, "appearance-reader");
        Assert(appearance.Value == ThemeAppearance.Light && appearanceRuns == 1, "Initial portable appearance was not light/normal.");
        theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.High); graph.Drain();
        Assert(appearance.Value == new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.High) && appearanceRuns == 2, "Appearance did not invalidate its own readers.");
        theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.High); graph.Drain();
        Assert(appearanceRuns == 2, "Equal appearance assignment invalidated readers.");
        Expect<ArgumentException>(() => theme.Appearance = new((ThemeColorScheme)99, ThemeContrast.Normal));
        var tokenA = composition.Child(composition.Root, "token-a"); var tokenB = composition.Child(composition.Root, "token-b");
        tokenA.Present(theme, author: Style.Empty.Set(TokenAValue, Accent)); tokenB.Present(theme, author: Style.Empty.Set(TokenBValue, AccentB));
        var tokenRuns = 0; var directRuns = 0;
        var token = composition.Root.Scope.Derived(() => { tokenRuns++; return tokenA.Resolve(TokenAValue).Value; }, "token-a-reader");
        var direct = composition.Root.Scope.Derived(() => { directRuns++; return tokenB.Resolve(TokenBValue).Value; }, "token-b-reader");
        Assert(token.Value == 10 && direct.Value == 20, "Token slots did not resolve typed values.");
        theme.Theme = theme.Theme.Set(Accent, 11); graph.Drain();
        Assert(token.Value == 11 && tokenRuns == 2 && direct.Value == 20 && directRuns == 1, "Theme replacement invalidated an unrelated token slot: " + tokenRuns + "/" + directRuns);
        theme.Theme = theme.Theme.Set(Accent, 11); graph.Drain();
        Assert(tokenRuns == 2 && directRuns == 1, "No-op token update invalidated readers.");
        var fallback = composition.Child(composition.Root, "fallback-token");
        fallback.Present(theme, author: Style.Empty.Set(FallbackValue, FallbackAccent));
        var fallbackRuns = 0;
        var fallbackReader = composition.Root.Scope.Derived(() => { fallbackRuns++; return fallback.Resolve(FallbackValue); }, "fallback-reader");
        Assert(fallbackReader.Value.Winner.Source.Contains("fallback", StringComparison.Ordinal) && fallbackRuns == 1, "Initial fallback provenance was not reported.");
        theme.Theme = theme.Theme.Set(FallbackAccent, 10); graph.Drain();
        Assert(fallbackReader.Value.Winner.Source.Contains("theme", StringComparison.Ordinal) && fallbackRuns == 2, "Same-value fallback-to-theme update did not invalidate provenance.");
        var maliciousToken = composition.Child(composition.Root, "malicious-token");
        maliciousToken.Present(theme, author: Style.Empty.Set(TokenAValue, MaliciousToken));
        var maliciousDump = composition.Dump();
        Assert(maliciousDump.Contains("token:bad\\r\\n:#[]", StringComparison.Ordinal) && maliciousDump.Split('\n').Count(line => line.Contains("token:bad", StringComparison.Ordinal)) == 1, "Token provenance injected a diagnostic line.");
        Expect<ArgumentException>(() => child.SetVariants((VariantState)64));
        var duplicate = composition.Child(composition.Root, "duplicate");
        Expect<ArgumentException>(() => duplicate.Present(theme, author: Style.Empty.Set(Value, 1).Set(new Property<int>("value", 0), 2)));
        var transitions = composition.Child(composition.Root, "duplicate-transitions"); var beforeTransitions = graph.Dump();
        Expect<ArgumentException>(() => transitions.Present(theme, transitions: [Transition.For(Opacity, 1), Transition.For(Opacity, 2)]));
        Assert(graph.Dump() == beforeTransitions, "Duplicate transition validation created graph nodes.");
        Expect<ArgumentException>(() => new Property<int>("bad-transition", 0, transition: (TransitionKind)99));
        Expect<ArgumentException>(() => new SemanticDeclaration((SemanticRole)99, "bad"));
        Expect<ArgumentException>(() => new SemanticDeclaration(SemanticRole.Text, "bad", actions: (SemanticAction)32));
    }

    private static void BehaviorIsolationAndRollback()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "behavior");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var element = composition.Child(composition.Root, "target"); element.Present(theme);
        var cleanup = 0;
        var missing = new Probe("missing", BehaviorOwnership.Semantics, context => context.OnDispose(() => cleanup++));
        Expect<InvalidOperationException>(() => element.AttachBehaviors(missing));
        Assert(cleanup == 1 && !composition.Dump().Contains("missing", StringComparison.Ordinal), "Missing semantics did not rollback cleanup/state.");
        var beforeGuard = composition.Dump(); var beforeGraph = graph.Dump();
        Expect<InvalidOperationException>(() => element.AttachBehaviors(new Probe("variant-escape", BehaviorOwnership.None, _ => element.SetVariants(VariantState.Selected))));
        Expect<InvalidOperationException>(() => element.AttachBehaviors(new Probe("theme-escape", BehaviorOwnership.None, _ => theme.Theme = new Theme("changed"))));
        Expect<InvalidOperationException>(() => element.AttachBehaviors(new Probe("motion-escape", BehaviorOwnership.None, _ => theme.ReducedMotion = true)));
        Expect<InvalidOperationException>(() => element.AttachBehaviors(new Probe("dispose-theme-escape", BehaviorOwnership.None, _ => theme.Dispose())));
        Assert(composition.Dump() == beforeGuard && graph.Dump() == beforeGraph && !theme.ReducedMotion, "Failed behavior attachment changed presentation/theme state or graph.");
        var active = composition.Root.Scope.Signal(false, "captured-region-active");
        CompositionContext? captured = null;
        Element? capturedRoot = null;
        var conditional = composition.When(composition.Root, "captured-conditional", () => active.Value, context => { captured = context; return capturedRoot = context.Element("captured-child"); });
        var rows = composition.Root.Scope.Signal(new[] { 1 }, "captured-rows");
        var keyed = composition.ForEach(composition.Root, "captured-keyed", () => rows.Value, value => value, (_, context) => { captured ??= context; return context.Element("captured-keyed-child"); });
        active.Value = true; graph.Drain();
        var structuralDump = composition.Dump(); var structuralGraph = graph.Dump();
        foreach (var action in new Action[]
        {
            () => conditional.Update(false), conditional.Refresh, conditional.Dispose,
            () => keyed.Update([1]), keyed.Refresh, keyed.Dispose,
            () => captured!.Element("captured-late"), () => captured!.Child(capturedRoot!, "captured-late-child"), captured!.Dispose
        })
        {
            Expect<InvalidOperationException>(() => element.AttachBehaviors(new Probe("captured", BehaviorOwnership.None, _ => action())));
            Assert(composition.Dump() == structuralDump && graph.Dump() == structuralGraph, "Behavior attachment mutated captured structure.");
        }
        foreach (var cleanupAction in new Action[] { () => conditional.Update(false), () => keyed.Update([1]), captured!.Dispose })
        {
            Expect<AggregateException>(() => element.AttachBehaviors(new Probe("cleanup-captured", BehaviorOwnership.None, context => context.OnDispose(cleanupAction)), new Probe("cleanup-rollback", BehaviorOwnership.Semantics, _ => { })));
            Assert(composition.Dump() == structuralDump && graph.Dump() == structuralGraph, "Behavior cleanup mutated captured structure.");
        }
        foreach (var action in new Action[]
        {
            () => composition.Child(element, "escaped"), () => element.Present(theme), () => element.AttachBehaviors(missing), () => element.Dispose(), () => element.Scope.Dispose(), () => composition.Dispose()
        })
            Expect<InvalidOperationException>(() => element.AttachBehaviors(new Probe("escape", BehaviorOwnership.None, _ => action())));
        var cleanupStructure = new Probe("cleanup", BehaviorOwnership.None, context => context.OnDispose(() => composition.Child(composition.Root, "cleanup-escape")));
        Expect<AggregateException>(() => element.AttachBehaviors(cleanupStructure, new Probe("rollback", BehaviorOwnership.Semantics, _ => { })));
        Assert(!composition.Dump().Contains("cleanup-escape", StringComparison.Ordinal), "Behavior cleanup changed structure.");
        element.AttachBehaviors(new Probe("action", BehaviorOwnership.Action | BehaviorOwnership.Semantics, context => context.SetSemantics(new(SemanticRole.Button, "Action", actions: SemanticAction.Invoke))));
        Expect<InvalidOperationException>(() => element.AttachBehaviors(new Probe("action2", BehaviorOwnership.Action | BehaviorOwnership.Semantics, context => context.SetSemantics(new(SemanticRole.Button, "Second", actions: SemanticAction.Invoke)))));
        var beforeOwnership = graph.Dump();
        Expect<ArgumentException>(() => element.AttachBehaviors(new Probe("bad-owner", (BehaviorOwnership)32, _ => { })));
        Assert(graph.Dump() == beforeOwnership, "Bad ownership validation created behavior graph nodes.");
        var malicious = composition.Child(composition.Root, "malicious"); malicious.Present(theme); malicious.AttachBehaviors(new Probe("evil\r\nname", BehaviorOwnership.None, _ => { }));
        Assert(composition.Dump().Contains("behavior name=\"evil\\r\\nname\"", StringComparison.Ordinal), "Behavior name was not CR/LF escaped.");
        var probe = Released(); ForceGc(); Assert(!probe.Payload.IsAlive, "Behavior delegates remained rooted after rollback/disposal."); GC.KeepAlive(probe.Root);
    }

    private static void BindingsRespectVariantsAndControlAuthority()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "bindings"); var theme = new ThemeContext(composition.Root.Scope, new Theme("bindings"));
        var source = composition.Root.Scope.Signal(2, "bound-value"); var reads = 0;
        var bound = composition.Child(composition.Root, "bound");
        bound.Present(theme, author: Style.Empty.When(VariantState.Hover, Style.Empty.Bind(Value, () => { reads++; return source.Value; })));
        graph.Drain();
        Assert(reads == 0 && bound.Resolve(Value).Value == 1, "Inactive binding evaluated or changed its candidate.");
        bound.SetVariants(VariantState.Hover); graph.Drain();
        Assert(reads == 1 && bound.Resolve(Value) is { Value: 2, Winner: { Source: "author", Condition: VariantState.Hover } }, "Active binding lost author provenance or variant condition.");
        source.Value = 3; graph.Drain();
        Assert(reads == 2 && bound.Resolve(Value).Value == 3, "Active binding did not track its expression.");
        bound.SetVariants(VariantState.None); graph.Drain(); source.Value = 4; graph.Drain();
        Assert(reads == 2 && bound.Resolve(Value).Value == 1, "Inactive binding retained a live dependency.");

        var precedence = composition.Child(composition.Root, "binding-precedence");
        precedence.Present(theme, Style.Empty.Set(Value, 7).Bind(Value, () => 8), Style.Empty.Set(Value, 9).Bind(Value, () => 10)); graph.Drain();
        var precedenceValue = precedence.Resolve(Value);
        Assert(precedenceValue.Value == 10 && precedenceValue.Winner is { Source: "author", Ordinal: 1 } && precedenceValue.Overridden.Any(item => item is { Source: "component", Ordinal: 1 }), "Binding candidates lost component/author or ordinal precedence.");

        var recover = composition.Root.Scope.Signal(true, "binding-failure"); var failed = composition.Child(composition.Root, "binding-failure");
        failed.Present(theme, author: Style.Empty.Set(Value, 6).Bind(Value, () => recover.Value ? throw new InvalidOperationException("binding read") : 11));
        Expect<AggregateException>(graph.Drain);
        var fallback = failed.Resolve(Value);
        Assert(fallback is { Value: 6, Winner: { Source: "author", Ordinal: 0 } } && fallback.Overridden.All(item => item.Ordinal != 1), "First failed binding published its uncommitted default candidate.");
        recover.Value = false; graph.Drain();
        Assert(failed.Resolve(Value) is { Value: 11, Winner: { Source: "author", Ordinal: 1 } }, "Recovered binding did not publish its first successful value.");
        recover.Value = true; Expect<AggregateException>(graph.Drain);
        Assert(failed.Resolve(Value) is { Value: 11, Winner: { Source: "author", Ordinal: 1 } }, "Later failed binding discarded its prior successful value.");

        var controlled = composition.Child(composition.Root, "controlled");
        var state = Controls.Loading(controlled, theme, "Loading", Style.Empty.Bind(SceneProperties.Text, () => "bound"));
        graph.Drain(); state.Label = "Ready"; graph.Drain();
        var resolved = controlled.Resolve(SceneProperties.Text);
        Assert(resolved.Value == "Ready" && resolved.Winner.Source == "control" && resolved.Overridden.Any(item => item.Source == "author"), "Control state did not override the bound author candidate with retained provenance.");
        var lateReads = reads; bound.Dispose(); source.Value = 5; graph.Drain();
        Assert(reads == lateReads, "Disposed binding accepted a late expression callback.");
        var released = ReleasedBinding(); ForceGc(); Assert(!released.Payload.IsAlive, "Disposed binding retained its callback payload."); GC.KeepAlive(released.Root);
    }

    private static void BindingRowScaleLifecycle()
    {
        var graph = new ReactiveGraph(); var composition = new Composition(graph, "binding-row-scale"); var theme = new ThemeContext(composition.Root.Scope, new Theme("binding-row-scale")); var value = composition.Root.Scope.Signal(1, "binding-row-value");
        const int rows = 64; var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < rows; index++)
        {
            var row = composition.Child(composition.Root, "binding-row-" + index); row.Present(theme, author: Style.Empty.Bind(Value, () => value.Value));
        }
        graph.Drain(); var allocated = GC.GetAllocatedBytesForCurrentThread() - before; var topology = graph.Dump();
        Assert(topology.Split("bind-effect", StringSplitOptions.None).Length - 1 == rows && allocated is > 0 and < 1_000_000, "Bound row-scale topology or allocation exceeded the bounded representative datapoint.");
        composition.Dispose(); Assert(graph.Dump() == "reactive-graph\n", "Disposed bound rows did not return their graph topology.");
    }

    private static void SemanticsAndDumps()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "semantic\r\nroot");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        Assert(composition.SemanticSnapshot() is null, "Empty semantic tree was not null.");
        var one = composition.Child(composition.Root, "one"); one.Present(theme); one.AttachBehaviors(Semantics("one", "one"));
        var snapshot = composition.SemanticSnapshot()!;
        Assert(snapshot.Identity.ElementId == composition.Root.Id && snapshot.Children.Count == 1 && composition.IsCurrent(snapshot.Identity), "Synthetic semantic root did not retain one child identity.");
        var many = composition.Child(composition.Root, "many"); many.Present(theme); many.AttachBehaviors(Semantics("many", "many"));
        snapshot = composition.SemanticSnapshot()!;
        Assert(snapshot.Children.Count == 2 && !composition.IsCurrent(new SemanticIdentity(snapshot.Identity.CompositionEpoch + 1, snapshot.Identity.ElementId, snapshot.Identity.Generation)), "Forged/cross-composition semantic identity was accepted.");
        var childIdentity = snapshot.Children.Single(node => node.Identity.ElementId == many.Id).Identity;
        many.Dispose();
        Assert(!composition.IsCurrent(childIdentity), "Disposed child semantic identity remained current.");
        var synthetic = snapshot.Identity;
        composition.Root.AttachBehaviors(Semantics("root", "root"));
        snapshot = composition.SemanticSnapshot()!;
        Assert(snapshot.Identity.ElementId == composition.Root.Id && snapshot.Identity.Generation == 1 && !composition.IsCurrent(synthetic), "Authored root semantics duplicated the synthetic root or accepted stale identity.");
        composition.Dispose();
        Assert(!composition.IsCurrent(snapshot.Identity), "Disposed semantic identity remained current.");
        var firstCulture = Dump(); var original = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); var secondCulture = Dump(); CultureInfo.CurrentCulture = original;
        Assert(firstCulture == secondCulture && firstCulture.Contains("\\r", StringComparison.Ordinal) && !firstCulture.Contains("secret", StringComparison.OrdinalIgnoreCase), "Dump was culture-sensitive, unsafe, or leaked semantic values.");
    }

    private static string Dump()
    {
        var graph = new ReactiveGraph(); using var composition = new Composition(graph, "dump\r\nroot"); var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        composition.Root.Present(theme, author: Style.Empty.Set(Value, 4)); composition.Root.AttachBehaviors(Semantics("dump", "secret")); return composition.Dump();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ProbeResult Released()
    {
        var graph = new ReactiveGraph(); var composition = new Composition(graph, "release"); var theme = new ThemeContext(composition.Root.Scope, new Theme("theme")); var payload = new Payload(); var weak = new WeakReference(payload);
        var element = composition.Child(composition.Root, "release-target"); element.Present(theme); element.AttachBehaviors(new Probe("release", BehaviorOwnership.None, context => context.OnDispose(() => GC.KeepAlive(payload)))); element.Dispose(); return new(weak, composition);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ProbeResult ReleasedBinding()
    {
        var graph = new ReactiveGraph(); var composition = new Composition(graph, "release-binding"); var theme = new ThemeContext(composition.Root.Scope, new Theme("theme")); var payload = new Payload(); var weak = new WeakReference(payload);
        var element = composition.Child(composition.Root, "release-binding-target"); element.Present(theme, author: Style.Empty.Bind(Value, () => { GC.KeepAlive(payload); return 1; })); graph.Drain(); element.Dispose(); return new(weak, composition);
    }
    private static Probe Semantics(string id, string name) => new(id, BehaviorOwnership.Semantics, context => context.SetSemantics(new(SemanticRole.Text, name)));
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void ForceGc() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private sealed class Probe(string name, BehaviorOwnership ownership, Action<BehaviorContext> attach) : Behavior { public override string Name => name; public override BehaviorOwnership Ownership => ownership; public override void Attach(BehaviorContext context) => attach(context); }
    private sealed record ProbeResult(WeakReference Payload, object Root); private sealed class Payload;
}
