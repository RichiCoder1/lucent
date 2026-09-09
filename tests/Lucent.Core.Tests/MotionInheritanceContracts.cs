using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class MotionInheritanceContracts
{
    [TestMethod]
    public void LocalInheritedPolicyStartsFromPriorSampleAndRetargetsIndependently()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "inherited-local-motion");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var black = Color.FromRgb(0, 0, 0);
        var white = Color.FromRgb(255, 255, 255);
        var color = graph.Signal(black, "color");
        var parent = composition.Child(composition.Root, "parent");
        parent.Present(
            theme,
            author: Style.Empty.Bind(TypographyProperties.TextColor, () => color.Value)
        );
        var child = composition.Child(parent, "child");
        child.Present(
            theme,
            author: Style.Empty.Transition(TypographyProperties.TextColor, Motion.Duration(100))
        );
        composition.SamplePresentation(TimeSpan.Zero);
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(1);
        Assert.IsTrue(composition.TryAcknowledgePresentation(1));
        color.Value = white;
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert.AreEqual(
            white,
            composition.ReadPresentedValue(parent, TypographyProperties.TextColor)
        );
        Assert.AreEqual(
            black,
            composition.ReadPresentedValue(child, TypographyProperties.TextColor)
        );
        composition.SamplePresentation(TimeSpan.FromMilliseconds(50));
        var middle = composition.ReadPresentedValue(child, TypographyProperties.TextColor);
        Assert.AreNotEqual(black, middle);
        Assert.AreNotEqual(white, middle);
        color.Value = black;
        graph.Drain();
        composition.CommitPresentationTargets();
        Assert.AreEqual(
            middle,
            composition.ReadPresentedValue(child, TypographyProperties.TextColor),
            "Retargeting a local inherited track must not reseed from its immediate parent."
        );
        composition.SamplePresentation(TimeSpan.FromMilliseconds(150));
        Assert.AreEqual(
            black,
            composition.ReadPresentedValue(child, TypographyProperties.TextColor)
        );
        Assert.IsFalse(composition.PresentationDemand.IsActive);
    }
}
