using System.Collections.ObjectModel;

namespace Lucent.Core;

/// <summary>Determines whether an editable ComboBox can commit text without a selected item.</summary>
public enum ComboBoxSelectionPolicy
{
    /// <summary>Only an enabled suggestion can be committed.</summary>
    SelectionRequired,

    /// <summary>Nonempty query text can be committed through the explicit free-text callback.</summary>
    AllowFreeText,
}

/// <summary>The caller-owned applied ComboBox selection, including its label when suggestions omit it.</summary>
public sealed record ComboBoxSelectedItem<TKey>
    where TKey : notnull
{
    /// <summary>Creates an applied item with a stable key and nonempty display label.</summary>
    public ComboBoxSelectedItem(TKey key, string label)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Key = key;
        Label = label;
    }

    /// <summary>Gets the stable application key.</summary>
    public TKey Key { get; }

    /// <summary>Gets the applied display label independently from the current suggestion snapshot.</summary>
    public string Label { get; }
}

/// <summary>A recoverable, typed suggestion-provider result.</summary>
public sealed class ComboBoxSuggestionResult<TKey>
    where TKey : notnull
{
    private readonly IReadOnlyList<ChoiceItem<TKey>> _items;

    private ComboBoxSuggestionResult(IEnumerable<ChoiceItem<TKey>> items, string? failure)
    {
        var copy = items.ToArray();
        if (copy.Any(static item => item is null))
            throw new ArgumentException("Suggestion items cannot contain null.", nameof(items));
        var keys = new HashSet<TKey>();
        foreach (var item in copy)
            if (!keys.Add(item.Key))
                throw new ArgumentException("Suggestion keys must be unique.", nameof(items));
        _items = new ReadOnlyCollection<ChoiceItem<TKey>>(copy);
        Failure = failure;
    }

    /// <summary>Gets the immutable successful choices, or an empty list for a failed result.</summary>
    public IReadOnlyList<ChoiceItem<TKey>> Items => _items;

    /// <summary>Gets the caller-approved recoverable failure message.</summary>
    public string? Failure { get; }

    /// <summary>Gets whether the provider reported an expected recoverable failure.</summary>
    public bool IsFailure => Failure is not null;

    internal static ComboBoxSuggestionResult<TKey> CreateSuccess(
        IEnumerable<ChoiceItem<TKey>> items
    ) => new(items, null);

    internal static ComboBoxSuggestionResult<TKey> CreateFailure(string message) =>
        new([], message);
}

/// <summary>Creates typed suggestion-provider results.</summary>
public static class ComboBoxSuggestionResult
{
    /// <summary>Creates a successful immutable snapshot. An empty snapshot represents no results.</summary>
    public static ComboBoxSuggestionResult<TKey> Success<TKey>(IEnumerable<ChoiceItem<TKey>> items)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        return ComboBoxSuggestionResult<TKey>.CreateSuccess(items);
    }

    /// <summary>Creates an expected recoverable failure with safe display text.</summary>
    public static ComboBoxSuggestionResult<TKey> Failed<TKey>(string message)
        where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return ComboBoxSuggestionResult<TKey>.CreateFailure(message);
    }
}

/// <summary>Bounded timing and commit policy for an editable ComboBox.</summary>
public sealed class ComboBoxOptions
{
    /// <summary>Creates options with a 150 millisecond debounce and the system time provider.</summary>
    public ComboBoxOptions(
        ComboBoxSelectionPolicy selectionPolicy = ComboBoxSelectionPolicy.SelectionRequired,
        TimeSpan? debounce = null,
        TimeProvider? timeProvider = null
    )
    {
        if (!Enum.IsDefined(selectionPolicy))
            throw new ArgumentOutOfRangeException(nameof(selectionPolicy));
        var delay = debounce ?? TimeSpan.FromMilliseconds(150);
        if (delay < TimeSpan.Zero || delay > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(debounce));
        SelectionPolicy = selectionPolicy;
        Debounce = delay;
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the permitted commit policy.</summary>
    public ComboBoxSelectionPolicy SelectionPolicy { get; }

    /// <summary>Gets the owned delay before a query starts provider work.</summary>
    public TimeSpan Debounce { get; }

    /// <summary>Gets the clock used for the debounce delay.</summary>
    public TimeProvider TimeProvider { get; }
}

internal enum ComboBoxSuggestionState
{
    Loading,
    Ready,
    Empty,
    Failed,
}
