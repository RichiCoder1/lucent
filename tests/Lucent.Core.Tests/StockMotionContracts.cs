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
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stock-motion");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var button = composition.Mount(
            composition.Root,
            theme,
            Components.Button("Action", () => { })
        );
        composition.SamplePresentation(TimeSpan.Zero);
        composition.CommitPresentationTargets();
        composition.CapturePresentationFrame(1);
        Assert.IsTrue(composition.TryAcknowledgePresentation(1));
        button.SetVariants(VariantState.Hover);
        composition.CommitPresentationTargets();
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
    }
}
