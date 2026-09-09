using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class SurfaceContracts
{
    [TestMethod]
    public void ModalQueueSkipsCanceledRequestsAndOwnerDisposalClosesRemainingRequests()
    {
        using var owner = new Composition(new ReactiveGraph(), "modal-queue");
        var first = new ModalProbe(owner);
        var canceled = new ModalProbe(owner);
        var last = new ModalProbe(owner);
        var published = new List<PopupSurfaceRequest>();
        owner.Input.SurfaceRequested += published.Add;
        owner.Input.RequestSurface(first);
        owner.Input.RequestSurface(canceled);
        owner.Input.RequestSurface(last);
        Assert.HasCount(1, published);
        canceled.Dismiss();
        first.Dismiss();
        owner.Input.CompleteSurface(first);
        Assert.AreSame(last, owner.Input.ActiveSurface);
        Assert.HasCount(2, published);
        Assert.IsTrue(canceled.Disposed);
        // A stale host completion must not release the new modal owner.
        owner.Input.CompleteSurface(first);
        Assert.AreSame(last, owner.Input.ActiveSurface);
        owner.Dispose();
        Assert.IsTrue(last.Disposed);
    }

    [TestMethod]
    public void PopoverRetainsOneSessionAndWaitsForControlledCloseAcknowledgement()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "surface-contract");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var open = owner.Root.Scope.Signal(false, "open");
        var requests = new List<bool>();
        var mounted = 0;
        owner.Mount(
            owner.Root,
            theme,
            Components.Popover(
                () => open.Value,
                requests.Add,
                ComponentRecipe.Defer(
                    "popup-content",
                    _ =>
                    {
                        mounted++;
                        return Components.Button("Inside", () => { });
                    }
                ),
                [Components.Button("Open", () => open.Value = true)]
            )
        );
        owner.Flush();
        Assert.IsNull(owner.Input.ActiveSurface);
        open.Value = true;
        owner.Flush();
        var first = owner.Input.ActiveSurface!;
        Assert.IsNotNull(first);
        Assert.IsTrue(first.ConsumeOutsideClick);
        Assert.IsTrue(first.IsInteractive);
        var popup = first.CreateComposition();
        Assert.AreSame(popup, first.CreateComposition());
        Assert.AreEqual(1, mounted);
        first.Dismiss();
        first.Dismiss();
        owner.Flush();
        Assert.AreEqual(1, requests.Count);
        Assert.IsFalse(requests[0]);
        Assert.IsNull(owner.Input.ActiveSurface);
        open.Value = false;
        owner.Flush();
        Assert.IsTrue(popup.IsDisposed);
        open.Value = true;
        owner.Flush();
        var second = owner.Input.ActiveSurface!;
        Assert.AreNotSame(first, second);
        owner.Dispose();
        Assert.IsTrue(second.IsDismissed);
    }

    [TestMethod]
    public void PopoverProjectsItsLiveAnchorAndAllowsExplicitDismissalPassthrough()
    {
        using var owner = new Composition(new ReactiveGraph(), "surface-anchor");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        owner.Mount(
            owner.Root,
            theme,
            Components.Popover(
                () => true,
                _ => { },
                Components.Text("Details"),
                [Components.Layout([], style: Style.Empty.Width(100).Height(32))],
                consumeOutsideClick: false,
                style: Style.Empty.Width(100).Height(32)
            )
        );
        owner.Flush();
        var request = owner.Input.ActiveSurface!;
        Assert.IsFalse(request.HasAnchor);
        using var scene = SceneLayout.Project(owner, new(400, 300, 1), new EmptyShaper());
        Assert.IsTrue(owner.Input.SetScene(scene));
        Assert.IsTrue(request.HasAnchor);
        Assert.AreEqual(100f, request.Anchor.Width);
        Assert.AreEqual(32f, request.Anchor.Height);
        Assert.IsFalse(request.ConsumeOutsideClick);
        Assert.AreEqual(8f, request.AnchorGap);
        var popup = request.CreateComposition();
        request.Dispose();
        request.Dispose();
        Assert.IsTrue(popup.IsDisposed);
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }

    private sealed class ModalProbe(Composition owner) : PopupSurfaceRequest
    {
        public bool Disposed { get; private set; }
        public override Composition Owner => owner;
        public override LayoutRect Anchor => new(0, 0, 100, 100);
        public override ThemeAppearance Appearance =>
            new(ThemeColorScheme.Light, ThemeContrast.Normal);
        public override bool IsValid => !owner.IsDisposed && !Disposed;
        public override bool IsDismissed => _dismissed || !IsValid;
        public override bool IsModal => true;
        private bool _dismissed;

        public override Composition CreateComposition() => throw new NotSupportedException();

        public override LayoutRect Measure(ITextShaper shaper, LayoutViewport available) => Anchor;

        public override void Dismiss() => _dismissed = true;

        public override bool RestoreFocus() => false;

        public override void Dispose() => Disposed = true;
    }
}
