using Lucent.ComponentBrowser;
using Lucent.Core;
using Lucent.Renderer.Skia;

namespace Lucent.ComponentBrowser.Tests;

[TestClass]
public sealed class BrowserNavigationTests
{
    [TestMethod]
    public void HistoryRetainsShellButRetiresAndResetsExampleState()
    {
        using var composition = new Composition(new ReactiveGraph(), "browser-route-history");
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var browser = new ComponentBrowserState(composition.Root.Scope);
        var shell = composition.Mount(
            composition.Root,
            theme,
            Components.ComponentBrowser(browser, theme)
        );
        composition.Flush();
        var search = Nodes(composition).Single(node => node.Name == "Search components").Identity;
        var apply = Nodes(composition)
            .Single(node => node.Role == SemanticRole.Button && node.Name == "Apply change");
        var oldControl = Elements(shell).Single(element => element.Id == apply.Identity.ElementId);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(apply.Identity, new(SemanticCommandKind.Invoke))
        );
        composition.Flush();
        Assert.IsTrue(Nodes(composition).Any(node => node.Name == "Invoked 1 time."));

        browser.Search = "command";
        browser.ToggleDensity();
        browser.Select("fields");
        composition.Flush();
        Assert.AreEqual("fields", browser.SelectedId);
        Assert.IsTrue(oldControl.IsDisposed);
        Assert.IsFalse(shell.IsDisposed);
        Assert.AreEqual(
            search,
            Nodes(composition).Single(node => node.Name == "Search components").Identity
        );
        var field = Nodes(composition)
            .Single(node => node.Role == SemanticRole.TextField && node.Name == "Workspace name");
        var fieldControl = Elements(shell)
            .Single(element => element.Id == field.Identity.ElementId);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                field.Identity,
                new(SemanticCommandKind.SetValue, "Local draft")
            )
        );
        composition.Flush();
        Assert.IsTrue(Nodes(composition).Any(node => node.Value == "Local draft"));

        Invoke(composition, "Back");
        Assert.AreEqual("buttons", browser.SelectedId);
        Assert.IsTrue(fieldControl.IsDisposed);
        Assert.IsTrue(Nodes(composition).Any(node => node.Name == "No action invoked yet."));
        Assert.AreEqual("command", browser.Search);
        Assert.AreEqual(BrowserDensity.Compact, browser.Density);
        Invoke(composition, "Forward");
        Assert.AreEqual("fields", browser.SelectedId);
        Assert.IsTrue(
            Nodes(composition)
                .Any(node => node.Role == SemanticRole.TextField && node.Value == "Lucent")
        );
        Assert.IsFalse(Nodes(composition).Any(node => node.Value == "Local draft"));
        Assert.AreEqual(
            search,
            Nodes(composition).Single(node => node.Name == "Search components").Identity
        );
    }

    [TestMethod]
    public void UnknownExamplesFailBeforeReplacingCurrentExampleAndSameSelectionDoesNotPush()
    {
        using var composition = new Composition(new ReactiveGraph(), "browser-route-rejection");
        composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var browser = new ComponentBrowserState(composition.Root.Scope);
        composition.Mount(composition.Root, theme, Components.ComponentBrowser(browser, theme));
        composition.Flush();
        var entry = browser.Navigation.Current!.EntryId;
        var apply = Nodes(composition)
            .Single(node => node.Role == SemanticRole.Button && node.Name == "Apply change")
            .Identity;
        browser.Select("buttons");
        composition.Flush();
        Assert.AreEqual(entry, browser.Navigation.Current!.EntryId);
        Assert.IsFalse(browser.Navigation.CanGoBack);
        var rejected = browser.Navigation.Navigate(
            ComponentBrowserRoutes.Example("not-in-catalog")
        );
        composition.Flush();
        Assert.IsTrue(rejected.Completion.IsCompletedSuccessfully);
        Assert.AreEqual(NavigationOutcomeKind.Failed, rejected.Completion.Result.Kind);
        Assert.IsFalse(browser.Navigation.IsTerminated);
        Assert.AreEqual(entry, browser.Navigation.Current!.EntryId);
        Assert.AreEqual(
            apply,
            Nodes(composition)
                .Single(node => node.Role == SemanticRole.Button && node.Name == "Apply change")
                .Identity
        );
    }

    private static void Invoke(Composition composition, string name)
    {
        var node = Nodes(composition)
            .Single(node => node.Role == SemanticRole.Button && node.Name == name);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(node.Identity, new(SemanticCommandKind.Invoke))
        );
        composition.Flush();
    }

    private static IEnumerable<SemanticSnapshot> Nodes(Composition composition) =>
        Flatten(composition.SemanticSnapshot()!);

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flatten));

    private static IEnumerable<Element> Elements(Element root) =>
        new[] { root }.Concat(root.Children.SelectMany(Elements));
}
