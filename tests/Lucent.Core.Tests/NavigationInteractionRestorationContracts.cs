using System.Text;
using Lucent.Core;

namespace Lucent.Core.Tests;

public sealed partial class NavigationInteractionContracts
{
    [TestMethod]
    public void JournalRestoresFreshActiveCaptureAndDormantInteractionStateOnForward()
    {
        using var source = new RestorationInteractionFixture();
        Completed(source.Session.Navigate(Location("/first")));
        source.Install();
        source.FirstViewport.Offset = new(0, 30);
        Completed(source.Session.Navigate(Location("/second")));
        source.Install();
        source.SecondViewport.Offset = new(0, 40);
        Completed(source.Session.Back());
        source.Install();
        Assert.AreEqual(new ScrollOffset(0, 30), source.FirstViewport.Offset);
        source.FirstViewport.Offset = new(0, 75);
        source.Install();
        var captured = source.Restoration.Capture(source.Session);
        Assert.AreEqual(NavigationRestorationStatus.Ready, captured.Status);
        using var destination = new RestorationInteractionFixture();
        var plan = destination.Restoration.Decode(captured.Utf8.Span);
        Assert.AreEqual(
            new ScrollOffset(0, 75),
            plan.Journal!.Entries[0].State!.Viewports.Single().Offset
        );
        Assert.AreEqual("action", plan.Journal.Entries[0].State!.FocusTargetId);
        Assert.AreEqual(
            new ScrollOffset(0, 40),
            plan.Journal.Entries[1].State!.Viewports.Single().Offset
        );
        Completed(destination.Session.Restore(plan));
        destination.Install();
        Assert.AreEqual("First restored action", FocusedName(destination.Composition));
        Assert.AreEqual(new ScrollOffset(0, 75), destination.FirstViewport.Offset);
        Assert.AreEqual(0, destination.Session.Journal.CurrentIndex);
        Completed(destination.Session.Forward());
        destination.Install();
        Assert.AreEqual("Second restored action", FocusedName(destination.Composition));
        Assert.AreEqual(new ScrollOffset(0, 40), destination.SecondViewport.Offset);
        Assert.AreEqual(1, destination.Session.Journal.CurrentIndex);
    }

    [TestMethod]
    public void RestoredMissingFocusFallsBackAndViewportClampsToLiveExtent()
    {
        using var fixture = new RestorationInteractionFixture();
        const string payload =
            """{"schema":"lucent.navigation","version":1,"scope":"interaction-v1","mode":"journal","activeKey":7,"entries":[{"key":7,"definition":"first","location":"/first","state":{"codec":"lucent.interaction","version":1,"focus":"removed-target","viewports":[{"target":"action","x":0,"y":9000}]}}]}""";
        Completed(
            fixture.Session.Restore(fixture.Restoration.Decode(Encoding.UTF8.GetBytes(payload)))
        );
        fixture.Install();
        Assert.AreEqual("First restored action", FocusedName(fixture.Composition));
        Assert.AreEqual(new ScrollOffset(0, 160), fixture.FirstViewport.Offset);
        var recaptured = fixture.Restoration.Decode(
            fixture.Restoration.Capture(fixture.Session).Utf8.Span
        );
        Assert.AreEqual("action", recaptured.Journal!.Entries[0].State!.FocusTargetId);
        Assert.AreEqual(
            new ScrollOffset(0, 160),
            recaptured.Journal.Entries[0].State!.Viewports.Single().Offset
        );
    }

    [TestMethod]
    public void DisposedInteractionReturnsAnInvalidCaptureBoundary()
    {
        using var fixture = new RestorationInteractionFixture();
        Completed(fixture.Session.Navigate(Location("/first")));
        fixture.Install();
        fixture.DisposeInteraction();
        var capture = fixture.Restoration.Capture(fixture.Session);
        Assert.AreEqual(NavigationRestorationStatus.InvalidState, capture.Status);
        Assert.IsTrue(capture.Utf8.IsEmpty);
    }

