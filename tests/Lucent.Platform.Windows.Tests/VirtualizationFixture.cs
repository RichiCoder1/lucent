using Lucent.Core;
using Lucent.Platform.Windows;

/// <summary>Test-owned external UIA fixture for fixed-height virtual children; it is not part of the reference application.</summary>
internal static class VirtualizationFixture
{
    internal static int Run()
    {
        try
        {
            var graph = new ReactiveGraph();
            using var composition = Create(graph, out var theme);
            return WindowsBootstrap.Run("Lucent Virtualization Fixture", composition, theme);
        }
        catch (Exception error) { Console.Error.WriteLine("Lucent virtualization fixture: " + error.Message); return 1; }
    }

    private static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        var composition = new Composition(graph, "virtualization-fixture");
        var context = new ThemeContext(composition.Root.Scope, ControlThemes.Light); theme = context;
        Controls.Column(composition.Root, context, "Fixture", Style.Empty.Set(LayoutProperties.Width, 400f).Set(LayoutProperties.Height, 500f).Set(LayoutProperties.Clip, true));
        var search = composition.Child(composition.Root, "search"); Controls.TextField(search, context, "Search", style: Style.Empty.Set(LayoutProperties.Width, 400f).Set(LayoutProperties.Height, 24f));
        var values = composition.Root.Scope.Signal(Enumerable.Range(1, 10_000).Reverse().ToArray(), "virtualization.rows");
        var viewport = composition.Child(composition.Root, "viewport"); Controls.ScrollViewport(viewport, context, "Issues", style: Style.Empty.Set(LayoutProperties.Width, 400f).Set(LayoutProperties.Height, 180f));
        _ = Controls.VirtualizedList(viewport, context, "rows", "Issues", () => values.Value, value => value, (value, factory) =>
        {
            var row = factory.Element("row"); Controls.Selectable(row, context, "Issue " + value); return row;
        }, 30f);
        var reorder = composition.Child(composition.Root, "reorder");
        Controls.Button(reorder, context, "Reorder", () =>
        {
            var next = values.Value.ToArray(); (next[0], next[1]) = (next[1], next[0]); values.Value = next;
        }, Style.Empty.Set(LayoutProperties.Width, 120f).Set(LayoutProperties.Height, 24f));
        var remove = composition.Child(composition.Root, "remove");
        Controls.Button(remove, context, "Remove", () => values.Value = values.Value[2..], Style.Empty.Set(LayoutProperties.Width, 120f).Set(LayoutProperties.Height, 24f));
        return composition;
    }
}
