using Lucent.Core;
using Lucent.IssueBrowser;
using Lucent.Renderer.Skia;
using TestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Lucent.IssueBrowser.Tests;

public sealed partial class IssueBrowserTests
{
    [TestMethod]
    public void OpenedRouteIsIndependentFromListSelectionAndCollectionNavigation()
    {
        using var composition = LoadedComposition(out var graph, out var browser);
        using var renderer = new SkiaSceneRenderer();
        Install(composition, renderer, new(1120, 760, 1));
        var issues = browser.VisibleIssues.Take(2).ToArray();
        TestAssert.AreEqual(2, issues.Length);
        var first = IssueRow(composition, issues[0].Number, "initial");
        TestAssert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(first.Identity, new(SemanticCommandKind.Select))
        );
        graph.Drain();
        Install(composition, renderer, new(1120, 760, 1));
        var titleId = Elements(composition.Root)
            .Single(element => element.Name == "issue-browser.details-title")
            .Id;
        var openedTitle = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Identity.ElementId == titleId);
        TestAssert.AreEqual(issues[0].Title, openedTitle.Name);
        browser.Select(issues[1].Number);
        graph.Drain();
        Install(composition, renderer, new(1120, 760, 1));
        TestAssert.AreEqual(issues[1].Number, browser.SelectedIssue!.Number);
        TestAssert.IsTrue(
            Flatten(composition.SemanticSnapshot()!)
                .Any(node =>
                    node.Identity.ElementId == openedTitle.Identity.ElementId
                    && node.Name == issues[0].Title
                )
        );
        var collection = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Button && node.Name == "All issues");
        TestAssert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(collection.Identity, new(SemanticCommandKind.Invoke))
        );
        graph.Drain();
        Install(composition, renderer, new(1120, 760, 1));
        TestAssert.AreEqual(issues[1].Number, browser.SelectedIssue!.Number);
        TestAssert.IsFalse(
            composition.Dump().Contains("issue-browser.details-title", StringComparison.Ordinal)
        );
        TestAssert.IsTrue(
            Flatten(composition.SemanticSnapshot()!).Any(node => node.Name == "Open an issue")
        );
    }

    [TestMethod]
    public void RealHostedFixtureResolvesFeatureServicesAndClosesCleanly()
    {
        var host = new HostedBrowserProbe();
        TestAssert.AreEqual(
            0,
            LucentApplication
                .CreateBuilder()
                .UseHost(host)
                .Build()
                .Run(IssueBrowserStructure.CreateHosted())
        );
        TestAssert.IsTrue(host.SawApplication);
    }

    private sealed class HostedBrowserProbe : IApplicationHost
    {
        internal bool SawApplication { get; private set; }

        public int Run(ApplicationSession session)
        {
            session.Composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
            session.Start();
            Pump(
                session,
                () => session.Status.Phase == ApplicationPhase.Running || session.IsCompleted
            );
            TestAssert.IsFalse(session.IsCompleted);
            SawApplication = Flatten(session.Composition.SemanticSnapshot()!)
                .Any(node => node.Name == "All issues");
            session.RequestClose();
            Pump(session, () => session.IsCompleted);
            return 0;
        }

        private static void Pump(ApplicationSession session, Func<bool> done)
        {
            var deadline = Environment.TickCount64 + 10_000;
            while (!done())
            {
                session.ProcessEvents();
                if (!session.Composition.IsDisposed)
                    session.Composition.Flush();
                if (Environment.TickCount64 >= deadline)
                    throw new TimeoutException(
                        "Hosted Issue Browser did not complete its lifecycle."
                    );
                Thread.Sleep(1);
            }
        }
    }

    private static IEnumerable<Element> Elements(Element root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Elements(child))
            yield return descendant;
    }
}
