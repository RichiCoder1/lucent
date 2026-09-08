using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class StockPresentationContracts
{
    [TestMethod]
    public void NamedTextRolesAreAuthorStylesBackedByLiveThemeTokens()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stock-text");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var text = composition.Child(composition.Root, "title");
        Controls.Text(text, theme, "Title", PresentationStyles.Title);
        graph.Drain();

        Assert.AreEqual(18f, text.Resolve(TypographyProperties.FontSize).Value);
        Assert.AreEqual(FontWeight.SemiBold, text.Resolve(TypographyProperties.FontWeight).Value);
        Assert.AreEqual(Color.Parse("#0f172a"), text.Resolve(TypographyProperties.TextColor).Value);
        Assert.IsTrue(
            text.Resolve(TypographyProperties.TextColor)
                .Winner.Source.StartsWith("author", StringComparison.Ordinal),
            "A named role must be an actual author style over Text's component default."
        );

        theme.Theme = ControlThemes.Dark;
        graph.Drain();
        Assert.AreEqual(Color.Parse("#f8fafc"), text.Resolve(TypographyProperties.TextColor).Value);
    }

    [TestMethod]
    public void MinimalPresentationRemovesDecorationWithoutRemovingFocusOrSemantics()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "minimal-controls");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var button = composition.Child(composition.Root, "button");
        Controls.Button(button, theme, "Action");
        graph.Drain();

        var standardPadding = button.Resolve(LayoutProperties.Padding).Value;
        var standardHeight = button.Resolve(LayoutProperties.MinHeight).Value;
        Assert.AreEqual(
            Color.Parse("#2563eb"),
            button.Resolve(VisualProperties.Background).Value.Color
        );
        Assert.IsNotNull(button.Resolve(VisualProperties.Border).Value.Brush);

        theme.PresentationMode = ControlPresentationMode.Minimal;
        graph.Drain();
        Assert.AreEqual(
            (byte?)0,
            button.Resolve(VisualProperties.Background).Value.Color?.A,
            "Minimal mode should clear the stock button background."
        );
        Assert.AreEqual(Border.None, button.Resolve(VisualProperties.Border).Value);
        Assert.AreEqual(standardPadding, button.Resolve(LayoutProperties.Padding).Value);
        Assert.AreEqual(standardHeight, button.Resolve(LayoutProperties.MinHeight).Value);

        button.SetVariants(VariantState.FocusVisible);
        graph.Drain();
        Assert.IsNotNull(
            button.Resolve(VisualProperties.FocusRing).Value.Brush,
            "Minimal mode must retain a visible keyboard focus ring."
        );
        Assert.IsNotNull(
            composition.SemanticSnapshot(),
            "Minimal mode must retain the control semantic declaration."
        );

        theme.PresentationMode = ControlPresentationMode.Standard;
        graph.Drain();
        Assert.AreEqual(
            Color.Parse("#2563eb"),
            button.Resolve(VisualProperties.Background).Value.Color,
            "Returning to standard mode must restore the stock control paint beneath the focus ring."
        );
    }

    [TestMethod]
    public void NormalFocusRingPreservesStockPaintAndSelectableRowsStayQuiet()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stock-focus-ring");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var button = composition.Child(composition.Root, "button");
        Controls.Button(button, theme, "Action");
        var field = composition.Child(composition.Root, "field");
        Controls.TextField(field, theme, "Search");
        var row = composition.Child(composition.Root, "row");
        Controls.Selectable(row, theme, "Issue");
        graph.Drain();

        Assert.AreEqual(Border.None, row.Resolve(VisualProperties.Border).Value);
        Assert.AreEqual(
            Color.Parse("#ffffff"),
            row.Resolve(VisualProperties.Background).Value.Color
        );

        button.SetVariants(VariantState.FocusVisible);
        field.SetVariants(VariantState.FocusVisible);
        row.SetVariants(VariantState.Selected | VariantState.FocusVisible);

        Assert.AreEqual(
            Color.Parse("#2563eb"),
            button.Resolve(VisualProperties.Background).Value.Color
        );
        Assert.AreEqual(
            Color.Parse("#ffffff"),
            button.Resolve(TypographyProperties.TextColor).Value
        );
        Assert.AreEqual(
            Color.Parse("#ffffff"),
            field.Resolve(VisualProperties.Background).Value.Color
        );
        Assert.AreEqual(
            Color.Parse("#0f172a"),
            field.Resolve(TypographyProperties.TextColor).Value
        );
        Assert.AreEqual(
            Color.Parse("#dbeafe"),
            row.Resolve(VisualProperties.Background).Value.Color
        );
        Assert.AreEqual(Color.Parse("#0f172a"), row.Resolve(TypographyProperties.TextColor).Value);
        Assert.IsNotNull(button.Resolve(VisualProperties.FocusRing).Value.Brush);
        Assert.IsNotNull(field.Resolve(VisualProperties.FocusRing).Value.Brush);
        Assert.IsNotNull(row.Resolve(VisualProperties.FocusRing).Value.Brush);
    }

    [TestMethod]
    public void DividerIsDecorativeAndUsesStockThemeBrush()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "divider");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(composition.Root, theme, "Root");
        var divider = composition.Mount(
            composition.Root,
            theme,
            Components.Divider(DividerOrientation.Vertical, 2)
        );
        graph.Drain();

        Assert.AreEqual(2f, divider.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(
            Color.Parse("#cbd5e1"),
            divider.Resolve(VisualProperties.Background).Value.Color
        );
        Assert.IsFalse(
            composition.SemanticDump().Contains("divider", StringComparison.OrdinalIgnoreCase),
            "A decorative divider must not create a semantic or input target."
        );

        theme.Theme = ControlThemes.Dark;
        graph.Drain();
        Assert.AreEqual(
            Color.Parse("#475569"),
            divider.Resolve(VisualProperties.Background).Value.Color
        );
    }

    [TestMethod]
    public void ComposedSelectableChildrenInheritStatePaintFromTheOwner()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "composed-selectable-presentation");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: PresentationStyles.Surface);

        var selectable = composition.Mount(
            composition.Root,
            theme,
            Components.Selectable(
                [
                    Components.Column([
                        Components.Row([
                            Components.Text("Nested", PresentationStyles.Typography(TextRole.Body)),
                        ]),
                    ]),
                ],
                () => "Nested row",
                () => false
            )
        );
        graph.Drain();

        var column = selectable.Children.Single();
        var row = column.Children.Single();
        var text = row.Children.Single();

        selectable.SetVariants(VariantState.Selected);
        graph.Drain();
        Assert.AreEqual(
            Color.Parse("#dbeafe"),
            selectable.Resolve(VisualProperties.Background).Value.Color
        );
        Assert.AreEqual(
            (byte)0,
            column.Resolve(VisualProperties.Background).Value.Color?.A,
            "A structural Column must not cover its selectable owner's state background."
        );
        Assert.AreEqual(
            (byte)0,
            row.Resolve(VisualProperties.Background).Value.Color?.A,
            "A structural Row must not cover its selectable owner's state background."
        );

        selectable.SetVariants(VariantState.Pressed);
        graph.Drain();
        var ownerTextColor = selectable.Resolve(TypographyProperties.TextColor).Value;
        Assert.AreEqual(Color.Parse("#ffffff"), ownerTextColor);
        Assert.AreEqual(
            ownerTextColor,
            text.Resolve(TypographyProperties.TextColor).Value,
            "Default composed text must inherit the selectable's readable pressed-state color."
        );
    }

    [TestMethod]
    public void DensityPresetsExposeValidatedMetricsAndAuthorAssignments()
    {
        var compact = DensityMetrics.For(DensityPreset.Compact);
        Assert.AreEqual(30, compact.ControlHeight);
        Assert.AreEqual(30, compact.RowHeight);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new DensityMetrics(0, 30, 30, 6, Insets.Zero)
        );

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "density");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var panel = composition.Child(composition.Root, "panel");
        panel.Present(theme, author: PresentationStyles.ForDensity(DensityPreset.Compact));
        graph.Drain();

        Assert.AreEqual(13f, panel.Resolve(TypographyProperties.FontSize).Value);
        Assert.AreEqual(30f, panel.Resolve(LayoutProperties.MinHeight).Value);
        Assert.AreEqual(6f, panel.Resolve(LayoutProperties.Spacing).Value);
        Assert.AreEqual(Insets.Symmetric(8, 6), panel.Resolve(LayoutProperties.Padding).Value);
    }

    [TestMethod]
    public void ApplicationBuilderSnapshotsMinimalPresentationMode()
    {
        var host = new PresentationModeHost();
        var app = LucentApplication
            .CreateBuilder()
            .SetPresentationMode(ControlPresentationMode.Minimal)
            .UseHost(host)
            .Build();

        Assert.AreEqual(0, app.Run(ComponentRecipe.Create("empty", static (_, _) => { })));
        Assert.AreEqual(ControlPresentationMode.Minimal, host.Mode);
        Assert.AreEqual(Color.Parse("#ffffff"), host.RootSurface);
        Assert.AreEqual(Color.Parse("#0f172a"), host.RootForeground);
    }

    private sealed class PresentationModeHost : IApplicationHost
    {
        public ControlPresentationMode Mode { get; private set; }
        public Color? RootSurface { get; private set; }
        public Color? RootForeground { get; private set; }

        public int Run(ApplicationSession session)
        {
            Mode = session.Theme.PresentationMode;
            RootSurface = session.Composition.Root.Resolve(VisualProperties.Background).Value.Color;
            RootForeground = session.Composition.Root.Resolve(TypographyProperties.TextColor).Value;
            session.Start();
            return 0;
        }
    }
}
