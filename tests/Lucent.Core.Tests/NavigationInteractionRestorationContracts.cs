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
    [DataRow("9000", 1f)]
    [DataRow("3e38", 2f)]
    public void RestoredMissingFocusFallsBackAndViewportClampsToLiveExtent(
        string offset,
        float scale
    )
    {
        using var fixture = new RestorationInteractionFixture();
        const string payload =
            """{"schema":"lucent.navigation","version":1,"scope":"interaction-v1","mode":"journal","activeKey":7,"entries":[{"key":7,"definition":"first","location":"/first","state":{"codec":"lucent.interaction","version":1,"focus":"removed-target","viewports":[{"target":"action","x":0,"y":9000}]}}]}""";
        Completed(
            fixture.Session.Restore(
                fixture.Restoration.Decode(
                    Encoding.UTF8.GetBytes(
                        payload.Replace("9000", offset, StringComparison.Ordinal)
                    )
                )
            )
        );
        fixture.Install(scale);
        Assert.AreEqual("first", fixture.Session.Current!.DefinitionId.Value);
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DisposedInteractionExpiresOnlyItsOwnPendingFocusRequest(
        bool applicationReplacesRequest
    )
    {
        using var fixture = new RestorationInteractionFixture();
        fixture.RestoreFirst(90);
        Assert.IsTrue(fixture.FirstFocus.IsPending);
        if (applicationReplacesRequest)
            fixture.FirstFocus.Request();
        fixture.DisposeInteraction();
        Assert.AreEqual(applicationReplacesRequest, fixture.FirstFocus.IsPending);
        fixture.Install();
        Assert.AreEqual(
            applicationReplacesRequest ? "First restored action" : null,
            FocusedName(fixture.Composition)
        );
        Assert.AreEqual(default(ScrollOffset), fixture.FirstViewport.Offset);
    }

    [TestMethod]
    public void SupersededRestorationCannotFocusOrScrollAnApplicationOwnedTargetWhenItRemounts()
    {
        using var fixture = new RestorationInteractionFixture();
        fixture.RestoreFirst(90);
        Completed(fixture.Session.Navigate(Location("/second")));
        fixture.Install();
        Assert.AreEqual("Second restored action", FocusedName(fixture.Composition));
        fixture.RemountFirstTarget();
        fixture.Install();
        Assert.AreEqual("Second restored action", FocusedName(fixture.Composition));
        Assert.AreEqual(default(ScrollOffset), fixture.FirstViewport.Offset);
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(17f)]
    public void ApplicationViewportWriteSupersedesPendingRestoration(float offset)
    {
        using var fixture = new RestorationInteractionFixture();
        fixture.RestoreFirst(90);
        fixture.FirstViewport.Offset = new(0, offset);
        fixture.Install();
        Assert.AreEqual(new ScrollOffset(0, offset), fixture.FirstViewport.Offset);
    }

    [TestMethod]
    public void CaptureBeforeSceneInstallationReadsActualViewportRatherThanPendingRestoration()
    {
        using var fixture = new RestorationInteractionFixture();
        fixture.RestoreFirst(90);
        var capture = fixture.Restoration.Decode(
            fixture.Restoration.Capture(fixture.Session).Utf8.Span
        );
        Assert.AreEqual(
            default(ScrollOffset),
            capture.Journal!.Entries[0].State!.Viewports.Single().Offset
        );
        fixture.Install();
        Assert.AreEqual(new ScrollOffset(0, 90), fixture.FirstViewport.Offset);
    }

    [TestMethod]
    public void DormantImportedOffsetClampsBeforeProjectionWhenForwardRetainsTheRoute()
    {
        using var fixture = new RestorationInteractionFixture();
        const string payload =
            """{"schema":"lucent.navigation","version":1,"scope":"interaction-v1","mode":"journal","activeKey":1,"entries":[{"key":1,"definition":"first","location":"/first"},{"key":2,"definition":"first","location":"/first","state":{"codec":"lucent.interaction","version":1,"focus":"action","viewports":[{"target":"action","x":0,"y":3e38}]}}]}""";
        Completed(
            fixture.Session.Restore(fixture.Restoration.Decode(Encoding.UTF8.GetBytes(payload)))
        );
        fixture.Install(2);
        var firstTarget = fixture.Composition.Input.FocusTargetIdentity(fixture.FirstFocus);
        Completed(fixture.Session.Forward());
        fixture.Install(2);
        Assert.AreEqual(
            firstTarget,
            fixture.Composition.Input.FocusTargetIdentity(fixture.FirstFocus)
        );
        Assert.AreEqual(1, fixture.Session.Journal.CurrentIndex);
        Assert.AreEqual(new ScrollOffset(0, 160), fixture.FirstViewport.Offset);
    }

    [TestMethod]
    public void ReconciliationPreservesNewerApplicationSelectAllRequest()
    {
        using var fixture = new RestorationInteractionFixture(textTarget: true);
        fixture.Session.RegisterCommitted(
            fixture.Composition.Root.Scope,
            _ => fixture.FirstFocus.Request(selectAll: true)
        );
        fixture.RestoreFirst(0);
        fixture.Install();
        Assert.AreEqual("First restored action", FocusedName(fixture.Composition));
        Assert.AreEqual("Restored text", fixture.FirstEditor!.SelectedText);
    }

    [TestMethod]
    public void ReconciliationPreservesNewerApplicationFocusOutsideTheRoute()
    {
        using var fixture = new RestorationInteractionFixture();
        var (focus, editor) = fixture.MountApplicationEditor();
        fixture.Session.RegisterCommitted(
            fixture.Composition.Root.Scope,
            _ => focus.Request(selectAll: true)
        );
        fixture.RestoreFirst(0);
        fixture.Install();
        Assert.AreEqual("Application editor", FocusedName(fixture.Composition));
        Assert.AreEqual("Application text", editor.SelectedText);
    }

    [TestMethod]
    public void RetiringRouteCannotCancelRestorationOwnedByNewTargetSharingItsViewport()
    {
        using var fixture = new RestorationInteractionFixture(sharedViewport: true);
        const string payload =
            """{"schema":"lucent.navigation","version":1,"scope":"interaction-v1","mode":"journal","activeKey":1,"entries":[{"key":1,"definition":"first","location":"/first"},{"key":2,"definition":"second","location":"/second","state":{"codec":"lucent.interaction","version":1,"focus":"action","viewports":[{"target":"action","x":0,"y":90}]}}]}""";
        Completed(
            fixture.Session.Restore(fixture.Restoration.Decode(Encoding.UTF8.GetBytes(payload)))
        );
        fixture.Install();
        Assert.AreSame(fixture.FirstViewport, fixture.SecondViewport);
        Completed(fixture.Session.Forward());
        fixture.Install();
        Assert.AreEqual("Second restored action", FocusedName(fixture.Composition));
        Assert.AreEqual(1, fixture.Session.Journal.CurrentIndex);
        Assert.AreEqual(new ScrollOffset(0, 90), fixture.SecondViewport.Offset);
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

        internal RestorationInteractionFixture(bool sharedViewport = false, bool textTarget = false)
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
            SecondViewport = sharedViewport
                ? FirstViewport
                : new ViewportState(Composition.Root.Scope, name: "restored-second-viewport");
            FirstEditor = textTarget
                ? new EditorSession(
                    Composition.Root.Scope,
                    "restored-first-editor",
                    "Restored text"
                )
                : null;
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
                            context.Mount(
                                root,
                                first && FirstEditor is { } editor
                                    ? Components.TextField(
                                        session: editor,
                                        label: label,
                                        focusTarget: focus
                                    )
                                    : Components.Button(label, focusTarget: focus)
                            );
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
        internal FocusTarget FirstFocus => _firstFocus;
        internal EditorSession? FirstEditor { get; }

        internal (FocusTarget, EditorSession) MountApplicationEditor()
        {
            var focus = new FocusTarget(Composition.Root.Scope, "application-editor-focus");
            var editor = new EditorSession(
                Composition.Root.Scope,
                "application-editor",
                "Application text"
            );
            Composition.Mount(
                Composition.Root,
                _theme,
                Components.TextField(
                    session: editor,
                    label: "Application editor",
                    focusTarget: focus
                )
            );
            return (focus, editor);
        }

        internal void Install(float scale = 1)
        {
            _scene?.Dispose();
            _scene = NavigationInteractionContracts.Install(Composition, _graph, scale);
        }

        internal void RestoreFirst(float offset)
        {
            var payload =
                """{"schema":"lucent.navigation","version":1,"scope":"interaction-v1","mode":"journal","activeKey":1,"entries":[{"key":1,"definition":"first","location":"/first","state":{"codec":"lucent.interaction","version":1,"focus":"action","viewports":[{"target":"action","x":0,"y":90}]}}]}""";
            Completed(
                Session.Restore(
                    Restoration.Decode(
                        Encoding.UTF8.GetBytes(
                            payload.Replace(
                                "90",
                                offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                StringComparison.Ordinal
                            )
                        )
                    )
                )
            );
        }

        internal void RemountFirstTarget() =>
            Composition.Mount(
                Composition.Root,
                _theme,
                Components.Column([
                    Components.Button("Remounted first target", focusTarget: _firstFocus),
                    Components.ScrollViewport(
                        [Components.Column([], Style.Empty.Height(200))],
                        style: Style.Empty.Width(100).Height(40),
                        viewport: FirstViewport
                    ),
                ])
            );

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
