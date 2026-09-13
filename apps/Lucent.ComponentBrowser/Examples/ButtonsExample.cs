namespace Lucent.ComponentBrowser;

[ComponentState]
internal sealed partial class ButtonsExampleState
{
    [State(0)]
    internal partial int ActivationCount { get; set; }
}

public static partial class Components
{
    [LucentComponent]
    public static ComponentRecipe ButtonsExample(ComponentBrowserState browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        return Component.Define<ButtonsExampleState>(
            "buttons-example",
            (_, state) => ButtonsExampleContent(browser, state)
        );
    }
}
