using Lucent.Core;
using Lucent.Performance.Fixture;
using Lucent.Renderer.Skia;

namespace Lucent.Performance.Tests;

[TestClass]
public sealed class FixedFixtureContracts
{
    [TestMethod]
    public void FixedNativeFixtureHasStableFocusAndVirtualListEndpoints()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "fixed-native-contract");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var root = composition.Mount(composition.Root, theme, FixedFixture.Create());
        var buttons = root.Children[0].Children.ToArray();
        var list = root.Children[1];
        Assert.AreEqual(4, buttons.Length);
        var router = composition.Input;
        using var renderer = new SkiaSceneRenderer();
        RetainedScene? accepted = null;
        try
        {
            RetainedScene Install()
            {
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    graph.Drain();
                    var candidate = SceneLayout.Project(
                        composition,
                        new(FixedFixture.WindowWidth, FixedFixture.WindowHeight, 1),
                        renderer
                    );
                    bool installed;
                    try
                    {
                        installed = router.SetScene(candidate);
                    }
                    catch
                    {
                        candidate.Dispose();
                        throw;
                    }
                    if (installed)
                    {
                        var previous = accepted;
                        accepted = candidate;
                        previous?.Dispose();
                        return candidate;
                    }
                    candidate.Dispose();
                }
                throw new InvalidOperationException("The fixed fixture did not settle.");
            }

            var initial = Install();
            Assert.AreEqual(
                60f,
                initial.Boxes.Single(box => box.Identity.ElementId == list.Id).Bounds.Height
            );
            Assert.AreEqual(30f, RowBox(initial, composition, "Benchmark row 1").Bounds.Height);
            Assert.AreEqual(30f, RowBox(initial, composition, "Benchmark row 2").Bounds.Height);
            var listIdentity = initial
                .Boxes.Single(box => box.Identity.ElementId == list.Id)
                .Identity;
            Assert.AreEqual(2940f, router.GetSemanticScroll(listIdentity)?.Maximum.Y);
            var firstNames = Descendants(composition.SemanticSnapshot()!)
                .Select(node => node.Name)
                .ToArray();
            Assert.AreEqual(
                "Benchmark action 1|Benchmark action 2|Benchmark action 3|Benchmark action 4",
                string.Join(
                    '|',
                    Descendants(composition.SemanticSnapshot()!)
                        .Where(node => node.Role == SemanticRole.Button)
                        .Select(node => node.Name)
                )
            );
            Assert.IsTrue(firstNames.Contains("Benchmark row 1"));
            Assert.IsTrue(firstNames.Contains("Benchmark row 2"));

            foreach (var button in buttons)
            {
                Assert.IsTrue(router.MoveFocus(FocusTraversalDirection.Next));
                Assert.AreEqual(button.Id, router.FocusedElement?.ElementId);
            }
            Assert.IsTrue(
                router.ScrollSemantic(
                    listIdentity,
                    new(SemanticCommandKind.Scroll, Endpoint: SemanticScrollEndpoint.End)
                )
            );
            var endpoint = Install();
            Assert.IsTrue(
                initial.IsDisposed,
                "Replacing the accepted scene must release its leases."
            );
            Assert.AreEqual(30f, RowBox(endpoint, composition, "Benchmark row 99").Bounds.Height);
            Assert.AreEqual(30f, RowBox(endpoint, composition, "Benchmark row 100").Bounds.Height);
            var endNames = Descendants(composition.SemanticSnapshot()!)
                .Select(node => node.Name)
                .ToArray();
            Assert.IsTrue(endNames.Contains("Benchmark row 99"));
            Assert.IsTrue(endNames.Contains("Benchmark row 100"));
            Assert.IsFalse(endNames.Contains("Benchmark row 1"));
        }
        finally
        {
            accepted?.Dispose();
        }
    }

    private static LayoutBox RowBox(RetainedScene scene, Composition composition, string label)
    {
        var row = Descendants(composition.SemanticSnapshot()!).Single(node => node.Name == label);
        return scene.Boxes.Single(box => box.Identity.ElementId == row.Identity.ElementId);
    }

    private static IEnumerable<SemanticSnapshot> Descendants(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}
