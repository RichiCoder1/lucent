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
            RecipeMountsAndMetadataSurface();
            ComponentRecipeContracts();
            FactoryMutationContainment();
            BuiltinRecipeContracts();
            NestedFactoryIsolationAndVirtualRows();
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

    private static void RecipeMountsAndMetadataSurface()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "recipe-mount");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var active = graph.Signal(true, "recipe-active");
        var items = graph.Signal(new[] { 1 }, "recipe-items");
        var changes = new List<string>();
        var invoked = 0;
        var root = composition.Mount(composition.Root, theme, context =>
        {
            Assert(ReferenceEquals(context.Theme, theme), "Root recipe did not receive its explicit theme.");
            var panel = context.Element("recipe-panel");
            Controls.Panel(panel, context.Theme, "Recipe panel");
            _ = context.Mount(panel, child => { Assert(ReferenceEquals(child.Theme, theme), "Nested mount lost its root theme."); var row = child.Element("recipe-row"); Controls.Row(row, child.Theme, "recipe-row"); var field = child.Child(row, "recipe-field"); var state = Controls.TextField(field, child.Theme, "recipe-field", "initial"); var prior = state.Value; _ = field.Scope.Effect(() => { var value = state.Value; if (value != prior) { prior = value; changes.Add(value); } }, "recipe-field.on-change"); return row; });
            _ = context.When(panel, "recipe-when", () => active.Value, child => { Assert(ReferenceEquals(child.Theme, theme), "Conditional recipe lost its root theme."); var button = child.Element("recipe-button"); Controls.Button(button, child.Theme, "Invoke", () => invoked++); return button; });
            _ = context.ForEach(panel, "recipe-items", () => items.Value, value => value, (value, child) => { Assert(ReferenceEquals(child.Theme, theme), "Keyed recipe lost its root theme."); var text = child.Element("recipe-text-" + value); Controls.Text(text, child.Theme, "Item " + value); return text; });
            return panel;
        });
        graph.Drain();
        Assert(root.Children.Count == 3 && composition.Dump().Contains("name=\"recipe-field\"", StringComparison.Ordinal) &&
            composition.SemanticSnapshot()!.Children.Single(node => node.Name == "Recipe panel").Children.Any(node => node.Name == "Invoke"),
            "Nested recipe mount, scalar content, or element content changed the retained tree.");
        var field = root.Children[0].Children.Single();
        var fieldNode = Descendants(composition.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.TextField);
        Assert(composition.ExecuteSemanticCommand(fieldNode.Identity, new(SemanticCommandKind.SetValue, "changed")) == SemanticCommandResult.Applied, "Recipe text field rejected its control command.");
        graph.Drain();
        var button = Descendants(composition.SemanticSnapshot()!).Single(node => node.Name == "Invoke");
        Assert(changes.SequenceEqual(["changed"]) && composition.ExecuteSemanticCommand(button.Identity, new(SemanticCommandKind.Invoke)) == SemanticCommandResult.Applied && invoked == 1 && field.Name == "recipe-field",
            "Recipe control callbacks or structural identities diverged from control behavior.");

        var before = composition.Dump();
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, _ => composition.Root));
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { _ = context.Element("wrong-root"); return composition.Root; }));
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var first = context.Element("bad-first"); _ = context.Element("bad-second"); return first; }));
        Assert(composition.Dump() == before && !root.IsDisposed, "Invalid recipe root did not roll back atomically.");
        Expect<AggregateException>(() => composition.Mount(composition.Root, theme, context => { var failed = context.Element("cleanup-failure"); failed.Scope.OnDispose(() => throw new InvalidOperationException("nested-cleanup")); throw new InvalidOperationException("nested-factory"); }));
        Assert(composition.Dump() == before, "Nested factory cleanup failure leaked provisional structure.");
        Expect<ArgumentException>(() => composition.Mount(composition.Root, theme, context => { var escaped = context.Element("escaped"); _ = context.Mount(composition.Root, child => { var text = child.Element("escape"); Controls.Text(text, child.Theme, "Escape"); return text; }); return escaped; }));
        Assert(composition.Dump() == before, "Nested recipe escaped its factory parent.");
        root.Dispose();
        Assert(root.IsDisposed && !composition.Dump().Contains("recipe-panel", StringComparison.Ordinal), "Mounted recipe disposal retained its scope-owned tree.");
    }

    private static void ComponentRecipeContracts()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-recipe-root");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var cleanup = 0;
        var leaf = ComponentRecipe.Create("leaf", (context, element) =>
        {
            Controls.Text(element, context.Theme, "Leaf");
            element.Scope.OnDispose(() => cleanup++);
        });
        var conditional = ComponentRecipe.Create("conditional-leaf", (context, element) => Controls.Text(element, context.Theme, "Conditional"));
        var rows = graph.Signal(new[] { 1, 2 }, "component-recipe-items");
        var active = graph.Signal(true, "component-recipe-active");
        ComponentContent content = [leaf, leaf.Named("named-leaf"), ContentRecipe.When("recipe-condition", () => active.Value, conditional),
            ContentRecipe.ForEach("recipe-items", () => rows.Value, item => item, item => ComponentRecipe.Create("row", (context, element) => Controls.Text(element, context.Theme, item.ToString())))];
        var container = ComponentRecipe.Create("container", (context, element) =>
        {
            Controls.Panel(element, context.Theme, "Container");
            context.Mount(element, content);
        });

        var first = composition.Mount(composition.Root, theme, container);
        var second = composition.Mount(composition.Root, theme, container);
        graph.Drain();
        Assert(first.Name == "container-1" && second.Name == "container-2" && first.Children.Count == 4 && second.Children.Count == 4 &&
            first.Children[0].Name == "leaf-1" && first.Children[1].Name == "named-leaf" && first.Children.All(child => child.Name != "fragment"),
            "Recipe names, repeated mounts, or content mounting changed the retained tree.");
        Assert(!ReferenceEquals(first, second) && !ReferenceEquals(first.Children[0], second.Children[0]), "Repeated recipe mounts reused a root or scope.");

        active.Value = false; rows.Value = [2, 3]; graph.Drain();
        var keyed = first.Children.Single(child => child.Name == "recipe-items");
        Assert(first.Children.Count == 4 && keyed.Children.Count == 2 && keyed.Children[0].Name == "row-2" && keyed.Children[1].Name == "row-3",
            "Conditional or keyed content did not retain existing region identity.");

        var empty = ComponentRecipe.Create("empty", (context, element) => context.Mount(element, ComponentContent.Empty));
        var emptyRoot = composition.Mount(composition.Root, theme, empty);
        Assert(emptyRoot.Children.Count == 0, "Empty content created a synthetic element.");
        var contentCleanup = 0;
        ComponentContent failingContent = [ComponentRecipe.Create("provisional", (_, child) => child.Scope.OnDispose(() => contentCleanup++)),
            ComponentRecipe.Create("throwing", (_, _) => throw new InvalidOperationException("content failure"))];
        var contentFailure = ComponentRecipe.Create("content-failure", (context, element) =>
        {
            context.Mount(element, failingContent);
        });
        var beforeFailure = composition.Dump();
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, contentFailure));
        Assert(composition.Dump() == beforeFailure && contentCleanup == 1, "Failed content did not roll back its mounted entries exactly once.");
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, ComponentRecipe.Create("failed", (context, element) =>
        {
            element.Scope.OnDispose(() => cleanup++);
            throw new InvalidOperationException("recipe failure");
        })));
        Assert(!composition.Dump().Contains("failed-", StringComparison.Ordinal), "Failed recipe mount left a root attached.");
        first.Dispose(); second.Dispose();
        Assert(cleanup == 5, "Recipe roots did not dispose owned scopes exactly once.");
        Expect<ArgumentException>(() => ComponentRecipe.Create("", (_, _) => { }));
        Expect<ArgumentNullException>(() => ComponentRecipe.Create("null-callback", null!));
    }

    private static void BuiltinRecipeContracts()
    {
        Expect<ArgumentException>(() => Components.Text(" "));
        Expect<ArgumentException>(() => Components.TextField("bad\nvalue"));
        Expect<ArgumentOutOfRangeException>(() => Components.Progress("Progress", 2f));
        Expect<ArgumentNullException>(() => Components.Row(null!));
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "built-in-recipes");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var source = graph.Signal("Live", "recipe-text");
        var selected = graph.Signal(false, "recipe-selected");
        var height = graph.Signal(10f, "recipe-row-height");
        ContentRecipe[] entries = [Components.Text("Snapshot")];
        var row = Components.Row(ComponentContent.Create(entries), Style.Empty.Width(100f).Height(20f));
        entries[0] = Components.Text("Changed");
        var mounted = composition.Mount(composition.Root, theme, row);
        var live = composition.Mount(composition.Root, theme, Components.Text(() => source.Value));
        _ = composition.Mount(composition.Root, theme, Components.Button("Button"));
        _ = composition.Mount(composition.Root, theme, Components.TextField("value"));
        _ = composition.Mount(composition.Root, theme, Components.Selectable("Selectable"));
        var liveSelectable = composition.Mount(composition.Root, theme, Components.Selectable(() => source.Value, () => selected.Value));
        _ = composition.Mount(composition.Root, theme, Components.ScrollViewport([Components.Text("Viewport")], style: Style.Empty.Width(100f).Height(20f)));
        _ = composition.Mount(composition.Root, theme, Components.Status("Ready"));
        _ = composition.Mount(composition.Root, theme, Components.Status(() => source.Value));
        _ = composition.Mount(composition.Root, theme, Components.Progress("Static", .5f));
        _ = composition.Mount(composition.Root, theme, Components.Progress("Live", () => .75f));
        var list = composition.Mount(composition.Root, theme, Components.VirtualizedList(() => new[] { 1, 2 }, value => value, value => Components.Text(value.ToString()), () => height.Value, style: Style.Empty.Width(100f).Height(20f)));
        graph.Drain(); _ = SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper());
        Assert(mounted.Children.Count == 1 && mounted.Children[0].Resolve(ProjectionProperties.Text).Value == "Snapshot" && mounted.Children.All(child => child.Name != "fragment") &&
            live.Resolve(ProjectionProperties.Text).Value == "Live" && list.Children.Single().Children.Count > 0,
            "Built-in recipes did not use the ordinary recipe/content transaction.");
        source.Value = "Changed"; height.Value = 12f; graph.Drain();
        selected.Value = true; graph.Drain();
        Assert(live.Resolve(ProjectionProperties.Text).Value == "Changed" && liveSelectable.Resolve(ProjectionProperties.Text).Value == "Changed" &&
            Descendants(composition.SemanticSnapshot()!).Single(node => node.Identity.ElementId == liveSelectable.Id).Selected && list.Children.Single().Resolve(LayoutProperties.VirtualRowHeight).Value == 12f,
            "Live built-in recipe inputs did not update their retained roots.");

        var textSource = graph.Signal("Text", "cached-text-source"); var selectableSource = graph.Signal("Selectable", "cached-selectable-source");
        var selectedSource = graph.Signal(false, "cached-selected-source"); var statusSource = graph.Signal("Status", "cached-status-source"); var progressSource = graph.Signal(.25f, "cached-progress-source"); var heightSource = graph.Signal(10f, "cached-height-source"); var fragileSource = graph.Signal("Fragile", "cached-fragile-source");
        var textReads = 0; var selectableReads = 0; var selectedReads = 0; var statusReads = 0; var progressReads = 0; var heightReads = 0; var fragileReads = 0;
        var cachedText = composition.Mount(composition.Root, theme, Components.Text(() => { textReads++; return textSource.Value; }));
        var cachedSelectable = composition.Mount(composition.Root, theme, Components.Selectable(() => { selectableReads++; return selectableSource.Value; }, () => { selectedReads++; return selectedSource.Value; }));
        var cachedStatus = composition.Mount(composition.Root, theme, Components.Status(() => { statusReads++; return statusSource.Value; }));
        var cachedProgress = composition.Mount(composition.Root, theme, Components.Progress("Cached", () => { progressReads++; return progressSource.Value; }));
        var cachedList = composition.Mount(composition.Root, theme, Components.VirtualizedList(() => Array.Empty<int>(), value => value, value => Components.Text(value.ToString()), () => { heightReads++; return heightSource.Value; }, style: Style.Empty.Width(100f).Height(20f)));
        _ = composition.Mount(composition.Root, theme, Components.Text(() => { if (++fragileReads > 1) throw new InvalidOperationException("second live read"); return fragileSource.Value; }));
        graph.Drain(); graph.Drain();
        Assert(textReads == 1 && selectableReads == 1 && selectedReads == 1 && statusReads == 1 && progressReads == 1 && heightReads == 1 && fragileReads == 1,
            "Live recipes evaluated again during their first effect drain.");
        textSource.Value = "Text 2"; selectableSource.Value = "Selectable 2"; selectedSource.Value = true; statusSource.Value = "Status 2"; progressSource.Value = .5f; heightSource.Value = 12f;
        graph.Drain();
        Assert(textReads == 2 && selectableReads == 2 && selectedReads == 2 && statusReads == 2 && progressReads == 2 && heightReads == 2,
            "Live recipe readers did not evaluate exactly once per source invalidation.");
        cachedText.Dispose(); cachedSelectable.Dispose(); cachedStatus.Dispose(); cachedProgress.Dispose(); cachedList.Dispose();
        textSource.Value = "Detached"; selectableSource.Value = "Detached"; selectedSource.Value = false; statusSource.Value = "Detached"; progressSource.Value = .75f; heightSource.Value = 14f;
        graph.Drain();
        Assert(textReads == 2 && selectableReads == 2 && selectedReads == 2 && statusReads == 2 && progressReads == 2 && heightReads == 2,
            "Disposed live recipe readers remained attached.");
        var reads = 0;
        var detached = composition.Mount(composition.Root, theme, Components.Text(() => { reads++; return source.Value; }));
        graph.Drain(); var beforeDispose = reads; detached.Dispose(); source.Value = "Detached"; graph.Drain();
        Assert(reads == beforeDispose, "Disposed live recipe reader remained attached.");

        var atomicLabel = graph.Signal("Atomic", "atomic-selectable-label");
        var atomicSelected = graph.Signal(false, "atomic-selectable-selected");
        var throwSelected = false;
        var atomic = composition.Mount(composition.Root, theme, Components.Selectable(() => atomicLabel.Value, () =>
        {
            var selectedValue = atomicSelected.Value;
            if (throwSelected) throw new InvalidOperationException("atomic selected failure");
            return selectedValue;
        }));
        graph.Drain();
        var beforeText = atomic.Resolve(ProjectionProperties.Text).Value;
        var beforeSemantic = Descendants(composition.SemanticSnapshot()!).Single(node => node.Identity.ElementId == atomic.Id);
        atomicLabel.Value = "Rejected"; atomicSelected.Value = true; throwSelected = true;
        ExpectAggregate(graph.Drain);
        var rejectedSemantic = Descendants(composition.SemanticSnapshot()!).Single(node => node.Identity.ElementId == atomic.Id);
        Assert(atomic.Resolve(ProjectionProperties.Text).Value == beforeText && rejectedSemantic.Name == beforeSemantic.Name && rejectedSemantic.Selected == beforeSemantic.Selected,
            "A failed live selectable reader committed label, semantics, or selection before both values resolved.");
        throwSelected = false; atomicSelected.Value = false; atomicSelected.Value = true; graph.Drain();
        var recoveredSemantic = Descendants(composition.SemanticSnapshot()!).Single(node => node.Identity.ElementId == atomic.Id);
        Assert(atomic.Resolve(ProjectionProperties.Text).Value == "Rejected" && recoveredSemantic.Name == "Rejected" && recoveredSemantic.Selected,
            "Recovered live selectable readers did not commit label, semantics, and selection together.");
    }

    private static void FactoryMutationContainment()
    {
        AssertFactoryMutationGuard("present", (_, theme) => _.Present(theme));
        AssertFactoryMutationGuard("behavior", (element, _) => element.AttachBehaviors(new PassiveBehavior()));
        AssertFactoryMutationGuard("variants", (element, _) => element.SetVariants(VariantState.Pressed), (element, theme) => element.Present(theme));
        AssertFactoryMutationGuard("scope-child", (element, _) => element.Scope.CreateChild("foreign-child"));
        AssertFactoryMutationGuard("scope-signal", (element, _) => element.Scope.Signal(1, "foreign-signal"));
        Signal<int>? foreignSignal = null;
        AssertFactoryMutationGuard("signal-write", (_, _) => foreignSignal!.Value = 1, (element, _) => foreignSignal = element.Scope.Signal(0, "foreign-value"));
        AssertFactoryMutationGuard("scope-derived", (element, _) => element.Scope.Derived(() => 1, "foreign-derived"));
        AssertFactoryMutationGuard("scope-effect", (element, _) => element.Scope.Effect(() => { }, "foreign-effect"));
        AssertFactoryMutationGuard("scope-async", (element, _) => element.Scope.Async(token => Task.FromResult(1), "foreign-async"));
        AssertFactoryMutationGuard("scope-own", (element, _) => element.Scope.Own(new ProbeDisposable()));
        AssertFactoryMutationGuard("scope-cleanup", (element, _) => element.Scope.OnDispose(() => { }));
        AssertFactoryMutationGuard("scope-dispose", (element, _) => element.Scope.Dispose());
        AssertFactoryMutationGuard("dispose", (element, _) => element.Dispose(), (element, _) => { element.Composition.Child(element, "foreign-descendant"); });

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "provisional-mutation-root");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var mounted = composition.Mount(composition.Root, theme, context =>
        {
            var root = context.Element("provisional-root");
            root.Present(theme); root.SetVariants(VariantState.Pressed); root.AttachBehaviors(new PassiveBehavior());
            var child = context.Child(root, "provisional-child");
            child.Present(theme); _ = child.Scope.Signal(1, "provisional-signal");
            var scope = child.Scope.CreateChild("provisional-scope");
            _ = scope.Derived(() => 1, "provisional-derived"); _ = scope.Effect(() => { }, "provisional-effect");
            _ = scope.Async(token => Task.FromResult(1), "provisional-async"); scope.Own(new ProbeDisposable()); scope.OnDispose(() => { }); scope.Dispose();
            return root;
        });
        graph.Drain();
        Assert(!mounted.IsDisposed && mounted.Children.Single().HasPresentation, "Provisional element mutations or descendant scopes were rejected.");
    }

    private static void AssertFactoryMutationGuard(string name, Action<Element, ThemeContext> mutation, Action<Element, ThemeContext>? setup = null)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "factory-mutation-" + name);
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var foreign = composition.Child(composition.Root, "foreign");
        setup?.Invoke(foreign, theme);
        var cleanup = 0;
        foreign.Scope.OnDispose(() => cleanup++);
        var active = graph.Signal(false, "factory-mutation-active");
        var region = composition.When(composition.Root, "factory-mutation-region", () => active.Value, context =>
        {
            var provisional = context.Element("provisional");
            mutation(foreign, theme);
            return provisional;
        });
        graph.Drain();
        var dump = composition.Dump(); var topology = graph.Dump(); var children = composition.Root.Children.ToArray();
        active.Value = true;
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == dump && graph.Dump() == topology && composition.Root.Children.SequenceEqual(children) &&
            !foreign.IsDisposed && cleanup == 0 && region.Active is null && region.Region.Children.Count == 0,
            "Foreign " + name + " mutation changed presentation, graph, or lifetime state after factory rollback.");
    }

    private static void NestedFactoryIsolationAndVirtualRows()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "factory-isolation");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var conditionalReads = 0; var keyedReads = 0;
        var conditional = composition.When(composition.Root, "live-conditional", () => { conditionalReads++; return false; }, context => context.Element("live-child"));
        var keyed = composition.ForEach(composition.Root, "live-keyed", () => { keyedReads++; return Array.Empty<int>(); }, value => value, (_, context) => context.Element("live-keyed-child"));
        graph.Drain();
        var before = composition.Dump();
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-conditional"); conditional.Update(true); return root; }));
        Assert(composition.Dump() == before && conditional.Active is null, "An unrelated conditional mutated during an outer recipe.");
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-keyed"); keyed.Update([1]); return root; }));
        Assert(composition.Dump() == before && keyed.Items.Count == 0, "An unrelated keyed region mutated during an outer recipe.");
        var initialConditionalReads = conditionalReads; var initialKeyedReads = keyedReads;
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-conditional-refresh"); conditional.Refresh(); return root; }));
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-keyed-refresh"); keyed.Refresh(); return root; }));
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-conditional-dispose"); conditional.Dispose(); return root; }));
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-keyed-dispose"); keyed.Dispose(); return root; }));
        Assert(conditionalReads == initialConditionalReads && keyedReads == initialKeyedReads && !conditional.IsDisposed && !keyed.IsDisposed && composition.Dump() == before,
            "Foreign refresh evaluated its source or foreign disposal escaped an outer recipe.");

        Controls.Panel(composition.Root, theme, "root");
        var viewport = composition.Child(composition.Root, "viewport");
        _ = Controls.ScrollViewport(viewport, theme, "rows", style: Style.Empty.Set(LayoutProperties.Width, 10f).Set(LayoutProperties.Height, 10f));
        var virtualReads = 0;
        var themed = Controls.VirtualizedList(viewport, theme, "themed-rows", "Rows", () => { virtualReads++; return new[] { 1 }; }, value => value, (value, context) =>
        {
            Assert(ReferenceEquals(context.Theme, theme), "Virtualized row lost its source theme.");
            var row = context.Element("themed-row"); Controls.Text(row, context.Theme, "Row " + value); return row;
        }, 10f);
        graph.Drain(); themed.Realize(new(10, 10, 1));
        Assert(themed.Items.Count == 1 && themed.Items.Single().Resolve(ProjectionProperties.Text).Value == "Row 1", "Annotated virtualized row did not mount through its themed context.");
        var virtualBefore = composition.Dump();
        var initialVirtualReads = virtualReads;
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-virtualized"); themed.Update([1, 2]); return root; }));
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-virtualized-refresh"); themed.Refresh(); return root; }));
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-virtualized-height"); themed.SetRowHeight(12); return root; }));
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-virtualized-realize"); themed.Realize(new(10, 10, 1)); return root; }));
        Expect<InvalidOperationException>(() => composition.Mount(composition.Root, theme, context => { var root = context.Element("outer-virtualized-dispose"); themed.Dispose(); return root; }));
        Assert(composition.Dump() == virtualBefore && virtualReads == initialVirtualReads && !themed.IsDisposed && themed.RowHeight == 10 && themed.SourceCount == 1 && themed.Items.Count == 1,
            "An unrelated virtualized region evaluated, mutated, realized, or disposed during an outer recipe.");

        var failingViewport = composition.Child(composition.Root, "failing-viewport");
        _ = Controls.ScrollViewport(failingViewport, theme, "failing rows", style: Style.Empty.Set(LayoutProperties.Width, 10f).Set(LayoutProperties.Height, 10f));
        Element? provisional = null;
        var failing = Controls.VirtualizedList(failingViewport, theme, "failing-rows", "Rows", () => new[] { 1 }, value => value, (_, context) =>
        {
            provisional = context.Element("failing-row");
            throw new InvalidOperationException("virtual-row-failure");
        }, 10f);
        graph.Drain();
        Expect<InvalidOperationException>(() => failing.Realize(new(10, 10, 1)));
        Assert(provisional!.IsDisposed && failing.Items.Count == 0 && failing.Region.Children.Count == 0, "Throwing virtualized row retained a provisional context or element.");
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
        var disposalCleanup = 0;
        _ = disposal.When(disposal.Root, "dispose-region", () => trigger.Value, context =>
        {
            var child = context.Element("dispose-child");
            child.Scope.OnDispose(() => disposalCleanup++);
            disposal.Dispose();
            return child;
        });
        disposalGraph.Drain();
        trigger.Value = true;
        ExpectAggregate(disposalGraph.Drain);
        Assert(disposal.IsDisposed && disposal.Root.IsDisposed && disposalCleanup == 1 &&
            !disposalGraph.Dump().Contains("dispose-root", StringComparison.Ordinal) && !disposalGraph.Dump().Contains("dispose-region", StringComparison.Ordinal) && !disposalGraph.Dump().Contains("dispose-child", StringComparison.Ordinal),
            "Disposal during a factory did not retire composition-owned scopes/nodes exactly once.");
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
        Assert(composition.Dump() == before && region.Items.Count == 0 && region.Region.Children.Count == 0 && cleanup == 2,
            "Later keyed factories left a disposed provisional root or descendant, or leaked rollback cleanup.");

        RetainedEntryRollback();

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

    private static void RetainedEntryRollback()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1 }, "keyed-retained-rows");
        using var composition = new Composition(graph, "keyed-retained-root");
        Element? retained = null;
        var region = composition.ForEach(composition.Root, "keyed-retained-region", () => rows.Value, value => value, (value, context) =>
        {
            var root = context.Element("keyed-retained-child");
            if (value == 1)
            {
                retained = root;
            }
            else
            {
                retained!.Scope.Dispose();
            }
            return root;
        });
        graph.Drain();
        var dump = composition.Dump(); var topology = graph.Dump();
        rows.Value = [1, 2];
        ExpectAggregate(graph.Drain);
        Assert(region.Items.Count == 1 && ReferenceEquals(region.Items.Single(), retained) && !retained!.IsDisposed &&
            composition.Dump() == dump && graph.Dump() == topology, "Foreign retained-entry disposal changed keyed state after rollback.");
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

    private static IEnumerable<SemanticSnapshot> Descendants(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static void ExpectAggregate(Action action)
    {
        try { action(); throw new InvalidOperationException("Expected aggregate failure."); }
        catch (AggregateException) { }
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
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
    private sealed record KeyPreparationProbe(IReadOnlyList<WeakReference> Payloads, object Root);
    private sealed class MutableKey(int hash)
    {
        internal int Hash { get; set; } = hash;
        public override int GetHashCode() => Hash;
    }
    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0) return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun("recipe", "recipe", 400, 5, 0, "recipe", 0, "recipe#0", request.Direction, request.Language, request.FontSize, 0, request.FontSize, -request.FontSize, 0, request.Text.Length, [glyph]);
            return new("recipe", request.Text.Length, request.FontSize, [run]);
        }
    }
    private sealed class PassiveBehavior : Behavior
    {
        public override string Name => "passive";
        public override void Attach(BehaviorContext context) { }
    }
    private sealed class ProbeDisposable : IDisposable { public void Dispose() { } }
    private sealed class Payload;
}
