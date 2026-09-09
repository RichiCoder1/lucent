using Lucent.Core;
using Lucent.Icons.Lucide;

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
        Controls.TextField(field, theme, "Search", placeholder: "");
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
    public void FocusedAccentButtonsAndIconButtonsKeepThreeToOneRingContrast()
    {
        foreach (
            var (palette, appearance, name) in new[]
            {
                (ControlThemes.Light, ThemeAppearance.Light, "light"),
                (
                    ControlThemes.Dark,
                    new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal),
                    "dark"
                ),
            }
        )
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, name + "-accent-focus");
            var theme = new ThemeContext(composition.Root.Scope, palette, appearance: appearance);
            composition.Root.Present(
                theme,
                author: PresentationStyles.Surface.Width(160).Height(80)
            );
            var button = composition.Child(composition.Root, "button");
            Controls.Button(button, theme, "Action");
            var iconButton = composition.Child(composition.Root, "icon-button");
            Controls.IconButton(
                iconButton,
                theme,
                LucideIcons.Ellipsis,
                "More actions",
                style: Style.Empty.Set(
                    VisualProperties.Participation,
                    ElementParticipation.Collapsed
                )
            );
            button.SetVariants(VariantState.FocusVisible);
            iconButton.SetVariants(VariantState.FocusVisible);
            graph.Drain();

            AssertFocusStateContrast(button, name + " focused button");
            AssertFocusStateContrast(iconButton, name + " focused icon button");
            AssertProjectedFocusRing(composition, button, name + " focused button");
        }
    }

    [TestMethod]
    public void MinimalHighContrastFocusRetainsVisibleRenderedIndication()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "minimal-high-contrast-focus");
        var theme = new ThemeContext(
            composition.Root.Scope,
            ControlThemes.HighContrast,
            appearance: new(ThemeColorScheme.Light, ThemeContrast.High),
            presentationMode: ControlPresentationMode.Minimal
        );
        composition.Root.Present(theme, author: PresentationStyles.Surface.Width(160).Height(80));
        var button = composition.Child(composition.Root, "button");
        Controls.Button(button, theme, "Action");
        graph.Drain();
        Assert.AreEqual(
            (byte?)0,
            button.Resolve(VisualProperties.Background).Value.Color?.A,
            "Minimal high-contrast controls must stay undecorated before focus."
        );

        button.SetVariants(VariantState.FocusVisible);
        graph.Drain();
        var background = button.Resolve(VisualProperties.Background).Value.Color;
        var surface = composition.Root.Resolve(VisualProperties.Background).Value.Color;
        var ring = button.Resolve(VisualProperties.FocusRing).Value.Brush?.Color;
        Assert.IsTrue(
            surface is { } rootSurface
                && ring is { } ringColor
                && Contrast(ringColor, background is { A: > 0 } fill ? fill : rootSurface) >= 3,
            "Minimal high-contrast focus indication must contrast with its composited stock surface."
        );
        AssertProjectedFocusRing(composition, button, "minimal high-contrast focused button");
    }

    [TestMethod]
    public void FocusAndDisabledPaletteStatesStayContrastingAcrossAppearanceChanges()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stock-appearance-states");
        var theme = new ThemeContext(
            composition.Root.Scope,
            ControlThemes.Light,
            appearance: ThemeAppearance.Light
        );
        composition.Root.Present(theme, author: PresentationStyles.Surface);
        var button = composition.Child(composition.Root, "button");
        Controls.Button(button, theme, "Action");
        var field = composition.Child(composition.Root, "field");
        Controls.TextField(field, theme, "Search", placeholder: "");
        var selected = composition.Child(composition.Root, "selected");
        Controls.Selectable(selected, theme, "Issue");
        var disabled = composition.Child(composition.Root, "disabled");
        Controls.Button(disabled, theme, "Disabled");
        graph.Drain();

        field.SetVariants(VariantState.FocusVisible);
        button.SetVariants(VariantState.FocusVisible | VariantState.Pressed);
        selected.SetVariants(VariantState.FocusVisible | VariantState.Selected);
        disabled.SetVariants(VariantState.Disabled);
        graph.Drain();
        AssertFocusStateContrast(button, "light pressed button");
        AssertFocusStateContrast(selected, "light selected row");
        Assert.IsTrue(
            Contrast(
                field.Resolve(VisualProperties.FocusRing).Value.Brush!.Color!.Value,
                Color.Parse("#ffffff")
            ) >= 3,
            "The light focus ring must remain visible against a light field surface."
        );

        theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.Normal);
        theme.Theme = ControlThemes.Dark;
        graph.Drain();
        AssertFocusStateContrast(button, "dark pressed button");
        AssertFocusStateContrast(selected, "dark selected row");
        Assert.IsTrue(
            Contrast(
                field.Resolve(VisualProperties.FocusRing).Value.Brush!.Color!.Value,
                Color.Parse("#111827")
            ) >= 3,
            "The dark focus ring must remain visible against a dark field surface."
        );

        theme.Appearance = new(ThemeColorScheme.Light, ThemeContrast.High);
        theme.Theme = ControlThemes.HighContrast;
        graph.Drain();
        AssertFocusStateContrast(button, "high-contrast pressed button");
        AssertFocusStateContrast(selected, "high-contrast selected row");
        Assert.AreEqual(
            Color.Parse("#ffff00"),
            field.Resolve(VisualProperties.Background).Value.Color,
            "Mounted focus state did not switch to the high-contrast treatment."
        );
        Assert.AreEqual(
            Color.Parse("#000000"),
            field.Resolve(TypographyProperties.TextColor).Value,
            "High-contrast focus text did not switch with its background."
        );
        Assert.AreEqual(
            Color.Parse("#ffffff"),
            disabled.Resolve(TypographyProperties.TextColor).Value,
            "High-contrast disabled text must remain distinct from its grey surface."
        );
        Assert.IsTrue(
            Contrast(
                disabled.Resolve(TypographyProperties.TextColor).Value,
                disabled.Resolve(VisualProperties.Background).Value.Color!.Value
            ) >= 3,
            "High-contrast disabled text must not collide with its surface."
        );

        theme.Appearance = ThemeAppearance.Light;
        theme.Theme = ControlThemes.Light;
        graph.Drain();
        Assert.AreEqual(
            Color.Parse("#ffffff"),
            field.Resolve(VisualProperties.Background).Value.Color,
            "Mounted focus state did not return to the light treatment."
        );
        Assert.AreEqual(
            Color.Parse("#0f172a"),
            field.Resolve(TypographyProperties.TextColor).Value,
            "Light focus text did not restore the stock foreground."
        );
    }

    [TestMethod]
    public void DecorativeRowAndColumnComponentsDoNotPublishGenericGroups()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "decorative-layout-semantics");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Root.Present(theme, author: PresentationStyles.Surface);
        _ = composition.Mount(
            composition.Root,
            theme,
            Components.Column([Components.Row([Components.Text("content")])])
        );
        graph.Drain();

        var snapshot = composition.SemanticSnapshot();
        Assert.IsNotNull(snapshot);
        var names = Flatten(snapshot!).Select(item => item.Name).ToArray();
        CollectionAssert.DoesNotContain(names, "Row");
        CollectionAssert.DoesNotContain(names, "Column");
        CollectionAssert.Contains(names, "content");
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

        selectable.SetVariants(VariantState.Disabled);
        graph.Drain();
        ownerTextColor = selectable.Resolve(TypographyProperties.TextColor).Value;
        Assert.AreEqual(
            ownerTextColor,
            text.Resolve(TypographyProperties.TextColor).Value,
            "Nested typography must inherit the selectable's readable disabled-state color."
        );

        theme.Theme = ControlThemes.HighContrast;
        theme.Appearance = new(ThemeColorScheme.Light, ThemeContrast.High);
        graph.Drain();
        ownerTextColor = selectable.Resolve(TypographyProperties.TextColor).Value;
        Assert.AreEqual(Color.Parse("#ffffff"), ownerTextColor);
        Assert.AreEqual(
            ownerTextColor,
            text.Resolve(TypographyProperties.TextColor).Value,
            "Nested typography must inherit high-contrast disabled text color."
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

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot snapshot)
    {
        yield return snapshot;
        foreach (var child in snapshot.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.03928
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        var firstLuminance =
            0.2126 * Channel(first.R) + 0.7152 * Channel(first.G) + 0.0722 * Channel(first.B);
        var secondLuminance =
            0.2126 * Channel(second.R) + 0.7152 * Channel(second.G) + 0.0722 * Channel(second.B);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static void AssertFocusStateContrast(Element element, string state)
    {
        var background = element.Resolve(VisualProperties.Background).Value.Color;
        var foreground = element.Resolve(TypographyProperties.TextColor).Value;
        var ring = element.Resolve(VisualProperties.FocusRing).Value.Brush?.Color;
        Assert.IsTrue(
            background is { } surface && Contrast(foreground, surface) >= 3,
            $"The {state} foreground must remain readable against its resolved surface."
        );
        Assert.IsTrue(
            background is { } ringSurface
                && ring is { } ringColor
                && Contrast(ringColor, ringSurface) >= 3,
            $"The {state} focus ring must remain visible against its resolved surface."
        );
    }

    private static void AssertProjectedFocusRing(
        Composition composition,
        Element element,
        string state
    )
    {
        using var scene = SceneLayout.Project(composition, new(160, 80, 1), new MetricShaper());
        var resolved = element.Resolve(VisualProperties.FocusRing).Value;
        var projected = SceneNodes(scene.Nodes)
            .OfType<PaintSceneNode>()
            .Single(node =>
                node.Identity.Element.ElementId == element.Id
                && node.Identity.Kind == SceneNodeKind.FocusRing
            );
        Assert.AreEqual(
            resolved.Brush,
            projected.Brush,
            $"The {state} retained focus paint diverged from its resolved ring."
        );
        Assert.AreEqual(resolved.Thickness, projected.InsetWidths?.Left);
    }

    private static IEnumerable<SceneNode> SceneNodes(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            var children = node switch
            {
                ClipSceneNode clip => clip.Children,
                OpacitySceneNode opacity => opacity.Children,
                _ => null,
            };
            if (children is not null)
                foreach (var child in SceneNodes(children))
                    yield return child;
        }
    }

    private sealed class MetricShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "metric",
                "metric",
                400,
                5,
                0,
                "metric",
                0,
                "metric#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                request.Text.Length,
                [glyph]
            );
            return new("metric", request.Text.Length, request.FontSize, [run]);
        }
    }
}
