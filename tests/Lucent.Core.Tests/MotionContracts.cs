using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class MotionContracts
{
    [TestMethod]
    public void MotionSpecsAndThemePreferencesAreBounded()
    {
        Assert(Motion.None.DurationMilliseconds == 0, "None was not immediate.");
        Assert(Motion.Quick == Motion.Duration(120, Easing.EaseOut), "Quick contract changed.");
        Expect<ArgumentOutOfRangeException>(() => Motion.Duration(-1));
        Expect<ArgumentOutOfRangeException>(() => Motion.Duration(60_001));
        Expect<ArgumentOutOfRangeException>(() => Motion.Duration(1, (Easing)99));

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "theme-motion");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        Assert(!theme.EffectiveReducedMotion, "Initial effective preference was reduced.");
        theme.ReducedMotion = true;
        theme.SetPlatformReducedMotion(false);
        Assert(theme.EffectiveReducedMotion, "Platform false overwrote application suppression.");
        theme.ReducedMotion = false;
        theme.SetPlatformReducedMotion(true);
        Assert(theme.EffectiveReducedMotion, "Platform suppression was ignored.");
        theme.SetPlatformReducedMotion(null);
        Assert(!theme.EffectiveReducedMotion, "Unknown platform preference suppressed motion.");
    }

    [TestMethod]
    public void AbsoluteSamplingSeparatesTargetsAndAcknowledgedPresentation()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "motion");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var opacity = composition.Root.Scope.Signal(0f, "opacity");
        var element = composition.Child(composition.Root, "element");
        element.Present(
            theme,
            author: Style
                .Empty.Bind(VisualProperties.Opacity, () => opacity.Value)
                .Transition(VisualProperties.Opacity, Motion.Duration(100))
        );
        graph.Drain();
        composition.SamplePresentation(TimeSpan.Zero);
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(1);
        Assert(composition.TryAcknowledgePresentation(1), "Initial frame was not acknowledged.");

        opacity.Value = 1f;
        graph.Drain();
        composition.SamplePresentation(TimeSpan.FromMilliseconds(10));
        composition.CommitPresentationTargets();
        Assert(
            element.Resolve(VisualProperties.Opacity).Value == 1f,
            "Target was delayed by motion."
        );
        Assert(
            composition.ReadPresentedValue(element, VisualProperties.Opacity) == 0f,
            "Transition did not start from the acknowledged presentation."
        );
        Assert(composition.PresentationDemand.IsActive, "Active track did not request a frame.");

        var half = composition.SamplePresentation(TimeSpan.FromMilliseconds(60));
        Assert(half.PixelsChanged, "Half sample reported no pixel change.");
        Assert(
            Math.Abs(composition.ReadPresentedValue(element, VisualProperties.Opacity) - .5f)
                < .0001f,
            "Linear half sample changed."
        );
        composition.SamplePresentation(TimeSpan.FromSeconds(5));
        Assert(
            composition.ReadPresentedValue(element, VisualProperties.Opacity) == 1f
                && !composition.PresentationDemand.IsActive,
            "Long frame gap did not finish exactly at target."
        );
        Expect<ArgumentOutOfRangeException>(() =>
            composition.SamplePresentation(TimeSpan.FromSeconds(4))
        );
    }

    [TestMethod]
    public void ColorInterpolationIsLinearLightPremultiplied()
    {
        var midpoint = MotionTimeline.InterpolateColor(
            Color.FromRgb(0, 0, 0),
            Color.FromRgb(255, 255, 255),
            .5
        );
        Assert(
            midpoint == Color.FromRgb(188, 188, 188),
            "Black/white midpoint was not linear-light."
        );
        var alpha = MotionTimeline.InterpolateColor(
            Color.FromArgb(0, 255, 0, 0),
            Color.FromArgb(255, 0, 0, 255),
            .5
        );
        Assert(
            alpha == Color.FromArgb(128, 0, 0, 255),
            "Transparent/opaque interpolation was not premultiplied."
        );
    }

    [TestMethod]
    public void UnavailableAndReducedMotionSnapAndDisarm()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "suppression");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var color = composition.Root.Scope.Signal(Color.FromRgb(0, 0, 0), "color");
        var element = composition.Child(composition.Root, "element");
        element.Present(
            theme,
            author: Style
                .Empty.Bind(TypographyProperties.TextColor, () => color.Value)
                .Transition(TypographyProperties.TextColor, Motion.Quick)
        );
        graph.Drain();
        composition.SamplePresentation(TimeSpan.Zero);
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(1);
        composition.TryAcknowledgePresentation(1);
        color.Value = Color.FromRgb(255, 255, 255);
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(composition.PresentationDemand.IsActive, "Color change did not start a track.");
        theme.ReducedMotion = true;
        composition.SamplePresentation(TimeSpan.FromMilliseconds(1));
        Assert(
            !composition.PresentationDemand.IsActive
                && composition.ReadPresentedValue(element, TypographyProperties.TextColor)
                    == color.Value,
            "Sample-time reduced motion did not snap and disarm."
        );
        theme.ReducedMotion = false;
        color.Value = Color.FromRgb(0, 0, 0);
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(
            composition.PresentationDemand.IsActive,
            "Later target change did not restart after suppression cleared."
        );
        composition.SetPresentationAvailable(false);
        Assert(
            !composition.PresentationDemand.IsActive
                && composition.ReadPresentedValue(element, TypographyProperties.TextColor)
                    == color.Value,
            "Unavailable presentation did not snap and disarm."
        );
        composition.Dispose();
        Assert(
            !composition.TryAcknowledgePresentation(1),
            "Disposed composition accepted a queued acknowledgement."
        );
    }

    [TestMethod]
    public void PolicyPrecedenceAndControlAuthorityCancelDeterministically()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "authority");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var opacity = composition.Root.Scope.Signal(0f, "opacity");
        var element = composition.Child(composition.Root, "element");
        element.Present(
            theme,
            component: Style.Empty.Transition(VisualProperties.Opacity, Motion.Duration(500)),
            author: Style
                .Empty.Bind(VisualProperties.Opacity, () => opacity.Value)
                .Transition(VisualProperties.Opacity, Motion.Duration(100))
                .When(
                    VariantState.Pressed,
                    Style.Empty.Transition(VisualProperties.Opacity, Motion.None)
                )
        );
        graph.Drain();
        composition.SamplePresentation(TimeSpan.Zero);
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(1);
        composition.TryAcknowledgePresentation(1);
        opacity.Value = 1f;
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(
            composition.PresentationDemand.IsActive,
            "Author policy did not win and start motion."
        );
        element.SetVariants(VariantState.Pressed);
        composition.CommitPresentationTargets();
        Assert(
            !composition.PresentationDemand.IsActive
                && composition.ReadPresentedValue(element, VisualProperties.Opacity) == 1f,
            "More-specific None policy did not cancel and snap."
        );

        element.SetVariants(VariantState.None);
        opacity.Value = 0f;
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(
            composition.PresentationDemand.IsActive,
            "Motion did not restart after a later target change."
        );
        element.UpdateControl(VisualProperties.Opacity, 0f);
        composition.CommitPresentationTargets();
        Assert(
            !composition.PresentationDemand.IsActive
                && composition.ReadPresentedValue(element, VisualProperties.Opacity) == 0f,
            "Equal-valued control authority did not cancel motion."
        );
    }

    [TestMethod]
    public void InvalidLaterEndpointDoesNotPublishEarlierTarget()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "transaction");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var background = composition.Root.Scope.Signal<Brush>(Color.FromRgb(0, 0, 0), "background");
        var opacity = composition.Root.Scope.Signal(0f, "opacity");
        var element = composition.Child(composition.Root, "element");
        element.Present(
            theme,
            author: Style
                .Empty.Bind(VisualProperties.Background, () => background.Value)
                .Transition(VisualProperties.Background, Motion.Quick)
                .Bind(VisualProperties.Opacity, () => opacity.Value)
                .Transition(VisualProperties.Opacity, Motion.Quick)
        );
        graph.Drain();
        composition.SamplePresentation(TimeSpan.Zero);
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(1);
        composition.TryAcknowledgePresentation(1);
        background.Value = Color.FromRgb(255, 255, 255);
        opacity.Value = 2f;
        graph.Drain();
        Expect<ArgumentOutOfRangeException>(() => composition.CommitPresentationTargets());
        Assert(
            composition
                .ReadPresentedValue(element, VisualProperties.Background)
                .Equals(Brush.Solid(Color.FromRgb(0, 0, 0)))
                && !composition.PresentationDemand.IsActive,
            "Failed batch partially published an earlier property."
        );
    }

    [TestMethod]
    public void CoalescingRetargetingAndCapturedPolicyAreStable()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "retarget");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var opacity = composition.Root.Scope.Signal(0f, "opacity");
        var element = composition.Child(composition.Root, "element");
        element.Present(
            theme,
            author: Style
                .Empty.Bind(VisualProperties.Opacity, () => opacity.Value)
                .Transition(VisualProperties.Opacity, Motion.Duration(100))
                .When(
                    VariantState.Hover,
                    Style.Empty.Transition(VisualProperties.Opacity, Motion.Duration(500))
                )
        );
        graph.Drain();
        AcknowledgeFirst(composition);
        opacity.Value = .5f;
        opacity.Value = 1f;
        graph.Drain();
        composition.CommitPresentationTargets();
        var started = composition.PresentationDiagnostics;
        Assert(
            started is { ActiveTracks: 1, Starts: 1 },
            "A to B to C did not coalesce into one start."
        );
        composition.CommitPresentationTargets();
        Assert(
            composition.PresentationDiagnostics.Starts == started.Starts,
            "Equal target restarted motion."
        );
        composition.SamplePresentation(TimeSpan.FromMilliseconds(50));
        Assert(
            Math.Abs(composition.ReadPresentedValue(element, VisualProperties.Opacity) - .5f)
                < .0001f,
            "Initial sample changed."
        );
        opacity.Value = 0f;
        graph.Drain();
        composition.CommitPresentationTargets();
        element.SetVariants(VariantState.Hover);
        composition.CommitPresentationTargets();
        composition.SamplePresentation(TimeSpan.FromMilliseconds(100));
        Assert(
            Math.Abs(composition.ReadPresentedValue(element, VisualProperties.Opacity) - .25f)
                < .0001f,
            "Retarget did not use the current sample and captured full original duration."
        );
        Assert(
            composition.PresentationDiagnostics.Retargets == 1,
            "Retarget counter changed more than once."
        );
    }

    [TestMethod]
    public void InheritedPresentationDelegatesAndNoneSnaps()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "inheritance");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var color = composition.Root.Scope.Signal(Color.FromRgb(0, 0, 0), "color");
        var parent = composition.Child(composition.Root, "parent");
        parent.Present(
            theme,
            author: Style
                .Empty.Bind(TypographyProperties.TextColor, () => color.Value)
                .Transition(TypographyProperties.TextColor, Motion.Duration(100))
        );
        var child = composition.Child(parent, "child");
        child.Present(theme);
        graph.Drain();
        AcknowledgeFirst(composition);
        color.Value = Color.FromRgb(255, 255, 255);
        graph.Drain();
        composition.CommitPresentationTargets();
        composition.SamplePresentation(TimeSpan.FromMilliseconds(50));
        Assert(
            composition.ReadPresentedValue(child, TypographyProperties.TextColor)
                == composition.ReadPresentedValue(parent, TypographyProperties.TextColor),
            "Inherited child did not join the ancestor sample."
        );
        Assert(
            composition.PresentationDiagnostics.ActiveTracks == 1,
            "Inherited delegation created a second track."
        );

        var immediate = composition.Child(parent, "immediate");
        immediate.Present(
            theme,
            author: Style.Empty.Transition(TypographyProperties.TextColor, Motion.None)
        );
        composition.CommitPresentationTargets();
        Assert(
            composition.ReadPresentedValue(immediate, TypographyProperties.TextColor)
                == color.Value,
            "Explicit None did not block inherited motion."
        );

        var policyChild = composition.Child(parent, "policy-child");
        policyChild.Present(
            theme,
            author: Style.Empty.When(
                VariantState.Hover,
                Style.Empty.Transition(TypographyProperties.TextColor, Motion.Duration(100))
            )
        );
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(2);
        composition.TryAcknowledgePresentation(2);
        policyChild.SetVariants(VariantState.Hover);
        composition.CommitPresentationTargets();
        Assert(
            composition.ReadPresentedValue(policyChild, TypographyProperties.TextColor)
                == composition.ReadPresentedValue(parent, TypographyProperties.TextColor)
                && composition.PresentationDiagnostics.ActiveTracks == 1,
            "Adding a local policy alone stopped inherited delegation."
        );
        color.Value = Color.FromRgb(0, 0, 0);
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(
            composition.PresentationDiagnostics.ActiveTracks == 2,
            "Inherited target change did not start the local policy track."
        );
        policyChild.SetVariants(VariantState.None);
        composition.CommitPresentationTargets();
        Assert(
            composition.PresentationDiagnostics.ActiveTracks == 1
                && composition.ReadPresentedValue(policyChild, TypographyProperties.TextColor)
                    == composition.ReadPresentedValue(parent, TypographyProperties.TextColor),
            "Removing a local inherited policy did not resume delegation."
        );
    }

    [TestMethod]
    public void AcknowledgementVisibilityAndLifetimePreventEntryMotion()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "lifetime");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var opacity = composition.Root.Scope.Signal(0f, "opacity");
        var participation = composition.Root.Scope.Signal(
            ElementParticipation.Visible,
            "participation"
        );
        var parent = composition.Child(composition.Root, "parent");
        parent.Present(
            theme,
            author: Style.Empty.Bind(VisualProperties.Participation, () => participation.Value)
        );
        var child = composition.Child(parent, "child");
        child.Present(
            theme,
            author: Style
                .Empty.Bind(VisualProperties.Opacity, () => opacity.Value)
                .Transition(VisualProperties.Opacity, Motion.Quick)
        );
        graph.Drain();
        composition.SamplePresentation(TimeSpan.Zero);
        composition.CommitPresentationTargets();
        opacity.Value = 1f;
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(
            !composition.PresentationDemand.IsActive
                && composition.ReadPresentedValue(child, VisualProperties.Opacity) == 1f,
            "Unacknowledged first presentation animated."
        );
        composition.CapturePresentationFrame(1);
        opacity.Value = .5f;
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(
            !composition.TryAcknowledgePresentation(1),
            "A mutated captured presentation was acknowledged."
        );
        composition.CapturePresentationFrame(2);
        Assert(
            composition.TryAcknowledgePresentation(2) && !composition.TryAcknowledgePresentation(1),
            "Current or stale acknowledgement contract changed."
        );

        opacity.Value = 0f;
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(composition.PresentationDemand.IsActive, "Acknowledged owner did not start motion.");
        participation.Value = ElementParticipation.Hidden;
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(
            !composition.PresentationDemand.IsActive,
            "Hidden ancestor retained descendant demand."
        );
        child.Dispose();
        Assert(
            composition.PresentationDiagnostics.ActiveTracks == 0,
            "Disposed owner retained a track."
        );
    }

    [TestMethod]
    public void UnsupportedPropertiesAndBrushPairsStayDiscrete()
    {
        Expect<ArgumentException>(() =>
            Style.Empty.Transition(
                new Property<float>("custom", 0, transition: TransitionKind.Opacity),
                Motion.Quick
            )
        );
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "brush-pair");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var brush = composition.Root.Scope.Signal<Brush>(Color.FromRgb(0, 0, 0), "brush");
        var element = composition.Child(composition.Root, "element");
        element.Present(
            theme,
            author: Style
                .Empty.Bind(VisualProperties.Background, () => brush.Value)
                .Transition(VisualProperties.Background, Motion.Quick)
        );
        graph.Drain();
        AcknowledgeFirst(composition);
        brush.Value = new LinearGradient(
            new(0, 0),
            new(1, 1),
            [new(0, Color.FromRgb(0, 0, 0)), new(1, Color.FromRgb(255, 255, 255))]
        );
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(
            !composition.PresentationDemand.IsActive
                && composition
                    .ReadPresentedValue(element, VisualProperties.Background)
                    .Equals(brush.Value),
            "Incompatible brush pair did not snap."
        );
        Assert(
            composition
                .PresentationDump()
                .Contains("reason=pair-ineligible", StringComparison.Ordinal),
            "Discrete brush reason was not observable."
        );
    }

    [TestMethod]
    public void AppearanceSuppressionSurvivesUntilTargetCommit()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "appearance-suppression");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("light"));
        var opacity = composition.Root.Scope.Signal(0f, "opacity");
        var element = composition.Child(composition.Root, "element");
        element.Present(
            theme,
            author: Style
                .Empty.Bind(VisualProperties.Opacity, () => opacity.Value)
                .Transition(VisualProperties.Opacity, Motion.Duration(100))
        );
        graph.Drain();
        AcknowledgeFirst(composition);
        opacity.Value = 1f;
        graph.Drain();
        composition.CommitPresentationTargets();
        composition.SamplePresentation(TimeSpan.FromMilliseconds(25));
        theme.Theme = new Theme("dark");
        composition.SamplePresentation(TimeSpan.FromMilliseconds(30));
        Assert(
            !composition.PresentationDemand.IsActive
                && composition.ReadPresentedValue(element, VisualProperties.Opacity) == 1f,
            "Equal-target appearance change did not cancel and snap."
        );

        opacity.Value = 0f;
        graph.Drain();
        composition.CommitPresentationTargets();
        composition.SamplePresentation(TimeSpan.FromMilliseconds(40));
        opacity.Value = .75f;
        theme.Theme = new Theme("contrast");
        graph.Drain();
        composition.SamplePresentation(TimeSpan.FromMilliseconds(50));
        composition.CommitPresentationTargets();
        Assert(
            !composition.PresentationDemand.IsActive
                && composition.ReadPresentedValue(element, VisualProperties.Opacity) == .75f,
            "Appearance suppression was consumed before the new palette target committed."
        );
    }

    [TestMethod]
    public void DemandObserverFailureCancelsPublishedWork()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "observer-failure");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var opacity = composition.Root.Scope.Signal(0f, "opacity");
        var element = composition.Child(composition.Root, "element");
        element.Present(
            theme,
            author: Style
                .Empty.Bind(VisualProperties.Opacity, () => opacity.Value)
                .Transition(VisualProperties.Opacity, Motion.Quick)
        );
        graph.Drain();
        AcknowledgeFirst(composition);
        composition.PresentationDemandAvailable += () =>
            throw new InvalidOperationException("observer");
        opacity.Value = 1f;
        graph.Drain();
        Expect<InvalidOperationException>(() => composition.CommitPresentationTargets());
        Assert(
            !composition.PresentationDemand.IsActive
                && composition.PresentationDiagnostics.ActiveTracks == 0
                && composition
                    .PresentationDump()
                    .Contains("reason=fatal-failure", StringComparison.Ordinal),
            "Unexpected demand observer failure left presentation work armed."
        );
    }

    [TestMethod]
    public void InheritedPolicyRequiresItsOwnAcknowledgedFrameAndNoneCanResumeDelegation()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "inherited-ack");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("theme"));
        var color = composition.Root.Scope.Signal(Color.FromRgb(0, 0, 0), "color");
        var parent = composition.Child(composition.Root, "parent");
        parent.Present(
            theme,
            author: Style
                .Empty.Bind(TypographyProperties.TextColor, () => color.Value)
                .Transition(TypographyProperties.TextColor, Motion.Duration(100))
        );
        graph.Drain();
        AcknowledgeFirst(composition);

        var late = composition.Child(parent, "late");
        late.Present(
            theme,
            author: Style.Empty.Transition(TypographyProperties.TextColor, Motion.Duration(200))
        );
        composition.CommitPresentationTargets();
        color.Value = Color.FromRgb(255, 255, 255);
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert(
            composition.PresentationDiagnostics.ActiveTracks == 1
                && composition.ReadPresentedValue(late, TypographyProperties.TextColor)
                    == color.Value,
            "An unacknowledged inherited child inferred the ancestor's baseline."
        );

        composition.SamplePresentation(TimeSpan.FromMilliseconds(50));
        composition.CapturePresentationFrame(2);
        composition.TryAcknowledgePresentation(2);
        var switching = composition.Child(parent, "switching");
        switching.Present(
            theme,
            author: Style
                .Empty.Transition(TypographyProperties.TextColor, Motion.Duration(200))
                .When(
                    VariantState.Pressed,
                    Style.Empty.Transition(TypographyProperties.TextColor, Motion.None)
                )
        );
        switching.SetVariants(VariantState.Pressed);
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(3);
        composition.TryAcknowledgePresentation(3);
        color.Value = Color.FromRgb(0, 0, 0);
        graph.Drain();
        composition.CommitPresentationTargets();
        composition.SamplePresentation(TimeSpan.FromMilliseconds(100));
        Assert(
            composition.ReadPresentedValue(switching, TypographyProperties.TextColor)
                == color.Value,
            "Explicit None did not remain pinned to the inherited target."
        );
        switching.SetVariants(VariantState.None);
        composition.CommitPresentationTargets();
        Assert(
            composition.ReadPresentedValue(switching, TypographyProperties.TextColor)
                == composition.ReadPresentedValue(parent, TypographyProperties.TextColor),
            "None to non-None policy change did not resume ancestor delegation."
        );
    }

    private static void AcknowledgeFirst(Composition composition)
    {
        composition.SamplePresentation(TimeSpan.Zero);
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(1);
        Assert(
            composition.TryAcknowledgePresentation(1),
            "First presentation acknowledgement failed."
        );
    }

    private static void Expect<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
