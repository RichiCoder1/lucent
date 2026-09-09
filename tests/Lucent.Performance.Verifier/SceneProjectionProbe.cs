using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
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
        try
        {
            var observations = new List<ProjectionObservation>();
            using (var wide = CreateSyntheticFixture("wide", 500, depth: 1))
                observations.Add(Measure(wide, "wide-unchanged", _ => new(1200, 800, 1)));
            using (var deep = CreateSyntheticFixture("deep", breadth: 1, depth: 128))
                observations.Add(Measure(deep, "deep-unchanged", _ => new(600, 800, 1)));
            using (var browser = CreateIssueBrowserFixture())
            {
                observations.Add(
                    Measure(browser, "issue-browser-unchanged", _ => new(1120, 760, 1))
                );
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-same-wide-bucket",
                        index => new((index & 1) == 0 ? 1100 : 1120, 760, 1)
                    )
                );
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-breakpoint-change",
                        index => new((index & 1) == 0 ? 760 : 1120, 760, 1)
                    )
                );
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-scale-change",
                        index => new(1120, 760, (index & 1) == 0 ? 1 : 1.5f)
                    )
                );
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-scroll-change",
                        _ => new(1120, 760, 1),
                        browser.ToggleScroll
                    )
                );
                observations.Add(
                    Measure(
                        browser,
                        "issue-browser-input-change",
                        _ => new(1120, 760, 1),
                        browser.ToggleInput
                    )
                );
            }

            var report = new ProjectionReport(
                Environment.MachineName,
                RuntimeInformation.OSDescription,
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.ProcessArchitecture.ToString(),
                "Release",
                WarmupSamples,
                Samples,
                observations
            );
            Console.WriteLine(
                JsonSerializer.Serialize(report, ProjectionJsonContext.Default.ProjectionReport)
            );
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Lucent scene projection probe: FAIL: " + error);
            return 1;
        }
    }

    private static ProjectionObservation Measure(
        Fixture fixture,
        string scenario,
        Func<int, LayoutViewport> viewport,
        Action<int>? before = null
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
        var boxes = 0;
        for (var index = 0; index < Samples; index++)
        {
            before?.Invoke(index);
            fixture.Graph.Drain();
            var sample = ProjectAccepted(fixture, viewport(index));
            elapsed[index] = sample.Milliseconds;
            allocated[index] = sample.AllocatedBytes;
            attempts[index] = sample.Attempts;
            boxes = sample.Boxes;
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
            Percentile(allocated, 0.95)
        );
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
            if (fixture.Composition.Input.SetScene(scene))
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

    private static Fixture CreateIssueBrowserFixture()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "projection-issue-browser");
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        _ = composition.Mount(composition.Root, theme, IssueBrowserStructure.Create());
        using var renderer = new SkiaSceneRenderer();
        var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
        do
        {
            graph.Drain();
            var scene = SceneLayout.Project(composition, new(1120, 760, 1), renderer);
            if (!composition.Input.SetScene(scene))
            {
                scene.Dispose();
                continue;
            }
            if (
                composition.SemanticSnapshot() is { } snapshot
                && Flatten(snapshot).Any(node => node.Role == SemanticRole.ListItem)
            )
                break;
            Thread.Sleep(1);
        } while (Stopwatch.GetTimestamp() < until);
        var semantic = composition.SemanticSnapshot();
        if (semantic is null || !Flatten(semantic).Any(node => node.Role == SemanticRole.ListItem))
            throw new InvalidOperationException("Issue Browser rows did not load.");
        var inputTarget = Elements(composition.Root).First(element => element.Parent is not null);
        var inputToggle = false;
        var scrollEnd = false;
        return new(
            graph,
            composition,
            new SkiaSceneRenderer(),
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
                    throw new InvalidOperationException("Issue Browser scroll mutation failed.");
            },
            _ =>
            {
                inputToggle = !inputToggle;
                inputTarget.UpdateControl(InputProperties.PointerTransparent, inputToggle);
            }
        );
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
        string Machine,
        string OperatingSystem,
        string Framework,
        string Architecture,
        string Configuration,
        int WarmupSamples,
        int Samples,
        IReadOnlyList<ProjectionObservation> Observations
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
        long P95AllocatedBytes
    );

    private readonly record struct ProjectionSample(
        double Milliseconds,
        long AllocatedBytes,
        int Attempts,
        int Boxes
    );

    [System.Text.Json.Serialization.JsonSerializable(typeof(ProjectionReport))]
    private sealed partial class ProjectionJsonContext
        : System.Text.Json.Serialization.JsonSerializerContext;
}
