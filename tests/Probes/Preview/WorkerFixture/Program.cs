using System.Globalization;
using Lucent.Core;
using Lucent.Preview;
using Lucent.Preview.Hosting;
using PreviewFixtures;

// Explicit development bootstrap. No production Main or reflection discovery runs.
var catalog = new PreviewCatalogBuilder();
foreach (
    var id in new[] { "card/empty", "card/fractional", "card/setup-wait", "card/cleanup-fail" }
)
{
    var descriptor = new PreviewScenarioDescriptor(
        id,
        "Worker fixture",
        new PreviewSource(
            "Lucent.Preview.Fixtures.csproj",
            "ScenarioCard.lui",
            "PreviewFixtures.Components.ScenarioCard"
        ),
        new PreviewPresentation(
            id == "card/fractional"
                ? new LayoutViewport(160, 120, 1.1f)
                : new LayoutViewport(320, 240, 1),
            ThemeAppearance.Light,
            static _ => ControlThemes.Light,
            1,
            CultureInfo.InvariantCulture,
            CultureInfo.InvariantCulture,
            DateTimeOffset.UnixEpoch
        )
    );
    catalog.Add(
        descriptor,
        async (context, token) =>
        {
            if (id == "card/cleanup-fail")
                context.OnDispose(() =>
                    throw new InvalidOperationException("fixture-cleanup-failure")
                );
            if (id == "card/setup-wait")
            {
                context.OnDispose(() =>
                {
                    Console.Error.WriteLine("Fixture setup disposed.");
                    return ValueTask.CompletedTask;
                });
                Console.Error.WriteLine("Fixture setup waiting.");
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return new ScenarioContent("Empty", "Compiled worker fixture.");
        },
        (content, _) => PreviewFixtures.Components.ScenarioCard(content)
    );
}
return await PreviewWorker.RunAsync(catalog.Build(), args);
