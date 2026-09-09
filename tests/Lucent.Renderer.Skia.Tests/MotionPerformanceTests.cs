using System.Diagnostics;
using System.Text.Json;
using Lucent.Core;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class MotionPerformanceTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(100)]
    [DataRow(1000)]
    public void CharacterizeRetainedMotionFrames(int count)
    {
        var output = Environment.GetEnvironmentVariable("LUCENT_MOTION_CHARACTERIZATION");
        if (string.IsNullOrWhiteSpace(output))
            Assert.Inconclusive(
                "Set LUCENT_MOTION_CHARACTERIZATION to an output directory for the opt-in frame characterization."
            );
        Directory.CreateDirectory(output);
        var baseline = Run(count, false);
        var active = Run(count, true);
        Assert.AreEqual(0, active.AdditionalShapeCalls);
        Assert.AreEqual(0, baseline.AdditionalShapeCalls);
        Assert.AreEqual(count, active.ActiveTracks);
        Assert.AreEqual(0, baseline.ActiveTracks);
        using var stream = File.Create(Path.Combine(output, $"motion-{count}.json"));
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("count", count);
        WriteMeasurements(writer, "baseline", baseline);
        WriteMeasurements(writer, "active", active);
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteMeasurements(Utf8JsonWriter writer, string name, Measurements values)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("projectionP50Ms", values.ProjectionP50Ms);
        writer.WriteNumber("projectionP95Ms", values.ProjectionP95Ms);
        writer.WriteNumber("rasterP50Ms", values.RasterP50Ms);
        writer.WriteNumber("rasterP95Ms", values.RasterP95Ms);
        writer.WriteNumber("allocatedBytesP50", values.AllocatedBytesP50);
        writer.WriteNumber("activeTracks", values.ActiveTracks);
        writer.WriteNumber("additionalShapeCalls", values.AdditionalShapeCalls);
        writer.WriteNumber("wakeRequests", values.WakeRequests);
        writer.WriteEndObject();
    }

    private static Measurements Run(int count, bool motion)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "motion-characterization");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var active = graph.Signal(false, "active");
        var style = Style
            .Empty.Height(18)
            .Set(ProjectionProperties.Text, "Retained text")
            .Bind(
                VisualProperties.Background,
                () => Brush.Solid(Color.Parse(active.Value ? "#FFFFFF" : "#000000"))
            )
            .Transition(
                VisualProperties.Background,
                motion ? Motion.Duration(1000, Easing.Linear) : Motion.None
            );
        for (var index = 0; index < count; index++)
            composition.Child(composition.Root, "row-" + index).Present(theme, author: style);
        using var renderer = new SkiaSceneRenderer();
        var shaper = new CountingShaper(renderer);
        var viewport = new LayoutViewport(160, count * 18, 1);
        composition.SamplePresentation(TimeSpan.Zero);
        using var initial = SceneLayout.ProjectFrame(composition, viewport, shaper, null);
        Assert.IsTrue(composition.Input.SetScene(initial));
        Assert.IsTrue(composition.TryAcknowledgePresentation(initial.Generation));
        active.Value = true;
        using var target = SceneLayout.ProjectFrame(composition, viewport, shaper, initial);
        Assert.IsTrue(composition.Input.SetScene(target));
        Assert.IsTrue(composition.TryAcknowledgePresentation(target.Generation));
        var tracks = composition.PresentationDiagnostics.ActiveTracks;
        var shapes = shaper.Calls;
        using var bitmap = new SKBitmap(160, count * 18);
        using var canvas = new SKCanvas(bitmap);
        var phases = new List<double>();
        var rasters = new List<double>();
        var allocations = new List<long>();
        for (var frame = 1; frame <= 30; frame++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            composition.SamplePresentation(TimeSpan.FromMilliseconds(frame * 20));
            using var scene = SceneLayout.ProjectFrame(composition, viewport, shaper, target);
            Assert.IsTrue(scene.IsPaintOnly);
            Assert.IsTrue(composition.Input.SetScene(scene));
            var painted = Stopwatch.GetTimestamp();
            renderer.Render(scene, canvas);
            Assert.IsTrue(composition.TryAcknowledgePresentation(scene.Generation));
            var finished = Stopwatch.GetTimestamp();
            if (frame <= 5)
                continue;
            phases.Add(Stopwatch.GetElapsedTime(start, painted).TotalMilliseconds);
            rasters.Add(Stopwatch.GetElapsedTime(painted, finished).TotalMilliseconds);
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
        }
        composition.SamplePresentation(TimeSpan.FromMilliseconds(1000));
        Assert.IsFalse(composition.PresentationDemand.IsActive);
        return new(
            Percentile(phases, .5),
            Percentile(phases, .95),
            Percentile(rasters, .5),
            Percentile(rasters, .95),
            allocations.Order().ElementAt(allocations.Count / 2),
            tracks,
            shaper.Calls - shapes,
            composition.PresentationDiagnostics.WakeRequests
        );
    }

    private static double Percentile(List<double> values, double percentile) =>
        values
            .Order()
            .ElementAt(
                Math.Min(values.Count - 1, (int)Math.Ceiling(values.Count * percentile) - 1)
            );

    private sealed record Measurements(
        double ProjectionP50Ms,
        double ProjectionP95Ms,
        double RasterP50Ms,
        double RasterP95Ms,
        long AllocatedBytesP50,
        int ActiveTracks,
        int AdditionalShapeCalls,
        long WakeRequests
    );

    private sealed class CountingShaper(ITextShaper inner) : ITextShaper
    {
        internal int Calls { get; private set; }

        public ShapedText Shape(TextMeasureRequest request)
        {
            Calls++;
            return inner.Shape(request);
        }
    }
}
