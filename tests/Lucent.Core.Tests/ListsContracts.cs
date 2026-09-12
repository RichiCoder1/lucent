using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ListsContracts
{
    [TestMethod]
    public void ListBoxFollowsFocusSkipsDisabledAndPreservesControlledSelection()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "list-box");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var items = graph.Signal(
            new[]
            {
                new ChoiceItem<string>("a", "Alpha"),
                new ChoiceItem<string>("b", "Beta", enabled: false),
                new ChoiceItem<string>("c", "Charlie"),
            },
            "choices"
        );
        var selected = graph.Signal("a", "selected");
        var requests = new List<string>();
        composition.Mount(
            composition.Root,
            theme,
            Components.ListBox(
                "Choices",
                () => items.Value,
                () => selected.Value,
                requests.Add,
                style: Style.Empty.Width(240).Height(120)
            )
        );
        graph.Drain();
        var scene = Install(composition, graph, 280, 160);

        var list = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.List && node.Name == "Choices");
        Assert.AreEqual(new SemanticSelectionSnapshot(false, true), list.Selection);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.AreEqual("a", requests.Single());
        Assert.AreEqual("Alpha", Selected(composition).Name);

        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled);
        graph.Drain();
        Assert.AreEqual("c", requests[^1]);
        Assert.AreEqual("Charlie", Focused(composition).Name);
        Assert.AreEqual(
            "Alpha",
            Selected(composition).Name,
            "Focus-following requests must not replace the caller's applied key."
        );
        var charlie = Options(composition).Single(node => node.Name == "Charlie");
        Assert.AreEqual(3, charlie.PositionInSet);
        Assert.AreEqual(3, charlie.SizeOfSet);

        var requestCount = requests.Count;
        items.Value = [new("a", "Alpha"), new("b", "Beta", enabled: false)];
        graph.Drain();
        Assert.AreEqual(requestCount, requests.Count);
        Assert.AreEqual(1, Options(composition).Count(node => node.Selected));
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void ExplicitListBoxCommitsOnlyOnConfirmation()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "explicit-list-box");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal(SelectedKey.Some("a"), "selected");
        var requests = new List<string>();
        composition.Mount(
            composition.Root,
            theme,
            Components.ListBox(
                "Explicit choices",
                () => new[] { new ChoiceItem<string>("a", "Alpha"), new("b", "Beta") },
                () => selected.Value,
                requests.Add,
                ListBoxSelectionMode.ExplicitConfirmation,
                style: Style.Empty.Width(240).Height(100)
            )
        );
        graph.Drain();
        var scene = Install(composition, graph, 280, 140);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled);
        graph.Drain();
        Assert.AreEqual(0, requests.Count);
        Assert.AreEqual("Beta", Focused(composition).Name);
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled);
        Assert.AreEqual("b", requests.Single());
        Assert.AreEqual("Alpha", Selected(composition).Name);
        var list = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.List && node.Name == "Explicit choices");
        Assert.AreEqual(false, list.Selection?.IsSelectionRequired);
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void TenThousandChoicesRevealTheActiveEndWithoutRealizingEveryRow()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "large-list-box");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var choices = Enumerable
            .Range(0, 10_000)
            .Select(index => new ChoiceItem<int>(index, "Choice " + index))
            .ToArray();
        composition.Mount(
            composition.Root,
            theme,
            Components.ListBox(
                "Large choices",
                () => choices,
                () => 0,
                _ => { },
                style: Style.Empty.Width(260).Height(108)
            )
        );
        graph.Drain();
        var scene = Install(composition, graph, 300, 150);
        var list = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.List && node.Name == "Large choices");
        Assert.AreEqual(10_000, list.Collection!.ItemCount);
        Assert.AreEqual(0, list.Collection.SelectedIndex);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                list.Identity,
                new(SemanticCommandKind.RealizeItem, ItemIndex: 9_999)
            )
        );
        scene.Dispose();
        scene = Install(composition, graph, 300, 150);

        var options = Options(composition).ToArray();
        Assert.IsTrue(
            options.Length < 20,
            "Virtualization realized an unbounded number of choices."
        );
        var last = options.Single(node => node.Name == "Choice 9999");
        Assert.AreEqual(9_999, last.CollectionIndex);
        Assert.AreEqual(10_000, last.PositionInSet);
        Assert.AreEqual(10_000, last.SizeOfSet);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(last.Identity, new(SemanticCommandKind.Focus))
        );
        graph.Drain();
        Assert.AreEqual("Choice 9999", Focused(composition).Name);
        GC.KeepAlive(scene);
    }

    [TestMethod]
    [DataRow(0, 100f)]
    [DataRow(1, 100f)]
    [DataRow(3, 160f)]
    [DataRow(20, 280f)]
    public void SelectPopupTracksAnchorWidthAndBoundsHeightToChoiceRows(
        int choiceCount,
        float maximumHeight
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "select-popup-geometry");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var choices = Enumerable
            .Range(0, choiceCount)
            .Select(index => new ChoiceItem<int>(index, index == 0 ? "Alpha" : $"Choice {index}"))
            .ToArray();
        composition.Mount(
            composition.Root,
            theme,
            Components.Select(
                "Workspace",
                () => choices,
                () => SelectedKey.None<int>(),
                _ => { },
                rowHeight: 32,
                style: Style.Empty.Width(880).Height(40)
            )
        );
        graph.Drain();
        using var ownerScene = Install(composition, graph, 920, 100);
        var anchor = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.ComboBox);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(anchor.Identity, new(SemanticCommandKind.Expand))
        );
        graph.Drain();

        var request = composition.Input.ActiveSurface!;
        var measured = request.Measure(new MetricShaper(), new(1200, 800, 1));
        Assert.IsTrue(
            measured.Width >= request.Anchor.Width,
            $"The {measured.Width}-pixel Select popup was narrower than its {request.Anchor.Width}-pixel anchor."
        );
        Assert.IsTrue(
            measured.Height <= maximumHeight,
            $"The {choiceCount}-row Select popup measured {measured.Height} pixels tall."
        );

        if (choiceCount == 3)
        {
            var popup = request.CreateComposition();
            using var popupScene = Install(
                popup,
                graph,
                measured.Width,
                Math.Max(measured.Height, 1)
            );
            var labels = SceneNodes(popupScene.Nodes)
                .OfType<TextSceneNode>()
                .Where(node => node.Text.SourceText is "Alpha" or "Choice 1" or "Choice 2")
                .ToArray();
            Assert.HasCount(3, labels);
            Assert.IsFalse(
                labels.Any(label => label.Text.DidOverflow),
                "Short Select choices wrapped or overflowed in an anchor-width popup."
            );
        }
    }

    [TestMethod]
    public void SelectSeparatesActiveAndAppliedKeysAndEscapeCancelsMovement()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "select");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var items = graph.Signal(
            new[] { new ChoiceItem<string>("a", "Alpha"), new("b", "Beta") },
            "select.items"
        );
        var selected = graph.Signal(SelectedKey.Some("a"), "select.selected");
        var requests = new List<string>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Select(
                "Choice",
                () => items.Value,
                () => selected.Value,
                requests.Add,
                style: Style.Empty.Width(220).Height(40)
            )
        );
        graph.Drain();
        var ownerScene = Install(composition, graph, 280, 100);
        var anchor = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.ComboBox);
        Assert.AreEqual("Alpha", anchor.Value);
        Assert.AreEqual(false, anchor.Expanded);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(anchor.Identity, new(SemanticCommandKind.Expand))
        );
        graph.Drain();
        var request = composition.Input.ActiveSurface!;
        Assert.IsTrue(request.ConsumeOutsideClick);
        var popup = request.CreateComposition();
        graph.Drain();
        var popupScene = Install(popup, graph, 280, 180);
        Assert.IsTrue(popup.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled);
        graph.Drain();
        Assert.AreEqual(0, requests.Count);
        Assert.AreEqual("Beta", Focused(popup).Name);
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled);
        graph.Drain();
        Assert.AreEqual("b", requests.Single());
        anchor = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.ComboBox);
        Assert.AreEqual(
            "Alpha",
            anchor.Value,
            "Select must retain the applied label during controlled lag."
        );
        Assert.AreEqual(false, anchor.Expanded);

        selected.Value = SelectedKey.Some("b");
        graph.Drain();
        anchor = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.ComboBox);
        Assert.AreEqual("Beta", anchor.Value);
        _ = composition.ExecuteSemanticCommand(anchor.Identity, new(SemanticCommandKind.Expand));
        graph.Drain();
        request = composition.Input.ActiveSurface!;
        popup = request.CreateComposition();
        graph.Drain();
        popupScene.Dispose();
        popupScene = Install(popup, graph, 280, 180);
        Assert.IsTrue(popup.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Up)).Handled);
        graph.Drain();
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled);
        graph.Drain();
        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual(
            false,
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.ComboBox)
                .Expanded
        );

        items.Value = [new("a", "Alpha")];
        graph.Drain();
        Assert.AreEqual(
            "Unavailable selection",
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.ComboBox)
                .Value
        );
        GC.KeepAlive(ownerScene);
        GC.KeepAlive(popupScene);
    }

    private static RetainedScene Install(
        Composition composition,
        ReactiveGraph graph,
        float width,
        float height
    )
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            graph.Drain();
            var scene = SceneLayout.Project(composition, new(width, height, 1), new MetricShaper());
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        throw new InvalidOperationException("List scene did not converge.");
    }

    private static void ConfigureImages(Composition composition) =>
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));

    private static SemanticSnapshot Selected(Composition composition) =>
        Options(composition).Single(node => node.Selected);

    private static SemanticSnapshot Focused(Composition composition) =>
        Options(composition).Single(node => node.Focused);

    private static IEnumerable<SemanticSnapshot> Options(Composition composition) =>
        Nodes(composition.SemanticSnapshot()!).Where(node => node.Role == SemanticRole.ListItem);

    private static IEnumerable<SceneNode> SceneNodes(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            var children = node switch
            {
                ClipSceneNode clip => clip.Children,
                OpacitySceneNode opacity => opacity.Children,
                _ => null,
            };
            if (children is not null)
                foreach (var child in SceneNodes(children))
                    yield return child;
        }
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
    }

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) =>
            request.Text.Length == 0
                ? new("empty", 0, 0, [])
                : new(
                    "lists",
                    request.Text.Length,
                    request.FontSize,
                    [
                        new ShapedRun(
                            "lists",
                            "lists",
                            400,
                            5,
                            0,
                            "lists",
                            0,
                            "lists#0",
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
