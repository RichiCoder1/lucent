namespace Lucent.Core;

/// <summary>One immutable keyed choice displayed by a ListBox or Select.</summary>
public sealed class ChoiceItem<TKey>
    where TKey : notnull
{
    /// <summary>Creates a choice with an optional visual-content factory.</summary>
    public ChoiceItem(
        TKey key,
        string label,
        bool enabled = true,
        Func<ComponentRecipe>? content = null
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Key = key;
        Label = label;
        Enabled = enabled;
        Content = content;
    }

    /// <summary>Gets the stable application key.</summary>
    public TKey Key { get; }

    /// <summary>Gets the visible and accessible label.</summary>
    public string Label { get; }

    /// <summary>Gets whether this choice can become active or selected.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the optional visual-content factory invoked only for a realized row.</summary>
    public Func<ComponentRecipe>? Content { get; }
}

/// <summary>Determines when keyboard focus requests a ListBox selection.</summary>
public enum ListBoxSelectionMode
{
    /// <summary>Moving focus requests selection immediately.</summary>
    FollowsFocus,

    /// <summary>Moving focus changes only the active key until Enter or Space confirms it.</summary>
    ExplicitConfirmation,
}
