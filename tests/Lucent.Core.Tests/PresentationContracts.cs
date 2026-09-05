using System.Globalization;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class PresentationContracts
{
    private static readonly Property<int> Value = new("value", 1);
    private static readonly Property<int> Inherited = new("inherited", 2, inherits: true);
    private static readonly Property<int> TokenAValue = new("token-a", 0);
    private static readonly Property<int> TokenBValue = new("token-b", 0);
    private static readonly Token<int> Accent = new("accent", 10);
    private static readonly Token<int> AccentB = new("accent-b", 20);
    private static readonly Token<int> FallbackAccent = new("fallback-accent", 10);
    private static readonly Token<int> FactoryAccent = new("factory-accent", 10);
    private static readonly Token<int> MaliciousToken = new("bad\r\n:#[]", 1);
    private static readonly Property<int> FallbackValue = new("fallback-value", 0);

    [TestMethod]
    public void StylesTransitionsAndDependencies()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "styles");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("light").Set(Accent, 10));
        composition.Root.Present(theme, author: Style.Empty.Set(Inherited, 9));
        var middle = composition.Child(composition.Root, "middle");
        var child = composition.Child(middle, "child");
        var nested = Style.Compose(
            Style.Empty.Set(Value, 2).When(VariantState.Hover, Style.Empty.Set(Value, 3)),
            Style.Empty.When(
                VariantState.Selected,
                Style.Empty.When(VariantState.Pressed, Style.Empty.Set(Value, 4))
            )
        );
        var author = Style
            .Empty.Set(Value, 5)
            .When(VariantState.Hover | VariantState.Selected, Style.Empty.Set(Value, 6));
        child.Present(theme, nested, author, Transition.For(VisualProperties.Opacity, 100));
        child.SetVariants(VariantState.Hover | VariantState.Selected | VariantState.Pressed);
        var resolved = child.Resolve(Value);
        Assert(
            resolved.Value == 4
                && resolved.Winner.Source == "component"
                && resolved.Winner.Condition == (VariantState.Selected | VariantState.Pressed),
            "Nested styles did not normalize by cardinality/vector/component order."
        );
        Assert(
            resolved.Overridden.Select(item => item.Source + item.Ordinal).Distinct().Count()
                == resolved.Overridden.Count,
            "Repeated candidates lost deterministic source/ordinals."
        );
        Assert(
            child.Resolve(Inherited).Value == 9,
            "Inheritance did not cross an unpresented intermediate element."
        );
        var childDump = composition.Dump();
        var childStart = childDump.IndexOf("name=\"child\"", StringComparison.Ordinal);
        var childEnd = childDump.IndexOf("element ", childStart + 1, StringComparison.Ordinal);
        Assert(
            childStart >= 0
                && childDump[childStart..(childEnd < 0 ? childDump.Length : childEnd)]
                    .Contains(
                        "property name=\"inherited\" winner=\"inherited\"",
                        StringComparison.Ordinal
                    ),
            "Child dump omitted inherited provenance through an unpresented ancestor."
        );
        var frozen = composition.Child(composition.Root, "frozen");
        var states = new[]
        {
            VariantState.Hover,
            VariantState.FocusVisible,
            VariantState.Selected,
            VariantState.Pressed,
            VariantState.Invalid,
            VariantState.Disabled,
        };
        var frozenStyle = states
            .Select((state, index) => Style.Empty.When(state, Style.Empty.Set(Value, index + 2)))
            .Aggregate(Style.Empty, (left, right) => Style.Compose(left, right));
        frozen.Present(theme, frozenStyle);
        frozen.SetVariants(states.Aggregate(VariantState.None, (all, state) => all | state));
        Assert(frozen.Resolve(Value).Value == 7, "Frozen six-state order did not prefer Disabled.");
        var equal = composition.Child(composition.Root, "equal");
        equal.Present(
            theme,
            Style
                .Empty.When(VariantState.Hover | VariantState.Selected, Style.Empty.Set(Value, 2))
                .When(VariantState.Hover | VariantState.Pressed, Style.Empty.Set(Value, 3))
        );
        equal.SetVariants(VariantState.Hover | VariantState.Selected | VariantState.Pressed);
        Assert(
            equal.Resolve(Value).Value == 3,
            "Equal-cardinality compound vector order was not deterministic."
        );
        var transitionRuns = 0;
        var transition = composition.Root.Scope.Derived(
            () =>
            {
                transitionRuns++;
                return child.Resolve(VisualProperties.Opacity).Value;
            },
            "transition-slot"
        );
        Assert(
            transition.Value == 1f && transitionRuns == 1,
            "Transition reader did not resolve before a sample."
        );
        child.StartTransition(VisualProperties.Opacity, .4f);
        Assert(
            transition.Value == .4f && transitionRuns == 2,
            "Transition start did not invalidate a prior derived reader."
        );
        composition.AdvanceTransitions(50);
        Assert(
            transition.Value == .4f && transitionRuns == 2,
            "Non-expiring transition advance created work."
        );
        composition.AdvanceTransitions(50);
        Assert(
            transition.Value == 1f && transitionRuns == 3,
            "Transition expiry did not invalidate a derived reader."
        );
        child.StartTransition(VisualProperties.Opacity, .5f);
        theme.ReducedMotion = true;
        Assert(
            child.Resolve(VisualProperties.Opacity).SuppressedTransition?.Source
                == "transition-suppressed"
                && child.Resolve(VisualProperties.Opacity).Winner.Source == "default",
            "Reduced motion did not suppress the active sample."
        );
        var appearanceRuns = 0;
        var appearance = composition.Root.Scope.Derived(
            () =>
            {
                appearanceRuns++;
                return theme.Appearance;
            },
            "appearance-reader"
        );
        Assert(
            appearance.Value == ThemeAppearance.Light && appearanceRuns == 1,
            "Initial portable appearance was not light/normal."
        );
        theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.High);
        graph.Drain();
        Assert(
            appearance.Value == new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.High)
                && appearanceRuns == 2,
            "Appearance did not invalidate its own readers."
        );
        theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.High);
        graph.Drain();
        Assert(appearanceRuns == 2, "Equal appearance assignment invalidated readers.");
        Expect<ArgumentException>(() =>
            theme.Appearance = new((ThemeColorScheme)99, ThemeContrast.Normal)
        );
        var tokenA = composition.Child(composition.Root, "token-a");
        var tokenB = composition.Child(composition.Root, "token-b");
        tokenA.Present(theme, author: Style.Empty.Set(TokenAValue, Accent));
        tokenB.Present(theme, author: Style.Empty.Set(TokenBValue, AccentB));
        var tokenRuns = 0;
        var directRuns = 0;
        var token = composition.Root.Scope.Derived(
            () =>
            {
                tokenRuns++;
                return tokenA.Resolve(TokenAValue).Value;
            },
            "token-a-reader"
        );
        var direct = composition.Root.Scope.Derived(
            () =>
            {
                directRuns++;
                return tokenB.Resolve(TokenBValue).Value;
            },
            "token-b-reader"
        );
        Assert(
            token.Value == 10 && direct.Value == 20,
            "Token slots did not resolve typed values."
        );
        theme.Theme = theme.Theme.Set(Accent, 11);
        graph.Drain();
        Assert(
            token.Value == 11 && tokenRuns == 2 && direct.Value == 20 && directRuns == 1,
            "Theme replacement invalidated an unrelated token slot: " + tokenRuns + "/" + directRuns
        );
        theme.Theme = theme.Theme.Set(Accent, 11);
        graph.Drain();
        Assert(tokenRuns == 2 && directRuns == 1, "No-op token update invalidated readers.");
        var fallback = composition.Child(composition.Root, "fallback-token");
        fallback.Present(theme, author: Style.Empty.Set(FallbackValue, FallbackAccent));
        var fallbackRuns = 0;
        var fallbackReader = composition.Root.Scope.Derived(
            () =>
            {
                fallbackRuns++;
                return fallback.Resolve(FallbackValue);
            },
            "fallback-reader"
        );
        Assert(
            fallbackReader.Value.Winner.Source.Contains("fallback", StringComparison.Ordinal)
                && fallbackRuns == 1,
            "Initial fallback provenance was not reported."
        );
        theme.Theme = theme.Theme.Set(FallbackAccent, 10);
        graph.Drain();
        Assert(
            fallbackReader.Value.Winner.Source.Contains("theme", StringComparison.Ordinal)
                && fallbackRuns == 2,
            "Same-value fallback-to-theme update did not invalidate provenance."
        );
        var maliciousToken = composition.Child(composition.Root, "malicious-token");
        maliciousToken.Present(theme, author: Style.Empty.Set(TokenAValue, MaliciousToken));
        var maliciousDump = composition.Dump();
        Assert(
            maliciousDump.Contains("token:bad\\r\\n:#[]", StringComparison.Ordinal)
                && maliciousDump
                    .Split('\n')
                    .Count(line => line.Contains("token:bad", StringComparison.Ordinal)) == 1,
            "Token provenance injected a diagnostic line."
        );
        Expect<ArgumentException>(() => child.SetVariants((VariantState)64));
        var duplicate = composition.Child(composition.Root, "duplicate");
        Expect<ArgumentException>(() =>
            duplicate.Present(
                theme,
                author: Style.Empty.Set(Value, 1).Set(new Property<int>("value", 0), 2)
            )
        );
        var transitions = composition.Child(composition.Root, "duplicate-transitions");
        var beforeTransitions = graph.Dump();
        Expect<ArgumentException>(() =>
            transitions.Present(
                theme,
                transitions:
                [
                    Transition.For(VisualProperties.Opacity, 1),
                    Transition.For(VisualProperties.Opacity, 2),
                ]
            )
        );
        Assert(
            graph.Dump() == beforeTransitions,
            "Duplicate transition validation created graph nodes."
        );
        Expect<ArgumentException>(() =>
            new Property<int>("bad-transition", 0, transition: (TransitionKind)99)
        );
        Assert(
            TypographyProperties.TextColor.Transition == TransitionKind.Color
                && VisualProperties.Background.Transition == TransitionKind.None
                && VisualProperties.Opacity.Transition == TransitionKind.Opacity
                && !VisualProperties.Opacity.Inherits
                && VisualProperties.Opacity.DefaultValue == 1f,
            "Shipped visual transition/default eligibility changed."
        );
        _ = new Property<Color>(
            "valid-color-transition",
            default,
            transition: TransitionKind.Color
        );
        Expect<ArgumentException>(() =>
            new Property<uint>("legacy-color-transition", 0, transition: TransitionKind.Color)
        );
        Expect<ArgumentException>(() =>
            new Property<Brush>(
                "brush-color-transition",
                Brush.Solid(default),
                transition: TransitionKind.Color
            )
        );
        Expect<ArgumentException>(() => new SemanticDeclaration((SemanticRole)99, "bad"));
        Expect<ArgumentException>(() =>
            new SemanticDeclaration(SemanticRole.Text, "bad", actions: (SemanticAction)128)
        );
    }

    [TestMethod]
    public void TokenFactoryRollback()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "token-factory");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("token-factory-theme"));
        using var unrelatedScope = graph.CreateScope("unrelated-theme-scope");
        var unrelatedTheme = new ThemeContext(unrelatedScope, new Theme("unrelated-token-theme"));
        Expect<ArgumentException>(() =>
            composition.Mount(
                composition.Root,
                unrelatedTheme,
                ComponentRecipe.Create("foreign-theme", (_, _) => { })
            )
        );
        var baseline = graph.Dump();
        Expect<ArgumentException>(() =>
            composition.Mount(
                composition.Root,
                theme,
                context =>
                {
                    var failed = context.Element("captured-foreign-theme");
                    failed.Present(
                        unrelatedTheme,
                        author: Style.Empty.Set(TokenAValue, FactoryAccent)
                    );
                    return failed;
                }
            )
        );
        Assert(
            unrelatedTheme.TokenCount == 0 && graph.Dump() == baseline,
            "Element presentation accepted a captured theme outside its composition."
        );
        Expect<InvalidOperationException>(() =>
            composition.Mount(
                composition.Root,
                theme,
                context =>
                {
                    var failed = context.Element("failed-token");
                    failed.Present(theme, author: Style.Empty.Set(TokenAValue, FactoryAccent));
                    throw new InvalidOperationException("token factory failure");
                }
            )
        );
        Assert(
            theme.TokenCount == 0 && graph.Dump() == baseline,
            "Failed first token resolution retained a shared slot or graph signal."
        );

        Expect<InvalidOperationException>(() =>
            composition.Mount(
                composition.Root,
                theme,
                outer =>
                {
                    var root = outer.Element("failed-outer-token");
                    _ = outer.Mount(
                        root,
                        inner =>
                        {
                            var nested = inner.Element("failed-nested-token");
                            nested.Present(theme, author: Style.Empty.Set(TokenAValue, AccentB));
                            return nested;
                        }
                    );
                    throw new InvalidOperationException("outer token factory failure");
                }
            )
        );
        Assert(
            theme.TokenCount == 0 && graph.Dump() == baseline,
            "Nested token rollback was not promoted to its enclosing recipe transaction."
        );

        var childTheme = new ThemeContext(
            composition.Root.Scope.CreateChild("child-theme-scope"),
            new Theme("child-token-theme")
        );
        var childBaseline = graph.Dump();
        Expect<InvalidOperationException>(() =>
            composition.Mount(
                composition.Root,
                childTheme,
                context =>
                {
                    var failed = context.Element("failed-child-token");
                    failed.Present(childTheme, author: Style.Empty.Set(TokenAValue, AccentB));
                    throw new InvalidOperationException("child token factory failure");
                }
            )
        );
        Assert(
            childTheme.TokenCount == 0 && graph.Dump() == childBaseline,
            "Child-scope theme did not inherit recipe rollback registration."
        );

        var retained = composition.Mount(
            composition.Root,
            theme,
            context =>
            {
                var element = context.Element("retained-token");
                element.Present(theme, author: Style.Empty.Set(TokenAValue, FactoryAccent));
                return element;
            }
        );
        var reads = 0;
        var value = composition.Root.Scope.Derived(
            () =>
            {
                reads++;
                return retained.Resolve(TokenAValue).Value;
            },
            "retained-token-reader"
        );
        Assert(
            theme.TokenCount == 1 && value.Value == 10 && reads == 1,
            "Committed token resolution did not retain exactly one shared slot."
        );
        theme.Theme = theme.Theme.Set(FactoryAccent, 11);
        graph.Drain();
        Assert(
            theme.TokenCount == 1 && value.Value == 11 && reads == 2,
            "Committed token slot did not persist or invalidate on its theme update."
        );
    }

    [TestMethod]
    public void FluentStylesAndComposition()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "fluent-styles");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("fluent"));
        var color = Color.Parse("#123456");
        var element = composition.Child(composition.Root, "all-properties");
        element.Present(
            theme,
            author: Style
                .Empty.Axis(LayoutAxis.Row)
                .Width(10f)
                .Height(11f)
                .MinWidth(1f)
                .MinHeight(2f)
                .MaxWidth(20f)
                .MaxHeight(21f)
                .Spacing(3f)
                .MainGrow(4f)
                .MainAlignment(LayoutAlignment.Center)
                .CrossAlignment(LayoutAlignment.End)
                .Padding(Insets.Uniform(4f))
                .Clip(true)
                .Scroll(new ScrollOffset(1, 2))
                .Background((Brush)color)
                .Opacity(.5f)
                .TextColor(color)
                .FontFamily("Fluent")
                .FontSize(12f)
                .Language("fr")
                .Direction(TextDirection.RightToLeft)
                .Enabled(false)
                .Visible(false)
        );
        Assert(
            element.Resolve(LayoutProperties.Axis).Value == LayoutAxis.Row
                && element.Resolve(LayoutProperties.Width).Value == 10f
                && element.Resolve(LayoutProperties.Height).Value == 11f
                && element.Resolve(LayoutProperties.MinWidth).Value == 1f
                && element.Resolve(LayoutProperties.MinHeight).Value == 2f
                && element.Resolve(LayoutProperties.MaxWidth).Value == 20f
                && element.Resolve(LayoutProperties.MaxHeight).Value == 21f
                && element.Resolve(LayoutProperties.Spacing).Value == 3f
                && element.Resolve(LayoutProperties.MainGrow).Value == 4f
                && element.Resolve(LayoutProperties.MainAlignment).Value == LayoutAlignment.Center
                && element.Resolve(LayoutProperties.CrossAlignment).Value == LayoutAlignment.End
                && element.Resolve(LayoutProperties.Padding).Value == Insets.Uniform(4f)
                && element.Resolve(LayoutProperties.Clip).Value
                && element.Resolve(LayoutProperties.Scroll).Value == new ScrollOffset(1, 2)
                && element.Resolve(VisualProperties.Background).Value.Color == color
                && element.Resolve(VisualProperties.Opacity).Value == .5f
                && element.Resolve(TypographyProperties.TextColor).Value == color
                && element.Resolve(TypographyProperties.FontFamily).Value == "Fluent"
                && element.Resolve(TypographyProperties.FontSize).Value == 12f
                && element.Resolve(TypographyProperties.Language).Value == "fr"
                && element.Resolve(TypographyProperties.Direction).Value
                    == TextDirection.RightToLeft
                && !element.Resolve(InputProperties.Enabled).Value
                && !element.Resolve(InputProperties.Visible).Value,
            "Typed fluent static properties changed their Style.Set mapping."
        );

        var live = composition.Root.Scope.Signal(.6f, "fluent-live");
        var precedence = composition.Child(composition.Root, "fluent-precedence");
        precedence.Present(
            theme,
            Style.Empty.Opacity(.1f).When(VariantState.Hover, Style.Empty.Opacity(.2f)),
            Style
                .Empty.Opacity(.3f)
                .With(null)
                .With(Style.Empty.Opacity(.4f))
                .When(VariantState.Hover, Style.Empty.Opacity(() => live.Value))
        );
        graph.Drain();
        Assert(
            precedence.Resolve(VisualProperties.Opacity)
                is { Value: .4f, Winner: { Source: "author", Condition: VariantState.None } },
            "Style.With did not preserve rightmost static precedence."
        );
        precedence.SetVariants(VariantState.Hover);
        graph.Drain();
        Assert(
            precedence.Resolve(VisualProperties.Opacity)
                is { Value: .6f, Winner: { Source: "author", Condition: VariantState.Hover } },
            "Fluent live variant lost author provenance."
        );
        live.Value = .7f;
        graph.Drain();
        Assert(
            precedence.Resolve(VisualProperties.Opacity).Value == .7f,
            "Fluent live property did not track its reader."
        );
    }

    [TestMethod]
    public void BehaviorIsolationAndRollback()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "behavior");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var element = composition.Child(composition.Root, "target");
        element.Present(theme);
        var cleanup = 0;
        var missing = new Probe(
            "missing",
            BehaviorOwnership.Semantics,
            context => context.OnDispose(() => cleanup++)
        );
        Expect<InvalidOperationException>(() => element.AttachBehaviors(missing));
        Assert(
            cleanup == 1 && !composition.Dump().Contains("missing", StringComparison.Ordinal),
            "Missing semantics did not rollback cleanup/state."
        );
        var beforeGuard = composition.Dump();
        var beforeGraph = graph.Dump();
        Expect<InvalidOperationException>(() =>
            element.AttachBehaviors(
                new Probe(
                    "variant-escape",
                    BehaviorOwnership.None,
                    _ => element.SetVariants(VariantState.Selected)
                )
            )
        );
        Expect<InvalidOperationException>(() =>
            element.AttachBehaviors(
                new Probe(
                    "theme-escape",
                    BehaviorOwnership.None,
                    _ => theme.Theme = new Theme("changed")
                )
            )
        );
        Expect<InvalidOperationException>(() =>
            element.AttachBehaviors(
                new Probe("motion-escape", BehaviorOwnership.None, _ => theme.ReducedMotion = true)
            )
        );
        Expect<InvalidOperationException>(() =>
            element.AttachBehaviors(
                new Probe("dispose-theme-escape", BehaviorOwnership.None, _ => theme.Dispose())
            )
        );
        Assert(
            composition.Dump() == beforeGuard
                && graph.Dump() == beforeGraph
                && !theme.ReducedMotion,
            "Failed behavior attachment changed presentation/theme state or graph."
        );
        var active = composition.Root.Scope.Signal(false, "captured-region-active");
        CompositionContext? captured = null;
        Element? capturedRoot = null;
        var conditional = composition.When(
            composition.Root,
            "captured-conditional",
            () => active.Value,
            context =>
            {
                captured = context;
                return capturedRoot = context.Element("captured-child");
            }
        );
        var rows = composition.Root.Scope.Signal(new[] { 1 }, "captured-rows");
        var keyed = composition.ForEach(
            composition.Root,
            "captured-keyed",
            () => rows.Value,
            value => value,
            (_, context) =>
            {
                captured ??= context;
                return context.Element("captured-keyed-child");
            }
        );
        active.Value = true;
        graph.Drain();
        var structuralDump = composition.Dump();
        var structuralGraph = graph.Dump();
        foreach (
            var action in new Action[]
            {
                () => conditional.Update(false),
                conditional.Refresh,
                conditional.Dispose,
                () => keyed.Update([1]),
                keyed.Refresh,
                keyed.Dispose,
                () => captured!.Element("captured-late"),
                () => captured!.Child(capturedRoot!, "captured-late-child"),
                captured!.Dispose,
            }
        )
        {
            Expect<InvalidOperationException>(() =>
                element.AttachBehaviors(
                    new Probe("captured", BehaviorOwnership.None, _ => action())
                )
            );
            Assert(
                composition.Dump() == structuralDump && graph.Dump() == structuralGraph,
                "Behavior attachment mutated captured structure."
            );
        }
        foreach (
            var cleanupAction in new Action[]
            {
                () => conditional.Update(false),
                () => keyed.Update([1]),
                captured!.Dispose,
            }
        )
        {
            Expect<AggregateException>(() =>
                element.AttachBehaviors(
                    new Probe(
                        "cleanup-captured",
                        BehaviorOwnership.None,
                        context => context.OnDispose(cleanupAction)
                    ),
                    new Probe("cleanup-rollback", BehaviorOwnership.Semantics, _ => { })
                )
            );
            Assert(
                composition.Dump() == structuralDump && graph.Dump() == structuralGraph,
                "Behavior cleanup mutated captured structure."
            );
        }
        foreach (
            var action in new Action[]
            {
                () => composition.Child(element, "escaped"),
                () => element.Present(theme),
                () => element.AttachBehaviors(missing),
                () => element.Dispose(),
                () => element.Scope.Dispose(),
                () => composition.Dispose(),
            }
        )
            Expect<InvalidOperationException>(() =>
                element.AttachBehaviors(new Probe("escape", BehaviorOwnership.None, _ => action()))
            );
        var cleanupStructure = new Probe(
            "cleanup",
            BehaviorOwnership.None,
            context =>
                context.OnDispose(() => composition.Child(composition.Root, "cleanup-escape"))
        );
        Expect<AggregateException>(() =>
            element.AttachBehaviors(
                cleanupStructure,
                new Probe("rollback", BehaviorOwnership.Semantics, _ => { })
            )
        );
        Assert(
            !composition.Dump().Contains("cleanup-escape", StringComparison.Ordinal),
            "Behavior cleanup changed structure."
        );
        element.AttachBehaviors(
            new Probe(
                "action",
                BehaviorOwnership.Action | BehaviorOwnership.Semantics,
                context =>
                    context.SetSemantics(
                        new(SemanticRole.Button, "Action", actions: SemanticAction.Invoke)
                    )
            )
        );
        Expect<InvalidOperationException>(() =>
            element.AttachBehaviors(
                new Probe(
                    "action2",
                    BehaviorOwnership.Action | BehaviorOwnership.Semantics,
                    context =>
                        context.SetSemantics(
                            new(SemanticRole.Button, "Second", actions: SemanticAction.Invoke)
                        )
                )
            )
        );
        var beforeOwnership = graph.Dump();
        Expect<ArgumentException>(() =>
            element.AttachBehaviors(new Probe("bad-owner", (BehaviorOwnership)32, _ => { }))
        );
        Assert(
            graph.Dump() == beforeOwnership,
            "Bad ownership validation created behavior graph nodes."
        );
        var malicious = composition.Child(composition.Root, "malicious");
        malicious.Present(theme);
        malicious.AttachBehaviors(new Probe("evil\r\nname", BehaviorOwnership.None, _ => { }));
        Assert(
            composition
                .Dump()
                .Contains("behavior name=\"evil\\r\\nname\"", StringComparison.Ordinal),
            "Behavior name was not CR/LF escaped."
        );
        var probe = Released();
        ForceGc();
        Assert(
            !probe.Payload.IsAlive,
            "Behavior delegates remained rooted after rollback/disposal."
        );
        GC.KeepAlive(probe.Root);
    }

    [TestMethod]
    public void BindingsRespectVariantsAndControlAuthority()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "bindings");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("bindings"));
        var source = composition.Root.Scope.Signal(2, "bound-value");
        var reads = 0;
        var bound = composition.Child(composition.Root, "bound");
        bound.Present(
            theme,
            author: Style.Empty.When(
                VariantState.Hover,
                Style.Empty.Bind(
                    Value,
                    () =>
                    {
                        reads++;
                        return source.Value;
                    }
                )
            )
        );
        graph.Drain();
        Assert(
            reads == 0 && bound.Resolve(Value).Value == 1,
            "Inactive binding evaluated or changed its candidate."
        );
        bound.SetVariants(VariantState.Hover);
        graph.Drain();
        Assert(
            reads == 1
                && bound.Resolve(Value)
                    is { Value: 2, Winner: { Source: "author", Condition: VariantState.Hover } },
            "Active binding lost author provenance or variant condition."
        );
        source.Value = 3;
        graph.Drain();
        Assert(
            reads == 2 && bound.Resolve(Value).Value == 3,
            "Active binding did not track its expression."
        );
        bound.SetVariants(VariantState.None);
        graph.Drain();
        source.Value = 4;
        graph.Drain();
        Assert(
            reads == 2 && bound.Resolve(Value).Value == 1,
            "Inactive binding retained a live dependency."
        );

        var precedence = composition.Child(composition.Root, "binding-precedence");
        precedence.Present(
            theme,
            Style.Empty.Set(Value, 7).Bind(Value, () => 8),
            Style.Empty.Set(Value, 9).Bind(Value, () => 10)
        );
        graph.Drain();
        var precedenceValue = precedence.Resolve(Value);
        Assert(
            precedenceValue.Value == 10
                && precedenceValue.Winner is { Source: "author", Ordinal: 1 }
                && precedenceValue.Overridden.Any(item =>
                    item is { Source: "component", Ordinal: 1 }
                ),
            "Binding candidates lost component/author or ordinal precedence."
        );

        var recover = composition.Root.Scope.Signal(true, "binding-failure");
        var failed = composition.Child(composition.Root, "binding-failure");
        failed.Present(
            theme,
            author: Style
                .Empty.Set(Value, 6)
                .Bind(
                    Value,
                    () => recover.Value ? throw new InvalidOperationException("binding read") : 11
                )
        );
        Expect<AggregateException>(graph.Drain);
        var fallback = failed.Resolve(Value);
        Assert(
            fallback is { Value: 6, Winner: { Source: "author", Ordinal: 0 } }
                && fallback.Overridden.All(item => item.Ordinal != 1),
            "First failed binding published its uncommitted default candidate."
        );
        recover.Value = false;
        graph.Drain();
        Assert(
            failed.Resolve(Value) is { Value: 11, Winner: { Source: "author", Ordinal: 1 } },
            "Recovered binding did not publish its first successful value."
        );
        recover.Value = true;
        Expect<AggregateException>(graph.Drain);
        Assert(
            failed.Resolve(Value) is { Value: 11, Winner: { Source: "author", Ordinal: 1 } },
            "Later failed binding discarded its prior successful value."
        );

        var controlled = composition.Child(composition.Root, "controlled");
        var state = Controls.Loading(
            controlled,
            theme,
            "Loading",
            Style.Empty.Bind(ProjectionProperties.Text, () => "bound")
        );
        graph.Drain();
        state.Label = "Ready";
        graph.Drain();
        var resolved = controlled.Resolve(ProjectionProperties.Text);
        Assert(
            resolved.Value == "Ready"
                && resolved.Winner.Source == "control"
                && resolved.Overridden.Any(item => item.Source == "author"),
            "Control state did not override the bound author candidate with retained provenance."
        );
        var lateReads = reads;
        bound.Dispose();
        source.Value = 5;
        graph.Drain();
        Assert(reads == lateReads, "Disposed binding accepted a late expression callback.");
        var released = ReleasedBinding();
        ForceGc();
        Assert(!released.Payload.IsAlive, "Disposed binding retained its callback payload.");
        GC.KeepAlive(released.Root);
    }

    [TestMethod]
    public void TypographyInheritanceScale()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "typography-scale");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("typography-scale"));
        var color = Color.Parse("#123456");
        Assert(
            composition.Root.Resolve(TypographyProperties.TextColor).Value == Color.FromRgb(0, 0, 0)
                && composition.Root.Resolve(TypographyProperties.FontFamily).Value == "Segoe UI"
                && composition.Root.Resolve(TypographyProperties.FontSize).Value == 14f
                && composition.Root.Resolve(TypographyProperties.Language).Value == "en"
                && composition.Root.Resolve(TypographyProperties.Direction).Value
                    == TextDirection.LeftToRight,
            "Typography defaults changed."
        );
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Set(TypographyProperties.TextColor, color)
                .Set(TypographyProperties.FontFamily, "Cascadia Mono")
                .Set(TypographyProperties.FontSize, 15f)
                .Set(TypographyProperties.Language, "fr")
                .Set(TypographyProperties.Direction, TextDirection.RightToLeft)
        );
        var local = composition.Child(composition.Root, "typography-local");
        local.Present(theme, author: Style.Empty.Set(TypographyProperties.FontSize, 20f));
        Assert(
            local.Resolve(TypographyProperties.TextColor).Value == color
                && local.Resolve(TypographyProperties.FontFamily).Value == "Cascadia Mono"
                && local.Resolve(TypographyProperties.FontSize).Value == 20f
                && local.Resolve(TypographyProperties.Language).Value == "fr"
                && local.Resolve(TypographyProperties.Direction).Value == TextDirection.RightToLeft,
            "Typography inheritance or local override changed."
        );
        var localDump = composition.Dump();
        var localStart = localDump.IndexOf("name=\"typography-local\"", StringComparison.Ordinal);
        var localEnd = localDump.IndexOf("element ", localStart + 1, StringComparison.Ordinal);
        var localSegment = localDump[localStart..(localEnd < 0 ? localDump.Length : localEnd)];
        Assert(
            new[]
            {
                "typography-text-color",
                "typography-font-family",
                "typography-language",
                "typography-direction",
            }.All(name =>
                localSegment.Contains(
                    $"property name=\"{name}\" winner=\"inherited\"",
                    StringComparison.Ordinal
                )
            )
                && localSegment.Contains(
                    "property name=\"typography-font-size\" winner=\"author\"",
                    StringComparison.Ordinal
                ),
            "Typography dump omitted inherited or local provenance."
        );
        var rows = new Element[10_000];
        for (var index = 0; index < rows.Length; index++)
            rows[index] = composition.Child(composition.Root, "typography-row-" + index);
        graph.Drain();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var checksum = 0d;
        foreach (var row in rows)
        {
            checksum += row.Resolve(TypographyProperties.TextColor).Value.R;
            checksum += row.Resolve(TypographyProperties.FontSize).Value;
            checksum +=
                row.Resolve(TypographyProperties.FontFamily).Value.Length
                + row.Resolve(TypographyProperties.Language).Value.Length
                + (int)row.Resolve(TypographyProperties.Direction).Value;
        }
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert(
            checksum
                == rows.Length
                    * (
                        0x12
                        + 15
                        + "Cascadia Mono".Length
                        + "fr".Length
                        + (int)TextDirection.RightToLeft
                    ),
            "Inherited typography changed across the 10k-row corpus."
        );
        Console.WriteLine(
            $"Lucent.Core typography inheritance: rows={rows.Length} elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3} allocatedBytes={allocated}"
        );
    }

    [TestMethod]
    public void BindingRowScaleLifecycle()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "binding-row-scale");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("binding-row-scale"));
        var value = composition.Root.Scope.Signal(1, "binding-row-value");
        const int rows = 64;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < rows; index++)
        {
            var row = composition.Child(composition.Root, "binding-row-" + index);
            row.Present(theme, author: Style.Empty.Bind(Value, () => value.Value));
        }
        graph.Drain();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var topology = graph.Dump();
        Assert(
            topology.Split("bind-effect", StringSplitOptions.None).Length - 1 == rows
                && allocated is > 0 and < 1_000_000,
            "Bound row-scale topology or allocation exceeded the bounded representative datapoint."
        );
        composition.Dispose();
        Assert(
            graph.Dump() == "reactive-graph\n",
            "Disposed bound rows did not return their graph topology."
        );
    }

    [TestMethod]
    public void SemanticsAndDumps()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "semantic\r\nroot");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        Assert(composition.SemanticSnapshot() is null, "Empty semantic tree was not null.");
        var one = composition.Child(composition.Root, "one");
        one.Present(theme);
        one.AttachBehaviors(Semantics("one", "one"));
        var snapshot = composition.SemanticSnapshot()!;
        Assert(
            snapshot.Identity.ElementId == composition.Root.Id
                && snapshot.Children.Count == 1
                && composition.IsCurrent(snapshot.Identity),
            "Synthetic semantic root did not retain one child identity."
        );
        var many = composition.Child(composition.Root, "many");
        many.Present(theme);
        many.AttachBehaviors(Semantics("many", "many"));
        snapshot = composition.SemanticSnapshot()!;
        Assert(
            snapshot.Children.Count == 2
                && !composition.IsCurrent(
                    new SemanticIdentity(
                        snapshot.Identity.CompositionEpoch + 1,
                        snapshot.Identity.ElementId,
                        snapshot.Identity.Generation
                    )
                ),
            "Forged/cross-composition semantic identity was accepted."
        );
        var childIdentity = snapshot
            .Children.Single(node => node.Identity.ElementId == many.Id)
            .Identity;
        many.Dispose();
        Assert(
            !composition.IsCurrent(childIdentity),
            "Disposed child semantic identity remained current."
        );
        var synthetic = snapshot.Identity;
        composition.Root.AttachBehaviors(Semantics("root", "root"));
        snapshot = composition.SemanticSnapshot()!;
        Assert(
            snapshot.Identity.ElementId == composition.Root.Id
                && snapshot.Identity.Generation == 1
                && !composition.IsCurrent(synthetic),
            "Authored root semantics duplicated the synthetic root or accepted stale identity."
        );
        composition.Dispose();
        Assert(
            !composition.IsCurrent(snapshot.Identity),
            "Disposed semantic identity remained current."
        );
        var firstCulture = Dump();
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        var secondCulture = Dump();
        CultureInfo.CurrentCulture = original;
        Assert(
            firstCulture == secondCulture
                && firstCulture.Contains("\\r", StringComparison.Ordinal)
                && !firstCulture.Contains("secret", StringComparison.OrdinalIgnoreCase),
            "Dump was culture-sensitive, unsafe, or leaked semantic values."
        );
    }

    private static string Dump()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "dump\r\nroot");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        composition.Root.Present(theme, author: Style.Empty.Set(Value, 4));
        composition.Root.AttachBehaviors(Semantics("dump", "secret"));
        return composition.Dump();
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static ProbeResult Released()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "release");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var payload = new Payload();
        var weak = new WeakReference(payload);
        var element = composition.Child(composition.Root, "release-target");
        element.Present(theme);
        element.AttachBehaviors(
            new Probe(
                "release",
                BehaviorOwnership.None,
                context => context.OnDispose(() => GC.KeepAlive(payload))
            )
        );
        element.Dispose();
        return new(weak, composition);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static ProbeResult ReleasedBinding()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "release-binding");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var payload = new Payload();
        var weak = new WeakReference(payload);
        var element = composition.Child(composition.Root, "release-binding-target");
        element.Present(
            theme,
            author: Style.Empty.Bind(
                Value,
                () =>
                {
                    GC.KeepAlive(payload);
                    return 1;
                }
            )
        );
        graph.Drain();
        element.Dispose();
        return new(weak, composition);
    }

    private static Probe Semantics(string id, string name) =>
        new(
            id,
            BehaviorOwnership.Semantics,
            context => context.SetSemantics(new(SemanticRole.Text, name))
        );

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

    private static void Assert(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class Probe(
        string name,
        BehaviorOwnership ownership,
        Action<BehaviorContext> attach
    ) : Behavior
    {
        public override string Name => name;
        public override BehaviorOwnership Ownership => ownership;

        public override void Attach(BehaviorContext context) => attach(context);
    }

    private sealed record ProbeResult(WeakReference Payload, object Root);

    private sealed class Payload;
}
