namespace Lucent.Core;

/// <summary>Selects the bounded version 1 capture envelope.</summary>
public enum NavigationRestorationMode
{
    /// <summary>Captures one committed canonical location.</summary>
    Location,

    /// <summary>Captures bounded history around the committed active entry.</summary>
    Journal,
}

/// <summary>Selects closed, statically implemented optional entry-state codecs.</summary>
[Flags]
public enum NavigationRestorationStateCodecs
{
    /// <summary>No entry state is persisted or decoded.</summary>
    None = 0,

    /// <summary>Stable authored focus targets and bounded logical viewport offsets.</summary>
    Interaction = 1,
}

/// <summary>Immutable opt-in bounds and state selection for navigation persistence.</summary>
public sealed class NavigationRestorationOptions
{
    /// <summary>The hard version 1 retained-entry limit.</summary>
    public const int MaximumJournalEntries = 64;

    /// <summary>The maximum encoded state size for one entry.</summary>
    public const int MaximumStateBytes = 4 * 1024;

    /// <summary>The maximum viewport positions in one interaction state.</summary>
    public const int MaximumViewports = 16;

    /// <summary>Creates explicit journal and codec options; default callers retain location mode.</summary>
    public NavigationRestorationOptions(
        NavigationRestorationMode mode = NavigationRestorationMode.Location,
        int maximumEntries = 32,
        NavigationRestorationStateCodecs stateCodecs = NavigationRestorationStateCodecs.None
    )
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntries, MaximumJournalEntries);
        if ((stateCodecs & ~NavigationRestorationStateCodecs.Interaction) != 0)
            throw new ArgumentOutOfRangeException(nameof(stateCodecs));
        Mode = mode;
        MaximumEntries = maximumEntries;
        StateCodecs = stateCodecs;
    }

    /// <summary>Gets the capture mode; journal mode also accepts location input.</summary>
    public NavigationRestorationMode Mode { get; }

    /// <summary>Gets the retained-entry limit, further bounded by session capacity at capture.</summary>
    public int MaximumEntries { get; }

    /// <summary>Gets the explicitly registered static state codecs.</summary>
    public NavigationRestorationStateCodecs StateCodecs { get; }
}

internal sealed record NavigationRestorationEntry(
    int Key,
    NavigationRestorationTarget Target,
    NavigationEntryInteractionState? State
);

internal sealed record NavigationRestorationJournal(
    IReadOnlyList<NavigationRestorationEntry> Entries,
    int ActiveIndex
);
