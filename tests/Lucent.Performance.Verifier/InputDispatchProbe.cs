using System.Diagnostics;
using System.Text.Json;
using Lucent.Core;

internal static class InputDispatchProbe
{
    private const int FlatElementCount = 1_001;
    private const int DeepElementCount = 101;
    private const int Samples = 64;

    internal static int Run()
    {
        var observations = new List<Observation>();
        string? incompleteScenario = null;
        Exception? failure = null;
        try
        {
            foreach (
                var scenario in new[]
                {
                    (Shape: "flat", Count: 101),
                    (Shape: "flat", Count: FlatElementCount),
                    (Shape: "deep", Count: 21),
                    (Shape: "deep", Count: DeepElementCount),
                }
            )
            {
                incompleteScenario = $"{scenario.Shape}-{scenario.Count}/setup";
                using var fixture = Fixture.Create(scenario.Shape, scenario.Count);
                incompleteScenario = $"{scenario.Shape}-{scenario.Count}/pointer";
                Measure(
                    fixture,
                    "pointer",
                    () =>
                        fixture.Router.DispatchPointer(new(PointerCommandKind.Move, 1, 1, 1)).Status
                        == InputDispatchStatus.Delivered,
                    observations
                );
                incompleteScenario = $"{scenario.Shape}-{scenario.Count}/key";
                Measure(
                    fixture,
                    "key",
                    () =>
                        fixture.Router.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Status
                        == InputDispatchStatus.Delivered,
                    observations
                );
                incompleteScenario = $"{scenario.Shape}-{scenario.Count}/focus-same-target";
                Measure(
                    fixture,
                    "focus-same-target",
                    () => fixture.Router.FocusSemantic(fixture.FocusIdentity),
                    observations
                );
                incompleteScenario = $"{scenario.Shape}-{scenario.Count}/teardown";
            }
        }
        catch (Exception error)
        {
            failure = error;
            Console.Error.WriteLine("Lucent input dispatch probe: FAIL: " + error);
        }

        using var output = Console.OpenStandardOutput();
        using var writer = new Utf8JsonWriter(output, new() { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("status", failure is null ? "complete" : "failed");
        if (failure is null)
        {
            writer.WriteNull("incompleteScenario");
            writer.WriteNull("fatalError");
        }
        else
        {
            writer.WriteString("incompleteScenario", incompleteScenario);
            writer.WriteString("fatalError", failure.ToString());
        }
        writer.WriteNumber("samples", Samples);
        writer.WriteStartArray("observations");
        foreach (var observation in observations)
        {
            writer.WriteStartObject();
            writer.WriteString("shape", observation.Shape);
            writer.WriteString("operation", observation.Operation);
            writer.WriteNumber("elementCount", observation.ElementCount);
            if (observation.AllocatedBytesPerDispatch is { } allocated)
                writer.WriteNumber("allocatedBytesPerDispatch", allocated);
            else
                writer.WriteNull("allocatedBytesPerDispatch");
            if (observation.NanosecondsPerDispatch is { } nanoseconds)
                writer.WriteNumber("nanosecondsPerDispatch", nanoseconds);
            else
                writer.WriteNull("nanosecondsPerDispatch");
            writer.WriteNumber("completedDispatches", observation.CompletedDispatches);
            writer.WriteBoolean("complete", observation.Complete);
            writer.WriteNumber("batchAllocatedBytes", observation.BatchAllocatedBytes);
            writer.WriteNumber("batchElapsedNanoseconds", observation.BatchElapsedNanoseconds);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        Console.WriteLine();
        return failure is null ? 0 : 1;
    }

    private static void Measure(
        Fixture fixture,
        string operation,
        Func<bool> dispatch,
        List<Observation> observations
    )
    {
        for (var index = 0; index < 4; index++)
            if (!dispatch())
                throw new InvalidOperationException(operation + " warmup was rejected.");
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var completed = 0;
        try
        {
            for (var index = 0; index < Samples; index++)
            {
                if (!dispatch())
                    throw new InvalidOperationException(operation + " sample was rejected.");
                completed++;
            }
        }
        catch
        {
            var partialElapsed = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
            var partialAllocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            observations.Add(
                new(
                    fixture.Shape,
                    operation,
                    fixture.ElementCount,
                    null,
                    null,
                    completed,
                    false,
                    partialAllocated,
                    partialElapsed
                )
            );
            throw;
        }
        var elapsed = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        observations.Add(
            new(
                fixture.Shape,
                operation,
                fixture.ElementCount,
                allocated / Samples,
                elapsed / Samples,
                completed,
                true,
                allocated,
                elapsed
            )
        );
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            string shape,
            int elementCount,
            Composition composition,
            InputRouter router,
            ElementIdentity focusIdentity,
            RetainedScene scene
        )
        {
            Shape = shape;
            ElementCount = elementCount;
            Composition = composition;
            Router = router;
            FocusIdentity = focusIdentity;
            Scene = scene;
        }

        internal string Shape { get; }
        internal int ElementCount { get; }
        internal Composition Composition { get; }
        internal InputRouter Router { get; }
        internal ElementIdentity FocusIdentity { get; }
        private RetainedScene Scene { get; }

        internal static Fixture Create(string shape, int count)
        {
            var graph = new ReactiveGraph();
            var composition = new Composition(graph, "input-dispatch-" + shape);
            RetainedScene? scene = null;
            try
            {
                var theme = new ThemeContext(composition.Root.Scope, new Theme("input-dispatch"));
                Present(composition.Root, theme);
                var parent = composition.Root;
                Element focus = parent;
                for (var index = 1; index < count; index++)
                {
                    parent = composition.Child(
                        shape == "flat" ? composition.Root : parent,
                        "item-" + index
                    );
                    Present(parent, theme);
                    focus = parent;
                }
                focus.AttachBehaviors(new DispatchBehavior());
                var router = composition.Input;
                scene = SceneLayout.Project(composition, new(10, 10, 1), new EmptyShaper());
                if (
                    !router.SetScene(scene)
                    || !router.FocusSemantic(new(composition.Epoch, focus.Id))
                )
                    throw new InvalidOperationException(
                        "Input dispatch fixture did not install or focus."
                    );
                return new(
                    shape,
                    count,
                    composition,
                    router,
                    new(composition.Epoch, focus.Id),
                    scene
                );
            }
            catch
            {
                scene?.Dispose();
                composition.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Scene.Dispose();
            Composition.Dispose();
        }

        private static void Present(Element element, ThemeContext theme) =>
            element.Present(
                theme,
                Style.Empty.Set(LayoutProperties.Width, 10f).Set(LayoutProperties.Height, 10f)
            );
    }

    private sealed class DispatchBehavior : Behavior
    {
        public override string Name => "dispatch";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(
                SemanticDeclaration.Create(SemanticRole.Group, "dispatch").Build()
            );
            context.MakeFocusable();
            context.OnPointer(_ => { });
            context.OnKey(_ => { });
            context.OnFocus(_ => { });
        }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }

    private sealed record Observation(
        string Shape,
        string Operation,
        int ElementCount,
        long? AllocatedBytesPerDispatch,
        double? NanosecondsPerDispatch,
        int CompletedDispatches,
        bool Complete,
        long BatchAllocatedBytes,
        double BatchElapsedNanoseconds
    );
}
