using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class GaugeContracts
{
    [TestMethod]
    public void GaugeKeepsAuthoritativeReadOnlyRangeAndAuthoredMetadataOnItsRoot()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "gauge");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var value = composition.Root.Scope.Signal<double?>(25, "value");
        var root = composition.Mount(
            composition.Root,
            theme,
            Components
                .Gauge("Capacity", () => value.Value, new(unit: "%"))
                .Style(Style.Empty.Width(120))
                .Aria.Name("Disk capacity")
                .Description("Read only")
                .End
        );
        graph.Drain();
        var before = Find(composition);
        Assert.AreEqual(root.Id, before.Identity.ElementId);
        Assert.AreEqual(1, composition.Root.Children.Count);
        Assert.AreEqual(1, root.Children.Count);
        Assert.AreEqual(120f, root.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(96f, root.Resolve(LayoutProperties.Height).Value);
        Assert.AreEqual("Disk capacity", before.Name);
        Assert.AreEqual("Read only", before.Description);
        Assert.AreEqual("25 %", before.Value);
        Assert.IsTrue(before.Range!.IsReadOnly);
        Assert.AreEqual(25d, before.Range.Value);
        Assert.AreEqual(SemanticAction.None, before.Actions);
        Assert.AreEqual(
            SemanticCommandResult.Rejected,
            composition.ExecuteSemanticCommand(
                before.Identity,
                new(SemanticCommandKind.SetRangeValue, NumericValue: 50)
            )
        );
        value.Value = 100;
        graph.Drain();
        var after = Find(composition);
        Assert.AreEqual(root.Id, after.Identity.ElementId);
        Assert.AreEqual("Disk capacity", after.Name);
        Assert.AreEqual(100d, after.Range!.Value);
    }

    [TestMethod]
    public void EmptyAndInvalidValuesKeepTheirBoxAndNeverPublishInvalidRanges()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "gauge-invalid");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var value = composition.Root.Scope.Signal<double?>(null, "value");
        var root = composition.Mount(
            composition.Root,
            theme,
            Components.Gauge("Reading", () => value.Value, new(-10, 10, "V"))
        );
        foreach (
            var input in new double?[]
            {
                null,
                double.NaN,
                double.PositiveInfinity,
                -11,
                11,
                -10,
                0,
                10,
            }
        )
        {
            value.Value = input;
            graph.Drain();
            var semantic = Find(composition);
            Assert.AreEqual(root.Id, semantic.Identity.ElementId);
            Assert.AreEqual(96f, root.Resolve(LayoutProperties.Width).Value);
            Assert.AreEqual(96f, root.Resolve(LayoutProperties.Height).Value);
            var valid =
                input is { } number && double.IsFinite(number) && number >= -10 && number <= 10;
            Assert.AreEqual(valid, semantic.Range is not null);
            if (input is null)
                Assert.AreEqual("— V", semantic.Value);
            else if (!valid)
                Assert.AreEqual("Unavailable", semantic.Value);
        }
    }

    [TestMethod]
    public void GaugeRejectsIncoherentConfigurationBeforeMount()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Components.Gauge(" ", () => 0));
        Assert.ThrowsExactly<ArgumentNullException>(() => Components.Gauge("Reading", null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GaugeOptions(1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GaugeOptions(double.NaN, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new GaugeOptions(-double.MaxValue, double.MaxValue)
        );
    }

    private static SemanticSnapshot Find(Composition composition) =>
        Descendants(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.ProgressBar);

    private static IEnumerable<SemanticSnapshot> Descendants(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}
