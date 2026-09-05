using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class CommandContracts
{
    [TestMethod]
    public void AsyncCommandOwnsBusyErrorRetryAndCancellation()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("commands");
        var enabled = owner.Signal(false, "save-enabled");
        var attempts = 0;
        using var command = new ApplicationCommand(
            owner,
            _ =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new InvalidOperationException("save failed"))
                    : Task.CompletedTask;
            },
            () => enabled.Value,
            "save"
        );

        Assert.IsFalse(command.TryExecute(), "Disabled command accepted work.");
        enabled.Value = true;
        Assert.IsTrue(command.TryExecute(), "Enabled command rejected work.");
        Assert.IsTrue(
            command.IsBusy,
            "Accepted asynchronous work was not busy before owner drain."
        );
        Assert.IsFalse(command.TryExecute(), "Busy command accepted overlapping work.");
        graph.Drain();
        Assert.IsFalse(command.IsBusy);
        Assert.IsInstanceOfType<InvalidOperationException>(command.Error);

        Assert.IsTrue(command.TryExecute(), "Failed command could not be retried.");
        graph.Drain();
        Assert.IsNull(command.Error, "Successful retry retained the previous error.");
        Assert.AreEqual(2, attempts);

        CancellationToken observed = default;
        var pending = new ApplicationCommand(
            owner,
            cancellation =>
            {
                observed = cancellation;
                return Task.Delay(Timeout.Infinite, cancellation);
            },
            name: "pending"
        );
        Assert.IsTrue(pending.TryExecute());
        pending.Dispose();
        Assert.IsTrue(
            observed.IsCancellationRequested,
            "Disposal did not cancel command-owned work."
        );
    }

    [TestMethod]
    public void InitialObserversReceiveBusyEnabledAndErrorTransitions()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("observed-command");
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var command = new ApplicationCommand(owner, _ => completion.Task, name: "observed");
        var observed = new List<(bool Enabled, bool Busy, string? Error)>();
        _ = owner.Effect(
            () => observed.Add((command.IsEnabled, command.IsBusy, command.Error?.Message)),
            "command-observer"
        );

        graph.Drain();
        CollectionAssert.AreEqual(
            new[] { (Enabled: true, Busy: false, Error: (string?)null) },
            observed
        );

        Assert.IsTrue(command.TryExecute());
        graph.Drain();
        Assert.AreEqual(
            (Enabled: false, Busy: true, Error: (string?)null),
            observed[^1],
            "An observer installed before first execution missed pending state."
        );

        using var completionPosted = new ManualResetEventSlim();
        graph.WorkAvailable += completionPosted.Set;
        Task.Run(() => completion.SetException(new InvalidOperationException("observed failure")))
            .GetAwaiter()
            .GetResult();
        Assert.IsTrue(
            completionPosted.Wait(TimeSpan.FromSeconds(2)),
            "Async command completion did not wake its owning graph."
        );
        graph.Drain();
        Assert.AreEqual(
            (Enabled: true, Busy: false, Error: "observed failure"),
            observed[^1],
            "Graph-owned async completion did not publish error and availability."
        );
    }

    [TestMethod]
    public void AsyncCommandCallbackSignalAccessDoesNotBecomeAReactiveDependency()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("untracked-command");
        var enabled = owner.Signal(true, "untracked-enabled");
        var callbackInput = owner.Signal(1, "callback-input");
        var callbackOutput = owner.Signal(0, "callback-output");
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var runs = 0;
        using var command = new ApplicationCommand(
            owner,
            async _ =>
            {
                runs++;
                callbackOutput.Value = callbackInput.Value + 1;
                if (runs == 1)
                    await completion.Task;
            },
            () => enabled.Value,
            "untracked"
        );

        Assert.IsTrue(command.TryExecute());
        Assert.AreEqual(1, runs);
        Assert.AreEqual(2, callbackOutput.Value);

        callbackInput.Value = 10;
        callbackOutput.Value = 20;
        graph.Drain();
        Assert.IsTrue(command.IsBusy);
        Assert.AreEqual(1, runs, "Signal changes reran a pending command callback.");

        using var completionPosted = new ManualResetEventSlim();
        graph.WorkAvailable += completionPosted.Set;
        Task.Run(completion.SetResult).GetAwaiter().GetResult();
        Assert.IsTrue(
            completionPosted.Wait(TimeSpan.FromSeconds(2)),
            "Async command completion did not wake its owning graph."
        );
        graph.Drain();
        Assert.IsFalse(command.IsBusy);

        callbackInput.Value = 30;
        callbackOutput.Value = 40;
        graph.Drain();
        Assert.IsFalse(command.IsBusy);
        Assert.AreEqual(1, runs, "Signal changes reran a completed command callback.");

        enabled.Value = false;
        Assert.IsFalse(command.IsEnabled, "Enabled callback stopped tracking its signal.");
        enabled.Value = true;
        Assert.IsTrue(command.IsEnabled, "Enabled callback did not react to its signal.");

        Assert.IsTrue(command.TryExecute());
        graph.Drain();
        Assert.AreEqual(2, runs, "Explicit execution did not rerun the callback.");
        Assert.AreEqual(31, callbackOutput.Value);
    }

    [TestMethod]
    public void NearestCommandScopeConsumesDisabledBusyAndExactChords()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "command-routing");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("command-routing"));
        Present(composition.Root, theme, 100, 100);
        var outer = composition.Child(composition.Root, "outer-scope");
        Present(outer, theme, 100, 100);
        var inner = composition.Child(outer, "inner-scope");
        Present(inner, theme, 100, 100);
        var target = composition.Child(inner, "target");
        Present(target, theme, 100, 20);
        target.AttachBehaviors(new FocusTarget());

        var outerRuns = 0;
        var innerRuns = 0;
        var innerEnabled = composition.Root.Scope.Signal(false, "inner-enabled");
        using var outerSave = new ApplicationCommand(
            composition.Root.Scope,
            _ =>
            {
                outerRuns++;
                return Task.CompletedTask;
            },
            name: "outer-save"
        );
        using var innerSave = new ApplicationCommand(
            composition.Root.Scope,
            _ =>
            {
                innerRuns++;
                return Task.CompletedTask;
            },
            () => innerEnabled.Value,
            "inner-save"
        );
        outer.AttachBehaviors(
            new CommandScopeBehavior(new CommandBindings([new(outerSave, KeyChord.Ctrl(Key.S))]))
        );
        inner.AttachBehaviors(
            new CommandScopeBehavior(new CommandBindings([new(innerSave, KeyChord.Ctrl(Key.S))]))
        );

        var router = composition.Input;
        Assert.IsTrue(
            router.SetScene(SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper()))
        );
        Assert.IsTrue(router.MoveFocus(FocusTraversalDirection.Next));

        var disabled = router.DispatchKey(new(KeyCommandKind.Down, Key.S, KeyModifiers.Control));
        Assert.IsTrue(disabled.Handled, "A nearest disabled binding did not own its chord.");
        Assert.AreEqual(0, innerRuns);
        Assert.AreEqual(0, outerRuns);

        innerEnabled.Value = true;
        Assert.IsTrue(
            router.DispatchKey(new(KeyCommandKind.Down, Key.S, KeyModifiers.Control)).Handled
        );
        Assert.AreEqual(1, innerRuns);
        Assert.AreEqual(0, outerRuns);
        Assert.IsTrue(
            router
                .DispatchKey(new(KeyCommandKind.Down, Key.S, KeyModifiers.Control, IsRepeat: true))
                .Handled == false,
            "A repeating application chord was executed."
        );
        Assert.IsFalse(
            router
                .DispatchKey(
                    new(KeyCommandKind.Down, Key.S, KeyModifiers.Control | KeyModifiers.Alt)
                )
                .Handled,
            "AltGr-like Control+Alt input was stolen by a Control-only command."
        );
        graph.Drain();
    }

    [TestMethod]
    public void CommandScopeComponentRoutesFromFocusedDescendant()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "command-component");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var runs = 0;
        using var capture = new ApplicationCommand(
            composition.Root.Scope,
            _ =>
            {
                runs++;
                return Task.CompletedTask;
            },
            name: "capture"
        );
        var bindings = new CommandBindings([new(capture, KeyChord.Ctrl(Key.N))]);
        var commandScope = composition.Mount(
            composition.Root,
            theme,
            Components.CommandScope(
                [
                    Components.ScrollViewport(
                        [],
                        label: "Target",
                        style: Style
                            .Empty.Set(LayoutProperties.Width, 100f)
                            .Set(LayoutProperties.Height, 40f)
                    ),
                ],
                bindings
            )
        );
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper());
        Assert.AreEqual(
            100f,
            scene.Boxes.Single(box => box.Identity.ElementId == commandScope.Id).Bounds.Height,
            "Mounted CommandScope did not fill its available main axis."
        );
        Assert.IsTrue(composition.Input.SetScene(scene));
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(
            composition
                .Input.DispatchKey(new(KeyCommandKind.Down, Key.N, KeyModifiers.Control))
                .Handled
        );
        Assert.AreEqual(1, runs, "Mounted CommandScope did not route from its focused child.");
    }

    [TestMethod]
    public void CommandBindingsRejectDuplicateChords()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("duplicate");
        using var first = new ApplicationCommand(owner, _ => Task.CompletedTask, name: "first");
        using var second = new ApplicationCommand(owner, _ => Task.CompletedTask, name: "second");
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CommandBindings([
                new(first, KeyChord.Ctrl(Key.F)),
                new(second, KeyChord.Ctrl(Key.F)),
            ])
        );
    }

    private static void Present(Element element, ThemeContext theme, float width, float height) =>
        element.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, width)
                .Set(LayoutProperties.Height, height)
                .Set(LayoutProperties.Axis, LayoutAxis.Column)
                .Set(LayoutProperties.Clip, true)
        );

    private sealed class FocusTarget : Behavior
    {
        public override string Name => "focus-target";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Focus;

        public override void Attach(BehaviorContext context) => context.MakeFocusable();
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }
}
