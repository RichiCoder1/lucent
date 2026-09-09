using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class SelectionContracts
{
    [TestMethod]
    public void ControlledTogglePreservesAppliedStateAndRequestsExactlyOnce()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "selection-toggle");
        ConfigureSelectionImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var check = graph.Signal(CheckState.Mixed, "check");
        var enabled = graph.Signal(false, "switch");
        var checkRequests = new List<CheckState>();
        var switchRequests = new List<bool>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Column([
                Components.CheckBox(
                    "Aggregate",
                    () => check.Value,
                    checkRequests.Add,
                    style: Style.Empty.Width(200).Height(36)
                ),
                Components.Switch(
                    "Immediate setting",
                    () => enabled.Value,
                    switchRequests.Add,
                    Style.Empty.Width(200).Height(36)
                ),
            ])
        );
        graph.Drain();

        var aggregate = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Aggregate");
        Assert.AreEqual(SemanticRole.CheckBox, aggregate.Role);
        Assert.AreEqual(SemanticToggleState.Indeterminate, aggregate.ToggleState);
        Assert.AreEqual(
            SemanticCommandResult.Requested,
            composition.ExecuteSemanticCommand(aggregate.Identity, new(SemanticCommandKind.Toggle))
        );
        graph.Drain();
        Assert.AreEqual(CheckState.On, checkRequests.Single());
        Assert.AreEqual(
            SemanticToggleState.Indeterminate,
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Name == "Aggregate")
                .ToggleState,
            "A request must not optimistically replace the caller's applied mixed state."
        );

        check.Value = CheckState.On;
        graph.Drain();
        aggregate = Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Aggregate");
        Assert.AreEqual(SemanticToggleState.On, aggregate.ToggleState);
        Assert.AreEqual(
            SemanticCommandResult.Requested,
            composition.ExecuteSemanticCommand(aggregate.Identity, new(SemanticCommandKind.Toggle))
        );
        Assert.AreEqual(CheckState.Off, checkRequests[^1]);

        var scene = Install(composition, graph, 240, 100);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Space));
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Space, IsRepeat: true));
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Up, Key.Space));
        Assert.AreEqual(
            3,
            checkRequests.Count,
            "Space must request once and ignore key repeat/up."
        );
        Assert.AreEqual(CheckState.Off, checkRequests[^1]);

        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Space));
        Assert.AreEqual(true, switchRequests.Single());
        Assert.AreEqual(
            SemanticToggleState.Off,
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Name == "Immediate setting")
                .ToggleState
        );
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void RadioGroupRovesSkipsDisabledAndDoesNotFabricateAppliedSelection()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "radio-group");
        ConfigureSelectionImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var items = graph.Signal(
            new[]
            {
                new RadioOption<string>("a", "Alpha"),
                new RadioOption<string>("b", "Beta", enabled: false),
                new RadioOption<string>("c", "Charlie"),
            },
            "options"
        );
        var selected = graph.Signal("a", "selected");
        var requests = new List<string>();
        composition.Mount(
            composition.Root,
            theme,
            Components.RadioGroup(
                "Choice",
                () => items.Value,
                () => selected.Value,
                requests.Add,
                style: Style.Empty.Width(240).Height(140)
            )
        );
        graph.Drain();

        var group = Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Choice");
        Assert.AreEqual(SemanticRole.RadioGroup, group.Role);
        Assert.AreEqual(new SemanticSelectionSnapshot(false, true), group.Selection);
        Assert.AreEqual("Alpha", SelectedRadio(composition).Name);
        var beta = Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Beta");
        Assert.AreEqual(
            SemanticCommandResult.Disabled,
            composition.ExecuteSemanticCommand(beta.Identity, new(SemanticCommandKind.Select))
        );

        var scene = Install(composition, graph, 260, 180);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.AreEqual("Alpha", FocusedRadio(composition).Name);
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled);
        graph.Drain();
        Assert.AreEqual("Charlie", FocusedRadio(composition).Name);
        Assert.AreEqual("c", requests.Single());
        Assert.AreEqual(
            "Alpha",
            SelectedRadio(composition).Name,
            "Arrow navigation must not optimistically commit controlled selection."
        );

        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Space));
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Space, IsRepeat: true));
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual("c", requests[^1]);
        selected.Value = "c";
        graph.Drain();
        Assert.AreEqual("Charlie", SelectedRadio(composition).Name);
        scene = Install(composition, graph, 260, 180);

        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Home));
        graph.Drain();
        Assert.AreEqual("Alpha", FocusedRadio(composition).Name);
        Assert.AreEqual("a", requests[^1]);
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.End));
        graph.Drain();
        Assert.AreEqual("Charlie", FocusedRadio(composition).Name);

        var countBeforeRemoval = requests.Count;
        items.Value = [new("a", "Alpha"), new("b", "Beta", enabled: false)];
        graph.Drain();
        Assert.AreEqual(
            countBeforeRemoval,
            requests.Count,
            "Removal must not fabricate an application choice."
        );
        Assert.AreEqual(0, RadioNodes(composition).Count(node => node.Selected));
        Assert.AreEqual(
            true,
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Name == "Choice")
                .Selection?.IsSelectionRequired
        );
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void TriStateCycleAndOptionValidationAreExplicit()
    {
        var state = CheckState.Off;
        var requests = new List<CheckState>();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "tri-state");
        ConfigureSelectionImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Mount(
            composition.Root,
            theme,
            Components.CheckBox(
                "Cycle",
                () => state,
                next =>
                {
                    requests.Add(next);
                    state = next;
                },
                CheckStateCycle.TriState
            )
        );
        graph.Drain();
        for (var index = 0; index < 3; index++)
        {
            var node = Nodes(composition.SemanticSnapshot()!).Single(node => node.Name == "Cycle");
            _ = composition.ExecuteSemanticCommand(node.Identity, new(SemanticCommandKind.Toggle));
            graph.Drain();
        }
        Assert.IsTrue(requests.SequenceEqual([CheckState.On, CheckState.Mixed, CheckState.Off]));
        Assert.ThrowsExactly<ArgumentException>(() => new RadioOption<string>("key", " "));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Components.CheckBox("Bad", () => CheckState.Off, _ => { }, (CheckStateCycle)99)
        );
    }

    [TestMethod]
    public void KeyedPolicyCanDeferNavigationUntilConfirmationAndCancel()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "confirmation-policy");
        using var scope = composition.Root.Scope.CreateChild("confirmation-policy");
        IReadOnlyList<RadioOption<string>> items = [new("a", "Alpha"), new("b", "Beta")];
        var applied = "a";
        var requests = new List<string>();
        var policy = new KeyedSelectionPolicy<string, RadioOption<string>>(
            scope,
            "confirmation-policy",
            () => items,
            item => item.Key,
            item => item.Enabled,
            () => SelectedKey.Some(applied),
            requests.Add,
            KeyedSelectionCommitMode.OnConfirmation
        );

        Assert.IsTrue(policy.Move(Key.Down));
        Assert.IsTrue(policy.IsRoving("b"));
        Assert.AreEqual(0, requests.Count);
        Assert.IsTrue(policy.RequestRoving());
        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("b", requests[0]);
        policy.CancelRoving();
        Assert.IsTrue(policy.IsRoving("a"));
        Assert.IsFalse(policy.Move(Key.Space));
    }

    [TestMethod]
    public void OptionalValueTypeKeyDistinguishesNoneFromDefaultKey()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "optional-value-key");
        ConfigureSelectionImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal(SelectedKey.None<int>(), "optional-selected");
        composition.Mount(
            composition.Root,
            theme,
            Components.RadioGroup(
                "Optional number",
                () => new[] { new RadioOption<int>(0, "Zero"), new(1, "One") },
                () => selected.Value,
                _ => { },
                RadioSelectionRequirement.Optional
            )
        );
        graph.Drain();
        Assert.AreEqual(0, RadioNodes(composition).Count(node => node.Selected));
        Assert.AreEqual(
            false,
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Name == "Optional number")
                .Selection?.IsSelectionRequired
        );

        selected.Value = SelectedKey.Some(0);
        graph.Drain();
        Assert.AreEqual("Zero", SelectedRadio(composition).Name);
        Assert.AreEqual(0, selected.Value.Value);
    }

    private static RetainedScene Install(
        Composition composition,
        ReactiveGraph graph,
        float width,
        float height
    )
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            graph.Drain();
            var scene = SceneLayout.Project(composition, new(width, height, 1), new EmptyShaper());
            if (composition.Input.SetScene(scene))
                return scene;
        }
        throw new InvalidOperationException("Selection scene did not converge.");
    }

    private static void ConfigureSelectionImages(Composition composition) =>
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));

    private static SemanticSnapshot SelectedRadio(Composition composition) =>
        RadioNodes(composition).Single(node => node.Selected);

    private static SemanticSnapshot FocusedRadio(Composition composition) =>
        RadioNodes(composition).Single(node => node.Focused);

    private static IEnumerable<SemanticSnapshot> RadioNodes(Composition composition) =>
        Nodes(composition.SemanticSnapshot()!).Where(node => node.Role == SemanticRole.RadioButton);

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) =>
            request.Text.Length == 0
                ? new("empty", 0, 0, [])
                : new(
                    "selection",
                    request.Text.Length,
                    request.FontSize,
                    [
                        new ShapedRun(
                            "selection",
                            "selection",
                            400,
                            5,
                            0,
                            "selection",
                            0,
                            "selection#0",
                            request.Direction,
                            request.Language,
                            request.FontSize,
                            0,
                            request.FontSize,
                            -request.FontSize,
                            0,
                            request.Text.Length,
                            [new(1, 0, 0, 0, request.Text.Length, 0, 0)]
                        ),
                    ]
                );
    }

    private sealed class ImmediatePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken token
        ) =>
            ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, new byte[] { 0, 0, 0, 255 }));
    }
}
