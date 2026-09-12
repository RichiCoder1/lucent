using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ComboBoxContracts
{
    [TestMethod]
    public void LatestSuggestionGenerationWinsAndSelectionRemainsControlled()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "combo-latest");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var pending =
            new List<(string Query, TaskCompletionSource<ComboBoxSuggestionResult<string>> Work)>();
        var requests = new List<string>();
        var selected = graph.Signal<ComboBoxSelectedItem<string>?>(null, "selected");
        composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Choice",
                field =>
                    Components.ComboBox(
                        field,
                        () => selected.Value,
                        requests.Add,
                        (query, _) =>
                        {
                            var work = new TaskCompletionSource<ComboBoxSuggestionResult<string>>();
                            pending.Add((query, work));
                            return new(work.Task);
                        },
                        new ComboBoxOptions(debounce: TimeSpan.Zero)
                    )
            )
        );
        graph.Drain();
        var ownerScene = Install(composition, graph, 320, 120);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        graph.Drain();
        Assert.AreEqual(1, pending.Count);
        var surface = composition.Input.ActiveSurface!;
        var loadingSize = surface.Measure(new MetricShaper(), new(600, 500, 1));
        var popup = surface.CreateComposition();
        graph.Drain();
        Assert.AreEqual("Loading suggestions", Status(popup).Name);
        ownerScene.Dispose();
        ownerScene = Install(composition, graph, 320, 120);

        var text = composition.Input.DispatchText(new(TextInputKind.Commit, "a"));
        Assert.AreEqual(InputDispatchStatus.Delivered, text.Status);
        Assert.IsTrue(text.Handled);
        graph.Drain();
        Assert.AreEqual(
            "a",
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.TextField)
                .Value
        );
        ownerScene.Dispose();
        ownerScene = Install(composition, graph, 320, 120);
        _ = popup.SemanticSnapshot();
        graph.Drain();
        Assert.AreEqual("a", pending[^1].Query);
        ownerScene.Dispose();
        ownerScene = Install(composition, graph, 320, 120);
        var secondText = composition.Input.DispatchText(new(TextInputKind.Commit, "b"));
        Assert.AreEqual(InputDispatchStatus.Delivered, secondText.Status);
        Assert.IsTrue(secondText.Handled);
        graph.Drain();
        ownerScene.Dispose();
        ownerScene = Install(composition, graph, 320, 120);
        _ = popup.SemanticSnapshot();
        graph.Drain();
        Assert.AreEqual("ab", pending[^1].Query);
        var latest = pending[^1].Work;
        CompleteOnWorker(
            latest,
            ComboBoxSuggestionResult.Success([new ChoiceItem<string>("ab", "AB result")])
        );
        graph.Drain();
        var readySize = surface.Measure(new MetricShaper(), new(600, 500, 1));
        Assert.IsTrue(
            readySize.Height <= 3 * 36f,
            $"A single ready suggestion measured {readySize.Height} pixels tall after the {loadingSize.Height}-pixel loading state."
        );
        var popupScene = Install(popup, graph, 300, 180);
        Assert.AreEqual("AB result", Option(popup).Name);

        CompleteOnWorker(
            pending[0].Work,
            ComboBoxSuggestionResult.Success([new ChoiceItem<string>("old", "Old result")])
        );
        graph.Drain();
        Assert.AreEqual("AB result", Option(popup).Name);
        Assert.IsTrue(popup.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled);
        graph.Drain();
        Assert.AreEqual("ab", requests.Single());
        Assert.AreEqual(
            string.Empty,
            Combo(composition).Value,
            "A controlled request must not fabricate an applied item."
        );

        selected.Value = new("ab", "Applied AB");
        graph.Drain();
        Assert.AreEqual("Applied AB", Combo(composition).Value);
        GC.KeepAlive(ownerScene);
        GC.KeepAlive(popupScene);
    }

    [TestMethod]
    public void RecoverableFailureDoesNotReplaceUnavailableAppliedItem()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "combo-failure");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal<ComboBoxSelectedItem<int>?>(
            new(7, "Persisted choice"),
            "selected"
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Choice",
                field =>
                    Components.ComboBox(
                        field,
                        () => selected.Value,
                        _ => { },
                        (_, _) =>
                            ValueTask.FromResult(
                                ComboBoxSuggestionResult.Failed<int>("Suggestions unavailable")
                            ),
                        new ComboBoxOptions(debounce: TimeSpan.Zero)
                    )
            )
        );
        graph.Drain();
        var scene = Install(composition, graph, 320, 120);
        Assert.AreEqual("Persisted choice", Combo(composition).Value);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        graph.Drain();
        var popup = composition.Input.ActiveSurface!.CreateComposition();
        graph.Drain();
        Assert.AreEqual("Suggestions unavailable", Status(popup).Name);
        Assert.AreEqual(
            0,
            Nodes(popup.SemanticSnapshot()!).Count(node => node.Role == SemanticRole.ListItem)
        );
        Assert.AreEqual("Persisted choice", Combo(composition).Value);
        GC.KeepAlive(scene);
    }

    [TestMethod]
    public void FreeTextIsExplicitAndImeEnterDoesNotPrematurelyCommit()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "combo-free-text");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var freeText = new List<string>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Tag",
                field =>
                    Components.ComboBox<string>(
                        field,
                        () => null,
                        _ => { },
                        (_, _) =>
                            ValueTask.FromResult(ComboBoxSuggestionResult.Success<string>([])),
                        new ComboBoxOptions(
                            ComboBoxSelectionPolicy.AllowFreeText,
                            debounce: TimeSpan.Zero
                        ),
                        freeText.Add
                    )
            )
        );
        graph.Drain();
        var scene = Install(composition, graph, 320, 120);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        _ = composition.Input.DispatchText(new(TextInputKind.Commit, "draft"));
        graph.Drain();
        scene.Dispose();
        scene = Install(composition, graph, 320, 120);
        _ = composition.Input.DispatchText(new(TextInputKind.Preedit, "候", 1, 0));
        graph.Drain();
        scene.Dispose();
        scene = Install(composition, graph, 320, 120);
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled);
        Assert.AreEqual(0, freeText.Count);
        var committed = composition.Input.DispatchText(new(TextInputKind.Commit, "候"));
        Assert.AreEqual(InputDispatchStatus.Delivered, committed.Status);
        Assert.IsTrue(committed.Handled);
        graph.Drain();
        Assert.AreEqual(
            "draft候",
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.TextField)
                .Value
        );
        scene.Dispose();
        scene = Install(composition, graph, 320, 120);
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled);
        Assert.AreEqual("draft候", freeText.Single());
        GC.KeepAlive(scene);
    }

    private static void CompleteOnWorker<T>(TaskCompletionSource<T> work, T value) =>
        Task.Run(() => work.SetResult(value)).GetAwaiter().GetResult();

    private static SemanticSnapshot Combo(Composition composition) =>
        Nodes(composition.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.ComboBox);

    private static SemanticSnapshot Option(Composition composition) =>
        Nodes(composition.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.ListItem);

    private static SemanticSnapshot Status(Composition composition) =>
        Nodes(composition.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.Status);

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
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
        throw new InvalidOperationException("ComboBox scene did not converge.");
    }

    private static void ConfigureImages(Composition composition) =>
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) =>
            request.Text.Length == 0
                ? new("empty", 0, 0, [])
                : new(
                    "combo",
                    request.Text.Length,
                    request.FontSize,
                    [
                        new ShapedRun(
                            "combo",
                            "combo",
                            400,
                            5,
                            0,
                            "combo",
                            0,
                            "combo#0",
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
