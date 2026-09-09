namespace Lucent.Core;

/// <summary>Determines when keyboard movement requests a tab selection.</summary>
public enum TabActivationMode
{
    /// <summary>Arrow keys move focus; Enter or Space requests selection.</summary>
    Manual,

    /// <summary>Arrow keys move focus and request selection immediately.</summary>
    Automatic,
}

/// <summary>Determines the lifetime of inactive tab panels.</summary>
public enum TabPanelRetention
{
    /// <summary>Visited panels remain mounted and collapsed while inactive.</summary>
    RetainVisited,

    /// <summary>An inactive panel is disposed and reconstructed when selected again.</summary>
    UnmountInactive,
}

/// <summary>One keyed label and lazy content factory in a Tabs component.</summary>
public sealed class TabItem<TKey>
    where TKey : notnull
{
    /// <summary>Creates one tab descriptor.</summary>
    public TabItem(TKey key, string label, Func<ComponentRecipe> content, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(content);
        Key = key;
        Label = label;
        Content = content;
        Enabled = enabled;
    }

    /// <summary>Gets the stable application key.</summary>
    public TKey Key { get; }

    /// <summary>Gets the visible and accessible tab label.</summary>
    public string Label { get; }

    /// <summary>Gets the lazy panel factory.</summary>
    public Func<ComponentRecipe> Content { get; }

    /// <summary>Gets whether the tab can receive focus and selection requests.</summary>
    public bool Enabled { get; }
}
