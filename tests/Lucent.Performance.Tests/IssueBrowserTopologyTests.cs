using System.Diagnostics;
using Lucent.Core;
using Lucent.IssueBrowser;
using Lucent.Renderer.Skia;

namespace Lucent.Performance.Tests;

[TestClass]
public sealed class IssueBrowserTopologyTests
{
    [TestMethod]
    public void AuthoredRowsHaveThreeSemanticNodesAcrossWideAndCompactStates()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "issue-browser-topology");
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        _ = composition.Mount(composition.Root, theme, IssueBrowserStructure.Create());
        using var renderer = new SkiaSceneRenderer();
        RetainedScene? retained = null;
        try
        {
            var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
            do
            {
                graph.Drain(10_000);
                Replace(new(1120, 762, 1));
                if (Snapshot().Any(node => node.Role == SemanticRole.ListItem))
                    break;
                Thread.Sleep(1);
            } while (Stopwatch.GetTimestamp() < until);
            Assert.IsTrue(
                Snapshot().Any(node => node.Role == SemanticRole.ListItem),
                "Fixture rows did not load."
            );
            Record("wide-list", 56, 12, 20);
            var first = Snapshot().First(node => node.Role == SemanticRole.ListItem);
            Assert.AreEqual(
                SemanticCommandResult.Applied,
                composition.ExecuteSemanticCommand(first.Identity, new(SemanticCommandKind.Select))
            );
            graph.Drain(10_000);
            Replace(new(1120, 762, 1));
            Record("wide-details", 60, 12, 24);
            Replace(new(760, 762, 1));
            Record("compact-details", 21, 0, 21);
            var back = Snapshot()
                .Single(node => node.Role == SemanticRole.Button && node.Name == "Back");
            Assert.AreEqual(
                SemanticCommandResult.Applied,
                composition.ExecuteSemanticCommand(back.Identity, new(SemanticCommandKind.Invoke))
            );
            graph.Drain(10_000);
            Replace(new(760, 762, 1));
            Record("compact-list", 46, 10, 16);
        }
        finally
        {
            retained?.Dispose();
        }

        void Replace(LayoutViewport viewport)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var next = SceneLayout.Project(composition, viewport, renderer, 10_000);
                if (composition.Input.SetScene(next))
                {
                    var previous = retained;
                    retained = next;
                    previous?.Dispose();
                    return;
                }
                next.Dispose();
            }
            throw new InvalidOperationException("Topology scene did not settle.");
        }

        SemanticSnapshot[] Snapshot() => Flatten(composition.SemanticSnapshot()!).ToArray();

        void Record(string state, int expectedTotal, int expectedRows, int expectedStatic)
        {
            var snapshot = Snapshot();
            var rows = snapshot.Where(node => node.Role == SemanticRole.ListItem).ToArray();
            Assert.IsTrue(
                rows.All(row => Flatten(row).Count() == 3),
                $"{state} row subtree no longer has one item and two text nodes."
            );
            Assert.AreEqual(
                expectedTotal,
                snapshot.Length,
                $"{state} authored semantic topology changed."
            );
            Assert.AreEqual(expectedRows, rows.Length, $"{state} realized row count changed.");
            Assert.AreEqual(
                expectedStatic,
                snapshot.Length - rows.Length * 3,
                $"{state} static semantic topology changed."
            );
            Console.WriteLine(
                $"topology {state}: total={snapshot.Length} rows={rows.Length} static={snapshot.Length - rows.Length * 3}"
            );
        }
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }
}
