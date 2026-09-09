using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class SceneInstallEditingContracts
{
    [TestMethod]
    public void ScrollClampFocusAndCaretRevealConvergeWithinThreeInstallAttempts()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "scene-install-editing");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var viewportState = new ViewportState(composition.Root.Scope, name: "viewport-state");
        var contentHeight = graph.Signal(100f, "content-height");
        var participation = graph.Signal(ElementParticipation.Collapsed, "editor-participation");
        var target = new FocusTarget(composition.Root.Scope, "editor-focus");
        var session = new EditorSession(
            composition.Root.Scope,
            "editor",
            string.Join('\n', Enumerable.Repeat("line", 30)),
            multiline: true
        );

        Controls.Panel(
            composition.Root,
            theme,
            "Root",
            Style.Empty.Width(100).Height(20).Clip(true)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Width(100).Height(20),
            viewport: viewportState
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(
            content,
            theme,
            "Content",
            Style.Empty.Width(100).Height(() => contentHeight.Value)
        );
        var editor = composition.Child(content, "editor");
        Controls.TextArea(
            editor,
            theme,
            "Editor",
            session: session,
            focusTarget: target,
            style: Style.Empty.Width(100).Height(20).Participation(() => participation.Value)
        );

        using var initial = Install(composition, new(100, 20, 1), new FixedShaper());
        Assert.IsFalse(target.IsPending);
        viewportState.Offset = new(0, 80);
        composition.Flush();

        contentHeight.Value = 20;
        participation.Value = ElementParticipation.Visible;
        target.Request();

        var attempts = 0;
        RetainedScene? accepted = null;
        try
        {
            for (; attempts < 4; attempts++)
            {
                composition.Flush();
                var candidate = SceneLayout.Project(
                    composition,
                    new(100, 20, 1),
                    new FixedShaper()
                );
                if (composition.Input.SetScene(candidate))
                {
                    accepted = candidate;
                    break;
                }
                candidate.Dispose();
            }

            Assert.IsNotNull(accepted, "The combined scene-install sequence never converged.");
            Assert.IsTrue(
                attempts <= 2,
                $"Scroll clamp, focus, and caret reveal required a fourth install attempt (accepted index {attempts})."
            );
            Assert.AreEqual(editor.Id, composition.Input.FocusedElement?.ElementId);
            Assert.IsFalse(target.IsPending);
            Assert.AreEqual(0, viewportState.Offset.Y);
        }
        finally
        {
            accepted?.Dispose();
        }
    }

    private static RetainedScene Install(
        Composition composition,
        LayoutViewport viewport,
        ITextShaper shaper
    )
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            composition.Flush();
            var scene = SceneLayout.Project(composition, viewport, shaper);
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }

        Assert.Fail("Initial scene did not converge.");
        return null!;
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
                Math.Max(request.FontSize, lineTop),
                [run],
                lines,
                request.InlineConstraint.Limit is { } inline && x > inline,
                request.InlineConstraint,
                request.BlockConstraint
            );
        }
    }
}
