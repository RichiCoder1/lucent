using Lucent.Core;
using Lucent.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TemplateNamespace;

[TestClass]
public sealed class CounterTests
{
    [TestMethod]
    public async Task InvokingCounterUpdatesTextAndRetainsTheButton()
    {
        await using var app = await HeadlessApplication.StartAsync(Components.Counter());
        using var before = await app.SnapshotAsync();
        var button = before.Require(SemanticRole.Button, "Increment");
        Assert.IsNotNull(before.Require(SemanticRole.Text, "Count: 0"));

        var result = await app.InvokeAsync(context =>
            context.Composition.ExecuteSemanticCommand(
                button.Identity,
                new(SemanticCommandKind.Invoke)
            )
        );

        using var after = await app.SnapshotAsync();
        Assert.AreEqual(SemanticCommandResult.Applied, result);
        Assert.IsNotNull(after.Require(SemanticRole.Text, "Count: 1"));
        Assert.AreEqual(button.Identity, after.Require(SemanticRole.Button, "Increment").Identity);
    }
}
