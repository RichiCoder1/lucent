using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class TextNavigationContracts
{
    [TestMethod]
    public void WordNavigationKeepsGraphemeBoundariesAcrossUnicodeRuns()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("word-navigation");
        using var session = new EditorSession(
            owner,
            "document",
            "one  two—世界 e\u0301😀.",
            multiline: true
        );

        session.MoveHome();
        session.MoveWordRight();
        Assert.AreEqual(3, session.Caret);
        session.MoveWordRight();
        Assert.AreEqual(8, session.Caret);
        session.MoveWordRight();
        Assert.AreEqual(9, session.Caret);
        session.MoveWordRight();
        Assert.AreEqual(11, session.Caret);

        session.MoveEnd();
        session.MoveWordLeft();
        Assert.AreEqual(14, session.Caret);
        session.MoveWordLeft();
        Assert.AreEqual(12, session.Caret);
        session.MoveWordLeft();
        Assert.AreEqual(9, session.Caret);
        session.MoveWordLeft();
        Assert.AreEqual(8, session.Caret);

        session.SetSelection(12, 12);
        session.MoveWordRight(extend: true);
        Assert.AreEqual("e\u0301", session.SelectedText);
        Assert.IsTrue(session.SelectedText.EnumerateRunes().Count() == 2);
    }

    [TestMethod]
    public void WordDeletionCoalescesByDirectionAndLineBreaksSplitTypingUndo()
    {
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("word-delete");
        using var session = new EditorSession(owner, "document", "one two three", multiline: true);

        session.MoveEnd();
        session.DeleteWordBackward();
        session.DeleteWordBackward();
        Assert.AreEqual("one ", session.Text);
        session.Undo();
        Assert.AreEqual("one two three", session.Text);
        session.Redo();
        Assert.AreEqual("one ", session.Text);

        session.Text = "";
        session.Insert("a");
        session.Insert("\n");
        session.Insert("b");
        session.Undo();
        Assert.AreEqual("a\n", session.Text);
        session.Undo();
        Assert.AreEqual("a", session.Text);
        session.Undo();
        Assert.AreEqual("", session.Text);

        session.Insert("a");
        session.BreakInsertCoalescing();
        session.Insert("b");
        session.Undo();
        Assert.AreEqual("a", session.Text);
        session.Undo();
        Assert.AreEqual("", session.Text);
    }

    [TestMethod]
    public void WrappedVisualHomeEndAndPageNavigationUseShapedLines()
    {
        const string source = "abcdefghijkl";
        var graph = new ReactiveGraph();
        using var owner = graph.CreateScope("visual-navigation");
        using var session = new EditorSession(owner, "document", source, multiline: true);
        var paragraph = Paragraph(source);

        session.SetSelection(4, 4);
        session.MoveVisualHome(paragraph);
        Assert.AreEqual(3, session.Caret);
        session.MoveVisualEnd(paragraph);
        Assert.AreEqual(6, session.Caret);

        session.MoveHome();
        session.MovePageDown(paragraph, 20);
        Assert.AreEqual(6, session.Caret);
        session.MovePageUp(paragraph, 20);
        Assert.AreEqual(0, session.Caret);
    }

    [TestMethod]
    public void ShiftPointerExtendsExistingAnchorAndControlKeysUseWordCommands()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "text-navigation-pointer");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var field = composition.Child(composition.Root, "field");
        var state = Controls.TextField(
            field,
            theme,
            "Search",
            "one two",
            Style.Empty.Set(LayoutProperties.Width, 140f).Set(LayoutProperties.Height, 30f)
        );
        var router = composition.Input;
        var scene = Install(composition, router);
        var identity = scene.Input.Single(input => input.Identity.ElementId == field.Id).Identity;
        var text = scene
            .Nodes.SelectMany(Flatten)
            .OfType<TextSceneNode>()
            .Single(node => node.Identity.Element.ElementId == field.Id);
        var y = text.Bounds.Y + 6;
        var first = router.HitTestText(identity, text.Bounds.X + 11, y)!.Value;
        var second = router.HitTestText(identity, text.Bounds.X + 51, y)!.Value;

        Assert.IsTrue(
            router
                .DispatchPointer(
                    new(PointerCommandKind.Down, 1, text.Bounds.X + 11, y, PointerButton.Primary)
                )
                .Handled
        );
        Assert.IsTrue(
            router.DispatchPointer(new(PointerCommandKind.Up, 1, text.Bounds.X + 11, y)).Handled
        );
        Assert.AreEqual(first.Utf16Offset, state.Caret);

        Assert.IsTrue(
            router
                .DispatchPointer(
                    new(
                        PointerCommandKind.Down,
                        2,
                        text.Bounds.X + 51,
                        y,
                        PointerButton.Primary,
                        KeyModifiers.Shift
                    )
                )
                .Handled
        );
        Assert.IsTrue(
            router.DispatchPointer(new(PointerCommandKind.Up, 2, text.Bounds.X + 51, y)).Handled
        );
        Assert.AreEqual(first.Utf16Offset, state.Anchor);
        Assert.AreEqual(second.Utf16Offset, state.Caret);

        Assert.IsTrue(
            router.DispatchKey(new(KeyCommandKind.Down, Key.Left, KeyModifiers.Control)).Handled
        );
        Assert.AreEqual(first.Utf16Offset, state.Caret);
        Assert.IsTrue(
            router.DispatchKey(new(KeyCommandKind.Down, Key.Right, KeyModifiers.Control)).Handled
        );
        Assert.AreEqual(3, state.Caret);
    }

    private static RetainedScene Install(Composition composition, InputRouter router)
    {
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(140, 30, 1), new MetricShaper());
        if (!router.SetScene(scene))
        {
            composition.Flush();
            scene = SceneLayout.Project(composition, new(140, 30, 1), new MetricShaper());
            Assert.IsTrue(router.SetScene(scene), "Text navigation scene did not converge.");
        }
        return scene;
    }

    private static IEnumerable<SceneNode> Flatten(SceneNode node)
    {
        yield return node;
        if (node is ClipSceneNode clip)
            foreach (var child in clip.Children.SelectMany(Flatten))
                yield return child;
        else if (node is OpacitySceneNode opacity)
            foreach (var child in opacity.Children.SelectMany(Flatten))
                yield return child;
    }

    private static ShapedText Paragraph(string source)
    {
        var runs = new List<ShapedRun>();
        var lines = new List<ParagraphLine>();
        for (var lineIndex = 0; lineIndex < source.Length / 3; lineIndex++)
        {
            var start = lineIndex * 3;
            var glyphs = Enumerable
                .Range(start, 3)
                .Select(
                    (offset, index) => new ShapedGlyph(1, (uint)offset, index * 10, 0, 10, 0, 0)
                )
                .ToArray();
            runs.Add(
                new ShapedRun(
                    "line-" + lineIndex,
                    "probe",
                    400,
                    5,
                    0,
                    "fingerprint",
                    0,
                    "probe#0",
                    TextDirection.LeftToRight,
                    "en",
                    10,
                    0,
                    lineIndex * 10 + 10,
                    -10,
                    0,
                    30,
                    glyphs
                )
            );
            lines.Add(new(start, 3, lineIndex * 10, lineIndex * 10 + 10, -10, 0, 0, 30, 0, false));
        }
        return new(
            "wrapped",
            30,
            lines.Count * 10,
            runs,
            lines,
            false,
            new LayoutConstraint(30),
            LayoutConstraint.Unbounded
        );
    }

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new(
                    "empty",
                    0,
                    0,
                    [],
                    [],
                    false,
                    request.InlineConstraint,
                    request.BlockConstraint
                );
            var glyphs = request
                .Text.EnumerateRunes()
                .Select((rune, index) => new ShapedGlyph(1, (uint)index, index * 10, 0, 10, 0, 0))
                .ToArray();
            var run = new ShapedRun(
                "metric",
                "metric",
                400,
                5,
                0,
                "fingerprint",
                0,
                "metric#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                glyphs.Length * 10,
                glyphs
            );
            return new(
                "metric",
                glyphs.Length * 10,
                request.FontSize,
                [run],
                [
                    new(
                        0,
                        request.Text.Length,
                        0,
                        request.FontSize,
                        -request.FontSize,
                        0,
                        0,
                        glyphs.Length * 10,
                        0,
                        false
                    ),
                ],
                false,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }
    }
}
