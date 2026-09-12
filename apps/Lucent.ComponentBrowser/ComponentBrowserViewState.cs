namespace Lucent.ComponentBrowser;

public sealed class ComponentBrowserViewState
{
    public ComponentBrowserViewState(ReactiveScope owner, ComponentBrowserState browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        Split = new(owner, 300, 240, 440, name: "component-browser.split");
        NavigationViewport = new(owner, name: "component-browser.navigation");
        DetailViewport = new(owner, name: "component-browser.detail");
        SourceViewport = new(owner, name: "component-browser.source");
        string? previous = null;
        _ = owner.Effect(
            () =>
            {
                var selected = browser.SelectedId;
                if (selected == previous)
                    return;
                previous = selected;
                DetailViewport.Offset = default;
                SourceViewport.Offset = default;
            },
            "component-browser-view.selection"
        );
    }

    public SplitPaneState Split { get; }
    public ViewportState NavigationViewport { get; }
    public ViewportState DetailViewport { get; }
    public ViewportState SourceViewport { get; }
}
