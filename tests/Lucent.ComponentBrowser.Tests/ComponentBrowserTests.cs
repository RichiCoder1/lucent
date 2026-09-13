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
    public void DocumentationLaunchRequiresAnExplicitActionAndReportsTheInjectedOutcome()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("launcher-example");
        var launcher = new RecordingLauncher();
        var browser = new ComponentBrowserState(scope, uriLauncher: launcher);
        browser.Select("navigation");
        Assert.IsNull(launcher.Requested);
        browser.OpenDocumentation();
        graph.Drain();
        Assert.AreEqual("https", launcher.Requested?.Scheme);
        Assert.AreEqual("github.com", launcher.Requested?.Host);
        StringAssert.Contains(browser.LinkMessage, "denied");
    }

    private sealed class RecordingLauncher : IUriLauncher
    {
        internal Uri? Requested { get; private set; }

        public ValueTask<UriLaunchResult> LaunchAsync(
            Uri uri,
            CancellationToken cancellationToken = default
        )
        {
            Requested = uri;
            return ValueTask.FromResult(new UriLaunchResult(UriLaunchStatus.Denied));
        }
    }

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

        foreach (
            var helperFile in new[]
            {
                "PopoverExamplePopup.lui",
                "MenusExampleMenu.lui",
                "MenusExampleSubmenu.lui",
            }
        )
        {
            var helperPath = Path.Combine(
                RepositoryRoot,
                "apps",
                "Lucent.ComponentBrowser",
                "Examples",
                helperFile
            );
            var resourceName = "Lucent.ComponentBrowser.Examples." + helperFile;
            Assert.IsTrue(File.Exists(helperPath), $"Helper source is missing: {helperPath}");
            CollectionAssert.Contains(resources, resourceName);
            Assert.AreEqual(
                File.ReadAllText(helperPath),
                ComponentCatalog.ReadSource(helperFile),
                $"Embedded helper source drifted from the compiled file: {helperFile}."
            );
        }
    }

    [TestMethod]
    public void CatalogKeysRemainDistinctAndStable()
    {
        var expectedIds = new[]
        {
            "buttons",
            "fields",
            "password",
            "combo-box",
            "selection",
            "feedback",
            "menus",
            "surfaces",
            "numeric",
            "date-time",
            "navigation",
            "tree",
            "storage",
            "table",
        };
        var expectedSources = new[]
        {
            "ButtonsExample.lui",
            "FieldsExample.lui",
            "PasswordExample.lui",
            "ComboBoxExample.lui",
            "SelectionExample.lui",
            "FeedbackExample.lui",
            "MenusExample.lui",
            "PopoverExample.lui",
            "NumericExample.lui",
            "DateTimeExample.lui",
            "NavigationExample.lui",
            "TreeExample.lui",
            "StorageExample.lui",
            "TableExample.lui",
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
    public void SelectingAnotherExampleResetsRetainedDetailAndSourceScroll()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-view-test");
        var browser = new ComponentBrowserState(scope);
        var view = new ComponentBrowserViewState(scope, browser);

        graph.Drain();
        view.DetailViewport.Offset = new(0, 240);
        view.SourceViewport.Offset = new(0, 96);

        browser.Select("navigation");
        graph.Drain();

        Assert.AreEqual(default, view.DetailViewport.Offset);
        Assert.AreEqual(default, view.SourceViewport.Offset);
    }

    [TestMethod]
    public void DialogSubmissionOwnsTheAcceptedStatusAfterTheSessionCompletes()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-dialog-test");
        var browser = new ComponentBrowserState(scope);

        browser.OpenDialog();
        browser.SubmitDialog();
        graph.Drain();

        Assert.AreEqual(
            "The application action completed and the dialog closed.",
            browser.DialogMessage
        );
    }

    [TestMethod]
    public void TableExampleKeepsSortSelectionAndLargeFixtureCallerOwned()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-table-test");
        var browser = new ComponentBrowserState(scope);

        Assert.AreEqual(12, browser.TableRows.Count);
        Assert.AreEqual("row-00006", browser.TableSelection.Value);
        browser.SetTableSelection("row-00002");
        Assert.AreEqual("row-00002", browser.TableSelection.Value);

        browser.SetTableSort(new TableSort("issues", TableSortDirection.Descending));
        Assert.AreEqual("row-00008", browser.TableRows[0].Id);
        Assert.AreEqual("issues", browser.TableSort!.ColumnKey);

        browser.ToggleTableFixture();
        Assert.AreEqual(10_000, browser.TableRows.Count);
        Assert.IsTrue(browser.TableRows.Any(row => row.Id == "row-00002"));
        Assert.AreEqual("row-00002", browser.TableSelection.Value);
        Assert.AreEqual("10,000 generated rows", browser.TableFixtureLabel);
    }

    [TestMethod]
    public void DateTimeExampleKeepsAppliedValuesInsidePublishedBounds()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-date-time-test");
        var browser = new ComponentBrowserState(scope);

        Assert.AreEqual(new DateOnly(2024, 6, 15), browser.DateValue);
        Assert.AreEqual(new TimeOnly(9, 30), browser.TimeValue);

        browser.SetDateValue(new DateOnly(2023, 12, 31));
        browser.SetTimeValue(new TimeOnly(19, 0));
        Assert.AreEqual(new DateOnly(2024, 6, 15), browser.DateValue);
        Assert.AreEqual(new TimeOnly(9, 30), browser.TimeValue);

        browser.SetDateValue(new DateOnly(2025, 12, 31));
        browser.SetTimeValue(new TimeOnly(17, 30));
        Assert.AreEqual(new DateOnly(2025, 12, 31), browser.DateValue);
        Assert.AreEqual(new TimeOnly(17, 30), browser.TimeValue);
    }

    [TestMethod]
    public void DateTimeExampleDisablesEditorsAndDismissesItsOpenCalendar()
    {
        using var composition = new Composition(new ReactiveGraph(), "date-time-availability");
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var browser = new ComponentBrowserState(composition.Root.Scope);
        composition.Mount(
            composition.Root,
            theme,
            Lucent.ComponentBrowser.Components.DateTimeExample(browser)
        );
        composition.Flush();
        using var renderer = new SkiaSceneRenderer();
        using var scene = SceneLayout.Project(composition, new(1280, 900, 1), renderer);
        Assert.IsTrue(composition.Input.SetScene(scene));
        var open = Flatten(composition.SemanticSnapshot()!)
            .Single(node =>
                node.Role == SemanticRole.Button
                && node.Name.StartsWith("Open calendar", StringComparison.Ordinal)
            );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(open.Identity, new(SemanticCommandKind.Invoke))
        );
        Assert.IsNotNull(composition.Input.ActiveSurface);

        browser.SetExampleState(ExampleState.Disabled);
        composition.Flush();
        Assert.IsNull(
            composition.Input.ActiveSurface,
            "Disabling the example must close its calendar."
        );
        var disabled = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role is SemanticRole.Button or SemanticRole.TextField)
            .ToArray();
        Assert.IsTrue(disabled.Length >= 5);
        Assert.IsTrue(disabled.All(node => !node.Enabled));

        browser.SetExampleState(ExampleState.Default);
        composition.Flush();
        var enabled = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role is SemanticRole.Button or SemanticRole.TextField)
            .ToArray();
        Assert.IsTrue(enabled.All(node => node.Enabled));
        Assert.AreEqual(new DateOnly(2024, 6, 15), browser.DateValue);
        Assert.AreEqual(new TimeOnly(9, 30), browser.TimeValue);
    }

    [TestMethod]
    public void TreeExampleKeepsExpansionAndSelectionControlled()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-tree-test");
        var browser = new ComponentBrowserState(scope);

        Assert.AreEqual("src", browser.TreeSelection.Value);
        Assert.IsTrue(browser.IsTreeExpanded("src"));
        Assert.IsTrue(browser.TreeRoots.Count >= 3);

        browser.SetTreeSelection("fields");
        Assert.AreEqual("fields", browser.TreeSelection.Value);
        browser.SetTreeExpanded("components", true);
        Assert.IsTrue(browser.IsTreeExpanded("components"));
        browser.SetTreeExpanded("components", false);
        Assert.IsFalse(browser.IsTreeExpanded("components"));
        Assert.AreEqual("Selected Fields.", browser.TreeSelectionLabel);
        StringAssert.Contains(browser.TreeExpansionSummary, "2 branches");
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

    [TestMethod]
    public void ComponentNavigationKeepsOverflowInsideItsBoundedViewport()
    {
        using var composition = new Composition(
            new ReactiveGraph(),
            "component-browser-navigation"
        );
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        _ = composition.Mount(composition.Root, theme, ComponentBrowserStructure.Create());
        composition.Flush();

        var semantic = composition.SemanticSnapshot();
        Assert.IsNotNull(semantic);
        var navigation = Flatten(semantic!)
            .Single(node => node.Role == SemanticRole.Group && node.Name == "Component navigation");
        var navigationItems = ComponentCatalog
            .Items.Select(item =>
                Flatten(semantic!)
                    .Single(node =>
                        node.Role == SemanticRole.ListItem
                        && node.Name == item.Title + " · " + item.Family
                    )
            )
            .ToArray();

        using var renderer = new SkiaSceneRenderer();
        using var scene = SceneLayout.Project(composition, new(1282, 872, 1), renderer);
        Assert.IsTrue(composition.Input.SetScene(scene));

        var viewport = scene
            .Boxes.Single(box => box.Identity.ElementId == navigation.Identity.ElementId)
            .Bounds;
        var contentBottom = navigationItems.Max(item =>
        {
            var bounds = scene
                .Boxes.Single(box => box.Identity.ElementId == item.Identity.ElementId)
                .Bounds;
            return bounds.Y + bounds.Height;
        });
        var scrollbar = scene.ScrollBars.SingleOrDefault(bar =>
            bar.Viewport.ElementId == navigation.Identity.ElementId
        );

        Assert.IsTrue(
            scrollbar.Maximum.Y > 0 && contentBottom > viewport.Y + viewport.Height,
            $"Navigation content was not projected into a bounded scrolling viewport: viewport={viewport}, contentBottom={contentBottom}, maximum={scrollbar.Maximum}."
        );
    }

    [TestMethod]
    public void RenderMatrixCoversEveryCatalogExampleAndStockAppearance()
    {
        var appearances = new[]
        {
            (new ThemeAppearance(ThemeColorScheme.Light, ThemeContrast.Normal), "light"),
            (new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal), "dark"),
            (new ThemeAppearance(ThemeColorScheme.Light, ThemeContrast.High), "high-contrast"),
        };
        var viewport = new LayoutViewport(1280, 900, 1);
        var captureDirectory = Path.Combine(RepositoryRoot, "artifacts", "component-browser");
        Directory.CreateDirectory(captureDirectory);

        foreach (var (appearance, appearanceName) in appearances)
        {
            using var composition = new Composition(
                new ReactiveGraph(),
                "component-browser-matrix"
            );
            composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
            using var theme = new ThemeContext(
                composition.Root.Scope,
                StockTheme(appearance),
                appearance: appearance
            );
            _ = composition.Mount(composition.Root, theme, ComponentBrowserStructure.Create());
            composition.Flush();
            using var renderer = new SkiaSceneRenderer();

            RenderCatalogExamples(
                composition,
                renderer,
                viewport,
                captureDirectory,
                appearanceName,
                "comfortable"
            );

            var density = Flatten(composition.SemanticSnapshot()!)
                .Single(node =>
                    node.Role == SemanticRole.Button
                    && node.Name.StartsWith("Density: ", StringComparison.Ordinal)
                );
            Assert.AreEqual(
                SemanticCommandResult.Applied,
                composition.ExecuteSemanticCommand(
                    density.Identity,
                    new(SemanticCommandKind.Invoke)
                ),
                "The density switch should remain a semantic application command."
            );
            composition.Flush();
            RenderCatalogExamples(
                composition,
                renderer,
                viewport,
                captureDirectory,
                appearanceName,
                "compact"
            );
        }
    }

    private static void RenderCatalogExamples(
        Composition composition,
        SkiaSceneRenderer renderer,
        LayoutViewport viewport,
        string captureDirectory,
        string appearanceName,
        string densityName
    )
    {
        foreach (var item in ComponentCatalog.Items)
        {
            var navigation = Flatten(composition.SemanticSnapshot()!)
                .Single(node =>
                    node.Role == SemanticRole.ListItem
                    && node.Name == item.Title + " · " + item.Family
                );
            var selection = composition.ExecuteSemanticCommand(
                navigation.Identity,
                new(SemanticCommandKind.Select)
            );
            Assert.IsTrue(
                selection is SemanticCommandResult.Applied or SemanticCommandResult.Requested,
                $"Catalog item {item.Id} rejected semantic selection: {selection}."
            );
            composition.Flush();

            var semantic = composition.SemanticSnapshot();
            Assert.IsNotNull(semantic);
            Assert.IsTrue(
                Flatten(semantic!).Any(node => node.Name == item.Title || node.Value == item.Title),
                $"Catalog item {item.Id} did not expose its detail title. "
                    + string.Join(
                        "; ",
                        Flatten(semantic!)
                            .Where(node => node.Role == SemanticRole.Text)
                            .Select(node => $"{node.Name}/{node.Value}")
                            .Take(12)
                    )
            );
            Assert.IsTrue(
                Flatten(semantic!)
                    .Any(node =>
                        (node.Value ?? node.Name).Contains(
                            item.SourceFile,
                            StringComparison.Ordinal
                        )
                    ),
                $"The source panel did not expose {item.SourceFile}."
            );

            // The Skia preparer is intentionally asynchronous. Warm the requested icon leases,
            // then project the scene that is actually captured so placeholders cannot pass visual
            // review while the cache is still loading.
            using (var warmup = SceneLayout.Project(composition, viewport, renderer))
                WaitForPreparedImages(composition);
            composition.Flush();
            using var scene = SceneLayout.Project(composition, viewport, renderer);
            Assert.IsTrue(
                scene.Boxes.Count > 0,
                $"Catalog item {item.Id} produced no layout boxes."
            );
            Assert.IsTrue(composition.Input.SetScene(scene));

            if (item.Id == "tree")
            {
                var treeChevronElementIds = ElementIdsNamed(composition.Dump(), "tree-chevron");
                var treeChevronImages = SceneNodes(scene.Nodes)
                    .OfType<ImageSceneNode>()
                    .Where(node => treeChevronElementIds.Contains(node.Identity.Element.ElementId))
                    .ToArray();
                Assert.IsTrue(
                    treeChevronElementIds.Count >= 3,
                    "The realized tree must retain at least three named tree-chevron elements."
                );
                Assert.IsTrue(
                    treeChevronImages.Length >= 3,
                    "At least three realized tree-chevron elements must produce prepared image nodes."
                );
                Assert.IsTrue(
                    treeChevronImages.All(image =>
                        image.Image is ISkiaPreparedImage
                        && image.ColorMode == ImageColorMode.Monochrome
                        && image.Bounds.Width > 0
                        && image.Bounds.Height > 0
                        && image.SourceBounds.Width > 0
                        && image.SourceBounds.Height > 0
                    ),
                    "Each realized tree-chevron image must have nonempty layout and prepared source bounds."
                );
            }

            if (item.Id == "table")
            {
                const string fixtureLabel = "Use 10,000 row fixture";
                var fixtureSwitch = Flatten(semantic!)
                    .Single(node => node.Role == SemanticRole.Switch && node.Name == fixtureLabel);
                var fixtureBounds = scene
                    .Boxes.Single(box => box.Identity.ElementId == fixtureSwitch.Identity.ElementId)
                    .Bounds;
                var fixtureText = SceneNodes(scene.Nodes)
                    .OfType<TextSceneNode>()
                    .Single(node =>
                        node.Bounds.X >= fixtureBounds.X
                        && node.Bounds.X < fixtureBounds.X + fixtureBounds.Width
                        && node.Bounds.Y >= fixtureBounds.Y
                        && node.Bounds.Y < fixtureBounds.Y + fixtureBounds.Height
                    );
                Assert.IsFalse(
                    fixtureText.Text.DidOverflow,
                    "The long table-fixture label must fit without truncation."
                );
                Assert.IsTrue(
                    fixtureText.Bounds.X + fixtureText.Bounds.Width
                        <= fixtureBounds.X + fixtureBounds.Width
                        && fixtureText.Bounds.Y + fixtureText.Bounds.Height
                            <= fixtureBounds.Y + fixtureBounds.Height,
                    "The rendered table-fixture label must remain inside the Switch geometry."
                );
            }

            SaveCapture(
                renderer,
                scene,
                viewport,
                Path.Combine(captureDirectory, $"{appearanceName}-{densityName}-{item.Id}.png")
            );

            if (item.Id == "navigation")
            {
                Assert.IsTrue(
                    SceneNodes(scene.Nodes).OfType<ImageSceneNode>().Count() >= 2,
                    "The navigation example should render prepared stock artwork."
                );
                SaveCapture(
                    renderer,
                    scene,
                    viewport,
                    Path.Combine(captureDirectory, $"{appearanceName}-{densityName}.png")
                );
            }
        }
    }

    private static void WaitForPreparedImages(Composition composition)
    {
        Assert.IsNotNull(composition.Images);
        Assert.IsTrue(
            SpinWait.SpinUntil(
                () =>
                {
                    var metrics = composition.Images!.Metrics;
                    return metrics.Queued == 0 && metrics.Active == 0;
                },
                TimeSpan.FromSeconds(10)
            ),
            "Stock image preparation did not settle before capture."
        );
    }

    private static void SaveCapture(
        SkiaSceneRenderer renderer,
        RetainedScene scene,
        LayoutViewport viewport,
        string path
    )
    {
        var width = checked((int)MathF.Round(viewport.Width * viewport.Scale));
        var height = checked((int)MathF.Round(viewport.Height * viewport.Scale));
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static Theme StockTheme(ThemeAppearance appearance) =>
        appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast
        : appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark
        : ControlThemes.Light;

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

    private static HashSet<long> ElementIdsNamed(string dump, string nameFragment)
    {
        var ids = new HashSet<long>();
        const string elementPrefix = "element ";
        const string namePrefix = " name=\"";
        foreach (var line in dump.Split('\n'))
        {
            if (!line.StartsWith(elementPrefix, StringComparison.Ordinal))
                continue;
            var idEnd = line.IndexOf(' ', elementPrefix.Length);
            if (
                idEnd <= elementPrefix.Length
                || !long.TryParse(
                    line.AsSpan(elementPrefix.Length, idEnd - elementPrefix.Length),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var id
                )
            )
                continue;
            var nameStart = line.IndexOf(namePrefix, StringComparison.Ordinal);
            if (nameStart < 0)
                continue;
            nameStart += namePrefix.Length;
            var nameEnd = line.IndexOf('"', nameStart);
            if (
                nameEnd > nameStart
                && line.AsSpan(nameStart, nameEnd - nameStart)
                    .Contains(nameFragment.AsSpan(), StringComparison.Ordinal)
            )
                ids.Add(id);
        }
        return ids;
    }
}
