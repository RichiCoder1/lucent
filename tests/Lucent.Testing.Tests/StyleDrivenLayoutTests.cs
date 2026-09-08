using Lucent.Core;

namespace Lucent.Testing.Tests;

[TestClass]
public sealed class StyleDrivenLayoutTests
{
    [TestMethod]
    public async Task StyleBindingsSwitchGridToFlexAndCollapseChildrenWithoutRemounting()
    {
        await using var app = await HeadlessApplication.StartAsync(
            context =>
                HeadlessFixtures.Components.StyleDrivenLayout(new(context.Composition.Root.Scope)),
            new() { Viewport = new(800, 300, 1) }
        );
        var wide = await app.SnapshotAsync();
        var draft = wide.Require(SemanticRole.TextField, "Draft");
        var navigation = wide.Require(SemanticRole.TextField, "Navigation");
        Assert.AreEqual(180f, wide.RequireBox(navigation).Bounds.Width);
        Assert.AreEqual(188f, wide.RequireBox(draft).Bounds.X);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            await app.InvokeAsync(context =>
                context.Composition.ExecuteSemanticCommand(
                    draft.Identity,
                    new(SemanticCommandKind.SetValue, "retained draft")
                )
            )
        );

        var narrow = await app.ResizeAsync(new(420, 300, 1.5f));
        var narrowDraft = narrow.Require(SemanticRole.TextField, "Draft");
        AssertSameMount(draft, narrowDraft);
        Assert.AreEqual("retained draft", narrowDraft.Value);
        Assert.AreEqual(0, narrow.FindAll(SemanticRole.TextField, "Navigation").Count);
        Assert.AreEqual(0f, narrow.RequireBox(narrowDraft).Bounds.X);
        Assert.AreEqual(420f, narrow.RequireBox(narrowDraft).Bounds.Width);

        var restored = await app.ResizeAsync(new(800, 300, 2));
        AssertSameMount(navigation, restored.Require(SemanticRole.TextField, "Navigation"));
        AssertSameMount(draft, restored.Require(SemanticRole.TextField, "Draft"));
        Assert.AreEqual("retained draft", restored.Require(SemanticRole.TextField, "Draft").Value);
        Assert.AreEqual(wide.Scene.Boxes.Count, restored.Scene.Boxes.Count);
    }

    private static void AssertSameMount(SemanticSnapshot before, SemanticSnapshot after)
    {
        // Value and availability updates may invalidate the semantic generation;
        // retained mount identity is the composition epoch plus element ID.
        Assert.AreEqual(before.Identity.CompositionEpoch, after.Identity.CompositionEpoch);
        Assert.AreEqual(before.Identity.ElementId, after.Identity.ElementId);
    }
}
