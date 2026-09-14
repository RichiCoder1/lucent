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
        var example = new NavigationExampleModel(scope, launcher);
        Assert.IsNull(launcher.Requested);
        example.OpenDocumentation();
        graph.Drain();
        Assert.AreEqual("https", launcher.Requested?.Scheme);
        Assert.AreEqual("github.com", launcher.Requested?.Host);
        StringAssert.Contains(example.LinkMessage, "denied");
    }

    [TestMethod]
    public void DocumentationLaunchReportsFailuresAndCancelsWithItsOwner()
    {
        var graph = new ReactiveGraph();
        using (var active = graph.CreateScope("launcher-failure"))
        {
            var example = new NavigationExampleModel(active, new ThrowingLauncher());
            example.OpenDocumentation();
            graph.Drain();
            Assert.AreEqual("The system could not open documentation.", example.LinkMessage);
        }

        var owner = graph.CreateScope("launcher-cancellation");
        var launcher = new DeferredLauncher();
        var canceled = new NavigationExampleModel(owner, launcher);
        canceled.OpenDocumentation();
        Assert.IsTrue(launcher.Cancellation.CanBeCanceled);
        Assert.IsFalse(launcher.Cancellation.IsCancellationRequested);

        owner.Dispose();
        Assert.IsTrue(launcher.Cancellation.IsCancellationRequested);
        launcher.Complete(new UriLaunchResult(UriLaunchStatus.Launched));
        graph.Drain();

        using var replacementOwner = graph.CreateScope("launcher-replacement");
        var replacement = new NavigationExampleModel(replacementOwner, new RecordingLauncher());
        Assert.AreEqual("No reference link invoked yet.", replacement.LinkMessage);
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

    private sealed class ThrowingLauncher : IUriLauncher
    {
        public ValueTask<UriLaunchResult> LaunchAsync(
            Uri uri,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromException<UriLaunchResult>(new InvalidOperationException("private"));
    }

    private sealed class DeferredLauncher : IUriLauncher
    {
        private readonly TaskCompletionSource<UriLaunchResult> _completion = new();

        internal CancellationToken Cancellation { get; private set; }

        public ValueTask<UriLaunchResult> LaunchAsync(
            Uri uri,
            CancellationToken cancellationToken = default
        )
        {
            Cancellation = cancellationToken;
            return new(_completion.Task);
        }

        internal void Complete(UriLaunchResult result) => _completion.SetResult(result);
    }

    [TestMethod]
    public void StoragePickerCompletionCannotOutliveItsExampleOwner()
    {
        var graph = new ReactiveGraph();
        var owner = graph.CreateScope("picker-cancellation");
        var picker = new DeferredPicker();
        var example = new StorageExampleModel(owner, picker);
        example.OpenFiles();
        Assert.IsTrue(picker.Cancellation.CanBeCanceled);
        Assert.IsFalse(picker.Cancellation.IsCancellationRequested);

        owner.Dispose();
        Assert.IsTrue(picker.Cancellation.IsCancellationRequested);
        picker.Complete(
            new FilePickerResult(
                FilePickerStatus.Selected,
                [new FilePickerItem(new Uri("file:///C:/example.lui"), "example.lui")]
            )
        );
        graph.Drain();

        using var replacementOwner = graph.CreateScope("picker-replacement");
        var replacement = new StorageExampleModel(replacementOwner, new DeferredPicker());
        Assert.AreEqual("No native picker request yet.", replacement.Message);
        Assert.AreEqual("A selected name and location will appear here.", replacement.Location);
    }

    private sealed class DeferredPicker : IFilePicker
    {
        private readonly TaskCompletionSource<FilePickerResult> _completion = new();

        internal CancellationToken Cancellation { get; private set; }

        public ValueTask<FilePickerResult> OpenFilesAsync(
            OpenFileOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Cancellation = cancellationToken;
            return new(_completion.Task);
        }

        public ValueTask<FilePickerResult> SaveFileAsync(
            SaveFileOptions? options = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(new FilePickerResult(FilePickerStatus.Unsupported));

        public ValueTask<FilePickerResult> PickFolderAsync(
            PickFolderOptions? options = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(new FilePickerResult(FilePickerStatus.Unsupported));

        internal void Complete(FilePickerResult result) => _completion.SetResult(result);
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
    public void BrowserStateKeepsOnlySharedSearchSelectionDensityAndExampleState()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-test");
        var browser = new ComponentBrowserState(scope);

        Assert.AreEqual("buttons", browser.SelectedId);
        browser.Search = "checkbox";
        Assert.AreEqual("selection", browser.VisibleItems.Single().Id);

        browser.Select("selection");
        graph.Drain();
        Assert.AreEqual("selection", browser.SelectedId);
        browser.ToggleDensity();
        Assert.AreEqual(BrowserDensity.Compact, browser.Density);
        browser.SetExampleState(ExampleState.Busy);
        Assert.AreEqual(ExampleState.Busy, browser.CurrentExampleState);
    }

    [TestMethod]
    public void ButtonsExampleKeepsGeneratedActivationStatePerMount()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-browser-button-state");
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var browser = new ComponentBrowserState(composition.Root.Scope);

        var first = composition.Mount(composition.Root, theme, Components.ButtonsExample(browser));
        graph.Drain();
        Assert.AreEqual(1, composition.Root.Children.Count);
        Assert.AreSame(first, composition.Root.Children[0]);
        var apply = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node is { Role: SemanticRole.Button, Name: "Apply change" });
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                apply.Identity,
                new SemanticCommand(SemanticCommandKind.Invoke)
            )
        );
        graph.Drain();
        Assert.IsTrue(
            Flatten(composition.SemanticSnapshot()!).Any(node => node.Name == "Invoked 1 time.")
        );

        var second = composition.Mount(composition.Root, theme, Components.ButtonsExample(browser));
        graph.Drain();
        Assert.AreEqual(2, composition.Root.Children.Count);
        Assert.AreSame(second, composition.Root.Children[1]);
        var statusNames = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.Status)
            .Select(node => node.Name)
            .ToArray();
        CollectionAssert.Contains(statusNames, "Invoked 1 time.");
        CollectionAssert.Contains(statusNames, "No action invoked yet.");

        first.Dispose();
        graph.Drain();
        Assert.IsTrue(
            Flatten(composition.SemanticSnapshot()!)
                .Any(node => node.Name == "No action invoked yet.")
        );
        second.Dispose();
    }

    [TestMethod]
    public void SelectionExampleKeepsToggleStatePerMount()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-browser-selection-state");
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var browser = new ComponentBrowserState(composition.Root.Scope);

        var first = composition.Mount(
            composition.Root,
            theme,
            Components.SelectionExample(browser)
        );
        graph.Drain();
        var checkbox = Flatten(composition.SemanticSnapshot()!)
            .Single(node =>
                node is { Role: SemanticRole.CheckBox, Name: "Include internal components" }
            );
        Assert.AreEqual(SemanticToggleState.Off, checkbox.ToggleState);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                checkbox.Identity,
                new SemanticCommand(SemanticCommandKind.Toggle)
            )
        );
        graph.Drain();
        Assert.AreEqual(
            SemanticToggleState.On,
            Flatten(composition.SemanticSnapshot()!)
                .Single(node =>
                    node is { Role: SemanticRole.CheckBox, Name: "Include internal components" }
                )
                .ToggleState
        );

        var second = composition.Mount(
            composition.Root,
            theme,
            Components.SelectionExample(browser)
        );
        graph.Drain();
        var states = Flatten(composition.SemanticSnapshot()!)
            .Where(node =>
                node is { Role: SemanticRole.CheckBox, Name: "Include internal components" }
            )
            .Select(node => node.ToggleState)
            .ToArray();
        CollectionAssert.Contains(states, SemanticToggleState.On);
        CollectionAssert.Contains(states, SemanticToggleState.Off);

        first.Dispose();
        graph.Drain();
        Assert.AreEqual(
            SemanticToggleState.Off,
            Flatten(composition.SemanticSnapshot()!)
                .Single(node =>
                    node is { Role: SemanticRole.CheckBox, Name: "Include internal components" }
                )
                .ToggleState
        );
        second.Dispose();
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
        var example = new NavigationExampleModel(scope, uriLauncher: null);

        example.OpenDialog();
        example.SubmitDialog();
        graph.Drain();

        Assert.AreEqual(
            "The application action completed and the dialog closed.",
            example.DialogMessage
        );
    }

    [TestMethod]
    public void TableExampleKeepsSortSelectionAndLargeFixtureCallerOwned()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-table-test");
        var model = new TableExampleModel(scope);

        Assert.AreEqual(12, model.Rows.Count);
        Assert.AreEqual("row-00006", model.Selection.Value);
        model.SetSelection("row-00002");
        Assert.AreEqual("row-00002", model.Selection.Value);

        model.SetSort(new TableSort("issues", TableSortDirection.Descending));
        Assert.AreEqual("row-00008", model.Rows[0].Id);
        Assert.AreEqual("issues", model.Sort!.ColumnKey);

        model.SetFixture(true);
        Assert.AreEqual(10_000, model.Rows.Count);
        Assert.IsTrue(model.Rows.Any(row => row.Id == "row-00002"));
        Assert.AreEqual("row-00002", model.Selection.Value);
        Assert.AreEqual("10,000 generated rows", model.FixtureLabel);
    }

    [TestMethod]
    public void DateTimeExampleKeepsAppliedValuesInsideEachMount()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "component-browser-date-time-state");
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var browser = new ComponentBrowserState(composition.Root.Scope);
        var first = composition.Mount(composition.Root, theme, Components.DateTimeExample(browser));
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
        graph.Drain();
        var request = composition.Input.ActiveSurface!;
        var popup = request.CreateComposition();
        popup.Flush();
        var nextDay = Flatten(popup.SemanticSnapshot()!)
            .Single(node =>
                node.Role == SemanticRole.ListItem
                && node.Name.Contains("June 16, 2024", StringComparison.Ordinal)
            );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            popup.ExecuteSemanticCommand(nextDay.Identity, new(SemanticCommandKind.Select))
        );
        Assert.IsTrue(request.IsDismissed);
        graph.Drain();
        Assert.IsTrue(
            Flatten(composition.SemanticSnapshot()!)
                .Any(node => node.Name == "Due Sunday, June 16, 2024 at 9:30 AM.")
        );

        var second = composition.Mount(
            composition.Root,
            theme,
            Components.DateTimeExample(browser)
        );
        graph.Drain();
        var statuses = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.Status)
            .Select(node => node.Name)
            .ToArray();
        CollectionAssert.Contains(statuses, "Due Sunday, June 16, 2024 at 9:30 AM.");
        CollectionAssert.Contains(statuses, "Due Saturday, June 15, 2024 at 9:30 AM.");

        first.Dispose();
        graph.Drain();
        Assert.IsTrue(
            Flatten(composition.SemanticSnapshot()!)
                .Any(node => node.Name == "Due Saturday, June 15, 2024 at 9:30 AM.")
        );
        second.Dispose();
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
        Assert.IsTrue(
            Flatten(composition.SemanticSnapshot()!)
                .Any(node => node.Name == "Due Saturday, June 15, 2024 at 9:30 AM.")
        );
    }

    [TestMethod]
    public void TreeExampleKeepsExpansionAndSelectionControlled()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("component-browser-tree-test");
        var model = new TreeExampleModel(scope);

        Assert.AreEqual("src", model.Selection.Value);
        Assert.IsTrue(model.IsExpanded("src"));
        Assert.IsTrue(model.Roots.Count >= 3);

        model.SetSelection("fields");
        Assert.AreEqual("fields", model.Selection.Value);
        model.SetExpanded("components", true);
        Assert.IsTrue(model.IsExpanded("components"));
        model.SetExpanded("components", false);
        Assert.IsFalse(model.IsExpanded("components"));
        Assert.AreEqual("Selected Fields.", model.SelectionLabel);
        StringAssert.Contains(model.ExpansionSummary, "2 branches");
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
