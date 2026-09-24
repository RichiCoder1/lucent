using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Core;
using Lucent.IssueBrowser;
using Lucent.Renderer.Skia;

internal static partial class SceneProjectionProbe
{
    private const int WarmupSamples = 10;
    private const int Samples = 40;

    internal static int Run()
    {
        var observations = new List<ProjectionObservation>();
        var semanticObservations = new List<SemanticObservation>();
        string? incompleteScenario = null;
        ProjectionSample[]? incompleteRawSamples = null;
        int? incompleteCompletedSamples = null;
        Exception? failure = null;
        try
        {
            incompleteScenario = "simple-controls";
            using (var simple = CreateSemanticFixture("simple-controls", 4, SemanticKind.Simple))
                semanticObservations.Add(
                    MeasureSemantics(simple, count => incompleteCompletedSamples = count)
                );
            incompleteScenario = "metadata-change";
            using (
                var metadata = CreateSemanticFixture("metadata-change", 1, SemanticKind.Metadata)
            )
                semanticObservations.Add(
                    MeasureSemantics(metadata, count => incompleteCompletedSamples = count)
                );
            incompleteScenario = "text-caret-change";
            using (var text = CreateSemanticFixture("text-caret-change", 1, SemanticKind.Text))
                semanticObservations.Add(
                    MeasureSemantics(text, count => incompleteCompletedSamples = count)
                );
            incompleteScenario = "deep-ancestor-reconciliation";
            using (
                var deep = CreateSemanticFixture(
                    "deep-ancestor-reconciliation",
                    1,
                    SemanticKind.Metadata,
                    depth: 128
                )
            )
                semanticObservations.Add(
                    MeasureSemantics(deep, count => incompleteCompletedSamples = count)
                );
            incompleteScenario = "wide-unchanged";
            using (var wide = CreateSyntheticFixture("wide", 500, depth: 1))
                observations.Add(
                    Measure(
                        wide,
                        "wide-unchanged",
                        _ => new(1200, 800, 1),
                        onPartial: samples => incompleteRawSamples = samples
                    )
                );
            incompleteScenario = "deep-unchanged";
            using (var deep = CreateSyntheticFixture("deep", breadth: 1, depth: 128))
                observations.Add(
                    Measure(
                        deep,
                        "deep-unchanged",
                        _ => new(600, 800, 1),
                        onPartial: samples => incompleteRawSamples = samples
                    )
                );
            incompleteScenario = "issue-browser-setup";
            using (var browser = CreateIssueBrowserFixture())
            {
                incompleteScenario = "issue-browser-unchanged";
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-unchanged",
                        _ => new(1120, 760, 1),
                        onPartial: samples => incompleteRawSamples = samples
                    )
                );
                incompleteScenario = "issue-browser-virtualized-list-input-change";
                semanticObservations.Add(
                    MeasureSemantics(
                        browser,
                        "issue-browser-virtualized-list-input-change",
                        browser.ToggleInput,
                        ancestorDepth: 0,
                        onPartial: count => incompleteCompletedSamples = count
                    )
                );
                incompleteScenario = "issue-browser-same-wide-bucket";
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-same-wide-bucket",
                        index => new((index & 1) == 0 ? 1100 : 1120, 760, 1),
                        onPartial: samples => incompleteRawSamples = samples
                    )
                );
                incompleteScenario = "issue-browser-breakpoint-change";
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-breakpoint-change",
                        index => new((index & 1) == 0 ? 760 : 1120, 760, 1),
                        onPartial: samples => incompleteRawSamples = samples
                    )
                );
                incompleteScenario = "issue-browser-scale-change";
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-scale-change",
                        index => new(1120, 760, (index & 1) == 0 ? 1 : 1.5f),
                        onPartial: samples => incompleteRawSamples = samples
                    )
                );
                incompleteScenario = "issue-browser-scroll-change";
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-scroll-change",
                        _ => new(1120, 760, 1),
                        browser.ToggleScroll,
                        samples => incompleteRawSamples = samples
                    )
                );
                incompleteScenario = "issue-browser-input-change";
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-input-change",
                        _ => new(1120, 760, 1),
                        browser.ToggleInput,
                        samples => incompleteRawSamples = samples
                    )
                );
                incompleteScenario = "issue-browser-teardown";
            }
        }
        catch (Exception error)
        {
            failure = error;
            Console.Error.WriteLine("Lucent scene projection probe: FAIL: " + error);
        }
        try
        {
            var report = new ProjectionReport(
                1,
                failure is null ? "complete" : "failed",
                failure is null ? null : incompleteScenario,
                failure?.ToString(),
                incompleteRawSamples,
                incompleteCompletedSamples ?? incompleteRawSamples?.Length,
                Environment.MachineName,
                RuntimeInformation.OSDescription,
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.ProcessArchitecture.ToString(),
                typeof(SceneProjectionProbe)
                    .Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()
                    ?.Configuration
                    ?? "unknown",
                WarmupSamples,
                Samples,
                observations,
                semanticObservations
            );
            Console.WriteLine(
                JsonSerializer.Serialize(report, ProjectionJsonContext.Default.ProjectionReport)
            );
            return failure is null ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Lucent scene projection probe report failed: " + error);
            return 1;
        }
    }

    private static SemanticObservation MeasureSemantics(
        SemanticFixture fixture,
        Action<int>? onPartial = null
    ) =>
        MeasureSemantics(
            fixture.Graph,
            fixture.Composition,
            fixture.Scenario,
            fixture.Mutate,
            fixture.AncestorDepth,
            fixture.RealizedRows,
            onPartial
        );

    private static SemanticObservation MeasureSemantics(
        Fixture fixture,
        string scenario,
        Action<int> mutate,
        int ancestorDepth,
        Action<int>? onPartial = null
    ) =>
        MeasureSemantics(
            fixture.Graph,
            fixture.Composition,
            scenario,
            mutate,
            ancestorDepth,
            fixture.RealizedRows,
            onPartial
        );

    private static SemanticObservation MeasureSemantics(
        ReactiveGraph graph,
        Composition composition,
        string scenario,
        Action<int> mutate,
        int ancestorDepth,
        Func<int> realizedRows,
        Action<int>? onPartial
    )
    {
        for (var index = 0; index < WarmupSamples; index++)
        {
            mutate(index);
            graph.Drain();
            _ = composition.SemanticSnapshot();
        }

        var updateElapsed = new double[Samples];
        var projectionElapsed = new double[Samples];
        var allocated = new long[Samples];
        var generationChanges = new int[Samples];
        var nodeCounts = new int[Samples];
        var prior = Generations(composition.SemanticSnapshot());
        var completed = 0;
        try
        {
            for (var index = 0; index < Samples; index++)
            {
                var allocationStart = GC.GetAllocatedBytesForCurrentThread();
                var updateStarted = Stopwatch.GetTimestamp();
                mutate(index + WarmupSamples);
                graph.Drain();
                updateElapsed[index] = Stopwatch.GetElapsedTime(updateStarted).TotalMilliseconds;
                var projectionStarted = Stopwatch.GetTimestamp();
                var snapshot = composition.SemanticSnapshot();
                projectionElapsed[index] = Stopwatch
                    .GetElapsedTime(projectionStarted)
                    .TotalMilliseconds;
                allocated[index] = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                var current = Generations(snapshot);
                generationChanges[index] = current.Count(pair =>
                    !prior.TryGetValue(pair.Key, out var generation) || generation != pair.Value
                );
                nodeCounts[index] = current.Count;
                prior = current;
                completed++;
            }
        }
        catch
        {
            onPartial?.Invoke(completed);
            throw;
        }
        Array.Sort(updateElapsed);
        Array.Sort(projectionElapsed);
        Array.Sort(allocated);
        return new(
            scenario,
            nodeCounts.Max(),
            realizedRows(),
            ancestorDepth,
            Samples,
            generationChanges.Sum(),
            generationChanges.Max(),
            updateElapsed.Average(),
            Percentile(updateElapsed, 0.5),
            Percentile(updateElapsed, 0.95),
            projectionElapsed.Average(),
            Percentile(projectionElapsed, 0.5),
            Percentile(projectionElapsed, 0.95),
            allocated.Average(),
            Percentile(allocated, 0.5),
            Percentile(allocated, 0.95)
        );
    }

    private static Dictionary<long, long> Generations(SemanticSnapshot? snapshot) =>
        snapshot is null
            ? []
            : Flatten(snapshot)
                .ToDictionary(node => node.Identity.ElementId, node => node.Identity.Generation);

    private static SemanticFixture CreateSemanticFixture(
        string scenario,
        int semanticNodes,
        SemanticKind kind,
        int depth = 0
    )
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "semantic-projection-" + scenario);
        try
        {
            var theme = new ThemeContext(composition.Root.Scope, new Theme("semantic-probe"));
            composition.Root.Present(theme, author: Style.Empty.Axis(LayoutAxis.Column));
            var phase = composition.Root.Scope.Signal(0, scenario + ".phase");
            var parent = composition.Root;
            for (var level = 0; level < depth; level++)
            {
                var ancestor = composition.Child(parent, $"ancestor-{level}");
                ancestor.Present(theme, author: Style.Empty);
                parent = ancestor;
            }
            for (var index = 0; index < semanticNodes; index++)
            {
                var node = composition.Child(parent, $"semantic-{index}");
                node.Present(theme, author: Style.Empty);
                var captured = index;
                node.AttachBehaviors(
                    new ProbeSemanticBehavior(() =>
                    {
                        var current = phase.Value;
                        return kind switch
                        {
                            SemanticKind.Simple => captured switch
                            {
                                0 => SemanticDeclaration
                                    .Create(SemanticRole.Button, "Button " + current)
                                    .Build(),
                                1 => SemanticDeclaration
                                    .Create(SemanticRole.CheckBox, "Check box")
                                    .Toggle(
                                        (current & 1) == 0
                                            ? SemanticToggleState.Off
                                            : SemanticToggleState.On,
                                        canToggle: true
                                    )
                                    .Build(),
                                2 => SemanticDeclaration
                                    .Create(SemanticRole.Slider, "Slider")
                                    .Range(
                                        new(current & 1, 0, 1, 1, 1, isReadOnly: true),
                                        canSetValue: false
                                    )
                                    .Build(),
                                _ => SemanticDeclaration
                                    .Create(SemanticRole.TextField, "Text field")
                                    .Value((current & 1).ToString(CultureInfo.InvariantCulture))
                                    .Editing(
                                        new("ab", current & 1, current & 1, isReadOnly: true),
                                        canSetValue: false,
                                        canSelectText: false,
                                        canScrollTextIntoView: false
                                    )
                                    .Build(),
                            },
                            SemanticKind.Text => SemanticDeclaration
                                .Create(SemanticRole.TextField, "Editor")
                                .Value("ab")
                                .Editing(
                                    new("ab", current & 1, current & 1),
                                    canSetValue: false,
                                    canSelectText: false,
                                    canScrollTextIntoView: false
                                )
                                .Build(),
                            _ => SemanticDeclaration
                                .Create(SemanticRole.Group, "Metadata " + current)
                                .Description("Description " + current)
                                .Build(),
                        };
                    })
                );
            }
            graph.Drain();
            return new(
                graph,
                composition,
                scenario,
                index => phase.Value = index,
                depth,
                static () => 0
            );
        }
        catch
        {
            composition.Dispose();
            throw;
        }
    }

    private static ProjectionObservation Measure(
        Fixture fixture,
        string scenario,
        Func<int, LayoutViewport> viewport,
        Action<int>? before = null,
        Action<ProjectionSample[]>? onPartial = null
    )
    {
        _ = fixture.Composition.Input;
        for (var index = 0; index < WarmupSamples; index++)
        {
            before?.Invoke(index);
            fixture.Graph.Drain();
            _ = ProjectAccepted(fixture, viewport(index));
        }

        var elapsed = new double[Samples];
        var allocated = new long[Samples];
        var attempts = new int[Samples];
        var raw = new ProjectionSample[Samples];
        var boxes = 0;
        var completed = 0;
        try
        {
            for (var index = 0; index < Samples; index++)
            {
                before?.Invoke(index);
                fixture.Graph.Drain();
                var sample = ProjectAccepted(fixture, viewport(index));
                raw[index] = sample;
                elapsed[index] = sample.Milliseconds;
                allocated[index] = sample.AllocatedBytes;
                attempts[index] = sample.Attempts;
                boxes = sample.Boxes;
                completed++;
            }
        }
        catch
        {
            onPartial?.Invoke(raw[..completed]);
            throw;
        }
        Array.Sort(elapsed);
        Array.Sort(allocated);
        return new(
            scenario,
            fixture.AuthoredElements,
            boxes,
            fixture.RealizedRows(),
            attempts.Sum(value => value - 1),
            attempts.Max(),
            elapsed.Average(),
            Percentile(elapsed, 0.5),
            Percentile(elapsed, 0.95),
            allocated.Average(),
            Percentile(allocated, 0.5),
            Percentile(allocated, 0.95),
            raw,
            GeometryFingerprint(fixture.Scene),
            SemanticFingerprint(fixture.Composition.SemanticSnapshot())
        );
    }

    // These checks run after the measured corpus, not inside projection timing/allocation.
    private static string GeometryFingerprint(RetainedScene scene)
    {
        var text = new StringBuilder();
        foreach (var box in scene.Boxes)
            text.Append(
                CultureInfo.InvariantCulture,
                $"{box.Identity.ElementId}:{box.Bounds.X:R},{box.Bounds.Y:R},{box.Bounds.Width:R},{box.Bounds.Height:R}:{box.Text?.Width:R},{box.Text?.Height:R}\n"
            );
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string SemanticFingerprint(SemanticSnapshot? snapshot)
    {
        var text = new StringBuilder();
        if (snapshot is not null)
            foreach (var node in Flatten(snapshot))
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"{node.Identity.ElementId}:{node.Role}:{node.Name}:{node.Actions}:{node.Enabled}:{node.Focused}:{node.Selected}:{node.Children.Count}\n"
                );
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static ProjectionSample ProjectAccepted(Fixture fixture, LayoutViewport viewport)
    {
        var milliseconds = 0d;
        var allocatedBytes = 0L;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            var scene = SceneLayout.Project(fixture.Composition, viewport, fixture.Shaper);
            milliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            var boxes = scene.Boxes.Count;
            bool accepted;
            try
            {
                accepted = fixture.Composition.Input.SetScene(scene);
            }
            catch
            {
                scene.Dispose();
                throw;
            }
            if (accepted)
            {
                fixture.ReplaceScene(scene);
                return new(milliseconds, allocatedBytes, attempt, boxes);
            }
            scene.Dispose();
            fixture.Graph.Drain();
        }
        throw new InvalidOperationException(
            "Measured scene was rejected by input ownership four consecutive times."
        );
    }

    private static Fixture CreateSyntheticFixture(string name, int breadth, int depth)
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "projection-" + name);
        try
        {
            var theme = new ThemeContext(composition.Root.Scope, new Theme("projection-probe"));
            composition.Root.Present(theme, author: Style.Empty.Axis(LayoutAxis.Column));
            var parent = composition.Root;
            var count = 1;
            for (var level = 0; level < depth; level++)
            {
                for (var index = 0; index < breadth; index++)
                {
                    var child = composition.Child(parent, $"{name}-{level}-{index}");
                    child.Present(
                        theme,
                        author: Style.Empty.Height(1).Set(InputProperties.PointerTransparent, false)
                    );
                    count++;
                }
                parent = parent.Children[^1];
            }
            return new(graph, composition, new EmptyShaper(), count, static () => 0);
        }
        catch
        {
            composition.Dispose();
            throw;
        }
    }

    private static Fixture CreateIssueBrowserFixture()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "projection-issue-browser");
        RetainedScene? setupScene = null;
        SkiaSceneRenderer? measuredRenderer = null;
        try
        {
            composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            _ = composition.Mount(composition.Root, theme, IssueBrowserStructure.Create());
            using var renderer = new SkiaSceneRenderer();
            var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
            do
            {
                graph.Drain();
                var scene = SceneLayout.Project(composition, new(1120, 760, 1), renderer);
                bool accepted;
                try
                {
                    accepted = composition.Input.SetScene(scene);
                }
                catch
                {
                    scene.Dispose();
                    throw;
                }
                if (!accepted)
                {
                    scene.Dispose();
                    continue;
                }
                var previous = setupScene;
                setupScene = scene;
                previous?.Dispose();
                if (
                    composition.SemanticSnapshot() is { } snapshot
                    && Flatten(snapshot).Any(node => node.Role == SemanticRole.ListItem)
                )
                    break;
                Thread.Sleep(1);
            } while (Stopwatch.GetTimestamp() < until);
            var semantic = composition.SemanticSnapshot();
            if (
                setupScene is null
                || semantic is null
                || !Flatten(semantic).Any(node => node.Role == SemanticRole.ListItem)
            )
                throw new InvalidOperationException("Issue Browser rows did not load.");
            var inputTarget = Elements(composition.Root)
                .First(element => element.Parent is not null);
            var inputToggle = false;
            var scrollEnd = false;
            measuredRenderer = new SkiaSceneRenderer();
            var fixture = new Fixture(
                graph,
                composition,
                measuredRenderer,
                Elements(composition.Root).Count(),
                () =>
                    composition.SemanticSnapshot() is { } current
                        ? Flatten(current).Count(node => node.Role == SemanticRole.ListItem)
                        : 0,
                _ =>
                {
                    var current = composition.SemanticSnapshot()!;
                    var scroll = Flatten(current)
                        .First(node => node.Actions.HasFlag(SemanticAction.Scroll));
                    scrollEnd = !scrollEnd;
                    if (
                        !composition.Input.ScrollSemantic(
                            new(scroll.Identity.CompositionEpoch, scroll.Identity.ElementId),
                            new(
                                SemanticCommandKind.Scroll,
                                Endpoint: scrollEnd
                                    ? SemanticScrollEndpoint.End
                                    : SemanticScrollEndpoint.Start
                            )
                        )
                    )
                        throw new InvalidOperationException(
                            "Issue Browser scroll mutation failed."
                        );
                },
                _ =>
                {
                    inputToggle = !inputToggle;
                    inputTarget.UpdateControl(InputProperties.PointerTransparent, inputToggle);
                }
            );
            fixture.ReplaceScene(setupScene);
            setupScene = null;
            return fixture;
        }
        catch
        {
            setupScene?.Dispose();
            measuredRenderer?.Dispose();
            composition.Dispose();
            throw;
        }
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private static IEnumerable<Element> Elements(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
        foreach (var descendant in Elements(child))
            yield return descendant;
    }

    private static double Percentile(double[] sorted, double percentile) =>
        sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

    private static long Percentile(long[] sorted, double percentile) =>
        sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

    private sealed class Fixture(
        ReactiveGraph graph,
        Composition composition,
        ITextShaper shaper,
        int authoredElements,
        Func<int> realizedRows,
        Action<int>? toggleScroll = null,
        Action<int>? toggleInput = null
    ) : IDisposable
    {
        private RetainedScene? _scene;

        internal ReactiveGraph Graph { get; } = graph;
        internal Composition Composition { get; } = composition;
        internal RetainedScene Scene =>
            _scene ?? throw new InvalidOperationException("No accepted scene.");
        internal ITextShaper Shaper { get; } = shaper;
        internal int AuthoredElements { get; } = authoredElements;
        internal Func<int> RealizedRows { get; } = realizedRows;
        internal Action<int> ToggleScroll { get; } = toggleScroll ?? (_ => { });
        internal Action<int> ToggleInput { get; } = toggleInput ?? (_ => { });

        internal void ReplaceScene(RetainedScene scene)
        {
            var previous = _scene;
            _scene = scene;
            previous?.Dispose();
        }

        public void Dispose()
        {
            _scene?.Dispose();
            Composition.Dispose();
            if (Shaper is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }

    private sealed record ProjectionReport(
        int SchemaVersion,
        string Status,
        string? IncompleteScenario,
        string? FatalError,
        ProjectionSample[]? IncompleteRawSamples,
        int? IncompleteCompletedSamples,
        string Machine,
        string OperatingSystem,
        string Framework,
        string Architecture,
        string Configuration,
        int WarmupSamples,
        int Samples,
        IReadOnlyList<ProjectionObservation> Observations,
        IReadOnlyList<SemanticObservation> SemanticObservations
    );

    private sealed record SemanticObservation(
        string Scenario,
        int SemanticNodesMaximum,
        int RealizedRows,
        int AncestorDepth,
        int SnapshotBuilds,
        int GenerationChanges,
        int MaximumGenerationChangesPerSample,
        double MeanUpdateMilliseconds,
        double P50UpdateMilliseconds,
        double P95UpdateMilliseconds,
        double MeanProjectionMilliseconds,
        double P50ProjectionMilliseconds,
        double P95ProjectionMilliseconds,
        double MeanAllocatedBytes,
        long P50AllocatedBytes,
        long P95AllocatedBytes
    );

    private sealed record ProjectionObservation(
        string Scenario,
        int AuthoredElements,
        int ProjectedBoxes,
        int RealizedRows,
        int RejectedProjectionAttempts,
        int MaximumAttemptsPerSample,
        double MeanMilliseconds,
        double P50Milliseconds,
        double P95Milliseconds,
        double MeanAllocatedBytes,
        long P50AllocatedBytes,
        long P95AllocatedBytes,
        ProjectionSample[] RawSamples,
        string FinalGeometrySha256,
        string FinalSemanticsSha256
    );

    private readonly record struct ProjectionSample(
        double Milliseconds,
        long AllocatedBytes,
        int Attempts,
        int Boxes
    );

    private enum SemanticKind
    {
        Simple,
        Metadata,
        Text,
    }

    private sealed class ProbeSemanticBehavior(Func<SemanticDeclaration> read) : Behavior
    {
        public override string Name => "semantic-projection-probe";

        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Semantics | BehaviorOwnership.Action;

        public override void Attach(BehaviorContext context)
        {
            context.BindSemantics(read);
            context.OnSemanticCommand(static _ => true);
        }
    }

    private sealed class SemanticFixture(
        ReactiveGraph graph,
        Composition composition,
        string scenario,
        Action<int> mutate,
        int ancestorDepth,
        Func<int> realizedRows
    ) : IDisposable
    {
        internal ReactiveGraph Graph { get; } = graph;
        internal Composition Composition { get; } = composition;
        internal string Scenario { get; } = scenario;
        internal Action<int> Mutate { get; } = mutate;
        internal int AncestorDepth { get; } = ancestorDepth;
        internal Func<int> RealizedRows { get; } = realizedRows;

        public void Dispose() => Composition.Dispose();
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(ProjectionReport))]
    private sealed partial class ProjectionJsonContext
        : System.Text.Json.Serialization.JsonSerializerContext;
}
