using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class CompositionLifecycleContracts
{
    [TestMethod]
    public void ApplicationDrainConcurrentPostsStayBelowCallbackLimit()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "application-tail-race");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var lifecycle = new EmptyLifecycle();
        var session = new ApplicationSession(
            "Application tail race",
            composition,
            theme,
            lifecycle,
            static _ => { },
            static report => report()
        );

        session.Start();
        session.ProcessEvents();
        var context = lifecycle.Context!;
        const int postCount = 512;
        var callbackCount = 0;
        var settlingFailures = new List<Exception>();
        using var producerStarted = new ManualResetEventSlim();
        var producer = Task.Run(() =>
        {
            producerStarted.Set();
            for (var index = 0; index < postCount; index++)
            {
                context.Post(_ => Interlocked.Increment(ref callbackCount), null);
                Thread.SpinWait(256);
            }
        });
        Assert.IsTrue(producerStarted.Wait(TimeSpan.FromSeconds(5)));

        while (!producer.IsCompleted)
        {
            try
            {
                session.ProcessEvents();
            }
            catch (InvalidOperationException error)
                when (error.Message.Contains("did not settle", StringComparison.Ordinal))
            {
                settlingFailures.Add(error);
            }
        }
        producer.GetAwaiter().GetResult();
        for (var attempt = 0; callbackCount != postCount && attempt < 32; attempt++)
            session.ProcessEvents();

        Assert.AreEqual(postCount, callbackCount);
        Assert.AreEqual(
            0,
            settlingFailures.Count,
            "Concurrent posts below the per-drain budget triggered a false settling limit."
        );

        session.Abort(null);
        while (!session.IsCompleted)
            session.ProcessEvents();
    }

    [TestMethod]
    public void ElementDisposalContinuesAfterSemanticObserverAndScopeCleanupFailures()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "semantic-disposal");
        var element = composition.Child(composition.Root, "throwing-semantics");
        var cleanupCalls = 0;
        element.Scope.OnDispose(() =>
        {
            cleanupCalls++;
            throw new InvalidOperationException("scope-cleanup");
        });
        var observerCalls = 0;
        Action observer = () =>
        {
            observerCalls++;
            throw new InvalidOperationException("semantic-observer");
        };
        composition.SemanticsChanged += observer;

        try
        {
            var failure = Assert.ThrowsExactly<AggregateException>(element.Dispose);
            var messages = Flatten(failure).Select(error => error.Message).ToArray();
            Assert.AreEqual(1, observerCalls);
            Assert.AreEqual(1, cleanupCalls);
            Assert.IsTrue(element.IsDisposed, "The element did not enter its terminal state.");
            Assert.IsTrue(element.Scope.IsDisposed, "The element scope was left live.");
            Assert.AreEqual(
                0,
                composition.Root.Children.Count,
                "The parent retained a disposed child."
            );
            CollectionAssert.Contains(messages, "semantic-observer");
            CollectionAssert.Contains(messages, "scope-cleanup");
        }
        finally
        {
            composition.SemanticsChanged -= observer;
            try
            {
                composition.Dispose();
            }
            catch
            {
                // The pre-fix red case intentionally leaves the element's scope live.
            }
        }
    }

    [TestMethod]
    public void BehaviorAttachmentGuardIsInheritedByPrecreatedChildScopes()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "child-scope-guard");
        var element = composition.Child(composition.Root, "guarded-element");
        var childScope = element.Scope.CreateChild("precreated-child");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            element.AttachBehaviors(new ChildScopeMutationBehavior(childScope))
        );
    }

    private static IEnumerable<Exception> Flatten(Exception error)
    {
        if (error is not AggregateException aggregate)
        {
            yield return error;
            yield break;
        }

        foreach (var inner in aggregate.InnerExceptions)
        foreach (var nested in Flatten(inner))
            yield return nested;
    }

    private sealed class EmptyLifecycle : IApplicationLifecycle
    {
        public SynchronizationContext? Context { get; private set; }

        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
        {
            Context = SynchronizationContext.Current;
            return ValueTask.FromResult(
                ComponentRecipe.Create("application-tail-root", static (_, _) => { })
            );
        }

        public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ChildScopeMutationBehavior(ReactiveScope scope) : Behavior
    {
        public override string Name => "child-scope-mutation";

        public override void Attach(BehaviorContext context)
        {
            var signal = scope.Signal(0, "during-attach");
            signal.Value = 1;
        }
    }
}
