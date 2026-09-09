using Lucent.Core;
using Microsoft.Extensions.Time.Testing;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class TooltipContracts
{
    [TestMethod]
    public void HoverWaitsForDelayAndRequestsNoninteractiveSurface()
    {
        var clock = new FakeTimeProvider();
        using var composition = new Composition(new ReactiveGraph(), "tooltip-hover");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        OwnedSurfaceRequest? request = null;
        composition.Input.SurfaceRequested += value => request = (OwnedSurfaceRequest)value;
        composition.Mount(
            composition.Root,
            theme,
            Components.Tooltip(
                "More information",
                [Components.Button("Target", () => { })],
                delay: TimeSpan.FromMilliseconds(500),
                timeProvider: clock,
                style: Style.Empty.Width(180).Height(32)
            )
        );

        using var scene = Install(composition);
        var target = FindNode(composition, scene, SemanticRole.Button, "Target");
        composition.Input.DispatchPointer(
            new(PointerCommandKind.Move, 1, target.Bounds.X + 2, target.Bounds.Y + 2)
        );
        Assert.IsNull(request, "A tooltip appeared before its hover delay elapsed.");

        clock.Advance(TimeSpan.FromMilliseconds(499));
        composition.Flush();
        Assert.IsNull(request, "A tooltip appeared before its full hover delay elapsed.");

        clock.Advance(TimeSpan.FromMilliseconds(1));
        composition.Flush();
        Assert.IsNotNull(request);
        Assert.IsFalse(request!.IsInteractive);
        Assert.IsFalse(request.ConsumeOutsideClick);
        request.Dispose();
    }

    [TestMethod]
    public void PopupPointerGraceAllowsCrossingAnchorGapBeforeDismissing()
    {
        var clock = new FakeTimeProvider();
        using var composition = new Composition(new ReactiveGraph(), "tooltip-hoverable");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        OwnedSurfaceRequest? request = null;
        composition.Input.SurfaceRequested += value => request = (OwnedSurfaceRequest)value;
        composition.Mount(
            composition.Root,
            theme,
            Components.Tooltip(
                "Hoverable details",
                [Components.Button("Target", () => { })],
                timeProvider: clock,
                style: Style.Empty.Width(180).Height(32)
            )
        );

        using var scene = Install(composition);
        var target = FindNode(composition, scene, SemanticRole.Button, "Target");
        composition.Input.DispatchPointer(
            new(PointerCommandKind.Move, 1, target.Bounds.X + 2, target.Bounds.Y + 2)
        );
        clock.Advance(TimeSpan.FromMilliseconds(500));
        composition.Flush();
        Assert.IsNotNull(request);
        var surface = request!;

        composition.Input.ClearPointerHover();
        clock.Advance(TimeSpan.FromMilliseconds(149));
        composition.Flush();
        Assert.IsFalse(surface.IsDismissed, "The anchor gap consumed the tooltip grace period.");

        surface.SetPointerInside(true);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        composition.Flush();
        Assert.IsFalse(surface.IsDismissed, "Entering the tooltip surface did not cancel grace.");

        surface.SetPointerInside(false);
        clock.Advance(TimeSpan.FromMilliseconds(149));
        composition.Flush();
        Assert.IsFalse(surface.IsDismissed, "Tooltip dismissed before its leave grace elapsed.");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        composition.Flush();
        Assert.IsTrue(surface.IsDismissed, "Leaving both anchor and popup kept the tooltip open.");
        surface.Dispose();
    }

    [TestMethod]
    public void FocusShowsImmediatelyWithoutAddingAWrapperFocusStop()
    {
        var clock = new FakeTimeProvider();
        using var composition = new Composition(new ReactiveGraph(), "tooltip-focus");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        OwnedSurfaceRequest? request = null;
        composition.Input.SurfaceRequested += value => request = (OwnedSurfaceRequest)value;
        composition.Mount(
            composition.Root,
            theme,
            Components.Layout(
                [
                    Components.Tooltip(
                        "Focused description",
                        [
                            Components.Layout(
                                [
                                    Components.Button("Inside", () => { }),
                                    Components.Button("Inside secondary", () => { }),
                                ],
                                style: Style.Empty.Axis(LayoutAxis.Column)
                            ),
                        ],
                        delay: TimeSpan.FromSeconds(30),
                        timeProvider: clock
                    ),
                    Components.Button("Outside", () => { }),
                ],
                style: Style.Empty.Axis(LayoutAxis.Column).Spacing(8)
            )
        );

        using var scene = Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.AreEqual(
            "Inside",
            composition.Input.FocusedElement is { } focused
                ? FindNode(composition, focused).Name
                : null
        );
        Assert.IsNotNull(request, "Focus did not show the tooltip immediately.");

        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.AreEqual(
            "Inside secondary",
            composition.Input.FocusedElement is { } next ? FindNode(composition, next).Name : null
        );
        Assert.IsFalse(request!.IsDismissed, "Descendant focus transfer dismissed the tooltip.");

        request.Dispose();
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.AreEqual(
            "Outside",
            composition.Input.FocusedElement is { } outside
                ? FindNode(composition, outside).Name
                : null
        );
    }

    [TestMethod]
    public void EscapeRespectsActiveImeCompositionBeforeDismissing()
    {
        var clock = new FakeTimeProvider();
        using var composition = new Composition(new ReactiveGraph(), "tooltip-ime");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        TextFieldState? editor = null;
        OwnedSurfaceRequest? request = null;
        composition.Input.SurfaceRequested += value => request = (OwnedSurfaceRequest)value;
        composition.Mount(
            composition.Root,
            theme,
            Components.Tooltip(
                "Editor help",
                [
                    ComponentRecipe.Create(
                        "editor-content",
                        (_, root) =>
                        {
                            editor = Controls.TextField(
                                root,
                                theme,
                                "Editor",
                                style: Style.Empty.Width(180).Height(32)
                            );
                        }
                    ),
                ],
                timeProvider: clock
            )
        );

        using var scene = Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsNotNull(request);
        editor!.SetPreedit("候", 0, 1);

        var imeEscape = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape));
        Assert.IsTrue(imeEscape.Handled);
        Assert.IsFalse(editor.HasPreedit);
        Assert.IsFalse(request!.IsDismissed, "IME cancellation dismissed the tooltip.");

        var plainEscape = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape));
        Assert.IsTrue(plainEscape.Handled);
        Assert.IsTrue(request.IsDismissed, "A plain Escape did not dismiss the tooltip.");
    }

    [TestMethod]
    public void DescriptionMergesIntoFieldEditorWithoutReplacingRelationships()
    {
        using var composition = new Composition(new ReactiveGraph(), "tooltip-field");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var form = new FormSession(composition.Root.Scope, "profile");
        var validation = composition.Root.Scope.Signal(
            ValidationState.Invalid("Use a complete email address."),
            "email-validation"
        );
        composition.Mount(
            composition.Root,
            theme,
            Components.Tooltip(
                "Shown when the editor is focused.",
                [
                    Components.Field(
                        "Email",
                        field => Components.TextField(field, placeholder: "name@example.com"),
                        () => validation.Value,
                        () => "Used for account recovery.",
                        form,
                        "email",
                        required: true
                    ),
                ]
            )
        );
        composition.Flush();

        var editor = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);
        Assert.AreEqual("Shown when the editor is focused.", editor.Description);
        Assert.AreEqual("Used for account recovery.", editor.Relationships!.HelpText);
        Assert.IsFalse(editor.Relationships.IsInvalid);

        var submit = form.SubmitAsync().AsTask().GetAwaiter().GetResult();
        Assert.AreEqual(FormSubmitStatus.Invalid, submit.Status);
        composition.Flush();
        editor = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);
        Assert.AreEqual("Shown when the editor is focused.", editor.Description);
        Assert.AreEqual("Used for account recovery.", editor.Relationships!.HelpText);
        Assert.AreEqual("Use a complete email address.", editor.Relationships.ErrorText);
        Assert.IsTrue(editor.Relationships.IsInvalid);
    }

    private static RetainedScene Install(Composition composition)
    {
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(320, 180, 1), new EmptyShaper());
        Assert.IsTrue(composition.Input.SetScene(scene), "Tooltip test scene did not install.");
        return scene;
    }

    private static SemanticSnapshot FindNode(Composition composition, ElementIdentity identity) =>
        Flatten(composition.SemanticSnapshot()!)
            .Single(node =>
                node.Identity.CompositionEpoch == identity.CompositionEpoch
                && node.Identity.ElementId == identity.ElementId
            );

    private static (SemanticIdentity Identity, LayoutRect Bounds) FindNode(
        Composition composition,
        RetainedScene scene,
        SemanticRole role,
        string name
    )
    {
        var node = Flatten(composition.SemanticSnapshot()!)
            .Single(candidate => candidate.Role == role && candidate.Name == name);
        var bounds = scene
            .Boxes.Single(box => box.Identity.ElementId == node.Identity.ElementId)
            .Bounds;
        return (node.Identity, bounds);
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("tooltip-empty", 0, request.FontSize, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "tooltip",
                "tooltip",
                400,
                5,
                0,
                "tooltip",
                0,
                "tooltip#0",
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
            return new("tooltip", request.Text.Length, request.FontSize, [run]);
        }
    }
}