    [TestMethod]
    public void NewerNavigationPreventsRestoredFocusReconciliationFromTakingOver()
    {
        using var fixture = new RestorationInteractionFixture();
        const string payload =
            """{"schema":"lucent.navigation","version":1,"scope":"interaction-v1","mode":"journal","activeKey":1,"entries":[{"key":1,"definition":"first","location":"/first","state":{"codec":"lucent.interaction","version":1,"focus":"action","viewports":[]}}]}""";
        Completed(
            fixture.Session.Restore(fixture.Restoration.Decode(Encoding.UTF8.GetBytes(payload)))
        );
        Completed(fixture.Session.Navigate(Location("/second")));
        fixture.Install();
        Assert.AreEqual("second", fixture.Session.Current!.DefinitionId.Value);
        Assert.AreEqual("Second restored action", FocusedName(fixture.Composition));
    }

    private sealed class RestorationInteractionFixture : IDisposable
    {
        private readonly ReactiveGraph _graph = new();
        private readonly ReactiveScope _owner;
        private readonly ThemeContext _theme;
        private readonly NavigationInteraction _interaction;
        private readonly FocusTarget _firstFocus;
        private readonly FocusTarget _secondFocus;
        private RetainedScene? _scene;

        internal RestorationInteractionFixture()
        {
            _owner = _graph.CreateScope("restoration-interaction-owner");
            var table = Table("first", "second");
            Session = new NavigationSession(_owner, table);
            _interaction = new NavigationInteraction(_owner, Session);
            Restoration = new NavigationRestoration(
                table,
                "interaction-v1",
                Reference(table, "first"),
                _ => true,
                options: new(
                    NavigationRestorationMode.Journal,
                    stateCodecs: NavigationRestorationStateCodecs.Interaction
                )
            );
            Composition = new Composition(_graph, "restoration-interaction-composition");
            _theme = new ThemeContext(Composition.Root.Scope, ControlThemes.Light);
            _firstFocus = new FocusTarget(Composition.Root.Scope, "restored-first-focus");
            _secondFocus = new FocusTarget(Composition.Root.Scope, "restored-second-focus");
            FirstViewport = new ViewportState(
                Composition.Root.Scope,
                name: "restored-first-viewport"
            );
            SecondViewport = new ViewportState(
                Composition.Root.Scope,
                name: "restored-second-viewport"
            );
            var routes = Bundle(
                Descriptors(table),
                level =>
                {
                    var first = level.Id.Value == "first";
                    var focus = first ? _firstFocus : _secondFocus;
                    var viewport = first ? FirstViewport : SecondViewport;
                    var label = first ? "First restored action" : "Second restored action";
                    var content = ComponentRecipe.Create(
                        "restored-content",
                        (context, root) =>
                        {
                            root.Present(
                                context.Theme,
                                author: Style.Empty.Axis(LayoutAxis.Column).Width(160).Height(100)
                            );
                            context.Mount(root, Components.Button(label, focusTarget: focus));
                            context.Mount(
                                root,
                                Components.ScrollViewport(
                                    [
                                        ComponentRecipe.Create(
                                            "restored-scroll-content",
                                            (childContext, child) =>
                                                child.Present(
                                                    childContext.Theme,
                                                    author: Style.Empty.Width(80).Height(200)
                                                )
                                        ),
                                    ],
                                    style: Style.Empty.Width(100).Height(40),
                                    viewport: viewport
                                )
                            );
                        }
                    );
                    return Components.NavigationTarget(
                        [content],
                        _interaction,
                        "action",
                        label,
                        focus,
                        NavigationTargetKind.Heading,
                        viewport
                    );
                }
            );
            Composition.Mount(
                Composition.Root,
                _theme,
                Components.NavigationBoundary(
                    [
                        Components.Router(
                            [
                                Components.RouterOutlet(
                                    new RouteOutletOptions(interaction: _interaction)
                                ),
                            ],
                            routes,
                            session: Session
                        ),
                    ],
                    _interaction
                )
            );
            _graph.Drain();
        }

        internal NavigationSession Session { get; }
        internal NavigationRestoration Restoration { get; }
        internal Composition Composition { get; }
        internal ViewportState FirstViewport { get; }
        internal ViewportState SecondViewport { get; }

        internal void Install()
        {
            _scene?.Dispose();
            _scene = NavigationInteractionContracts.Install(Composition, _graph);
        }

        internal void DisposeInteraction() => _interaction.Dispose();

        public void Dispose()
        {
            _scene?.Dispose();
            Composition.Dispose();
            _theme.Dispose();
            _interaction.Dispose();
            Session.Dispose();
            _owner.Dispose();
        }
    }
}
