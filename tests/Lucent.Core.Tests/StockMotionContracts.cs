using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class StockMotionContracts
{
    [TestMethod]
    [DataRow(VariantState.Pressed)]
    [DataRow(VariantState.FocusVisible)]
    [DataRow(VariantState.Selected)]
    [DataRow(VariantState.Disabled)]
    [DataRow(VariantState.Invalid)]
    public void ActionableStockStatesCancelHoverOnTheirFirstCommittedFrame(VariantState state)
    {
        foreach (var role in Enum.GetValues<ButtonRole>())
            AssertFirstFrame(role, state);
    }

    private static void AssertFirstFrame(ButtonRole role, VariantState state)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stock-motion");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var button = composition.Mount(
            composition.Root,
            theme,
            Components.Button("Action", () => { }, Style.Empty.ButtonRole(role))
        );
        composition.Flush();
        composition.SamplePresentation(TimeSpan.Zero);
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(1);
        Assert.IsTrue(composition.TryAcknowledgePresentation(1));
        button.SetVariants(VariantState.Hover);
        composition.CommitPresentationTargets();
        Assert.AreEqual(
            role switch
            {
                ButtonRole.Primary => Color.Parse("#1d4ed8"),
                ButtonRole.Destructive => Color.Parse("#991b1b"),
                _ => Color.Parse("#f1f5f9"),
            },
            button.Resolve(VisualProperties.Background).Value.Color,
            $"{role} hover waited for a deferred reader or borrowed primary paint."
        );
        Assert.AreEqual(1, composition.PresentationDiagnostics.ActiveTracks);
        composition.SamplePresentation(TimeSpan.FromMilliseconds(30));
        button.SetVariants(VariantState.Hover | state);
        composition.CommitPresentationTargets();
        Assert.AreEqual(0, composition.PresentationDiagnostics.ActiveTracks);
        Assert.AreEqual(
            button.Resolve(VisualProperties.Background).Value,
            composition.ReadPresentedValue(button, VisualProperties.Background)
        );
        if (state == VariantState.FocusVisible)
            Assert.AreNotEqual(FocusRing.None, button.Resolve(VisualProperties.FocusRing).Value);
        if (state == VariantState.Pressed)
            Assert.AreEqual(
                role switch
                {
                    ButtonRole.Primary => Color.Parse("#1e40af"),
                    ButtonRole.Destructive => Color.Parse("#7f1d1d"),
                    _ => Color.Parse("#e2e8f0"),
                },
                button.Resolve(VisualProperties.Background).Value.Color,
                $"{role} pressed paint waited for a deferred reader or borrowed primary paint."
            );
    }
}
