using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class FieldContracts
{
    [TestMethod]
    public void BlurRevealsErrorsAndLabelActivationRequestsEditorFocus()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "field-blur");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        FieldContext? field = null;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Name",
                context =>
                {
                    field = context;
                    return Components.TextField(context);
                },
                () => ValidationState.Invalid("Name is required.")
            )
        );
        graph.Drain();
        Assert.IsFalse(field!.ShowErrors);
        field.Blur();
        graph.Drain();
        Assert.IsTrue(field.ShowErrors);

        using var scene = Install(composition);
        var label = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Text && node.Name == "Name");
        var bounds = scene
            .Boxes.Single(box => box.Identity.ElementId == label.Identity.ElementId)
            .Bounds;
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(
                        PointerCommandKind.Down,
                        1,
                        bounds.X + 1,
                        bounds.Y + 1,
                        PointerButton.Primary
                    )
                )
                .Handled
        );
        Assert.IsTrue(
            composition
                .Input.DispatchPointer(
                    new(PointerCommandKind.Up, 1, bounds.X + 1, bounds.Y + 1, PointerButton.Primary)
                )
                .Handled
        );
        Assert.IsTrue(field.FocusTarget.IsPending);
    }

    [TestMethod]
    public async Task LuiFieldFactoryKeepsAccessibleRelationshipsStableThroughSubmit()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "field-root");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var form = new FormSession(composition.Root.Scope, "profile");
        var validation = graph.Signal(
            ValidationState.Invalid("Use a complete email address."),
            "email-validation"
        );
        FieldContext? captured = null;
        var mounted = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Email",
                field =>
                {
                    captured = field;
                    return Components.TextField(field, placeholder: "name@example.com");
                },
                () => validation.Value,
                () => "Used for account recovery.",
                form,
                "email",
                required: true
            )
        );
        _ = composition.Mount(composition.Root, theme, Components.FormErrorSummary(form));
        graph.Drain();

        var before = Nodes(composition.SemanticSnapshot()!).ToArray();
        var editor = before.Single(node => node.Role == SemanticRole.TextField);
        var label = before.Single(node => node.Name == "Email *");
        var help = before.Single(node => node.Name == "Used for account recovery.");
        Assert.IsNotNull(captured);
        Assert.AreEqual("Email", editor.Name);
        Assert.AreEqual(label.Identity.ElementId, editor.Relationships!.Label!.Value.ElementId);
        Assert.AreEqual(help.Identity.ElementId, editor.Relationships.Help!.Value.ElementId);
        Assert.IsFalse(editor.Relationships.IsInvalid);
        Assert.AreEqual(0, editor.Relationships.Errors.Count);

        var submit = await form.SubmitAsync();
        graph.Drain();
        var after = Nodes(composition.SemanticSnapshot()!).ToArray();
        editor = after.Single(node => node.Role == SemanticRole.TextField);
        var error = after.Single(node => node.Name == "Use a complete email address.");
        Assert.AreEqual(FormSubmitStatus.Invalid, submit.Status);
        Assert.AreEqual("email", string.Join(",", submit.InvalidFields));
        Assert.IsTrue(captured.FocusTarget.IsPending);
        Assert.AreEqual(label.Identity.ElementId, editor.Relationships!.Label!.Value.ElementId);
        Assert.AreEqual(help.Identity.ElementId, editor.Relationships.Help!.Value.ElementId);
        Assert.AreEqual(error.Identity.ElementId, editor.Relationships.Errors.Single().ElementId);
        Assert.AreEqual("Used for account recovery.", editor.Relationships.HelpText);
        Assert.AreEqual("Use a complete email address.", editor.Relationships.ErrorText);
        Assert.IsTrue(editor.Relationships.IsInvalid);
        Assert.IsTrue(
            after.Any(node =>
                node.Role == SemanticRole.Button
                && node.Name == "email: Use a complete email address."
            )
        );

        mounted.Dispose();
        var empty = await form.SubmitAsync();
        Assert.AreEqual(FormSubmitStatus.Valid, empty.Status);
    }

    [TestMethod]
    public void DynamicHelpTextUpdatesRelationshipsWithoutRemountingTheEditor()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "field-help-live");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var help = graph.Signal("Initial help.", "field-help");
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field("Email", field => Components.TextField(field), help: () => help.Value)
        );
        graph.Drain();

        var before = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);
        Assert.AreEqual("Initial help.", before.Relationships!.HelpText);

        help.Value = "Updated help.";
        graph.Drain();

        var after = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);
        Assert.AreEqual(before.Identity.CompositionEpoch, after.Identity.CompositionEpoch);
        Assert.AreEqual(before.Identity.ElementId, after.Identity.ElementId);
        Assert.AreEqual("Updated help.", after.Relationships!.HelpText);
    }

    [TestMethod]
    public void ReturningToValidRemovesErrorRelationshipsFromTheSameEditor()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "field-errors-live");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var validation = graph.Signal(ValidationState.Valid, "field-validation");
        FieldContext? field = null;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Email",
                current =>
                {
                    field = current;
                    return Components.TextField(current);
                },
                () => validation.Value
            )
        );
        graph.Drain();
        var initial = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);

        field!.Blur();
        graph.Drain();
        validation.Value = ValidationState.Invalid("Email is required.");
        graph.Drain();

        var invalid = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);
        Assert.IsTrue(invalid.Relationships!.IsInvalid);
        Assert.AreEqual(1, invalid.Relationships.Errors.Count);
        Assert.AreEqual("Email is required.", invalid.Relationships.ErrorText);

        validation.Value = ValidationState.Valid;
        graph.Drain();

        var valid = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);
        Assert.AreEqual(initial.Identity.CompositionEpoch, valid.Identity.CompositionEpoch);
        Assert.AreEqual(initial.Identity.ElementId, valid.Identity.ElementId);
        Assert.IsFalse(valid.Relationships!.IsInvalid);
        Assert.AreEqual(0, valid.Relationships.Errors.Count);
        Assert.IsNull(valid.Relationships.ErrorText);
    }

    [TestMethod]
    public void ValidationMessagesAreImmutableAndOldGenerationsCannotComplete()
    {
        var source = new[] { "First", "Second" };
        var invalid = ValidationState.Invalid(source);
        source[0] = "Changed";
        Assert.AreEqual("First|Second", string.Join("|", invalid.Messages));

        var first = ValidationState.Pending(1);
        var second = ValidationState.Pending(2);
        Assert.IsTrue(first.TryComplete(1, invalid, out var firstResult));
        Assert.AreSame(invalid, firstResult);
        Assert.IsFalse(second.TryComplete(1, invalid, out var secondResult));
        Assert.AreSame(second, secondResult);
    }

    [TestMethod]
    public void SemanticRelationshipsCopyErrorsAndRejectIncoherentMetadata()
    {
        var label = new ElementIdentity(3, 4);
        var errors = new[] { new ElementIdentity(3, 5) };
        var relationships = new SemanticRelationships(
            label,
            errors: errors,
            errorText: "Invalid",
            isInvalid: true
        );
        errors[0] = new ElementIdentity(3, 6);
        Assert.AreEqual(5, relationships.Errors.Single().ElementId);
        Assert.Throws<ArgumentException>(() =>
            new SemanticRelationships(errors: [new ElementIdentity(3, 5)], errorText: "Invalid")
        );
        Assert.Throws<ArgumentException>(() =>
            new SemanticRelationships(errors: [default], isInvalid: true)
        );
    }

    [TestMethod]
    public async Task CollapsedParticipationAndRegistrationDisposalChooseFirstEligibleError()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "form-participation");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var form = new FormSession(composition.Root.Scope);
        FieldContext? collapsed = null;
        FieldContext? visible = null;
        var first = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Collapsed",
                field =>
                {
                    collapsed = field;
                    return Components.TextField(field);
                },
                () => ValidationState.Invalid("Hidden error"),
                session: form,
                fieldId: "collapsed",
                style: Style.Empty.Participation(ElementParticipation.Collapsed)
            )
        );
        var secondMount = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Visible",
                field =>
                {
                    visible = field;
                    return Components.TextField(field);
                },
                () => ValidationState.Invalid("Visible error"),
                session: form,
                fieldId: "visible"
            )
        );
        graph.Drain();
        var result = await form.SubmitAsync();
        Assert.AreEqual("visible", string.Join(",", result.InvalidFields));
        Assert.IsFalse(collapsed!.FocusTarget.IsPending);
        Assert.IsTrue(visible!.FocusTarget.IsPending);

        first.Dispose();
        Assert.AreEqual(1, form.Errors.Count);

        visible!.FocusTarget.Cancel();
        secondMount.Dispose();
        var always = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Always",
                field =>
                {
                    collapsed = field;
                    return Components.TextField(field);
                },
                () => ValidationState.Invalid("Always error"),
                session: form,
                fieldId: "always",
                participation: FieldParticipation.Always,
                style: Style.Empty.Participation(ElementParticipation.Collapsed)
            )
        );
        var alwaysResult = await form.SubmitAsync();
        Assert.IsTrue(alwaysResult.InvalidFields.Contains("always"));
        Assert.IsTrue(collapsed!.FocusTarget.IsPending);
        always.Dispose();
    }

    [TestMethod]
    public async Task RegistrationOrderSurvivesRemovalAndAppendsNewFields()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "form-registration-order");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var form = new FormSession(composition.Root.Scope);
        var validationA = graph.Signal(ValidationState.Valid, "validation-a");
        var validationB = graph.Signal(ValidationState.Valid, "validation-b");
        var validationC = graph.Signal(ValidationState.Valid, "validation-c");
        var validationD = graph.Signal(ValidationState.Valid, "validation-d");
        FieldContext? fieldC = null;
        FieldContext? fieldD = null;

        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "A",
                field => Components.TextField(field),
                () => validationA.Value,
                session: form,
                fieldId: "a"
            )
        );
        var removed = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "B",
                field => Components.TextField(field),
                () => validationB.Value,
                session: form,
                fieldId: "b"
            )
        );
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "C",
                field =>
                {
                    fieldC = field;
                    return Components.TextField(field);
                },
                () => validationC.Value,
                session: form,
                fieldId: "c"
            )
        );
        graph.Drain();

        removed.Dispose();
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "D",
                field =>
                {
                    fieldD = field;
                    return Components.TextField(field);
                },
                () => validationD.Value,
                session: form,
                fieldId: "d"
            )
        );
        graph.Drain();

        validationC.Value = ValidationState.Invalid("C is invalid.");
        validationD.Value = ValidationState.Invalid("D is invalid.");
        graph.Drain();
        var result = await form.SubmitAsync();

        Assert.AreEqual(FormSubmitStatus.Invalid, result.Status);
        Assert.AreEqual("c,d", string.Join(",", result.InvalidFields));
        Assert.AreEqual("c,d", string.Join(",", form.Errors.Select(error => error.FieldId)));
        Assert.IsTrue(fieldC!.FocusTarget.IsPending);
        Assert.IsFalse(fieldD!.FocusTarget.IsPending);
    }

    [TestMethod]
    public void ReadOnlyAndDisabledEditorMetadataAreExplicitAndDuplicateEditorsRollback()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "field-states");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Reference",
                field =>
                    Components.TextField(
                        field,
                        initialValue: "fixed",
                        enabled: () => false,
                        readOnly: () => true
                    )
            )
        );
        graph.Drain();
        using var scene = Install(composition);
        var editor = Nodes(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.TextField);
        Assert.IsFalse(editor.Enabled);
        Assert.IsTrue(editor.Text!.IsReadOnly);
        Assert.IsFalse(editor.Actions.HasFlag(SemanticAction.SetValue));

        Assert.Throws<InvalidOperationException>(() =>
            composition.Mount(
                composition.Root,
                theme,
                Components.Field(
                    "Duplicate",
                    field =>
                        Components.Column([
                            Components.TextField(field),
                            Components.TextField(field),
                        ])
                )
            )
        );
        Assert.AreEqual(
            1,
            Nodes(composition.SemanticSnapshot()!)
                .Count(node => node.Role == SemanticRole.TextField)
        );
    }

    [TestMethod]
    public async Task DuplicateFormIdentityRollsBackAndPendingSubmitRejectsWithoutFocus()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "form-duplicates");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var form = new FormSession(composition.Root.Scope);
        FieldContext? pending = null;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "First",
                field =>
                {
                    pending = field;
                    return Components.TextField(field);
                },
                () => ValidationState.Pending(2),
                session: form,
                fieldId: "same"
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            composition.Mount(
                composition.Root,
                theme,
                Components.Field(
                    "Second",
                    field => Components.TextField(field),
                    session: form,
                    fieldId: "same"
                )
            )
        );
        var result = await form.SubmitAsync();
        Assert.AreEqual(FormSubmitStatus.Pending, result.Status);
        Assert.IsFalse(pending!.FocusTarget.IsPending);
    }

    [TestMethod]
    public void AwaitedValidationRechecksOnTheOwningGraph()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "form-await");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var form = new FormSession(composition.Root.Scope);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var validation = graph.Signal(
            ValidationState.Pending(1, completion.Task),
            "async-validation"
        );
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Async",
                field => Components.TextField(field),
                () => validation.Value,
                session: form
            )
        );
        graph.Drain();

        var submit = form.SubmitAsync(PendingValidationPolicy.Await).AsTask();
        Assert.IsFalse(submit.IsCompleted);
        using var workAvailable = new ManualResetEventSlim();
        graph.WorkAvailable += workAvailable.Set;
        validation.Value = ValidationState.Valid;
        completion.SetResult();
        Assert.IsTrue(workAvailable.Wait(TimeSpan.FromSeconds(5)));
        graph.Drain();

        Assert.IsTrue(submit.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(FormSubmitStatus.Valid, submit.GetAwaiter().GetResult().Status);
    }

    [TestMethod]
    public void CancelledPendingSubmitCompletesAndReleasesItsOwnerRegistration()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "form-await-cancel");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        using var form = new FormSession(composition.Root.Scope);
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var validation = graph.Signal(
            ValidationState.Pending(1, completion.Task),
            "cancel-validation"
        );
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Cancel",
                field => Components.TextField(field),
                () => validation.Value,
                session: form
            )
        );
        graph.Drain();

        var submit = form.SubmitAsync(PendingValidationPolicy.Await, cancellation.Token).AsTask();
        using var workAvailable = new ManualResetEventSlim();
        graph.WorkAvailable += workAvailable.Set;
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => submit.GetAwaiter().GetResult());
        Assert.IsTrue(workAvailable.Wait(TimeSpan.FromSeconds(5)));
        graph.Drain();
    }

    [TestMethod]
    public void ParentScopeDisposalCompletesPendingSubmit()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "form-await-dispose");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var form = new FormSession(composition.Root.Scope);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var validation = graph.Signal(
            ValidationState.Pending(1, completion.Task),
            "dispose-validation"
        );
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Dispose",
                field => Components.TextField(field),
                () => validation.Value,
                session: form
            )
        );
        graph.Drain();

        var submit = form.SubmitAsync(PendingValidationPolicy.Await).AsTask();
        composition.Dispose();

        Assert.Throws<ObjectDisposedException>(() => submit.GetAwaiter().GetResult());
        form.Dispose();
        theme.Dispose();
    }

    [TestMethod]
    public void ControlledEditorRequestsDraftAndWaitsForAChangedAppliedValue()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "controlled-field");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal("A", "applied");
        string? requested = null;
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Field(
                "Code",
                field =>
                    Components.TextField(field, () => applied.Value, value => requested = value)
            )
        );
        graph.Drain();
        using var scene = Install(composition);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(composition.Input.DispatchText(new(TextInputKind.Commit, "x")).Handled);
        graph.Drain();
        Assert.AreEqual("Ax", requested);
        Assert.AreEqual("A", applied.Value);
        Assert.AreEqual(
            "Ax",
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.TextField)
                .Value
        );

        applied.Value = "server";
        graph.Drain();
        Assert.AreEqual(
            "server",
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.TextField)
                .Value
        );
    }

    [TestMethod]
    public void NullEditorFactoryAndSecondPrimaryEditorFailTheMountTransaction()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "field-factory-failure");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var before = composition.Dump();
        Assert.Throws<ArgumentNullException>(() =>
            composition.Mount(
                composition.Root,
                theme,
                Components.Field("Broken", static _ => null!)
            )
        );
        Assert.AreEqual(before, composition.Dump());
    }

    private static RetainedScene Install(Composition composition)
    {
        composition.Flush();
        var scene = SceneLayout.Project(composition, new(500, 500, 1), new FieldShaper());
        if (!composition.Input.SetScene(scene))
        {
            scene.Dispose();
            composition.Flush();
            scene = SceneLayout.Project(composition, new(500, 500, 1), new FieldShaper());
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

    private sealed class FieldShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("field-empty", 0, request.FontSize, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "field",
                "field",
                400,
                5,
                0,
                "field",
                0,
                "field#0",
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
            return new("field", request.Text.Length, request.FontSize, [run]);
        }
    }
}
