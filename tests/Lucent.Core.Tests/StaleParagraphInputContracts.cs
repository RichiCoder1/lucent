using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class StaleParagraphInputContracts
{
    [TestMethod]
    public void ShapedTextSourceSnapshotCannotBeReassociated()
    {
        var shaped = new ShapedText("fixed", 0, 0, []);
        shaped.SetSourceText("abc");

        Assert.ThrowsExactly<InvalidOperationException>(() => shaped.SetSourceText("xyz"));
        Assert.AreEqual("abc", shaped.SourceText);
    }

    [TestMethod]
    public void VisualNavigationAfterSameDrainEditDoesNotUseTheRetainedParagraph()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stale-paragraph-input");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var field = composition.Child(composition.Root, "area");
        var state = Controls.TextArea(
            field,
            theme,
            "Note",
            "abc\ndef",
            Style.Empty.Width(120).Height(40)
        );

        graph.Drain();
        var scene = SceneLayout.Project(composition, new(120, 40, 1), new FixedShaper());
        Assert.IsTrue(composition.Input.SetScene(scene), "Initial text scene did not install.");
        var identity = scene.Input.Single(item => item.Identity.ElementId == field.Id).Identity;
        Assert.IsTrue(composition.Input.FocusSemantic(identity), "Text area did not accept focus.");
        state.MoveEnd();

        Assert.IsTrue(
            composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Backspace)).Handled,
            "Backspace was not routed to the focused text area."
        );
        Assert.AreEqual("abc\nde", state.Value);

        Assert.IsTrue(
            composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.End)).Handled,
            "End was not routed to the focused text area."
        );
        Assert.AreEqual(state.Value.Length, state.Caret);
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Up));

        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == field.Id).Bounds;
        _ = composition.Input.DispatchPointer(
            new(PointerCommandKind.Down, 1, bounds.X + 5, bounds.Y + 5, PointerButton.Primary)
        );
        _ = composition.Input.DispatchPointer(
            new(PointerCommandKind.Up, 1, bounds.X + 5, bounds.Y + 5)
        );

        state.SelectAll();
        Assert.IsTrue(
            composition.Input.DispatchText(new(TextInputKind.Commit, "xyz\nde")).Handled,
            "A same-length text commit was not routed to the focused text area."
        );
        Assert.AreEqual("xyz\nde", state.Value);
        Assert.IsTrue(
            composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.End)).Handled,
            "End after the same-length commit was not handled."
        );
        Assert.AreEqual(state.Value.Length, state.Caret);
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Up));
    }

    private sealed class FixedShaper : ITextShaper
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
                "fixed",
                "fixed",
                400,
                5,
                0,
                "fixed#0",
                0,
                "fixed#0",
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
            var lines = new List<ParagraphLine>();
            var lineStart = 0;
            var lineTop = 0f;
            foreach (var lineText in request.Text.Split('\n'))
            {
                var lineWidth = lineText.Length * 10;
                lines.Add(
                    new(
                        lineStart,
                        lineText.Length,
                        lineTop,
                        lineTop + request.FontSize,
                        -request.FontSize,
                        0,
                        0,
                        lineWidth,
                        0,
                        false
                    )
                );
                lineStart += lineText.Length + 1;
                lineTop += request.FontSize;
            }

            return new(
                "fixed",
                x,
                request.FontSize,
                [run],
                lines,
                false,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }
    }
}
