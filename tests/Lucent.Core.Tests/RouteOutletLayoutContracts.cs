using Lucent.Core;

namespace Lucent.Core.Tests;

public sealed partial class RouteOutletContracts
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TransparentOutletsConstrainFlexibleRouteBodyAndKeepFooterInViewport(bool nested)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "route-outlet-constrained-layout");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var fixture = Fixture.Create();
        using var session = new NavigationSession(composition.Root.Scope, fixture.Table);
        using var handle = new RouteOutletHandle();
        var flexible = Style.Empty.MainGrow(1).MainShrink(1).MinWidth(0).MinHeight(0);
        var page = Components.Column(
            [
                Components.Column([], Style.Empty.Height(354)).Named("route-header"),
                Components.Column([], flexible.Height(160)).Named("route-body"),
                Components.Column([], Style.Empty.Height(38)).Named("route-footer"),
            ],
            flexible
        );
        var bundle = Bundle(
            fixture.Descriptors,
            level =>
                nested && level.Id.Value == "project"
                    ? Components.Column([Components.RouterOutlet()], flexible)
                    : page
        );
        composition.Root.Present(theme, author: Style.Empty.Axis(LayoutAxis.Column));
        composition.Mount(composition.Root, theme, Router(session, bundle, handle));
        Assert.AreEqual(
            NavigationOutcomeKind.Committed,
            Completed(
                session.Navigate(Location(nested ? "/projects/7/issues/42" : "/projects/7"))
            ).Kind
        );

        // The authored content prefers 552 pixels. Only the flexible body may shrink
        // when its transparent route hosts receive a 520-pixel viewport.
        using (var scene = SceneLayout.Project(composition, new(480, 520, 1), new EmptyShaper()))
        {
            AssertBounds(scene, "route-body", new(0, 354, 480, 128));
            AssertBounds(scene, "route-footer", new(0, 482, 480, 38));
            foreach (var level in handle.Snapshot.Levels)
                Assert.AreEqual(
                    new LayoutRect(0, 0, 480, 520),
                    scene.Boxes.Single(box => box.Identity.ElementId == level.ElementId).Bounds
                );
        }

        using (var scene = SceneLayout.Project(composition, new(720, 800, 1), new EmptyShaper()))
        {
            AssertBounds(scene, "route-body", new(0, 354, 720, 408));
            AssertBounds(scene, "route-footer", new(0, 762, 720, 38));
        }

        void AssertBounds(RetainedScene scene, string name, LayoutRect expected)
        {
            var element = composition.Elements().Single(element => element.Name == name);
            Assert.AreEqual(
                expected,
                scene.Boxes.Single(box => box.Identity.ElementId == element.Id).Bounds
            );
        }
    }
}
