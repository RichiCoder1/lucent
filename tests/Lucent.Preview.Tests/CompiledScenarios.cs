using Lucent.Core;
using PreviewFixtures;

namespace Lucent.Preview.Tests;

internal static class CompiledScenarios
{
    internal const string LongMessage =
        "This is explicit fixture content, supplied by the scenario rather than inferred by the component. "
        + "A long title or paragraph should exercise the same wrapping and layout code as a normal application. "
        + "It must remain complete when the viewport is narrow, including this final sentence.";

    internal static PreviewCatalog Create()
    {
        var builder = new PreviewCatalogBuilder();
        Add("empty", "Empty", "No saved items.");
        Add("loading", "Loading", "Retrieving saved items.");
        Add("error", "Error", "The fixture service is unavailable.");
        Add("long-text", "Ready", LongMessage);
        return builder.Build();

        void Add(string id, string state, string message)
        {
            var descriptor = PreviewCatalogTests.Descriptor(id);
            builder.Add(
                new PreviewScenarioDescriptor(
                    id,
                    "Card: " + state,
                    new PreviewSource(
                        "tests/Lucent.Preview.Fixtures/Lucent.Preview.Fixtures.csproj",
                        "ScenarioCard.lui",
                        "PreviewFixtures.Components.ScenarioCard"
                    ),
                    descriptor.Presentation
                ),
                (_, _) => ValueTask.FromResult(new ScenarioContent(state, message)),
                (content, _) => PreviewFixtures.Components.ScenarioCard(content)
            );
        }
    }
}

[TestClass]
public sealed class CompiledScenarioTests
{
    [TestMethod]
    [DataRow("empty", "Empty", "No saved items.")]
    [DataRow("loading", "Loading", "Retrieving saved items.")]
    [DataRow("error", "Error", "The fixture service is unavailable.")]
    [DataRow("long-text", "Ready", CompiledScenarios.LongMessage)]
    public async Task ScenarioRendersTheCompiledComponentWithItsExactFixtureData(
        string id,
        string state,
        string message
    )
    {
        var scenario = CompiledScenarios.Create().Get(id);
        await using var application = await PreviewCatalogTests.Start(scenario);
        using var frame = await application.SnapshotAsync();
        frame.Require(SemanticRole.Text, state);
        var messageNode = frame.Require(SemanticRole.Text, message);
        if (id == "long-text")
        {
            var stateBox = frame.RequireBox(frame.Require(SemanticRole.Text, state));
            var messageBox = frame.RequireBox(messageNode);
            Assert.IsGreaterThan(stateBox.Bounds.Height * 2, messageBox.Bounds.Height);
        }
    }

    [TestMethod]
    public void CompiledComponentAssemblyDoesNotDependOnPreviewOrTesting()
    {
        var references = typeof(ScenarioContent).Assembly.GetReferencedAssemblies();
        Assert.IsFalse(
            references.Any(reference =>
                reference.Name is "Lucent.Preview" or "Lucent.Testing" or "Lucent.Testing.Skia"
            )
        );
        Assert.IsTrue(references.Any(reference => reference.Name == "Lucent.Core"));
    }
}
