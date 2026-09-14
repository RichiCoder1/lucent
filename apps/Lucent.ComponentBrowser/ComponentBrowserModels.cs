namespace Lucent.ComponentBrowser;

public enum BrowserDensity
{
    Comfortable,
    Compact,
}

public enum ExampleState
{
    Default,
    Disabled,
    Busy,
    Error,
}

public sealed record ComponentCatalogItem(
    string Id,
    string Title,
    string Family,
    string Summary,
    string SourceFile,
    Func<ComponentBrowserState, ComponentRecipe> CreateExample,
    string Usage,
    string Accessibility,
    string? DependencyNote = null
);
