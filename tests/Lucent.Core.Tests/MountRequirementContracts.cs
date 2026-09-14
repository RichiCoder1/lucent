using System.Runtime.CompilerServices;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class MountRequirementContracts
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] DeferredEvents =
    [
        "outer-requirement:outer",
        "outer-setup:outer",
        "inner-requirement:inner",
        "inner-setup:inner",
        "body",
    ];
    private static readonly string[] ShadowedValues = ["inner", "outer"];
    private static readonly string[] PublicAdapterEvents = ["placed:mount", "placed:region"];
    private static readonly int[] VirtualRows = [7];
    private static readonly string[] StructuralEvents =
    [
        "layout:placed",
        "first:placed",
        "second:placed",
        "conditional:placed",
        "keyed:placed:3",
        "virtualized:placed:7",
    ];

    [TestMethod]
    public void DeferredProviderResolvesAtPlacementBeforeSetupWithoutAddingARoot()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "provider-deferred");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var outer = new Capability("outer");
        var inner = new Capability("inner");
        var events = new List<string>();
        ReactiveScope? outerOwner = null;
        ReactiveScope? innerOwner = null;
        var outerPlan = ComponentRequirements
            .Context<Capability>(Source("outer"))
            .Select(value =>
            {
                events.Add("outer-requirement:" + value.Name);
                return value;
            });
        var innerPlan = ComponentRequirements
            .Context<Capability>(Source("inner"))
            .Select(value =>
            {
                events.Add("inner-requirement:" + value.Name);
                return value;
            });
        var recipe = Context.Provide(
            outer,
            ComponentRecipe
                .Defer(
                    "outer-component",
                    outerPlan,
                    (owner, value) =>
                    {
                        outerOwner = owner;
                        events.Add("outer-setup:" + value.Name);
                        return Context.Provide(
                            inner,
                            ComponentRecipe.Defer(
                                "inner-component",
                                innerPlan,
                                (nestedOwner, nested) =>
                                {
                                    innerOwner = nestedOwner;
                                    events.Add("inner-setup:" + nested.Name);
                                    return ComponentRecipe.Create(
                                        "authored-root",
                                        (_, _) => events.Add("body")
                                    );
                                }
                            )
                        );
                    }
                )
                .Named("stable-root")
        );

        var root = composition.Mount(composition.Root, theme, recipe);

        Assert.AreEqual("stable-root", root.Name);
        Assert.AreSame(root.Scope, outerOwner);
        Assert.AreSame(outerOwner, innerOwner);
        Assert.AreEqual(0, root.Children.Count);
        CollectionAssert.AreEqual(DeferredEvents, events);
    }

    [TestMethod]
    public void ExactTypesShadowNearestAndRemainIsolatedAcrossSiblings()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "provider-exact");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var outer = new Capability("outer");
        var inner = new Capability("inner");
        var seen = new List<string>();
        var required = Required<Capability>("capability", value => seen.Add(value.Name));
        var first = composition.Mount(
            composition.Root,
            theme,
            Context.Provide(outer, Context.Provide(inner, required))
        );
        var second = composition.Mount(composition.Root, theme, Context.Provide(outer, required));

        CollectionAssert.AreEqual(ShadowedValues, seen);
        var before = composition.Dump();
        var concreteOnly = Context.Provide(
            new ConcreteCapability(),
            new ContextProviderSource(
                typeof(ConcreteCapability),
                "Test.ConcreteCapability",
                "ProviderExact.lui",
                4,
                3
            ),
            Required<ICapability>("interface", _ => Assert.Fail("assignable fallback resolved"))
        );
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(composition.Root, theme, concreteOnly)
        );
        StringAssert.Contains(failure.Message, "interface");
        StringAssert.Contains(
            failure.Message,
            $"mount {composition.Root.Name}#{composition.Root.Id}"
        );
        StringAssert.Contains(failure.Message, "Test.ConcreteCapability");
        StringAssert.Contains(failure.Message, "ProviderExact.lui:4:3");
        Assert.AreEqual(before, composition.Dump());
        Assert.IsFalse(first.IsDisposed);
        Assert.IsFalse(second.IsDisposed);
    }

    [TestMethod]
    public void TransparentProviderAppliesAuthoringToTheRealLeafRootOnce()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "provider-authoring");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var recipe = Context
            .Provide(
                new Capability("authoring"),
                Components.Layout(ComponentContent.Empty, style: Style.Empty.Width(37)).Recipe
            )
            .Named("provided-layout");

        var root = composition.Mount(composition.Root, theme, recipe);

        Assert.AreEqual("provided-layout", root.Name);
        Assert.AreEqual(37f, root.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(0, root.Children.Count);
    }

    [TestMethod]
    public void PublicMountAndRegionAdaptersPreserveParentPlacementEnvironment()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "public-placement-adapters");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var alternateTheme = new ThemeContext(composition.Root.Scope, new Theme("alternate"));
        var capability = new Capability("placed");
        var seen = new List<string>();
        var providedRoot = composition.Mount(
            composition.Root,
            theme,
            Context.Provide(
                capability,
                ComponentRecipe.Create("provided-parent", static (_, _) => { })
            )
        );

        _ = composition.Mount(
            providedRoot,
            alternateTheme,
            ComponentRecipe.Defer(
                "public-child",
                ComponentRequirements.Context<Capability>(Source("public-child")),
                (_, value) =>
                    ComponentRecipe.Create(
                        "public-child-root",
                        (context, _) =>
                        {
                            Assert.AreSame(alternateTheme, context.Theme);
                            seen.Add(value.Name + ":mount");
                        }
                    )
            )
        );
        var active = false;
        var region = composition.When(
            providedRoot,
            alternateTheme,
            "public-provider-region",
            () => active,
            context =>
                Required<Capability>("public-region", value => seen.Add(value.Name + ":region"))
                    .Mount(context)
        );
        active = true;
        region.Update(true);

        CollectionAssert.AreEqual(PublicAdapterEvents, seen);
    }

    [TestMethod]
    public void LayoutSplitPaneAndRetainedRegionsForwardThePlacementEnvironment()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "provider-structure");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var split = new SplitPaneState(
            composition.Root.Scope,
            initialExtent: 20,
            minimumFirst: 0,
            minimumSecond: 0,
            name: "provider-split"
        );
        var capability = new Capability("placed");
        var active = graph.Signal(false, "provider-active");
        var rows = graph.Signal(Array.Empty<int>(), "provider-rows");
        var seen = new List<string>();
        ConditionalRegion? conditional = null;
        KeyedRegion<int, int>? keyed = null;
        VirtualizedRegion<int, int>? virtualized = null;
        var host = ComponentRecipe.Create(
            "structure-host",
            (context, root) =>
            {
                Controls.Panel(root, context.Theme, "provider structure");
                context.Mount(
                    root,
                    Components.Layout(
                        ComponentContent.Create([
                            Required<Capability>(
                                "layout",
                                value => seen.Add("layout:" + value.Name)
                            ),
                        ])
                    )
                );
                context.Mount(
                    root,
                    Components.SplitPane(
                        ComponentContent.Create([
                            Required<Capability>("first", value => seen.Add("first:" + value.Name)),
                        ]),
                        ComponentContent.Create([
                            Required<Capability>(
                                "second",
                                value => seen.Add("second:" + value.Name)
                            ),
                        ]),
                        split
                    )
                );
                conditional = context.When(
                    root,
                    "provider-conditional",
                    () => active.Value,
                    mounted =>
                        Required<Capability>(
                                "conditional",
                                value => seen.Add("conditional:" + value.Name)
                            )
                            .Mount(mounted)
                );
                keyed = context.ForEach(
                    root,
                    "provider-keyed",
                    () => rows.Value,
                    value => value,
                    (value, mounted) =>
                        Required<Capability>(
                                "keyed",
                                item => seen.Add("keyed:" + item.Name + ":" + value.Value)
                            )
                            .Mount(mounted)
                );
                var viewport = context.Child(root, "provider-viewport");
                _ = Controls.ScrollViewport(
                    viewport,
                    context.Theme,
                    "provider rows",
                    style: Style.Empty.Width(20).Height(20)
                );
                virtualized = context.Virtualize(
                    viewport,
                    "provider-virtualized",
                    () => VirtualRows,
                    value => value,
                    (value, mounted) =>
                        Required<Capability>(
                                "virtualized",
                                item => seen.Add("virtualized:" + item.Name + ":" + value.Value)
                            )
                            .Mount(mounted),
                    10
                );
                Controls.List(virtualized.Region, context.Theme, "provider rows");
                virtualized.Configure();
            }
        );

        _ = composition.Mount(composition.Root, theme, Context.Provide(capability, host));
        graph.Drain();
        active.Value = true;
        rows.Value = [3];
        graph.Drain();
        virtualized!.Realize(new(20, 20, 1));

        CollectionAssert.IsSubsetOf(StructuralEvents, seen);
        Assert.IsNotNull(conditional!.Active);
        Assert.AreEqual(1, keyed!.Items.Count);
        Assert.AreEqual(1, virtualized.Items.Count);
    }

    [TestMethod]
    public void MissingRequirementAtVirtualizedRealizationRollsBackBeforeInitialization()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "virtualized-missing-context");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        VirtualizedRegion<int, int>? virtualized = null;
        var initialized = false;
        var host = ComponentRecipe.Create(
            "missing-context-host",
            (context, root) =>
            {
                Controls.ScrollViewport(
                    root,
                    context.Theme,
                    "missing context rows",
                    style: Style.Empty.Width(20).Height(20)
                );
                virtualized = context.Virtualize(
                    root,
                    "missing-context-rows",
                    () => VirtualRows,
                    value => value,
                    (_, mounted) =>
                        Required<Capability>("virtualized-required", _ => initialized = true)
                            .Mount(mounted),
                    10
                );
                Controls.List(virtualized.Region, context.Theme, "missing context rows");
                virtualized.Configure();
            }
        );
        composition.Mount(composition.Root, theme, host);
        graph.Drain();
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            virtualized!.Realize(new(20, 20, 1))
        );
        StringAssert.Contains(failure.Message, "virtualized-required");
        Assert.IsFalse(initialized);
        Assert.AreEqual(0, virtualized!.Items.Count);
        Assert.AreEqual(0, virtualized.Region.Children.Count);
    }

    [TestMethod]
    public void RequirementPlansRejectInvalidOrderingAndDuplicateExactTypes()
    {
        var context = ComponentRequirements.Context<Capability>(Source("first"));
        var duplicate = Assert.ThrowsExactly<InvalidOperationException>(() =>
            context.AndContext<Capability>(Source("second"))
        );
        StringAssert.Contains(duplicate.Message, "first");
        StringAssert.Contains(duplicate.Message, "second");

        var service = context.AndService<ServiceA>(Source("service"));
        var ordering = Assert.ThrowsExactly<InvalidOperationException>(() =>
            service.AndContext<int>(Source("late-context"))
        );
        StringAssert.Contains(ordering.Message, "before application service");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ComponentRequirements
                .Context<ServiceA>(Source("context-service"))
                .AndService<ServiceA>(Source("injected-service"))
        );
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            context.Select(value => value).AndService<ServiceA>(Source("after-map"))
        );
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            context.Select(value => value).Select(value => value)
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ComponentRequirementSource("value", "Test.Value", "../secret.lui", 1, 1)
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ComponentRequirementSource("value", "Test.Value", "C:/secret.lui", 1, 1)
        );
    }

    [TestMethod]
    public void ServiceRequirementsAreBorrowedCachedAndRevokedByTheirLifecycleBinding()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "service-binding");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var session = new ApplicationSession(
            "Binding test",
            composition,
            theme,
            new EmptyLifecycle()
        );
        var service = new ServiceA();
        var source = new ExactServiceSource(service);
        var binding = session.CreateServiceBinding(source);
        Assert.ThrowsExactly<InvalidOperationException>(() => session.CreateServiceBinding(source));
        var seen = new List<ServiceA>();
        ConditionalRegion? late = null;
        var required = ComponentRecipe.Defer(
            "service-consumer",
            ComponentRequirements.Service<ServiceA>(Source("service")),
            (owner, value) =>
            {
                seen.Add(value);
                owner.OnDispose(() => seen.Add(value));
                return ComponentRecipe.Create(
                    "service-root",
                    (context, root) =>
                        late = context.When(
                            root,
                            "late-service",
                            static () => false,
                            mounted =>
                                ComponentRecipe
                                    .Create("late-plain-root", static (_, _) => { })
                                    .Mount(mounted)
                        )
                );
            }
        );
        var attached = binding.Attach(required);
        Assert.ThrowsExactly<InvalidOperationException>(() => binding.Attach(required));

        var serviceRoot = composition.Mount(composition.Root, theme, attached);
        graph.Drain();
        Assert.AreEqual(1, source.Resolutions);
        Assert.AreSame(service, seen.Single());
        binding.StopAccepting();
        var stoppedPublicMount = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(
                serviceRoot,
                theme,
                ComponentRecipe.Create("late-public-root", static (_, _) => { })
            )
        );
        StringAssert.Contains(stoppedPublicMount.Message, "no longer accepting mounts");
        var stopped = Assert.ThrowsExactly<InvalidOperationException>(() => late!.Update(true));
        StringAssert.Contains(stopped.Message, "no longer accepting mounts");
        Assert.IsNull(late!.Active);
        Assert.AreEqual(1, source.Resolutions);
        composition.Dispose();
        Assert.AreEqual(2, seen.Count);
        Assert.AreSame(service, seen[0]);
        Assert.AreSame(service, seen[1]);
        Assert.IsFalse(service.IsDisposed);
        binding.Revoke();
        binding.Revoke();
        Assert.IsFalse(service.IsDisposed);
        service.Dispose();
        theme.Dispose();
    }

    [TestMethod]
    public void ServiceFailureDoesNotInitializeOrDisposeEarlierContainerValues()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "service-failure");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var session = new ApplicationSession(
            "Failure test",
            composition,
            theme,
            new EmptyLifecycle()
        );
        var service = new ServiceA();
        var source = new ExactServiceSource(service, failServiceB: true);
        var binding = session.CreateServiceBinding(source);
        var initialized = false;
        var plan = ComponentRequirements
            .Service<ServiceA>(Source("first-service"))
            .AndService<ServiceB>(Source("second-service"));
        var recipe = binding.Attach(
            ComponentRecipe.Defer(
                "failing-services",
                plan,
                (_, _) =>
                {
                    initialized = true;
                    return ComponentRecipe.Create("unreachable", static (_, _) => { });
                }
            )
        );

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(composition.Root, theme, recipe)
        );
        StringAssert.Contains(failure.ToString(), "second-service");
        Assert.IsFalse(initialized);
        Assert.AreEqual(2, source.Resolutions);
        Assert.IsFalse(service.IsDisposed);
        Assert.AreEqual(0, composition.Root.Children.Count);
        binding.StopAccepting();
        composition.Dispose();
        binding.Revoke();
        service.Dispose();
        theme.Dispose();
    }

    [TestMethod]
    public void ServiceRequirementsFailWithoutBindingOrThroughOrdinaryContext()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "missing-binding");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var service = new ServiceA();
        var source = new ExactServiceSource(service);
        var required = ComponentRecipe.Defer(
            "missing-service-binding",
            ComponentRequirements.Service<ServiceA>(Source("service")),
            (_, _) => ComponentRecipe.Create("unreachable", static (_, _) => { })
        );

        var missing = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(composition.Root, theme, required)
        );
        StringAssert.Contains(missing.Message, "application service binding");
        var contextual = Context.Provide<IComponentServiceSource>(source, required);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(composition.Root, theme, contextual)
        );
        Assert.AreEqual(0, source.Resolutions);
    }

    [TestMethod]
    public void MountShapesReportAllocationsAndStableWorkPerformsNoFurtherLookup()
    {
        const int Iterations = 32;
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "mount-shape-measurements");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var session = new ApplicationSession(
            "Mount shape measurements",
            composition,
            theme,
            new EmptyLifecycle()
        );
        using var service = new ServiceA();
        var source = new ExactServiceSource(service);
        var binding = session.CreateServiceBinding(source);
        static ComponentRecipe Leaf(string name) =>
            ComponentRecipe.Create(
                name,
                (context, root) => Controls.Panel(root, context.Theme, name)
            );
        var leaf = Leaf("measured-leaf");
        var deferred = ComponentRecipe.Defer("measured-deferred", _ => leaf);
        var required = Context.Provide(
            new Capability("measured"),
            ComponentRecipe.Defer(
                "measured-required",
                ComponentRequirements
                    .Context<Capability>(Source("measured-context"))
                    .AndService<ServiceA>(Source("measured-service")),
                (_, _) => leaf
            )
        );
        long leafBytes = 0;
        long deferredBytes = 0;
        long requiredBytes = 0;
        var host = binding.Attach(
            ComponentRecipe.Create(
                "measurement-host",
                (context, root) =>
                {
                    Controls.Panel(root, context.Theme, "measurement host");
                    _ = context.Mount(root, leaf);
                    _ = context.Mount(root, deferred);
                    _ = context.Mount(root, required);
                    leafBytes = MeasureMounts(Iterations, () => context.Mount(root, leaf));
                    deferredBytes = MeasureMounts(Iterations, () => context.Mount(root, deferred));
                    requiredBytes = MeasureMounts(Iterations, () => context.Mount(root, required));
                }
            )
        );

        _ = composition.Mount(composition.Root, theme, host);
        Assert.AreEqual(Iterations + 1, source.Resolutions);
        using (SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper())) { }
        var resolvedBeforeStableWork = source.Resolutions;
        composition.Flush();
        using (SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper())) { }
        Assert.AreEqual(resolvedBeforeStableWork, source.Resolutions);
        TestContext.WriteLine(
            $"Mount allocations ({Iterations} mounts): leaf={leafBytes}, deferred={deferredBytes}, contextService={requiredBytes}; exactResolutions={source.Resolutions}; stableAdditionalResolutions={source.Resolutions - resolvedBeforeStableWork}"
        );
        binding.StopAccepting();
        composition.Dispose();
        binding.Revoke();
        theme.Dispose();
    }

    [TestMethod]
    public void DisposedTemporaryThemesAreNotRetainedByMountEnvironmentReuse()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "temporary-theme-lifetime");
        var temporary = MountAndReleaseTemporaryTheme(composition);

        for (var attempt = 0; attempt < 3 && temporary.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(temporary.IsAlive, "A disposed temporary theme remained strongly retained.");
    }

    [TestMethod]
    public void RemovedProviderSubtreesLeaveNoRetainedProviderValueRegistryEntry()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "provider-value-lifetime");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var value = MountAndReleaseProviderValue(composition, theme);

        for (var attempt = 0; attempt < 3 && value.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(value.IsAlive, "A removed provider value remained retained by composition.");
    }

    [TestMethod]
    public void ProviderAndRequirementDiagnosticsAreExactDeterministicAndValueFree()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "context-diagnostics");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var outer = new SecretCapability("outer-secret");
        var inner = new SecretCapability("inner-secret");
        var requirement = ComponentRecipe.Defer(
            "diagnostic-consumer",
            ComponentRequirements.Context<SecretCapability>(
                new("navigation", "Test.SecretCapability", "Controls\\IssueToolbar.lui", 8, 13)
            ),
            (_, _) => ComponentRecipe.Create("diagnostic-leaf", static (_, _) => { })
        );
        var nested = Context.Provide(
            inner,
            new(typeof(SecretCapability), "Test.SecretCapability", "Shell/Inner.lui", 9, 4),
            requirement
        );
        var host = Context.Provide(
            outer,
            new(typeof(SecretCapability), "Test.SecretCapability", "App.lui", 14, 7),
            ComponentRecipe.Create(
                "diagnostic-host",
                (context, root) => _ = context.Mount(root, nested)
            )
        );

        var mounted = composition.Mount(composition.Root, theme, host);
        var dump = composition.ContextDump();
        StringAssert.Contains(dump, "provider owner=1");
        StringAssert.Contains(dump, "provider owner=2");
        StringAssert.Contains(dump, "shadowed=1");
        StringAssert.Contains(dump, "kind=context");
        StringAssert.Contains(dump, "member=\"navigation\"");
        StringAssert.Contains(dump, "Controls/IssueToolbar.lui:8:13");
        Assert.IsFalse(dump.Contains("outer-secret", StringComparison.Ordinal));
        Assert.IsFalse(dump.Contains("inner-secret", StringComparison.Ordinal));

        mounted.Dispose();
        Assert.AreEqual("context\n", composition.ContextDump());
    }

    [TestMethod]
    public void ProviderAndGeneratedRequirementMetadataRejectUnsafeOrConflictingDescriptors()
    {
        var source = new ContextProviderSource(
            typeof(Capability),
            "Test.Capability",
            "Screens\\Home.lui",
            3,
            5
        );
        Assert.AreEqual("Screens/Home.lui", source.ProjectRelativePath);
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ContextProviderSource(typeof(Capability), "Test.Capability", "../Home.lui", 3, 5)
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ContextProviderSource(typeof(Capability), "Test.Capability", "C:/Home.lui", 3, 5)
        );
        var metadata = new ComponentRequirementAttribute(
            typeof(Capability),
            ComponentRequirementKind.Context,
            "navigation",
            "Controls\\Toolbar.lui",
            6,
            9
        );
        Assert.AreEqual(typeof(Capability), metadata.ExactType);
        Assert.AreEqual(ComponentRequirementKind.Context, metadata.Kind);
        Assert.AreEqual("Controls/Toolbar.lui", metadata.ProjectRelativePath);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new ComponentRequirementAttribute(
                typeof(Capability),
                (ComponentRequirementKind)99,
                "navigation",
                "Toolbar.lui",
                1,
                1
            )
        );

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "conflicting-provider-metadata");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var conflicting = Context.Provide(
            new Capability("value"),
            new(typeof(string), "System.String", "App.lui", 1, 1),
            ComponentRecipe.Create("unreachable", static (_, _) => { })
        );
        var failure = Assert.ThrowsExactly<ArgumentException>(() =>
            composition.Mount(composition.Root, theme, conflicting)
        );
        StringAssert.Contains(failure.Message, "metadata declares exact type");
        Assert.AreEqual(0, composition.Root.Children.Count);
        var themeOverride = Context.Provide(
            theme,
            new ContextProviderSource(
                typeof(ThemeContext),
                "Lucent.Core.ThemeContext",
                "App.lui",
                2,
                1
            ),
            ComponentRecipe.Create("unreachable-theme", static (_, _) => { })
        );
        var themeFailure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            composition.Mount(composition.Root, theme, themeOverride)
        );
        StringAssert.Contains(themeFailure.Message, "framework-controlled");
        Assert.AreEqual(0, composition.Root.Children.Count);
    }

    [TestMethod]
    public void PopupBorrowsItsOriginRequirementsWithADistinctThemeAndLifetime()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "popup-requirement-owner");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var session = new ApplicationSession(
            "Popup requirement owner",
            composition,
            theme,
            new EmptyLifecycle()
        );
        var capability = new Capability("popup-origin");
        using var service = new ServiceA();
        var source = new ExactServiceSource(service);
        var binding = session.CreateServiceBinding(source);
        var target = composition.Mount(
            composition.Root,
            theme,
            binding.Attach(
                Context.Provide(
                    capability,
                    ComponentRecipe.Create(
                        "popup-origin",
                        (context, root) => Controls.Panel(root, context.Theme, "popup origin")
                    )
                )
            )
        );
        ThemeContext? popupTheme = null;
        Capability? resolvedCapability = null;
        ServiceA? resolvedService = null;
        var requirements = ComponentRequirements
            .Context<Capability>(Source("popup-context"))
            .AndService<ServiceA>(Source("popup-service"));
        var content = ComponentRecipe.Defer(
            "popup-consumer",
            requirements,
            (_, values) =>
                ComponentRecipe.Create(
                    "popup-leaf",
                    (context, root) =>
                    {
                        popupTheme = context.Theme;
                        resolvedCapability = values.Previous;
                        resolvedService = values.Value;
                        Controls.Panel(root, context.Theme, "popup leaf");
                    }
                )
        );
        using var request = new ContextMenuRequest(
            composition,
            new(composition.Epoch, target.Id),
            new(0, 0, 1, 1),
            content,
            theme
        );

        var popup = request.CreateComposition();

        Assert.AreSame(capability, resolvedCapability);
        Assert.AreSame(service, resolvedService);
        Assert.AreEqual(1, source.Resolutions);
        Assert.IsNotNull(popupTheme);
        Assert.AreNotSame(theme, popupTheme);
        Assert.AreSame(popup.Root.Scope, popupTheme.Scope);
        Assert.AreSame(theme.Theme, popupTheme.Theme);
        binding.StopAccepting();
        using var stoppedRequest = new ContextMenuRequest(
            composition,
            new(composition.Epoch, target.Id),
            new(0, 0, 1, 1),
            ComponentRecipe.Create("stopped-popup", static (_, _) => { }),
            theme
        );
        var stopped = Assert.ThrowsExactly<InvalidOperationException>(
            stoppedRequest.CreateComposition
        );
        StringAssert.Contains(stopped.Message, "no longer accepting mounts");
        composition.Dispose();
        Assert.IsTrue(popup.IsDisposed, "Disposing the origin left its popup borrower alive.");
        binding.Revoke();
        theme.Dispose();
    }

    [TestMethod]
    public void PopupBorrowerBlocksRevocationUntilReleasedAndFailureRollsBackItsLease()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "popup-borrower-lifecycle");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var session = new ApplicationSession(
            "Popup borrower lifecycle",
            composition,
            theme,
            new EmptyLifecycle()
        );
        using var service = new ServiceA();
        var binding = session.CreateServiceBinding(new ExactServiceSource(service));
        _ = composition.Mount(
            composition.Root,
            theme,
            binding.Attach(ComponentRecipe.Create("service-root", static (_, _) => { }))
        );
        var borrower = new Composition(graph, "detached-popup-borrower");
        borrower.Root.Scope.Own(binding.BorrowForPopup(borrower));

        binding.StopAccepting();
        composition.Dispose();
        var active = Assert.ThrowsExactly<InvalidOperationException>(binding.Revoke);
        StringAssert.Contains(active.Message, "popup compositions");
        borrower.Dispose();
        binding.Revoke();
        theme.Dispose();

        var failedComposition = new Composition(graph, "popup-failure-owner");
        var failedTheme = new ThemeContext(failedComposition.Root.Scope, ControlThemes.Light);
        var failedSession = new ApplicationSession(
            "Popup failure owner",
            failedComposition,
            failedTheme,
            new EmptyLifecycle()
        );
        var failedBinding = failedSession.CreateServiceBinding(new ExactServiceSource(service));
        var target = failedComposition.Mount(
            failedComposition.Root,
            failedTheme,
            failedBinding.Attach(
                ComponentRecipe.Create(
                    "failure-target",
                    (context, root) => Controls.Panel(root, context.Theme, "failure target")
                )
            )
        );
        using var request = new ContextMenuRequest(
            failedComposition,
            new(failedComposition.Epoch, target.Id),
            new(0, 0, 1, 1),
            ComponentRecipe.Create(
                "failing-popup",
                static (_, _) => throw new InvalidOperationException("popup-mount-failed")
            ),
            failedTheme
        );

        var failure = Assert.ThrowsExactly<InvalidOperationException>(request.CreateComposition);
        StringAssert.Contains(failure.Message, "popup-mount-failed");
        failedBinding.StopAccepting();
        failedComposition.Dispose();
        failedBinding.Revoke();
        failedTheme.Dispose();
    }

    [TestMethod]
    public void SubmenuBorrowsTheTriggerPlacementAndPreservesNearestContextShadowing()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "submenu-placement-owner");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var outer = new Capability("outer-popup");
        var inner = new Capability("inner-submenu");
        Capability? resolved = null;
        var target = composition.Mount(
            composition.Root,
            theme,
            Context.Provide(
                outer,
                Components.Button("Target", static () => { }, Style.Empty.Height(40))
            )
        );
        var content = Components.Menu([
            Context.Provide(
                inner,
                Components.MenuSubmenu(
                    "More",
                    () =>
                        Components.Menu([
                            Required<Capability>("submenu-context", value => resolved = value),
                        ])
                )
            ),
        ]);
        using var request = new ContextMenuRequest(
            composition,
            new(composition.Epoch, target.Id),
            new(0, 0, 1, 1),
            content,
            theme
        );
        var popup = request.CreateComposition();
        popup.Flush();
        var menu = popup.Root.Children.Single();
        var trigger = menu.Children.Single(child =>
            child.StandardMenuPart == StandardMenuPart.Submenu
        );

        Assert.IsTrue(request.OpenSubmenu(new(popup.Epoch, trigger.Id), focusFirst: false));
        Assert.AreSame(inner, resolved);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MountAndReleaseTemporaryTheme(Composition composition)
    {
        var owner = composition.Child(composition.Root, "temporary-theme-owner");
        var theme = new ThemeContext(owner.Scope, ControlThemes.Light);
        _ = composition.Mount(
            owner,
            theme,
            ComponentRecipe.Create("temporary-theme-root", static (_, _) => { })
        );
        owner.Dispose();
        return new WeakReference(theme);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MountAndReleaseProviderValue(
        Composition composition,
        ThemeContext theme
    )
    {
        var value = new SecretCapability("temporary-provider-value");
        var reference = new WeakReference(value);
        var mounted = composition.Mount(
            composition.Root,
            theme,
            Context.Provide(
                value,
                new ContextProviderSource(
                    typeof(SecretCapability),
                    "Test.SecretCapability",
                    "TemporaryProvider.lui",
                    1,
                    1
                ),
                ComponentRecipe.Create("temporary-provider-root", static (_, _) => { })
            )
        );
        mounted.Dispose();
        return reference;
    }

    private static long MeasureMounts(int iterations, Action mount)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < iterations; index++)
            mount();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static ComponentRecipe Required<T>(string member, Action<T> accept) =>
        ComponentRecipe.Defer(
            "requires-" + member,
            ComponentRequirements.Context<T>(Source(member)),
            (_, value) =>
            {
                accept(value);
                return ComponentRecipe.Create(member + "-root", static (_, _) => { });
            }
        );

    private static ComponentRequirementSource Source(string member) =>
        new(member, "Test." + member, "MountRequirementContracts.lui", 1, 1);

    private interface ICapability { }

    private sealed class ConcreteCapability : ICapability { }

    private sealed record Capability(string Name);

    private sealed class SecretCapability(string secret)
    {
        public override string ToString() => secret;
    }

    private sealed class ServiceA : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class ServiceB { }

    private sealed class ExactServiceSource(ServiceA service, bool failServiceB = false)
        : IComponentServiceSource
    {
        public int Resolutions { get; private set; }

        public T Resolve<T>()
            where T : class
        {
            Resolutions++;
            if (typeof(T) == typeof(ServiceA))
                return (T)(object)service;
            if (typeof(T) == typeof(ServiceB) && failServiceB)
                throw new InvalidOperationException("service-b-failed");
            throw new InvalidOperationException("No exact service: " + typeof(T).FullName);
        }
    }

    private sealed class EmptyLifecycle : IApplicationLifecycle
    {
        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session) =>
            throw new NotSupportedException();

        public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
