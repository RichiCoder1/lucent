using Lucent.ComponentBrowser;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.ComponentBrowser.Tests;

[TestClass]
public sealed class ComponentBrowserTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public void CatalogSourcesMatchEmbeddedCompiledExamples()
    {
        var assembly = typeof(ComponentCatalog).Assembly;
        var resources = assembly.GetManifestResourceNames();

        foreach (var item in ComponentCatalog.Items)
        {
            var sourcePath = Path.Combine(
                RepositoryRoot,
                "apps",
                "Lucent.ComponentBrowser",
                "Examples",
                item.SourceFile
            );
            var resourceName = "Lucent.ComponentBrowser.Examples." + item.SourceFile;

            Assert.IsTrue(File.Exists(sourcePath), $"Example source is missing: {sourcePath}");
            CollectionAssert.Contains(resources, resourceName);
            Assert.AreEqual(
                File.ReadAllText(sourcePath),
                ComponentCatalog.ReadSource(item.SourceFile),
                $"Embedded source drifted from {sourcePath}."
            );
        }

        var helperPath = Path.Combine(
            RepositoryRoot,
            "apps",
            "Lucent.ComponentBrowser",
            "Examples",
            "PopoverExamplePopup.lui"
        );
        Assert.IsTrue(File.Exists(helperPath), $"Popover helper source is missing: {helperPath}");
        CollectionAssert.Contains(
            resources,
            "Lucent.ComponentBrowser.Examples.PopoverExamplePopup.lui"
        );
        Assert.AreEqual(
            File.ReadAllText(helperPath),
            ComponentCatalog.ReadSource("PopoverExamplePopup.lui"),
            "Embedded popover helper source drifted from the compiled file."
        );
    }

    [TestMethod]
    public void CatalogKeysRemainDistinctAndStable()
    {
        var expectedIds = new[]
        {
            "buttons",
            "fields",
            "selection",
            "feedback",
            "menus",
            "surfaces",
        };
        var expectedSources = new[]
        {
            "ButtonsExample.lui",
            "FieldsExample.lui",
            "SelectionExample.lui",
            "FeedbackExample.lui",
            "MenusExample.lui",
            "PopoverExample.lui",
        };
        var actualIds = ComponentCatalog.Items.Select(item => item.Id).ToArray();
        var actualSources = ComponentCatalog.Items.Select(item => item.SourceFile).ToArray();

        CollectionAssert.AreEqual(expectedIds, actualIds);
        CollectionAssert.AreEqual(expectedSources, actualSources);
        Assert.AreEqual(actualIds.Length, actualIds.Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(
            actualSources.Length,
            actualSources.Distinct(StringComparer.Ordinal).Count()
        );

        foreach (var id in expectedIds)
            Assert.AreEqual(id, ComponentCatalog.Find(id).Id);
    }

    [TestMethod]
    public void BrowserStateKeepsSearchSelectionDensityAndExampleStateInApplicationModel()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-test");
        var browser = new ComponentBrowserState(scope);

        Assert.AreEqual("buttons", browser.SelectedId);
        browser.Search = "checkbox";
        Assert.AreEqual("selection", browser.VisibleItems.Single().Id);

        browser.Select("selection");
        Assert.AreEqual("selection", browser.SelectedId);
        browser.ToggleDensity();
        Assert.AreEqual(BrowserDensity.Compact, browser.Density);
        browser.SetExampleState(ExampleState.Busy);
        Assert.AreEqual(ExampleState.Busy, browser.CurrentExampleState);
        browser.SetPopoverOpen(true);
        Assert.IsTrue(browser.PopoverOpen);
        browser.SetPopoverOpen(false);
        Assert.IsFalse(browser.PopoverOpen);

        browser.SetCheckState(CheckState.Mixed);
        browser.SetSwitchEnabled(false);
        browser.SetRadioSelection("portable");
        Assert.AreEqual(CheckState.Mixed, browser.CheckState);
        Assert.IsFalse(browser.SwitchEnabled);
        Assert.AreEqual("portable", browser.RadioSelection);
    }

    [TestMethod]
    public void ThemeSwitchUsesTheStockThemeContextContract()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-theme-test");
        using var theme = new ThemeContext(scope, ControlThemes.Light);
        var dark = new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal);
        var highContrast = new ThemeAppearance(ThemeColorScheme.Light, ThemeContrast.High);

        ComponentBrowserTheme.SetAppearance(theme, dark);
        Assert.AreEqual(dark, theme.Appearance);
        ComponentBrowserTheme.SetAppearance(theme, highContrast);
        Assert.AreEqual(highContrast, theme.Appearance);
    }

    [TestMethod]
    public void MaintainedRootMountRendersCatalogAndSourceSurface()
    {
        using var composition = new Composition(new ReactiveGraph(), "component-browser-render");
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        _ = composition.Mount(composition.Root, theme, ComponentBrowserStructure.Create());
        composition.Flush();

        var semantic = composition.SemanticSnapshot();
        Assert.IsNotNull(semantic);
        var nodes = Flatten(semantic!).ToArray();
        var dump = composition.Dump();
        StringAssert.Contains(dump, "component-browser.title");
        StringAssert.Contains(dump, "component-browser.detail-title");
        StringAssert.Contains(dump, "component-browser.source");
        Assert.IsTrue(nodes.Any(node => node.Name == "Buttons" || node.Value == "Buttons"));
        using var renderer = new SkiaSceneRenderer();
        using var scene = SceneLayout.Project(composition, new(1280, 900, 1), renderer);
        Assert.IsTrue(composition.Input.SetScene(scene));
        using var bitmap = new SKBitmap(1280, 900);
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(scene, canvas);
        Assert.IsTrue(scene.Boxes.Count != 0);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (
                var directory = new DirectoryInfo(start);
                directory is not null;
                directory = directory.Parent
            )
            {
                if (File.Exists(Path.Combine(directory.FullName, "Lucent.slnx")))
                    return directory.FullName;
            }
        }

        throw new AssertFailedException("Could not locate the Lucent repository root.");
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }
}
