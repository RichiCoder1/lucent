using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class SemanticCapabilityContracts
{
    [TestMethod]
    public void MixedCapabilitiesCompileExplicitOperationsAndFlatAccessors()
    {
        var range = new SemanticRangeSnapshot(4, 0, 10, 1, 2);
        var declaration = SemanticDeclaration
            .Create(SemanticRole.TreeItem, "Expandable value")
            .Enabled(false)
            .Value("4")
            .Expansion(false, canExpandCollapse: true)
            .Range(range, canSetValue: true)
            .SelectionItem(selected: true, canSelect: true)
            .SetMembership(2, 8)
            .Hierarchy(3)
            .CollectionItem(5)
            .Build();

        Assert.AreEqual(
            SemanticAction.ExpandCollapse | SemanticAction.SetRangeValue | SemanticAction.Select,
            declaration.Actions
        );
        Assert.IsFalse(declaration.Enabled);
        Assert.IsTrue(declaration.Selected);
        Assert.AreEqual(false, declaration.Expanded);
        Assert.AreSame(range, declaration.Range);
        Assert.AreEqual(2, declaration.PositionInSet);
        Assert.AreEqual(8, declaration.SizeOfSet);
        Assert.AreEqual(3, declaration.Level);
        Assert.AreEqual(5, declaration.CollectionIndex);
    }

    [TestMethod]
    public void ValidationNamesDuplicateAndIncoherentCapabilities()
    {
        var duplicate = Assert.ThrowsExactly<InvalidOperationException>(() =>
            SemanticDeclaration.Create(SemanticRole.Button, "Duplicate").Invoke().Invoke()
        );
        StringAssert.Contains(duplicate.Message, "invoke capability");

        var range = Assert.ThrowsExactly<ArgumentException>(() =>
            SemanticDeclaration
                .Create(SemanticRole.Slider, "Range")
                .Range(new SemanticRangeSnapshot(1, 0, 2, 1, 1, isReadOnly: true), true)
        );
        StringAssert.Contains(range.Message, "Range capability read-only state");

        var selection = Assert.ThrowsExactly<ArgumentException>(() =>
            SemanticDeclaration.Create(SemanticRole.Table, "Table").Build()
        );
        StringAssert.Contains(selection.Message, "selection-container capability");

        var toggleOperation = Assert.ThrowsExactly<ArgumentException>(() =>
            SemanticDeclaration
                .Create(SemanticRole.CheckBox, "Toggle")
                .Toggle(SemanticToggleState.Off, canToggle: false)
                .Build()
        );
        StringAssert.Contains(toggleOperation.Message, "Toggle capability");

        var expansionOperation = Assert.ThrowsExactly<ArgumentException>(() =>
            SemanticDeclaration
                .Create(SemanticRole.Button, "Expansion")
                .Expansion(false, canExpandCollapse: false)
                .Build()
        );
        StringAssert.Contains(expansionOperation.Message, "Expansion capability");

        var missingRange = Assert.ThrowsExactly<ArgumentException>(() =>
            SemanticDeclaration
                .Create(SemanticRole.Slider, "Missing range")
                .Range(null, canSetValue: true)
        );
        StringAssert.Contains(missingRange.Message, "range operations");

        var indeterminateSwitch = Assert.ThrowsExactly<ArgumentException>(() =>
            SemanticDeclaration
                .Create(SemanticRole.Switch, "Switch")
                .Toggle(SemanticToggleState.Indeterminate, canToggle: true)
                .Build()
        );
        StringAssert.Contains(indeterminateSwitch.Message, "cannot be indeterminate");
    }

    [TestMethod]
    public void ConfidentialEditingAcceptsNoContentsInEitherBuilderOrder()
    {
        var confidential = SemanticDeclaration
            .Create(SemanticRole.TextField, "Password")
            .ConfidentialEditing(canSetValue: true)
            .Build();

        Assert.IsTrue(confidential.IsPassword);
        Assert.IsNull(confidential.Value);
        Assert.IsNull(confidential.Text);
        Assert.AreEqual(SemanticAction.SetValue, confidential.Actions);

        Assert.ThrowsExactly<ArgumentException>(() =>
            SemanticDeclaration
                .Create(SemanticRole.TextField, "Password")
                .Value("secret")
                .ConfidentialEditing(canSetValue: true)
                .Build()
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            SemanticDeclaration
                .Create(SemanticRole.TextField, "Password")
                .ConfidentialEditing(canSetValue: true)
                .Value("secret")
                .Build()
        );
        var editing = new SemanticTextSnapshot("visible", 0, 0);
        var confidentialThenEditing = Assert.ThrowsExactly<InvalidOperationException>(() =>
            SemanticDeclaration
                .Create(SemanticRole.TextField, "Password")
                .ConfidentialEditing(canSetValue: true)
                .Editing(editing, true, false, false)
        );
        StringAssert.Contains(confidentialThenEditing.Message, "value-pattern/editing");
        var editingThenConfidential = Assert.ThrowsExactly<InvalidOperationException>(() =>
            SemanticDeclaration
                .Create(SemanticRole.TextField, "Password")
                .Editing(editing, true, false, false)
                .ConfidentialEditing(canSetValue: true)
        );
        StringAssert.Contains(editingThenConfidential.Message, "value-pattern/editing");
    }

    [TestMethod]
    public void MetadataAndSnapshotReplacementsShareCapabilitiesAndFreezeChildren()
    {
        var declaration = SemanticDeclaration.Create(SemanticRole.Button, "Base").Invoke().Build();
        var authored = declaration.WithMetadata("Authored", "Description");
        Assert.AreSame(declaration.Capabilities, authored.Capabilities);

        var childPayload = SemanticDeclaration.Create(SemanticRole.Text, "Child").Build();
        var children = new[]
        {
            new SemanticSnapshot(
                new SemanticIdentity(1, 2, 1),
                childPayload,
                true,
                false,
                false,
                Array.Empty<SemanticSnapshot>()
            ),
        };
        var snapshot = new SemanticSnapshot(
            new SemanticIdentity(1, 1, 1),
            authored,
            true,
            false,
            false,
            children
        );

        children[0] = new SemanticSnapshot(
            new SemanticIdentity(1, 3, 1),
            childPayload,
            true,
            false,
            false,
            Array.Empty<SemanticSnapshot>()
        );
        Assert.AreSame(authored, snapshot.Payload);
        Assert.AreEqual(2, snapshot.Children.Single().Identity.ElementId);

        var disabled = snapshot.WithStateAndChildren(
            false,
            false,
            snapshot.Selected,
            snapshot.Children
        );
        Assert.AreSame(snapshot.Payload, disabled.Payload);
        Assert.AreSame(snapshot.Payload.Capabilities, disabled.Payload.Capabilities);
        Assert.IsFalse(disabled.Enabled);
    }

    [TestMethod]
    public void StructuralSnapshotsReusePayloadAndRefreshValidDescriptionChanges()
    {
        using var composition = new Composition(new ReactiveGraph(), "structural-payload");

        var first = composition.Root.CreateStructuralSemanticSnapshot([]);
        var reused = composition.Root.CreateStructuralSemanticSnapshot([]);
        Assert.AreSame(first.Payload, reused.Payload);

        composition.Root.SetSupplementalDescription("First description");
        var described = composition.Root.CreateStructuralSemanticSnapshot([]);
        Assert.AreNotSame(first.Payload, described.Payload);
        Assert.AreEqual("First description", described.Description);

        composition.Root.SetSupplementalDescription("Second description");
        var updated = composition.Root.CreateStructuralSemanticSnapshot([]);
        Assert.AreNotSame(described.Payload, updated.Payload);
        Assert.AreEqual("Second description", updated.Description);
        Assert.AreEqual("First description", described.Description);

        Assert.ThrowsExactly<ArgumentException>(() =>
            composition.Root.SetSupplementalDescription(null!)
        );
        var afterRejectedChange = composition.Root.CreateStructuralSemanticSnapshot([]);
        Assert.AreSame(updated.Payload, afterRejectedChange.Payload);
        Assert.AreEqual("Second description", afterRejectedChange.Description);
    }
}
