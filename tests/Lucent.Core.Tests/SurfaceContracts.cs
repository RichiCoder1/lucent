using Lucent.Core;
using Microsoft.Extensions.Time.Testing;

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
        var popupState = popup.Root.Scope.Signal(false, "popup-state");
        var mutationRevision = first.CaptureSharedMutationRevision();
        popupState.Value = true;
        Assert.AreNotEqual(
            mutationRevision,
            first.CaptureSharedMutationRevision(),
            "The surface host could not observe a mutation made through its shared popup graph."
        );
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

    [TestMethod]
    public void PopoverMeasuresAuthoredContentWithoutStretchingToTheAvailableWorkArea()
    {
        using var owner = new Composition(new ReactiveGraph(), "surface-compact-measure");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        owner.Mount(
            owner.Root,
            theme,
            Components.Popover(
                () => true,
                _ => { },
                Components.Layout([], style: Style.Empty.MinWidth(240)),
                [Components.Layout([], style: Style.Empty.Width(100).Height(32))]
            )
        );
        owner.Flush();
        var request = owner.Input.ActiveSurface!;
        Assert.ThrowsExactly<ArgumentException>(() =>
            request.ConfigureHostPadding(owner, Insets.Uniform(16))
        );

        var measured = request.Measure(new EmptyShaper(), new LayoutViewport(1_200, 800, 1));

        Assert.AreEqual(264f, measured.Width);
        Assert.AreNotEqual(1_200f, measured.Width);
    }

    [TestMethod]
    public void TooltipHoverSnapshotsPointerAnchorWhileKeyboardUsesElementBounds()
    {
        var clock = new FakeTimeProvider();
        using var owner = new Composition(new ReactiveGraph(), "tooltip-pointer-anchor");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        owner.Mount(
            owner.Root,
            theme,
            Components.Tooltip(
                "Wide target help",
                [Components.Button("Wide target", () => { })],
                delay: TimeSpan.FromMilliseconds(500),
                timeProvider: clock,
                style: Style.Empty.Width(300).Height(32)
            )
        );
        owner.Flush();
        using var scene = SceneLayout.Project(owner, new(400, 200, 1), new SurfaceShaper());
        Assert.IsTrue(owner.Input.SetScene(scene));
        var button = Flatten(owner.SemanticSnapshot()!)
            .Single(node => node is { Role: SemanticRole.Button, Name: "Wide target" });
        var bounds = scene
            .Boxes.Single(box => box.Identity.ElementId == button.Identity.ElementId)
            .Bounds;
        var anchorBounds = scene
            .Boxes.Single(box => box.Bounds.Width == 300 && box.Bounds.Height == 32)
            .Bounds;
        var pointerX = bounds.X + bounds.Width - 4;
        var pointerY = bounds.Y + bounds.Height / 2;

        owner.Input.DispatchPointer(new(PointerCommandKind.Move, 1, pointerX, pointerY));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        owner.Flush();
        var hover = owner.Input.ActiveSurface!;
        Assert.AreEqual(new LayoutRect(pointerX, pointerY, 0, 0), hover.Anchor);

        owner.Input.ClearPointerHover();
        hover.Dispose();
        owner.Input.CompleteSurface(hover);
        Assert.IsTrue(owner.Input.MoveFocus(FocusTraversalDirection.Next));
        var keyboard = owner.Input.ActiveSurface!;
        Assert.AreEqual(anchorBounds, keyboard.Anchor);
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }

    private sealed class SurfaceShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("surface-empty", 0, request.FontSize, []);
            var width = request.Text.Length * 8f;
            return new(
                "surface",
                width,
                request.FontSize,
                [
                    new ShapedRun(
                        "surface",
                        "surface",
                        400,
                        5,
                        0,
                        "surface",
                        0,
                        "surface#0",
                        request.Direction,
                        request.Language,
                        request.FontSize,
                        0,
                        request.FontSize,
                        -request.FontSize,
                        0,
                        width,
                        [new(1, 0, 0, 0, width, 0, 0)]
                    ),
                ]
            );
        }
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
