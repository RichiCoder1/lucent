using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class FatalApplicationContracts
{
    [TestMethod]
    public void AbortBypassesNeverCompletingPreparationAndRunsCleanupOnce()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "fatal-during-prepare");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var lifecycle = new PendingPrepareLifecycle();
        var session = new ApplicationSession("Fatal prepare", composition, theme, lifecycle);
        var fatal = new InvalidOperationException("fatal-host");

        session.Start();
        PumpUntil(session, () => session.Status.Phase == ApplicationPhase.Running);
        session.RequestClose();
        PumpUntil(session, () => session.Status.Phase == ApplicationPhase.PreparingClose);

        try
        {
            session.Abort(fatal);
            for (var attempt = 0; attempt < 8 && !session.IsCompleted; attempt++)
                session.ProcessEvents(64);

            Assert.IsTrue(session.IsCompleted, "Fatal abort waited for close preparation.");
            Assert.AreEqual(ApplicationPhase.Completed, session.Status.Phase);
            Assert.AreSame(fatal, session.Status.Error);
            Assert.AreEqual(1, lifecycle.StopCalls);
            Assert.AreEqual(1, lifecycle.DisposeCalls);
            Assert.IsTrue(lifecycle.PreparationToken.CanBeCanceled);
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => lifecycle.PreparationToken.IsCancellationRequested,
                    TimeSpan.FromSeconds(2)
                ),
                "Fatal abort did not signal cooperative preparation cancellation."
            );
        }
        finally
        {
            lifecycle.CompletePreparation(true);
            for (var attempt = 0; attempt < 8 && !session.IsCompleted; attempt++)
            {
                session.ProcessEvents(64);
                if (!composition.IsDisposed)
                    composition.Flush(256);
            }
        }
    }

    [TestMethod]
    public void LatePreparationFaultIsObservedOffTheCompletedOwnerContext()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "late-prepare-fault");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var lifecycle = new PendingPrepareLifecycle();
        var reports = new List<ApplicationFailureReport>();
        var reportWork = new Queue<Action>();
        var fatal = new InvalidOperationException("fatal-host");
        var late = new IOException("late-prepare");
        var session = new ApplicationSession(
            "Late prepare",
            composition,
            theme,
            lifecycle,
            report =>
            {
                reports.Add(report);
                throw new InvalidOperationException("reporter-failed");
            },
            reportWork.Enqueue
        );

        session.Start();
        PumpUntil(session, () => session.Status.Phase == ApplicationPhase.Running);
        session.RequestClose();
        PumpUntil(session, () => session.Status.Phase == ApplicationPhase.PreparingClose);
        session.Abort(fatal);

        Assert.IsTrue(session.IsCompleted);
        Assert.AreSame(fatal, session.Failure);
        Assert.AreEqual(1, reportWork.Count, "Terminal reporting was not dispatched.");
        reportWork.Dequeue()();
        Assert.AreEqual(ApplicationFailureKind.Terminal, reports[0].Kind);

        lifecycle.FailPreparation(late);

        Assert.AreEqual(
            1,
            reportWork.Count,
            "The late fault depended on the completed owner synchronization context."
        );
        reportWork.Dequeue()();
        Assert.AreEqual(2, reports.Count);
        Assert.AreEqual(ApplicationFailureKind.LateClosePreparation, reports[1].Kind);
        Assert.AreSame(late, reports[1].Error);
        Assert.AreSame(fatal, session.Failure, "Reporting replaced the terminal failure.");
        Assert.AreEqual(ApplicationPhase.Completed, session.Status.Phase);
    }

    [TestMethod]
    public void EscapingPreparationFailureTerminatesInsteadOfResuming()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "failed-prepare");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var lifecycle = new PendingPrepareLifecycle();
        var failure = new IOException("prepare-escaped");
        var session = new ApplicationSession(
            "Failed prepare",
            composition,
            theme,
            lifecycle,
            static _ => { },
            static report => report()
        );

        session.Start();
        PumpUntil(session, () => session.Status.Phase == ApplicationPhase.Running);
        session.RequestClose();
        PumpUntil(session, () => session.Status.Phase == ApplicationPhase.PreparingClose);
        lifecycle.FailPreparation(failure);
        PumpUntil(session, () => session.IsCompleted);

        Assert.AreSame(failure, session.Failure);
        Assert.AreSame(failure, session.Status.Error);
        Assert.AreEqual(1, lifecycle.StopCalls);
        Assert.AreEqual(1, lifecycle.DisposeCalls);
    }

    private static void PumpUntil(ApplicationSession session, Func<bool> complete)
    {
        for (var attempt = 0; attempt < 16 && !complete(); attempt++)
        {
            session.ProcessEvents(64);
            if (!session.Composition.IsDisposed)
                session.Composition.Flush(256);
        }
        Assert.IsTrue(complete(), "The bounded lifecycle pump did not reach its target phase.");
    }

    private sealed class PendingPrepareLifecycle : IApplicationLifecycle
    {
        private readonly TaskCompletionSource<bool> _preparation = new();

        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public CancellationToken PreparationToken { get; private set; }

        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session) =>
            ValueTask.FromResult(
                ComponentRecipe.Create("fatal-prepare-root", static (_, _) => { })
            );

        public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken)
        {
            PreparationToken = cancellationToken;
            return new(_preparation.Task);
        }

        public ValueTask StopAsync()
        {
            StopCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }

        public void CompletePreparation(bool accepted) => _preparation.TrySetResult(accepted);

        public void FailPreparation(Exception error) => _preparation.TrySetException(error);
    }
}
