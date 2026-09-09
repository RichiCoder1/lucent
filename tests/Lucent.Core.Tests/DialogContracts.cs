using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class DialogContracts
{
    [TestMethod]
    public async Task AcceptedValueCompletesTypedSessionAndCloses()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("dialog-accepted");
        using var controller = new DialogController<string>(owner);

        var session = controller.OpenAsync().AsTask();
        var submission = await controller.AcceptAsync(
            "workspace",
            static (_, _) => ValueTask.CompletedTask
        );

        Assert.AreEqual(DialogSubmissionStatus.Accepted, submission.Status);
        var result = await session;
        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual("workspace", result.Value);
        Assert.IsFalse(controller.IsOpen);
        Assert.IsFalse(controller.IsPending);
    }

    [TestMethod]
    public void PendingDuplicateIsBlockedAndFailureKeepsSessionOpen()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("dialog-pending");
        using var controller = new DialogController<int>(owner);
        var application = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var postObserved = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        graph.WorkAvailable += () => postObserved.TrySetResult(null);

        var initialSession = controller.OpenAsync().AsTask();
        var first = controller.AcceptAsync(1, (_, _) => new ValueTask(application.Task)).AsTask();
        var duplicate = controller
            .AcceptAsync(2, static (_, _) => ValueTask.CompletedTask)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        Assert.AreEqual(DialogSubmissionStatus.AlreadyPending, duplicate.Status);
        Assert.IsTrue(controller.IsPending);

        application.SetResult(null);
        Assert.IsTrue(
            SpinWait.SpinUntil(() => first.IsCompleted, TimeSpan.FromSeconds(5)),
            "The accepted application did not complete."
        );
        Assert.IsTrue(
            SpinWait.SpinUntil(() => postObserved.Task.IsCompleted, TimeSpan.FromSeconds(5)),
            "The accepted UI cleanup was not queued."
        );
        graph.Drain();
        Assert.AreEqual(DialogSubmissionStatus.Accepted, first.GetAwaiter().GetResult().Status);
        Assert.IsTrue(initialSession.GetAwaiter().GetResult().IsAccepted);

        var failureSession = controller.OpenAsync().AsTask();
        var failure = controller
            .AcceptAsync(
                3,
                static (_, _) =>
                    new ValueTask(Task.FromException(new InvalidOperationException("private")))
            )
            .AsTask();
        Assert.IsTrue(
            SpinWait.SpinUntil(() => failure.IsCompleted, TimeSpan.FromSeconds(5)),
            "The failed application did not complete."
        );
        graph.Drain();

        var failed = failure.GetAwaiter().GetResult();
        Assert.AreEqual(DialogSubmissionStatus.Failed, failed.Status);
        Assert.AreEqual("The action could not be completed.", failed.FailureMessage);
        Assert.AreEqual(failed.FailureMessage, controller.FailureMessage);
        Assert.IsTrue(controller.IsOpen);
        Assert.IsFalse(controller.IsPending);
        Assert.IsTrue(controller.TryCancel());
        Assert.IsTrue(failureSession.GetAwaiter().GetResult().IsCanceled);
    }

    [TestMethod]
    public void QueuedCompletionSettlesTypedAndSubmissionTasksWhenOwnerDisposes()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("dialog-disposal-race");
        using var controller = new DialogController<int>(owner);
        var application = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var postObserved = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        graph.WorkAvailable += () => postObserved.TrySetResult(null);

        var session = controller.OpenAsync().AsTask();
        var submission = controller
            .AcceptAsync(7, (_, _) => new ValueTask(application.Task))
            .AsTask();

        application.SetResult(null);
        Assert.IsTrue(
            SpinWait.SpinUntil(() => postObserved.Task.IsCompleted, TimeSpan.FromSeconds(5)),
            "The completion was not queued before owner disposal."
        );

        owner.Dispose();
        graph.Drain();

        Assert.AreEqual(
            DialogSubmissionStatus.Accepted,
            submission.GetAwaiter().GetResult().Status
        );
        var result = session.GetAwaiter().GetResult();
        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(7, result.Value);
    }

    [TestMethod]
    public void DestructiveDialogDisablesImplicitInitialFocusFallback()
    {
        using var composition = new Composition(new ReactiveGraph(), "dialog-surface");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var controller = new DialogController<int>(composition.Root.Scope);
        PopupSurfaceRequest? request = null;
        composition.Input.SurfaceRequested += value => request = value;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Dialog(
                controller,
                "Delete workspace",
                [Components.Text("This action cannot be undone.")],
                [Components.Button("Remove", () => { })],
                destructive: true
            )
        );

        var session = controller.OpenAsync().AsTask();
        composition.Flush();

        Assert.IsNotNull(request);
        Assert.IsTrue(request!.IsModal);
        Assert.IsTrue(request.ConsumeOutsideClick);
        Assert.IsFalse(request.AllowInitialFocusFallback);

        request.Dispose();
        Assert.IsTrue(session.GetAwaiter().GetResult().IsCanceled);
    }
}
