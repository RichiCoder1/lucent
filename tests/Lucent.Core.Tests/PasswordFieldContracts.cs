using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class PasswordFieldContracts
{
    private const string Secret = "a\u0301\U0001f600";

    [TestMethod]
    public void PointerRevealToggleCanShowThenHideAfterRelease()
    {
        using var composition = new Composition(new ReactiveGraph(), "password-pointer-toggle");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.PasswordField("Password", () => Secret, _ => { })
        );

        for (var click = 0; click < 4; click++)
        {
            using var before = Install(composition, new PasswordShaper());
            var button = Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.Button);
            var bounds = before
                .Boxes.Single(box => box.Identity.ElementId == button.Identity.ElementId)
                .Bounds;
            var x = bounds.X + bounds.Width / 2;
            var y = bounds.Y + bounds.Height / 2;
            _ = composition.Input.DispatchPointer(new(PointerCommandKind.Move, 1, x, y));
            Assert.IsTrue(
                composition
                    .Input.DispatchPointer(
                        new(PointerCommandKind.Down, 1, x, y, PointerButton.Primary)
                    )
                    .Handled
            );
            Assert.IsTrue(
                composition
                    .Input.DispatchPointer(
                        new(PointerCommandKind.Up, 1, x, y, PointerButton.Primary)
                    )
                    .Handled
            );
            var shaper = new PasswordShaper();
            using var released = Install(composition, shaper);
            var expected = click % 2 == 0 ? Secret : "\u25cf\u25cf";
            Assert.IsTrue(
                shaper.ConfidentialRequests.Contains(expected),
                $"Password click {click + 1} did not leave the expected reveal state after release."
            );
        }
    }

    [TestMethod]
    public void MaskedProjectionAndSemanticsRetainNoPlaintext()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "password-confidential");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal(Secret, "password-applied");
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.PasswordField("Password", () => applied.Value, _ => { })
        );
        graph.Drain();

        var shaper = new PasswordShaper();
        using var scene = Install(composition, shaper);
        var editor = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);
        var text = scene
            .Nodes.SelectMany(Flatten)
            .OfType<TextSceneNode>()
            .Single(node =>
                node.Identity.Element.CompositionEpoch == editor.Identity.CompositionEpoch
                && node.Identity.Element.ElementId == editor.Identity.ElementId
            )
            .Text;

        Assert.IsTrue(editor.IsPassword);
        Assert.IsNull(editor.Value);
        Assert.IsNull(editor.Text);
        Assert.IsTrue(shaper.ConfidentialRequests.Count > 0);
        Assert.IsTrue(shaper.ConfidentialRequests.All(value => value == "\u25cf\u25cf"));
        Assert.IsNull(text.SourceText);
        Assert.IsFalse(composition.Dump().Contains(Secret, StringComparison.Ordinal));
        Assert.IsFalse(scene.Dump().Contains(Secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public void CopyAndCutAreSuppressedWhilePastePublishesControlledRequest()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "password-clipboard");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal("initial", "password-applied");
        string? requested = null;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.PasswordField("Password", () => applied.Value, value => requested = value)
        );
        graph.Drain();
        using var scene = Install(composition, new PasswordShaper());
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.A, KeyModifiers.Control));
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.C, KeyModifiers.Control));
        Assert.IsFalse(composition.Input.TryTakeClipboardRequest(out _));
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.X, KeyModifiers.Control));
        Assert.IsFalse(composition.Input.TryTakeClipboardRequest(out _));

        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.V, KeyModifiers.Control));
        Assert.IsTrue(composition.Input.TryTakeClipboardRequest(out var paste));
        Assert.IsTrue(composition.Input.CompleteClipboardRequest(paste, true, "replacement"));
        graph.Drain();
        Assert.AreEqual("replacement", requested);
        Assert.AreEqual("initial", applied.Value);
    }

    [TestMethod]
    public void RevealIsConfidentialAndFocusLossRemasksAndClearsHistory()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "password-reveal");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal(Secret, "password-applied");
        var requests = new List<string>();
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.PasswordField("Password", () => applied.Value, requests.Add)
        );
        graph.Drain();
        using var initial = Install(composition, new PasswordShaper());
        var show = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Button && node.Name == "Show password");
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(show.Identity, new(SemanticCommandKind.Invoke))
        );
        graph.Drain();
        var revealedShaper = new PasswordShaper();
        using var revealed = Install(composition, revealedShaper);
        Assert.IsTrue(revealedShaper.ConfidentialRequests.Contains(Secret));
        Assert.IsTrue(composition.Input.DispatchText(new(TextInputKind.Commit, "x")).Handled);
        graph.Drain();
        Assert.AreEqual(Secret + "x", requests.Single());
        using var edited = Install(composition, new PasswordShaper());
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        graph.Drain();
        var maskedShaper = new PasswordShaper();
        using var masked = Install(composition, maskedShaper);
        Assert.IsTrue(
            maskedShaper.ConfidentialRequests.Contains("\u25cf\u25cf\u25cf"),
            "Blur must remask the retained three-grapheme draft without reverting it."
        );
        var button = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Button);
        Assert.AreEqual("Show password", button.Name);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Previous));
        _ = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Z, KeyModifiers.Control));
        graph.Drain();
        Assert.AreEqual(1, requests.Count);
    }

    private static RetainedScene Install(Composition composition, ITextShaper shaper)
    {
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(500, 500, 1), shaper);
        if (!composition.Input.SetScene(scene))
        {
            scene.Dispose();
            composition.Flush();
            scene = SceneLayout.Project(composition, new(500, 500, 1), shaper);
            Assert.IsTrue(composition.Input.SetScene(scene));
        }
        return scene;
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Nodes(child))
            yield return descendant;
    }

    private static IEnumerable<SceneNode> Flatten(SceneNode node)
    {
        yield return node;
        var children = node switch
        {
            ClipSceneNode clip => clip.Children,
            OpacitySceneNode opacity => opacity.Children,
            _ => [],
        };
        foreach (var child in children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private sealed class PasswordShaper : ITextShaper
    {
        internal List<string> ConfidentialRequests { get; } = [];

        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.IsConfidential)
                ConfidentialRequests.Add(request.Text);
            if (request.Text.Length == 0)
                return new("password-empty", 0, request.FontSize, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "password",
                "password",
                400,
                5,
                0,
                "password",
                0,
                "password#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                request.Text.Length,
                [glyph]
            );
            return new("password", request.Text.Length, request.FontSize, [run]);
        }
    }
}
