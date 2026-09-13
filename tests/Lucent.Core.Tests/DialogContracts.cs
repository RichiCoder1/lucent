using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class DialogContracts
{
    [TestMethod]
    public void SecondEscapePassesClosedTooltipAndCancelsDialog()
    {
        using var composition = new Composition(new ReactiveGraph(), "dialog-tooltip-escape");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var controller = new DialogController<int>(composition.Root.Scope);
        composition.Mount(
            composition.Root,
            theme,
            Components.Dialog(
                controller,
                "Review",
                [Components.Tooltip("Action help", [Components.Button("Action", () => { })])]
            )
        );
        var session = controller.OpenAsync().AsTask();
        composition.Flush();
        var request = composition.Input.ActiveSurface!;
        var popup = request.CreateComposition();
        popup.Flush();
        using var scene = SceneLayout.Project(popup, new(420, 340, 1), new EmptyShaper());
        Assert.IsTrue(popup.Input.SetScene(scene));
        Assert.IsTrue(request.FocusInitial());
        var tooltip = popup.Input.ActiveSurface;
        Assert.IsNotNull(tooltip);
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled);
        Assert.IsTrue(tooltip.IsDismissed);
        Assert.IsTrue(controller.IsOpen);
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled);
        Assert.IsFalse(controller.IsOpen, "A closed tooltip swallowed dialog cancellation.");
        Assert.IsTrue(session.IsCompletedSuccessfully);
        Assert.IsTrue(session.Result.IsCanceled);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HostDismissalDuringAcceptanceSettlesSessionWithoutCancelingApplication(
        bool succeeds
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "dialog-host-dismissal");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var controller = new DialogController<int>(composition.Root.Scope);
        composition.Mount(
            composition.Root,
            theme,
            Components.Dialog(controller, "Review", [Components.Text("Body")])
        );
        var session = controller.OpenAsync().AsTask();
        composition.Flush();
        var request = composition.Input.ActiveSurface!;
        var application = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var posted = new ManualResetEventSlim();
        using var postedLifetime = posted;
        var submission = controller
            .AcceptAsync(
                7,
                (_, token) =>
                {
                    Assert.IsFalse(token.CanBeCanceled);
                    return new ValueTask(application.Task);
                }
            )
            .AsTask();
        composition.Flush();
        graph.WorkAvailable += posted.Set;
        request.Dismiss();
        composition.Flush();
        posted.Reset();
        Assert.IsFalse(controller.IsOpen);
        Assert.IsFalse(application.Task.IsCompleted);
        Assert.IsFalse(session.IsCompleted);
        if (succeeds)
            application.SetResult();
        else
            application.SetException(new InvalidOperationException("Expected failure"));
        Assert.IsTrue(posted.Wait(TimeSpan.FromSeconds(5)));
        graph.Drain();
        Assert.IsTrue(submission.IsCompletedSuccessfully);
        Assert.AreEqual(
            succeeds ? DialogSubmissionStatus.Accepted : DialogSubmissionStatus.Failed,
            submission.Result.Status
        );
        Assert.IsTrue(
            session.IsCompletedSuccessfully,
            "A dismissed failed dialog stranded OpenAsync."
        );
        Assert.AreEqual(succeeds, session.Result.IsAccepted);
        Assert.IsFalse(controller.IsOpen);
        Assert.IsFalse(controller.IsPending);
        var reopened = controller.OpenAsync().AsTask();
        composition.Flush();
        Assert.IsNotNull(composition.Input.ActiveSurface);
        Assert.IsTrue(controller.TryCancel());
        Assert.IsTrue(reopened.Result.IsCanceled);
    }

    [TestMethod]
    public void DismissedModalDoesNotBlockPopoverBeforeHostCleanup()
    {
        using var composition = new Composition(new ReactiveGraph(), "dialog-popover-replacement");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var controller = new DialogController<int>(composition.Root.Scope);
        var open = composition.Root.Scope.Signal(false, "popover-open");
        composition.Mount(
            composition.Root,
            theme,
            Components.Dialog(controller, "Review", [Components.Text("Body")])
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Popover(
                () => open.Value,
                value => open.Value = value,
                Components.Text("Help"),
                [Components.Button("Help", () => { })]
            )
        );
        var session = controller.OpenAsync().AsTask();
        composition.Flush();
        var modal = composition.Input.ActiveSurface!;
        Assert.IsTrue(controller.TryCancel());
        open.Value = true;
        Assert.IsTrue(session.Result.IsCanceled);
        composition.Flush();
        Assert.IsTrue(modal.IsDismissed);
        Assert.IsNotNull(
            composition.Input.ActiveSurface,
            "Dismissed modal blocked a new nonmodal request."
        );
        Assert.IsFalse(composition.Input.ActiveSurface.IsModal);
        composition.Input.CompleteSurface(modal);
        Assert.IsNotNull(composition.Input.ActiveSurface);
    }

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

    [TestMethod]
    public void ExplicitInitialFocusKeepsImeKeysInsideDialogBeforeEscapeCancels()
    {
        using var composition = new Composition(new ReactiveGraph(), "dialog-ime");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var controller = new DialogController<string>(composition.Root.Scope);
        PopupSurfaceRequest? request = null;
        var focusCalls = 0;
        var defaultInvocations = 0;
        composition.Input.SurfaceRequested += value => request = value;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Dialog(
                controller,
                "Edit workspace",
                [Components.TextField(label: "Workspace name"), PassiveFocusTarget()],
                [Components.Button("Apply", () => { })],
                defaultAccept: () => defaultInvocations++,
                initialFocus: popup =>
                {
                    focusCalls++;
                    return popup.Input.MoveFocus(FocusTraversalDirection.Next);
                }
            )
        );

        var session = controller.OpenAsync().AsTask();
        composition.Flush();
        Assert.IsNotNull(request);
        var popup = request!.CreateComposition();
        popup.Flush();
        using var scene = SceneLayout.Project(popup, new(420, 340, 1), new EmptyShaper());
        Assert.IsTrue(popup.Input.SetScene(scene));
        Assert.IsTrue(request.FocusInitial());
        Assert.AreEqual(1, focusCalls);

        Assert.IsTrue(popup.Input.DispatchText(new(TextInputKind.Preedit, "候", 0, 1)).Handled);
        Assert.IsTrue(popup.Input.HasTextComposition);
        Assert.IsTrue(
            popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled,
            "Enter should remain inside the IME-owned editor while preedit is active."
        );
        Assert.AreEqual(0, defaultInvocations);
        Assert.IsTrue(controller.IsOpen);

        Assert.IsTrue(
            popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled,
            "Escape should cancel the active preedit rather than dismissing the dialog."
        );
        Assert.IsFalse(popup.Input.HasTextComposition);
        Assert.IsTrue(controller.IsOpen);

        // A text field owns Enter as its commit key. Move to a focusable child that leaves
        // Enter unhandled so the dialog's default action can receive the bubbled command.
        Assert.IsTrue(popup.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(
            popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled,
            "Enter should reach the dialog default action after preedit ends."
        );
        Assert.AreEqual(1, defaultInvocations);
        Assert.IsTrue(controller.IsOpen);

        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled);
        Assert.IsFalse(controller.IsOpen);
        Assert.IsTrue(session.GetAwaiter().GetResult().IsCanceled);
    }

    [TestMethod]
    public void DialogBodyScrollRetainsActionRowAndSemanticScrollContract()
    {
        using var composition = new Composition(new ReactiveGraph(), "dialog-scroll");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var controller = new DialogController<int>(composition.Root.Scope);
        var body = ComponentContent.Create(
            Enumerable
                .Range(0, 28)
                .Select(index =>
                    (ContentRecipe)
                        Components.Text($"Scrollable dialog line {index + 1} with useful context.")
                )
                .ToArray()
        );
        PopupSurfaceRequest? request = null;
        composition.Input.SurfaceRequested += value => request = value;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Dialog(
                controller,
                "Scrollable review",
                body,
                [Components.Button("Apply", () => { }), Components.DialogCancel()]
            )
        );

        var session = controller.OpenAsync().AsTask();
        composition.Flush();
        Assert.IsNotNull(request);
        var popup = request!.CreateComposition();
        popup.Flush();
        using var scene = SceneLayout.Project(popup, new(360, 220, 1), new EmptyShaper());
        Assert.IsTrue(popup.Input.SetScene(scene));
        var semantics = Flatten(popup.SemanticSnapshot()!).ToArray();
        Assert.IsTrue(
            semantics.Any(node =>
                node.Name == "Dialog content" && node.Actions.HasFlag(SemanticAction.Scroll)
            ),
            "Dialog content did not retain a semantic scroll surface."
        );
        Assert.IsTrue(semantics.Any(node => node.Name == "Apply"));
        Assert.IsTrue(semantics.Any(node => node.Name == "Cancel"));
        Assert.IsTrue(scene.Boxes.Count > 0);

        request.Dispose();
        Assert.IsTrue(session.GetAwaiter().GetResult().IsCanceled);
    }

    [TestMethod]
    public void DialogMeasurePreservesStockAndReactiveAuthoredWidthLimitsAcrossRemeasure()
    {
        using var composition = new Composition(new ReactiveGraph(), "dialog-measure");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var defaultController = new DialogController<int>(composition.Root.Scope);
        PopupSurfaceRequest? defaultRequest = null;
        composition.Input.SurfaceRequested += value => defaultRequest = value;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Dialog(
                defaultController,
                "Review workspace settings",
                [Components.Text("Confirm the workspace settings before applying them.")]
            )
        );

        var defaultSession = defaultController.OpenAsync().AsTask();
        composition.Flush();
        Assert.IsNotNull(defaultRequest);
        var defaultBounds = defaultRequest!.Measure(new EmptyShaper(), new(3440, 1200, 1));
        Assert.IsTrue(
            defaultBounds.Width is >= 280 and <= 640,
            $"The stock dialog measured {defaultBounds.Width} logical pixels wide."
        );
        defaultRequest.Dispose();
        Assert.IsTrue(defaultSession.GetAwaiter().GetResult().IsCanceled);

        using var authoredComposition = new Composition(
            new ReactiveGraph(),
            "dialog-authored-measure"
        );
        using var authoredTheme = new ThemeContext(
            authoredComposition.Root.Scope,
            ControlThemes.Light
        );
        using var authoredController = new DialogController<int>(authoredComposition.Root.Scope);
        var authoredWidth = authoredComposition.Root.Scope.Signal(520f, "dialog-authored-width");
        PopupSurfaceRequest? authoredRequest = null;
        authoredComposition.Input.SurfaceRequested += value => authoredRequest = value;
        _ = authoredComposition.Mount(
            authoredComposition.Root,
            authoredTheme,
            Components.Dialog(
                authoredController,
                "Reactive dialog width",
                [
                    Components.Text(
                        "This dialog keeps its authored width across host remeasurement."
                    ),
                ],
                style: Style
                    .Empty.Bind(LayoutProperties.MinWidth, () => authoredWidth.Value - 40)
                    .Bind(LayoutProperties.MaxWidth, () => authoredWidth.Value)
            )
        );

        var authoredSession = authoredController.OpenAsync().AsTask();
        authoredComposition.Flush();
        Assert.IsNotNull(authoredRequest);
        var narrow = authoredRequest!.Measure(new EmptyShaper(), new(320, 600, 1));
        Assert.AreEqual(320f, narrow.Width);

        var wide = authoredRequest.Measure(new EmptyShaper(), new(1200, 600, 1));
        Assert.IsTrue(
            wide.Width is >= 480 and <= 520,
            $"The authored dialog measured {wide.Width} logical pixels wide after widening."
        );

        authoredWidth.Value = 400;
        authoredComposition.Flush();
        var updated = authoredRequest.Measure(new EmptyShaper(), new(1200, 600, 1));
        Assert.IsTrue(
            updated.Width is >= 360 and <= 400,
            $"The reactively resized dialog measured {updated.Width} logical pixels wide."
        );

        authoredRequest.Dispose();
        Assert.IsTrue(authoredSession.GetAwaiter().GetResult().IsCanceled);
    }

    [TestMethod]
    public void DialogCompositionOwnsNestedPopoverSurface()
    {
        using var composition = new Composition(new ReactiveGraph(), "dialog-nested-surface");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var controller = new DialogController<int>(composition.Root.Scope);
        var nestedOpen = composition.Root.Scope.Signal(true, "nested-popover-open");
        var nested = Components.Popover(
            () => nestedOpen.Value,
            value => nestedOpen.Value = value,
            Components.Text("Nested dialog support content."),
            [Components.Button("Nested trigger", () => { })]
        );
        PopupSurfaceRequest? request = null;
        composition.Input.SurfaceRequested += value => request = value;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Dialog(controller, "Nested surface", [nested])
        );

        var session = controller.OpenAsync().AsTask();
        composition.Flush();
        Assert.IsNotNull(request);
        var popup = request!.CreateComposition();
        popup.Flush();
        var nestedSurface = popup.Input.ActiveSurface;
        Assert.IsNotNull(nestedSurface);
        Assert.AreSame(popup, nestedSurface!.Owner);
        Assert.IsFalse(nestedSurface.IsModal);
        Assert.IsTrue(nestedSurface.IsInteractive);

        request.Dispose();
        Assert.IsTrue(session.GetAwaiter().GetResult().IsCanceled);
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private static ComponentRecipe PassiveFocusTarget() =>
        ComponentRecipe.Create(
            "dialog-passive-focus",
            static (context, root) =>
            {
                root.Present(
                    context.Theme,
                    author: Style
                        .Empty.Set(LayoutProperties.Width, 220f)
                        .Set(LayoutProperties.Height, 24f)
                );
                root.AttachBehaviors(new PassiveFocusBehavior());
            }
        );

    private sealed class PassiveFocusBehavior : Behavior
    {
        public override string Name => "dialog-passive-focus";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Focus;

        public override void Attach(BehaviorContext context) => context.MakeFocusable();
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            var glyphs = new List<ShapedGlyph>();
            var offset = 0;
            var x = 0f;
            foreach (var rune in request.Text.EnumerateRunes())
            {
                glyphs.Add(new(1, (uint)offset, x, 0, 10, 0, 0));
                offset += rune.Utf16SequenceLength;
                x += 10;
            }
            var run = new ShapedRun(
                "dialog-fixed",
                "dialog-fixed",
                400,
                5,
                0,
                "dialog-fixed#0",
                0,
                "dialog-fixed#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                x,
                glyphs
            );
            var line = new ParagraphLine(
                0,
                request.Text.Length,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                0,
                x,
                0,
                false
            );
            return new(
                "dialog-fixed",
                x,
                request.FontSize,
                [run],
                [line],
                false,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }
    }
}
