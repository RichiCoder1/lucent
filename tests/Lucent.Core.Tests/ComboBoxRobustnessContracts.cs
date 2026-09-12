using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ComboBoxRobustnessContracts
{
    [TestMethod]
    [DataRow(0, 100f)]
    [DataRow(1, 100f)]
    [DataRow(20, 280f)]
    public void PopupTracksAnchorWidthAndBoundsHeightToRealizedSuggestionRows(
        int suggestionCount,
        float maximumHeight
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "combo-popup-geometry");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var suggestions = Enumerable
            .Range(0, suggestionCount)
            .Select(index => new ChoiceItem<int>(
                index,
                index == 0 ? "Alpha workspace" : $"Workspace {index}"
            ))
            .ToArray();
        composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Workspace",
                field =>
                    Components.ComboBox<int>(
                        field,
                        () => null,
                        _ => { },
                        (_, _) =>
                            ValueTask.FromResult(
                                ComboBoxSuggestionResult.Success<int>(suggestions)
                            ),
                        new ComboBoxOptions(debounce: TimeSpan.Zero),
                        style: Style.Empty.Width(220)
                    )
            )
        );
        graph.Drain();
        using var ownerScene = Install(composition, graph, 420, 120);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        graph.Drain();

        var request = composition.Input.ActiveSurface!;
        var measured = request.Measure(new GeometryShaper(), new(600, 500, 1));
        Assert.IsTrue(
            measured.Width >= request.Anchor.Width,
            $"The {measured.Width}-pixel popup was narrower than its {request.Anchor.Width}-pixel anchor."
        );
        Assert.IsTrue(
            measured.Height <= maximumHeight,
            $"The {suggestionCount}-row popup measured {measured.Height} pixels tall."
        );

        if (suggestionCount == 1)
        {
            var popup = request.CreateComposition();
            using var popupScene = Install(
                popup,
                graph,
                measured.Width,
                Math.Max(measured.Height, 1),
                new GeometryShaper()
            );
            var row = Nodes(popup.SemanticSnapshot()!)
                .Single(node => node is { Role: SemanticRole.ListItem, Name: "Alpha workspace" });
            var rowBounds = popupScene
                .Boxes.Single(box => box.Identity.ElementId == row.Identity.ElementId)
                .Bounds;
            var label = SceneNodes(popupScene.Nodes)
                .OfType<TextSceneNode>()
                .Single(node => node.Text.SourceText == "Alpha workspace");
            Assert.IsFalse(label.Text.DidOverflow, "The single suggestion label was truncated.");
            Assert.IsTrue(
                label.Bounds.X >= rowBounds.X
                    && label.Bounds.X + label.Bounds.Width <= rowBounds.X + rowBounds.Width,
                "The single suggestion label escaped its row geometry."
            );
        }
    }

    [TestMethod]
    public void TenThousandSuggestionsRemainBoundedlyRealizedThroughThePopup()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "combo-large-suggestions");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var suggestions = Enumerable
            .Range(0, 10_000)
            .Select(index => new ChoiceItem<int>(index, "Suggestion " + index))
            .ToArray();
        composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Choice",
                field =>
                    Components.ComboBox<int>(
                        field,
                        () => null,
                        _ => { },
                        (_, _) =>
                            ValueTask.FromResult(
                                ComboBoxSuggestionResult.Success<int>(suggestions)
                            ),
                        new ComboBoxOptions(debounce: TimeSpan.Zero)
                    )
            )
        );
        graph.Drain();
        using var ownerScene = Install(composition, graph, 320, 120);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        graph.Drain();

        var popup = composition.Input.ActiveSurface!.CreateComposition();
        using var firstPopupScene = Install(popup, graph, 300, 180);
        var list = Nodes(popup.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.List);
        Assert.AreEqual(10_000, list.Collection!.ItemCount);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            popup.ExecuteSemanticCommand(
                list.Identity,
                new(SemanticCommandKind.RealizeItem, ItemIndex: 9_999)
            )
        );

        firstPopupScene.Dispose();
        using var revealedPopupScene = Install(popup, graph, 300, 180);
        var realized = Nodes(popup.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.ListItem)
            .ToArray();
        Assert.IsTrue(
            realized.Length < 20,
            "ComboBox suggestion virtualization realized an unbounded number of rows."
        );
        var last = realized.Single(node => node.Name == "Suggestion 9999");
        Assert.AreEqual(9_999, last.CollectionIndex);
        Assert.AreEqual(10_000, last.PositionInSet);
        Assert.AreEqual(10_000, last.SizeOfSet);
    }

    [TestMethod]
    public void OutsideDismissalIsConsumedAndAClosedComboBoxCanReopen()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "combo-outside-dismissal");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Choice",
                field =>
                    Components.ComboBox<int>(
                        field,
                        () => null,
                        _ => { },
                        (_, _) =>
                            ValueTask.FromResult(
                                ComboBoxSuggestionResult.Success<int>([
                                    new ChoiceItem<int>(1, "First"),
                                ])
                            ),
                        new ComboBoxOptions(debounce: TimeSpan.Zero)
                    )
            )
        );
        graph.Drain();
        using var scene = Install(composition, graph, 320, 120);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        graph.Drain();

        var first = composition.Input.ActiveSurface!;
        Assert.IsTrue(first.ConsumeOutsideClick);
        first.Dismiss();
        composition.Input.CompleteSurface(first);
        graph.Drain();
        Assert.IsNull(composition.Input.ActiveSurface);
        var combo = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.ComboBox);
        Assert.AreEqual(false, combo.Expanded);

        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(combo.Identity, new(SemanticCommandKind.Expand))
        );
        graph.Drain();
        var reopened = composition.Input.ActiveSurface;
        Assert.IsNotNull(reopened);
        Assert.AreNotSame(first, reopened);
        Assert.IsTrue(reopened!.ConsumeOutsideClick);
        Assert.AreEqual(
            true,
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.ComboBox)
                .Expanded
        );
    }

    [TestMethod]
    [DataRow(false, SemanticCommandResult.Requested)]
    [DataRow(true, SemanticCommandResult.Applied)]
    public void PopupSelectionDistinguishesUnappliedRequestsFromAcceptedDismissal(
        bool accept,
        SemanticCommandResult expected
    )
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "combo-selection-result");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var selected = graph.Signal<ComboBoxSelectedItem<int>?>(null, "combo.selected");
        var requests = new List<int>();
        composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Choice",
                field =>
                    Components.ComboBox(
                        field,
                        () => selected.Value,
                        key =>
                        {
                            requests.Add(key);
                            if (accept)
                                selected.Value = new(key, "Alpha");
                        },
                        (_, _) =>
                            ValueTask.FromResult(
                                ComboBoxSuggestionResult.Success<int>([
                                    new ChoiceItem<int>(1, "Alpha"),
                                ])
                            ),
                        new ComboBoxOptions(debounce: TimeSpan.Zero)
                    )
            )
        );
        graph.Drain();
        using var ownerScene = Install(composition, graph, 320, 120);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        graph.Drain();

        var popup = composition.Input.ActiveSurface!.CreateComposition();
        using var popupScene = Install(popup, graph, 300, 180);
        var alpha = Nodes(popup.SemanticSnapshot()!)
            .Single(node => node is { Role: SemanticRole.ListItem, Name: "Alpha" });
        Assert.AreEqual(
            expected,
            popup.ExecuteSemanticCommand(alpha.Identity, new(SemanticCommandKind.Select))
        );

        Assert.HasCount(1, requests);
        Assert.AreEqual(1, requests[0]);
        Assert.AreEqual(accept ? (int?)1 : null, selected.Value?.Key);
        Assert.AreEqual(accept ? "Alpha" : string.Empty, Combo(composition).Value);
        Assert.IsNull(composition.Input.ActiveSurface);
    }

    [TestMethod]
    public void ComboBoxInsideModalDialogOwnsItsNestedSuggestionSurface()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "combo-nested-modal");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var controller = new DialogController<int>(composition.Root.Scope);
        PopupSurfaceRequest? dialogRequest = null;
        composition.Input.SurfaceRequested += request => dialogRequest = request;
        composition.Mount(
            composition.Root,
            theme,
            Components.Dialog(
                controller,
                "Choose an item",
                [
                    Components.Field(
                        "Choice",
                        field =>
                            Components.ComboBox<int>(
                                field,
                                () => null,
                                _ => { },
                                (_, _) =>
                                    ValueTask.FromResult(
                                        ComboBoxSuggestionResult.Success<int>([
                                            new ChoiceItem<int>(1, "First"),
                                        ])
                                    ),
                                new ComboBoxOptions(debounce: TimeSpan.Zero)
                            )
                    ),
                ]
            )
        );

        var dialogSession = controller.OpenAsync().AsTask();
        graph.Drain();
        Assert.IsNotNull(dialogRequest);
        Assert.IsTrue(dialogRequest!.IsModal);
        var dialog = dialogRequest.CreateComposition();
        using var dialogScene = Install(dialog, graph, 360, 220);
        Assert.IsTrue(dialog.Input.MoveFocus(FocusTraversalDirection.Next));
        graph.Drain();

        var suggestions = dialog.Input.ActiveSurface;
        Assert.IsNotNull(suggestions);
        Assert.AreSame(dialog, suggestions!.Owner);
        Assert.IsFalse(suggestions.IsModal);
        Assert.IsTrue(suggestions.IsInteractive);
        Assert.IsTrue(suggestions.ConsumeOutsideClick);

        dialogRequest.Dispose();
        Assert.IsTrue(dialogSession.GetAwaiter().GetResult().IsCanceled);
    }

    private static RetainedScene Install(
        Composition composition,
        ReactiveGraph graph,
        float width,
        float height,
        ITextShaper? shaper = null
    )
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            graph.Drain();
            var scene = SceneLayout.Project(
                composition,
                new(width, height, 1),
                shaper ?? new MetricShaper()
            );
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        throw new InvalidOperationException("ComboBox robustness scene did not converge.");
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
    }

    private static SemanticSnapshot Combo(Composition composition) =>
        Nodes(composition.SemanticSnapshot()!).Single(node => node.Role == SemanticRole.ComboBox);

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

    private static void ConfigureImages(Composition composition) =>
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) =>
            request.Text.Length == 0
                ? new("empty", 0, 0, [])
                : new(
                    "combo-robustness",
                    request.Text.Length,
                    request.FontSize,
                    [
                        new ShapedRun(
                            "combo-robustness",
                            "combo-robustness",
                            400,
                            5,
                            0,
                            "combo-robustness",
                            0,
                            "combo-robustness#0",
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

    private sealed class GeometryShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            var naturalWidth = request.Text.Length * 8f;
            var width = request.InlineConstraint.IsBounded
                ? Math.Min(naturalWidth, request.InlineConstraint.Limit!.Value)
                : naturalWidth;
            return new(
                "combo-geometry",
                width,
                request.FontSize,
                request.Text.Length == 0
                    ? []
                    :
                    [
                        new ShapedRun(
                            "combo-geometry",
                            "combo-geometry",
                            400,
                            5,
                            0,
                            "combo-geometry",
                            0,
                            "combo-geometry#0",
                            request.Direction,
                            request.Language,
                            request.FontSize,
                            0,
                            request.FontSize,
                            -request.FontSize,
                            0,
                            width,
                            [new(1, 0, 0, 0, width, 0, 0)]
                        ),
                    ],
                request.Text.Length == 0
                    ? []
                    :
                    [
                        new ParagraphLine(
                            0,
                            request.Text.Length,
                            0,
                            request.FontSize,
                            -request.FontSize,
                            0,
                            0,
                            width,
                            0,
                            false
                        ),
                    ],
                naturalWidth > width,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }
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
