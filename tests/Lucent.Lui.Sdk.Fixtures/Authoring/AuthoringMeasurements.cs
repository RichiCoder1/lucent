using System.Diagnostics;
using System.Globalization;
using Lucent.Core;

namespace AuthoringConsumer;

// Characterization, not a machine-dependent performance gate. All paths retain one Button and
// one state cell. Keep this in the package consumer so managed and NativeAOT report their own cost.
internal static class AuthoringMeasurements
{
    internal static void Run()
    {
        Measure(
            "raw-defer",
            ComponentRecipe.Defer(
                "counter",
                owner =>
                {
                    var count = owner.Signal(0, "counter.state");
                    return Lucent.Core.Components.Button(
                        () => count.Value.ToString(CultureInfo.InvariantCulture),
                        () => count.Value++
                    );
                }
            )
        );
        Measure(
            "component-context",
            Component.Define(
                "counter",
                ui =>
                {
                    var count = ui.State(0, "counter.state");
                    return Lucent.Core.Components.Button(
                        () => count.Value.ToString(CultureInfo.InvariantCulture),
                        () => count.Value++
                    );
                }
            )
        );
        Measure(
            "generated-state",
            Component.Define<MeasuredState>(
                "counter",
                (_, state) =>
                    Lucent.Core.Components.Button(
                        () => state.Count.ToString(CultureInfo.InvariantCulture),
                        () => state.Count++
                    )
            )
        );
    }

    private static void Measure(string name, ComponentRecipe recipe)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authoring-measurement");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        void Mount()
        {
            var root = composition.Mount(composition.Root, theme, recipe);
            graph.Drain();
            if (root.Children.Count != 0 || composition.Root.Children.Count != 1)
                throw new InvalidOperationException(
                    "Authoring measurement introduced a retained wrapper."
                );
            root.Dispose();
            graph.Drain();
        }
        for (var warmup = 0; warmup < 32; warmup++)
            Mount();
        const int iterations = 256;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var iteration = 0; iteration < iterations; iteration++)
            Mount();
        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine(
            FormattableString.Invariant(
                $"Authoring measurement {name}: mounts={iterations}; bytes-per-mount={allocated / iterations}; elapsed-ms={elapsed.TotalMilliseconds:F3}; retained-roots=1; child-roots=0"
            )
        );
    }
}

[ComponentState]
internal sealed partial class MeasuredState
{
    [State]
    public partial int Count { get; set; }
}
