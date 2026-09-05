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
            using var fixture = Fixture.Create(scenario.Shape, scenario.Count);
            Measure(
                fixture,
                "pointer",
                () =>
                    fixture.Router.DispatchPointer(new(PointerCommandKind.Move, 1, 1, 1)).Status
                    == InputDispatchStatus.Delivered,
                observations
            );
            Measure(
                fixture,
                "key",
                () =>
                    fixture.Router.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Status
                    == InputDispatchStatus.Delivered,
                observations
            );
            Measure(
                fixture,
                "focus-same-target",
                () => fixture.Router.FocusSemantic(fixture.FocusIdentity),
                observations
            );
        }

        using var output = Console.OpenStandardOutput();
        using var writer = new Utf8JsonWriter(output, new() { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("samples", Samples);
        writer.WriteStartArray("observations");
        foreach (var observation in observations)
        {
            writer.WriteStartObject();
            writer.WriteString("shape", observation.Shape);
            writer.WriteString("operation", observation.Operation);
            writer.WriteNumber("elementCount", observation.ElementCount);
            writer.WriteNumber("allocatedBytesPerDispatch", observation.AllocatedBytesPerDispatch);
            writer.WriteNumber("nanosecondsPerDispatch", observation.NanosecondsPerDispatch);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        Console.WriteLine();
        return 0;
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
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < Samples; index++)
            if (!dispatch())
                throw new InvalidOperationException(operation + " sample was rejected.");
        var elapsed = Stopwatch.GetElapsedTime(started);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        observations.Add(
            new(
                fixture.Shape,
                operation,
                fixture.ElementCount,
                allocated / Samples,
                elapsed.TotalNanoseconds / Samples
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
            ElementIdentity focusIdentity
        )
        {
            Shape = shape;
            ElementCount = elementCount;
            Composition = composition;
            Router = router;
            FocusIdentity = focusIdentity;
        }

        internal string Shape { get; }
        internal int ElementCount { get; }
        internal Composition Composition { get; }
        internal InputRouter Router { get; }
        internal ElementIdentity FocusIdentity { get; }

        internal static Fixture Create(string shape, int count)
        {
            var graph = new ReactiveGraph();
            var composition = new Composition(graph, "input-dispatch-" + shape);
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
            var scene = SceneLayout.Project(composition, new(10, 10, 1), new EmptyShaper());
            if (!router.SetScene(scene) || !router.FocusSemantic(new(composition.Epoch, focus.Id)))
                throw new InvalidOperationException(
                    "Input dispatch fixture did not install or focus."
                );
            return new(shape, count, composition, router, new(composition.Epoch, focus.Id));
        }

        public void Dispose() => Composition.Dispose();

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
            context.SetSemantics(new(SemanticRole.Group, "dispatch"));
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
        long AllocatedBytesPerDispatch,
        double NanosecondsPerDispatch
    );
}
