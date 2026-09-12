using System.Globalization;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class TreeContracts
{
    [TestMethod]
    public void TreeKeepsExpansionFocusAndSelectionDistinctAcrossControlledLagAndCollapse()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "tree");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal(SelectedKey.Some("root"), "selected");
        var expanded = graph.Signal(false, "expanded");
        var selectionRequests = new List<string>();
        var expansionRequests = new List<(string Key, bool Expanded)>();
        var child = new Node("child", "Child");
        var root = new Node("root", "Root", [child]);
        var sibling = new Node("sibling", "Disabled sibling", Enabled: false);
        var last = new Node("last", "Last");
        var roots = graph.Signal(new[] { root, sibling, last }, "roots");

        composition.Mount(
            composition.Root,
            theme,
            Components.TreeView(
                "Files",
                () => roots.Value,
                Source(),
                () => selected.Value,
                selectionRequests.Add,
                key => key == "root" && expanded.Value,
                (key, value) => expansionRequests.Add((key, value)),
                style: Style.Empty.Height(144)
            )
        );
        var scene = Install(composition, graph);
        Assert.AreEqual(
            0,
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.Tree)
                .Collection!.SelectedIndex
        );
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.AreEqual("Root", FocusedTreeItem(composition).Name);

        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled);
        Assert.AreEqual(("root", true), expansionRequests.Single());
        Assert.IsFalse(TreeItem(composition, "Root").Expanded!.Value);
        expanded.Value = true;
        var previousScene = scene;
        scene = Install(composition, graph);
        previousScene.Dispose();
        Assert.IsTrue(TreeItem(composition, "Root").Expanded!.Value);

        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled);
        graph.Drain();
        Assert.AreEqual("Child", FocusedTreeItem(composition).Name);
        Assert.AreEqual("child", selectionRequests.Last());
        Assert.IsTrue(TreeItem(composition, "Root").Selected);
        Assert.IsFalse(TreeItem(composition, "Child").Selected);

        selected.Value = SelectedKey.Some("child");
        graph.Drain();
        Assert.IsTrue(TreeItem(composition, "Child").Selected);
        Assert.AreEqual(
            1,
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.Tree)
                .Collection!.SelectedIndex
        );
        expanded.Value = false;
        previousScene = scene;
        scene = Install(composition, graph);
        previousScene.Dispose();
        Assert.AreEqual("Root", FocusedTreeItem(composition).Name);
        Assert.AreEqual(0, TreeItems(composition).Count(item => item.Name == "Child"));
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled);
        graph.Drain();
        Assert.AreEqual("Last", FocusedTreeItem(composition).Name);
        Assert.AreEqual("last", selectionRequests.Last());
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void TreeVirtualizationExposesLogicalCountAndRealizesTheRequestedFlatIndex()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "large-tree");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var roots = Enumerable
            .Range(0, 10_000)
            .Select(index => new Node(
                index.ToString(CultureInfo.InvariantCulture),
                "Item " + index
            ))
            .ToArray();
        composition.Mount(
            composition.Root,
            theme,
            Components.TreeView(
                "Files",
                () => roots,
                Source(),
                () => SelectedKey.None<string>(),
                _ => { },
                _ => false,
                (_, _) => { },
                style: Style.Empty.Height(108)
            )
        );
        var scene = Install(composition, graph);
        var tree = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Tree);
        Assert.AreEqual(10_000, tree.Collection!.ItemCount);
        Assert.IsTrue(TreeItems(composition).Count() < 20);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                tree.Identity,
                new(SemanticCommandKind.RealizeItem, ItemIndex: 9_999)
            )
        );
        scene.Dispose();
        scene = Install(composition, graph);
        var last = TreeItem(composition, "Item 9999");
        Assert.AreEqual(9_999, last.CollectionIndex);
        Assert.AreEqual(10_000, last.PositionInSet);
        Assert.AreEqual(10_000, last.SizeOfSet);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(last.Identity, new(SemanticCommandKind.Focus))
        );
        graph.Drain();
        Assert.AreEqual("Item 9999", FocusedTreeItem(composition).Name);
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void ChevronPointerTogglesExpansionWithoutSelectionOrDuplicateSemantics()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "tree-chevron-pointer");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var expansionRequests = new List<(string Key, bool Expanded)>();
        var selectionRequests = new List<string>();
        var root = new Node("root", "Root", [new Node("child", "Child")]);
        composition.Mount(
            composition.Root,
            theme,
            Components.TreeView(
                "Files",
                () => new[] { root },
                Source(),
                () => SelectedKey.Some("root"),
                selectionRequests.Add,
                _ => false,
                (key, expanded) => expansionRequests.Add((key, expanded)),
                style: Style.Empty.Height(108)
            )
        );
        var scene = Install(composition, graph);
        Assert.AreEqual(
            0,
            Nodes(composition.SemanticSnapshot()!).Count(node => node.Role == SemanticRole.Button),
            "The decorative chevron must not duplicate the TreeItem expansion action."
        );
        var chevron = composition
            .Elements()
            .Single(element =>
                element.Resolve(ImageProperties.Source).Value == NavigationArtwork.Collapsed
            );
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == chevron.Id).Bounds;
        var x = bounds.X + bounds.Width / 2;
        var y = bounds.Y + bounds.Height / 2;

        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Down, 41, x, y, PointerButton.Primary)
                )
                .Handled
        );
        Assert.IsTrue(
            composition.Input.DispatchPointer(new(PointerCommandKind.Up, 41, x, y)).Handled
        );
        Assert.AreEqual(("root", true), expansionRequests.Single());
        Assert.AreEqual(0, selectionRequests.Count);

        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Down, 42, x, y, PointerButton.Primary)
                )
                .Handled
        );
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Up, 42, bounds.X + bounds.Width + 4, y)
                )
                .Handled
        );
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Down, 43, x, y, PointerButton.Primary)
                )
                .Handled
        );
        Assert.IsTrue(
            composition.Input.DispatchPointer(new(PointerCommandKind.Cancel, 43, x, y)).Handled
        );
        Assert.AreEqual(1, expansionRequests.Count);
        Assert.AreEqual(0, selectionRequests.Count);
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void CollapseCancelsLazyGenerationAndLateCompletionCannotReplaceRetry()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "lazy-generation");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var root = new Node("root", "Root");
        var expanded = graph.Signal(true, "expanded");
        var loads =
            new List<(
                TaskCompletionSource<TreeChildrenResult<Node>> Work,
                CancellationToken Token
            )>();
        var source = new TreeDataSource<string, Node>(
            node => node.Key,
            node => node.Label,
            _ => null,
            _ => true,
            loadChildren: (_, token) =>
            {
                var work = new TaskCompletionSource<TreeChildrenResult<Node>>();
                loads.Add((work, token));
                return new(work.Task);
            }
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.TreeView(
                "Files",
                () => new[] { root },
                source,
                () => SelectedKey.None<string>(),
                _ => { },
                _ => expanded.Value,
                (_, _) => { },
                style: Style.Empty.Height(144)
            )
        );
        var scene = Install(composition, graph);
        Assert.AreEqual(1, loads.Count);
        expanded.Value = false;
        graph.Drain();
        Assert.IsTrue(loads[0].Token.IsCancellationRequested);
        expanded.Value = true;
        graph.Drain();
        Assert.AreEqual(2, loads.Count);
        CompleteOnWorker(
            loads[0].Work,
            TreeChildrenResult.Success([new Node("stale", "Stale child")])
        );
        graph.Drain();
        Assert.AreEqual(0, TreeItems(composition).Count(item => item.Name == "Stale child"));
        CompleteOnWorker(
            loads[1].Work,
            TreeChildrenResult.Success([new Node("current", "Current child")])
        );
        scene.Dispose();
        scene = Install(composition, graph);
        Assert.AreEqual(1, TreeItems(composition).Count(item => item.Name == "Current child"));
        Assert.AreEqual(0, TreeItems(composition).Count(item => item.Name == "Stale child"));
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void LazyChildrenRenderRecoverableFailureRetryAndCancelWhenAncestorDeparts()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "lazy-tree");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var root = new Node("root", "Root");
        var roots = graph.Signal(new[] { root }, "roots");
        var attempts = 0;
        var cancelled = false;
        var retry = new TaskCompletionSource<TreeChildrenResult<Node>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var source = new TreeDataSource<string, Node>(
            node => node.Key,
            node => node.Label,
            _ => null,
            _ => true,
            loadChildren: (_, token) =>
            {
                attempts++;
                if (attempts == 1)
                    return ValueTask.FromResult(
                        TreeChildrenResult.Failed<Node>("Folder could not be loaded")
                    );
                token.Register(() => cancelled = true);
                return new(retry.Task);
            }
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.TreeView(
                "Files",
                () => roots.Value,
                source,
                () => SelectedKey.None<string>(),
                _ => { },
                _ => true,
                (_, _) => { },
                style: Style.Empty.Height(144)
            )
        );
        var scene = Install(composition, graph);
        var failure = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Folder could not be loaded");
        var retryButton = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Retry");
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                retryButton.Identity,
                new(SemanticCommandKind.Invoke)
            )
        );
        graph.Drain();
        Assert.AreEqual(2, attempts);
        roots.Value = [];
        graph.Drain();
        Assert.IsTrue(cancelled);
        retry.SetResult(TreeChildrenResult.Success([new Node("late", "Late child")]));
        scene.Dispose();
        scene = Install(composition, graph);
        Assert.AreEqual(0, TreeItems(composition).Count());
        GC.KeepAlive(scene);
    }

    private static TreeDataSource<string, Node> Source() =>
        new(
            node => node.Key,
            node => node.Label,
            node => node.Children,
            node => node.Children is { Count: > 0 },
            node => node.Enabled
        );

    private static void CompleteOnWorker<T>(TaskCompletionSource<T> work, T value) =>
        Task.Run(() => work.SetResult(value)).GetAwaiter().GetResult();

    private static RetainedScene Install(Composition composition, ReactiveGraph graph)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            graph.Drain();
            var scene = SceneLayout.Project(composition, new(320, 180, 1), new MetricShaper());
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        throw new InvalidOperationException("Tree scene did not converge.");
    }

    private static SemanticSnapshot TreeItem(Composition composition, string name) =>
        TreeItems(composition).Single(node => node.Name == name);

    private static SemanticSnapshot FocusedTreeItem(Composition composition) =>
        TreeItems(composition).Single(node => node.Focused);

    private static IEnumerable<SemanticSnapshot> TreeItems(Composition composition) =>
        Nodes(composition.SemanticSnapshot()!).Where(node => node.Role == SemanticRole.TreeItem);

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
    }

    private sealed record Node(
        string Key,
        string Label,
        IReadOnlyList<Node>? Children = null,
        bool Enabled = true
    );

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) =>
            request.Text.Length == 0
                ? new("empty", 0, 0, [])
                : new(
                    "tree",
                    request.Text.Length,
                    request.FontSize,
                    [
                        new ShapedRun(
                            "tree",
                            "tree",
                            400,
                            5,
                            0,
                            "tree",
                            0,
                            "tree#0",
                            request.Direction,
                            request.Language,
                            request.FontSize,
                            0,
                            request.FontSize,
                            -request.FontSize,
                            0,
                            request.Text.Length,
                            [new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0)]
                        ),
                    ]
                );
    }

    private sealed class ImmediatePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, new byte[] { 0, 0, 0, 255 }));
    }
}
