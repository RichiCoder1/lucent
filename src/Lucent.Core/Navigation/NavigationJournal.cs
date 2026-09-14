namespace Lucent.Core;

/// <summary>Owner-thread journal state used by <see cref="NavigationSession"/>.</summary>
internal sealed class NavigationJournal
{
    private readonly int _capacity;
    private List<NavigationSnapshot> _entries = [];
    private int _currentIndex = -1;
    private long _nextEntryId = 1;

    internal NavigationJournal(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    internal int Count => _entries.Count;

    internal int CurrentIndex => _currentIndex;

    internal bool CanGoBack => _currentIndex > 0;

    internal bool CanGoForward => _currentIndex >= 0 && _currentIndex < _entries.Count - 1;

    internal NavigationSnapshot? Current => _currentIndex < 0 ? null : _entries[_currentIndex];

    internal NavigationSnapshot? Previous => _currentIndex > 0 ? _entries[_currentIndex - 1] : null;

    internal NavigationSnapshot? Next => CanGoForward ? _entries[_currentIndex + 1] : null;

    internal NavigationJournalSnapshot Snapshot() => new(_entries, _currentIndex, _capacity);

    internal NavigationSnapshot Preview(
        RouteLocation location,
        RouteMatch match,
        NavigationHistoryAction history
    )
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(match);
        ValidateRouteAction(history);
        return new(_nextEntryId, location, match);
    }

    internal NavigationJournalPlan Plan(NavigationHistoryAction history, NavigationSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return history switch
        {
            NavigationHistoryAction.Push => PlanPush(target),
            NavigationHistoryAction.Replace => PlanReplace(target),
            NavigationHistoryAction.Back => PlanTraversal(target, -1),
            NavigationHistoryAction.Forward => PlanTraversal(target, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(history)),
        };
    }

    internal void Commit(NavigationJournalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _entries = plan.Entries.ToList();
        _currentIndex = plan.CurrentIndex;
        _nextEntryId = plan.NextEntryId;
    }

    internal NavigationSnapshot Initialize(RouteLocation location, RouteMatch match)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(match);
        if (_entries.Count != 0)
            throw new InvalidOperationException(
                "A navigation journal can be initialized only once."
            );
        var entry = new NavigationSnapshot(_nextEntryId, location, match);
        _entries.Add(entry);
        _currentIndex = 0;
        _nextEntryId++;
        return entry;
    }

    private NavigationJournalPlan PlanPush(NavigationSnapshot target)
    {
        var entries = _entries.Take(_currentIndex + 1).ToList();
        var retired = _entries.Skip(_currentIndex + 1).ToList();
        var candidate = new NavigationSnapshot(_nextEntryId, target.Location, target.Match);
        entries.Add(candidate);
        while (entries.Count > _capacity)
        {
            retired.Add(entries[0]);
            entries.RemoveAt(0);
        }
        return new(entries, entries.Count - 1, checked(_nextEntryId + 1), candidate, retired);
    }

    private NavigationJournalPlan PlanReplace(NavigationSnapshot target)
    {
        if (_currentIndex < 0)
            return PlanPush(target);
        var entries = _entries.ToList();
        var retired = new List<NavigationSnapshot> { entries[_currentIndex] };
        var candidate = new NavigationSnapshot(_nextEntryId, target.Location, target.Match);
        entries[_currentIndex] = candidate;
        return new(entries, _currentIndex, checked(_nextEntryId + 1), candidate, retired);
    }

    private NavigationJournalPlan PlanTraversal(NavigationSnapshot target, int direction)
    {
        if (_currentIndex < 0)
            throw new InvalidOperationException("A history traversal requires a committed route.");
        var expectedIndex = checked(_currentIndex + direction);
        if (expectedIndex < 0 || expectedIndex >= _entries.Count)
            throw new InvalidOperationException(
                "The requested history direction is at its boundary."
            );
        var committed = _entries[expectedIndex];
        if (committed.EntryId != target.EntryId)
            throw new InvalidOperationException(
                "The history target no longer belongs to the journal."
            );
        return new(_entries, expectedIndex, _nextEntryId, committed, []);
    }

    private static void ValidateRouteAction(NavigationHistoryAction history)
    {
        if (history is not NavigationHistoryAction.Push and not NavigationHistoryAction.Replace)
            throw new ArgumentOutOfRangeException(nameof(history));
    }
}

/// <summary>A validated journal mutation awaiting the publication phase.</summary>
internal sealed class NavigationJournalPlan
{
    internal NavigationJournalPlan(
        IReadOnlyList<NavigationSnapshot> entries,
        int currentIndex,
        long nextEntryId,
        NavigationSnapshot target,
        IReadOnlyList<NavigationSnapshot> retired
    )
    {
        if (currentIndex < 0 || currentIndex >= entries.Count)
            throw new ArgumentOutOfRangeException(nameof(currentIndex));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextEntryId);
        Entries = entries.ToArray();
        CurrentIndex = currentIndex;
        NextEntryId = nextEntryId;
        Target = target;
        Retired = retired.ToArray();
    }

    internal IReadOnlyList<NavigationSnapshot> Entries { get; }

    internal int CurrentIndex { get; }

    internal long NextEntryId { get; }

    internal NavigationSnapshot Target { get; }

    internal IReadOnlyList<NavigationSnapshot> Retired { get; }

    internal NavigationJournalSnapshot Snapshot(int capacity) =>
        new(Entries, CurrentIndex, capacity);
}
