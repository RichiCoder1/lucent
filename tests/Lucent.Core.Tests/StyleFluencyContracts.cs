using Lucent.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class StyleFluencyContracts
{
    [TestMethod]
    public void GeneratedFamiliesPreserveSnapshotReaderTokenDefaultNullAndAliases()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "generated-style-families");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("generated"));
        var mode = composition.Root.Scope.Signal(LayoutMode.Flex, "generated-mode");
        var token = new Token<LayoutMode>("generated-mode-token", LayoutMode.Grid);

        var snapshot = composition.Child(composition.Root, "snapshot");
        var live = composition.Child(composition.Root, "live");
        var themed = composition.Child(composition.Root, "themed");
        var defaulted = composition.Child(composition.Root, "defaulted");
        var nullable = composition.Child(composition.Root, "nullable");
        var alias = composition.Child(composition.Root, "alias");
        var convenience = composition.Child(composition.Root, "convenience");

        snapshot.Present(theme, author: Style.Empty.Mode(LayoutMode.Grid));
        live.Present(theme, author: Style.Empty.Mode(() => mode.Value));
        themed.Present(theme, author: Style.Empty.Mode(token));
        defaulted.Present(theme, author: Style.Empty.Mode(default));
        nullable.Present(theme, author: Style.Empty.Width(null));
        alias.Present(theme, author: Style.Empty.Overflow(TextOverflow.Ellipsis));
        convenience.Present(theme, author: Style.Empty.Padding(12));
        graph.Drain();

        Assert.AreEqual(LayoutMode.Grid, snapshot.Resolve(LayoutProperties.Mode).Value);
        Assert.AreEqual(LayoutMode.Flex, live.Resolve(LayoutProperties.Mode).Value);
        Assert.AreEqual(LayoutMode.Grid, themed.Resolve(LayoutProperties.Mode).Value);
        Assert.AreEqual(LayoutMode.Flex, defaulted.Resolve(LayoutProperties.Mode).Value);
        Assert.IsNull(nullable.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(TextOverflow.Ellipsis, alias.Resolve(TypographyProperties.Overflow).Value);
        Assert.AreEqual(Insets.Uniform(12), convenience.Resolve(LayoutProperties.Padding).Value);

        mode.Value = LayoutMode.Grid;
        theme.Theme = theme.Theme.Set(token, LayoutMode.Flex);
        graph.Drain();

        Assert.AreEqual(LayoutMode.Grid, live.Resolve(LayoutProperties.Mode).Value);
        Assert.AreEqual(LayoutMode.Flex, themed.Resolve(LayoutProperties.Mode).Value);
        Assert.AreEqual(LayoutMode.Grid, snapshot.Resolve(LayoutProperties.Mode).Value);
    }

    [TestMethod]
    public void GeneratedHelpersCoverImageAndScrollWithAllDeclaredInputs()
    {
        var token = new Token<ImageFit>("fit", ImageFit.Contain);
        var style = Style
            .Empty.Fit(ImageFit.Cover)
            .Fit(token)
            .Fit(() => ImageFit.Fill)
            .Visibility(ScrollBarVisibility.Auto)
            .Visibility(new Token<ScrollBarVisibility>("visibility", ScrollBarVisibility.Hidden))
            .Visibility(() => ScrollBarVisibility.Always);

        Assert.IsNotNull(style);
    }

    [TestMethod]
    public void GeneratedRecipeHelpersStayOnStyledCapabilities()
    {
        var target = AuthorRecipe.Target<StyledCapability>((_, _, _) => { });
        var recipe = AuthorRecipe
            .Create("styled", target)
            .Padding(12)
            .Fit(ImageFit.Cover)
            .Fit(new Token<ImageFit>("recipe-fit", ImageFit.Contain))
            .ImageZoom(2f)
            .ThumbCornerRadius(() => 4f);

        Assert.IsTrue(recipe.IsValid);
        Assert.AreEqual("styled", recipe.Kind);
    }
}
