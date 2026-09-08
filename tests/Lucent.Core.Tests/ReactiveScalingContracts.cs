using System.Diagnostics;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ReactiveScalingContracts
{
    private static readonly int[] SettledReentrantValues = [0, 1];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EqualDerivedOutputSuppressesDerivedAndEffectConsumers()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("equal-output");
        var source = scope.Signal(0, "source");
        var parityRuns = 0;
        var parity = scope.Derived(
            () =>
            {
                parityRuns++;
                return source.Value & 1;
            },
            "parity"
        );
        var downstreamRuns = 0;
        var downstream = scope.Derived(
            () =>
            {
                downstreamRuns++;
                return parity.Value == 0 ? "even" : "odd";
            },
            "downstream"
        );
        var effectRuns = 0;
        _ = scope.Effect(
            () =>
            {
                _ = downstream.Value;
                effectRuns++;
            },
            "consumer"
        );
        graph.Drain();

        source.Value = 2;
        graph.Drain();
        Assert.AreEqual(2, parityRuns);
        Assert.AreEqual(1, downstreamRuns);
        Assert.AreEqual(1, effectRuns);

        source.Value = 3;
        graph.Drain();
        Assert.AreEqual(3, parityRuns);
        Assert.AreEqual(2, downstreamRuns);
        Assert.AreEqual(2, effectRuns);
    }

    [TestMethod]
    public void EqualDerivedSourceDoesNotCancelOrRestartAsyncGeneration()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("equal-async-source");
        var source = scope.Signal(0, "source");
        var parity = scope.Derived(() => source.Value & 1, "parity");
        var completions = new List<TaskCompletionSource<int>>();
        var tokens = new List<CancellationToken>();
        var resource = scope.Async(
            () => parity.Value,
            (value, token) =>
            {
                tokens.Add(token);
                var completion = new TaskCompletionSource<int>();
                completions.Add(completion);
                return completion.Task;
            },
            -1,
            "resource"
        );
        _ = scope.Effect(() => _ = resource.IsPending, "resource-consumer");
        graph.Drain();
        Assert.AreEqual(1, completions.Count);

        source.Value = 2;
        graph.Drain();
        Assert.AreEqual(1, completions.Count);
        Assert.IsFalse(tokens[0].IsCancellationRequested);

        source.Value = 3;
        graph.Drain();
        Assert.AreEqual(2, completions.Count);
        Assert.IsTrue(tokens[0].IsCancellationRequested);
    }

    [TestMethod]
    public void EqualBranchReplacementStillReleasesOldDependencies()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("equal-branch");
        var left = scope.Signal(4, "left");
        var right = scope.Signal(4, "right");
        var useLeft = scope.Signal(true, "use-left");
        var selected = scope.Derived(() => useLeft.Value ? left.Value : right.Value, "selected");
        var runs = 0;
        _ = scope.Effect(
            () =>
            {
                _ = selected.Value;
                runs++;
            },
            "consumer"
        );
        graph.Drain();

        useLeft.Value = false;
        graph.Drain();
        Assert.AreEqual(1, runs);
        left.Value = 5;
        graph.Drain();
        Assert.AreEqual(1, runs);
        right.Value = 6;
        graph.Drain();
        Assert.AreEqual(2, runs);
    }

    [TestMethod]
    public void NewlyReadDirtyDerivedDoesNotScheduleARedundantConsumerRun()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("new-dirty-dependency");
        var source = scope.Signal(0, "source");
        var include = scope.Signal(false, "include");
        var derived = scope.Derived(() => source.Value, "derived");
        Assert.AreEqual(0, derived.Value);
        source.Value = 1;
        var runs = 0;
        _ = scope.Effect(
            () =>
            {
                if (include.Value)
                    _ = derived.Value;
                runs++;
            },
            "consumer"
        );
        graph.Drain();

        include.Value = true;
        graph.Drain();

        Assert.AreEqual(2, runs);
        Assert.AreEqual(1, derived.Value);
    }

    [TestMethod]
    public void FirstDerivedEvaluationChangingItsSourceSchedulesOneSettlingRetry()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("first-reentrant-derived");
        var source = scope.Signal(0, "source");
        var derived = scope.Derived(
            () =>
            {
                var value = source.Value;
                if (value == 0)
                    source.Value = 1;
                return value;
            },
            "derived"
        );
        var observed = new List<int>();
        _ = scope.Effect(() => observed.Add(derived.Value), "consumer");

        graph.Drain();

        CollectionAssert.AreEqual(SettledReentrantValues, observed);
        Assert.AreEqual(1, source.Value);
    }

    [TestMethod]
    public void EqualProjectedOutputKeepsInstalledInputSceneCurrent()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "equal-input-projection");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("projection"));
        var source = composition.Root.Scope.Signal(0, "source");
        var width = composition.Root.Scope.Derived(
            () => (source.Value & 1) == 0 ? 20f : 30f,
            "width"
        );
        composition.Root.Present(
            theme,
            author: Style.Empty.Width(40).Height(40).Axis(LayoutAxis.Column)
        );
        var child = composition.Child(composition.Root, "child");
        child.Present(
            theme,
            author: Style.Empty.Bind(LayoutProperties.Width, () => width.Value).Height(20)
        );
        graph.Drain();
        var router = composition.Input;
        Assert.IsTrue(
            router.SetScene(SceneLayout.Project(composition, new(40, 40, 1), new EmptyShaper()))
        );

        source.Value = 2;
        graph.Drain();
        Assert.AreNotEqual(
            InputRejection.StaleScene,
            router.DispatchPointer(new(PointerCommandKind.Move, 1, 1, 1)).Rejection
        );

        source.Value = 3;
        graph.Drain();
        Assert.AreEqual(
            InputRejection.StaleScene,
            router.DispatchPointer(new(PointerCommandKind.Move, 1, 1, 1)).Rejection
        );
    }

    [TestMethod]
    public void InputProjectionRevisionValidatesPotentialDerivedChanges()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "derived-input-revision");
        var source = composition.Root.Scope.Signal(0, "source");
        var parity = composition.Root.Scope.Derived(() => source.Value & 1, "parity");
        _ = composition.CaptureInputProjection(() => parity.Value);
        var revision = composition.InputProjectionRevision;

        source.Value = 2;
        Assert.AreEqual(revision, composition.InputProjectionRevision);

        source.Value = 3;
        Assert.AreNotEqual(revision, composition.InputProjectionRevision);
    }

    [TestMethod]
    public void InPlaceMutationDoesNotClaimAChangedDerivedReference()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("in-place-reference");
        var trigger = scope.Signal(0, "trigger");
        var value = new MutableValue();
        var derived = scope.Derived(
            () =>
            {
                _ = trigger.Value;
                return value;
            },
            "same-reference"
        );
        var runs = 0;
        _ = scope.Effect(
            () =>
            {
                _ = derived.Value;
                runs++;
            },
            "consumer"
        );
        graph.Drain();

        value.Value = 2;
        trigger.Value++;
        graph.Drain();

        Assert.AreEqual(1, runs);
        Assert.AreEqual(2, derived.Value.Value);
    }

    [TestMethod]
    public void ComparerFailureRetainsDependenciesAndCanRecover()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("comparer-failure");
        var source = scope.Signal(0, "source");
        var throwComparison = false;
        var derived = scope.Derived(
            () => new ComparableValue(source.Value, () => throwComparison),
            "comparable"
        );
        var runs = 0;
        _ = scope.Effect(
            () =>
            {
                _ = derived.Value;
                runs++;
            },
            "consumer"
        );
        graph.Drain();
        throwComparison = true;
        source.Value = 1;
        var failure = Assert.ThrowsExactly<AggregateException>(graph.Drain);
        Assert.IsTrue(
            failure.Flatten().InnerExceptions.Any(error => error.Message == "comparison-failed")
        );
        Assert.AreEqual(1, runs);

        throwComparison = false;
        source.Value = 2;
        graph.Drain();
        Assert.AreEqual(2, runs);
        Assert.AreEqual(2, derived.Value.Value);
    }

    [TestMethod]
    public void TenThousandDependencyReplacementPreservesDynamicOwnership()
    {
        const int count = 10_000;
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("broad-dependencies");
        var left = Enumerable
            .Range(0, count)
            .Select(index => scope.Signal(index, "left-" + index))
            .ToArray();
        var right = Enumerable
            .Range(0, count)
            .Select(index => scope.Signal(index, "right-" + index))
            .ToArray();
        var chooseLeft = scope.Signal(true, "choose-left");
        var runs = 0;
        _ = scope.Effect(
            () =>
            {
                var selected = chooseLeft.Value ? left : right;
                for (var index = 0; index < selected.Length; index++)
                    _ = selected[index].Value;
                runs++;
            },
            "broad-consumer"
        );

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var elapsed = Stopwatch.StartNew();
        graph.Drain();
        chooseLeft.Value = false;
        graph.Drain();
        elapsed.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        TestContext.WriteLine(
            $"10k dependency collect+replace: {elapsed.Elapsed.TotalMilliseconds:F3} ms, {allocated} bytes"
        );

        left[^1].Value++;
        graph.Drain();
        Assert.AreEqual(2, runs);
        right[^1].Value++;
        graph.Drain();
        Assert.AreEqual(3, runs);
    }

    [TestMethod]
    public void TenThousandIndividualDisposalsReleaseRegistriesAndParentOwnership()
    {
        const int count = 10_000;
        var graph = new ReactiveGraph();
        var nodes = Enumerable
            .Range(0, count)
            .Select(index => graph.Signal(index, "node-" + index))
            .ToArray();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var elapsed = Stopwatch.StartNew();
        foreach (var node in nodes)
            node.Dispose();
        elapsed.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        TestContext.WriteLine(
            $"10k individual node disposal: {elapsed.Elapsed.TotalMilliseconds:F3} ms, {allocated} bytes"
        );
        Assert.AreEqual("reactive-graph\n", graph.Dump());

        using var owner = graph.CreateScope("owner");
        var children = Enumerable
            .Range(0, count)
            .Select(index => owner.CreateChild("child-" + index))
            .ToArray();
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        elapsed.Restart();
        foreach (var child in children)
            child.Dispose();
        elapsed.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        TestContext.WriteLine(
            $"10k individual child-scope disposal: {elapsed.Elapsed.TotalMilliseconds:F3} ms, {allocated} bytes"
        );
        Assert.IsFalse(graph.Dump().Contains("child-", StringComparison.Ordinal));
    }

    private sealed class ComparableValue(int value, Func<bool> throwComparison)
    {
        public int Value { get; } = value;

        public override bool Equals(object? obj)
        {
            if (throwComparison())
                throw new InvalidOperationException("comparison-failed");
            return obj is ComparableValue other && Value == other.Value;
        }

        public override int GetHashCode() => Value;
    }

    private sealed class MutableValue
    {
        public int Value { get; set; }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
