namespace Lucent.Core;

internal enum KeyedSelectionCommitMode
{
    OnNavigation,
    OnConfirmation,
}

internal enum KeyedSelectionBoundaryMode
{
    Wrap,
    Clamp,
}

/// <summary>
/// Internal keyed navigation policy shared by selection controls. It owns only the roving key;
/// the application remains authoritative for the applied selection.
/// </summary>
internal sealed class KeyedSelectionPolicy<TKey, TItem>
    where TKey : notnull
    where TItem : class
{
    private readonly Func<IReadOnlyList<TItem>> _items;
    private readonly Func<TItem, TKey> _key;
    private readonly Func<TItem, bool> _enabled;
    private readonly Func<SelectedKey<TKey>> _applied;
    private readonly Action<TKey> _requested;
    private readonly IEqualityComparer<TKey> _comparer;
    private readonly KeyedSelectionCommitMode _commitMode;
    private readonly KeyedSelectionBoundaryMode _boundaryMode;
    private readonly Signal<RovingKey> _roving;
    private readonly Dictionary<TKey, ElementIdentity> _targets;
    private readonly Dictionary<TKey, FocusTarget> _focusTargets;
    private RovingKey _pendingFocus;
    private FocusTarget? _transferredFocus;

    internal KeyedSelectionPolicy(
        ReactiveScope scope,
        string name,
        Func<IReadOnlyList<TItem>> items,
        Func<TItem, TKey> key,
        Func<TItem, bool> enabled,
        Func<SelectedKey<TKey>> applied,
        Action<TKey> requested,
        KeyedSelectionCommitMode commitMode = KeyedSelectionCommitMode.OnNavigation,
        KeyedSelectionBoundaryMode boundaryMode = KeyedSelectionBoundaryMode.Wrap,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(requested);
        if (!Enum.IsDefined(commitMode))
            throw new ArgumentOutOfRangeException(nameof(commitMode));
        if (!Enum.IsDefined(boundaryMode))
            throw new ArgumentOutOfRangeException(nameof(boundaryMode));
        _items = items;
        _key = key;
        _enabled = enabled;
        _applied = applied;
        _requested = requested;
        _commitMode = commitMode;
        _boundaryMode = boundaryMode;
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _roving = scope.Signal(default(RovingKey), name + ".roving");
        _targets = new(_comparer);
        _focusTargets = new(_comparer);
    }

    internal bool IsApplied(TKey key) =>
        _applied() is { HasValue: true } applied && _comparer.Equals(applied.Value, key);

    internal bool IsRoving(TKey key) =>
        EffectiveRoving() is { HasValue: true } active && _comparer.Equals(active.Key, key);

    internal bool TryGetRoving(out TKey key)
    {
        var active = EffectiveRoving();
        if (active.HasValue)
        {
            key = active.Key!;
            return true;
        }
        key = default!;
        return false;
    }

    internal bool Activate(TKey key, bool request)
    {
        var item = Find(key);
        if (item is null || !_enabled(item))
            return false;
        _roving.Value = new(true, key);
        if (request)
            _requested(key);
        return true;
    }

    internal bool Move(Key navigationKey, BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Move(navigationKey))
            return false;
        QueueFocus(context);
        return true;
    }

    internal bool MoveBySourceRows(int delta, BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfZero(delta);
        if (!MoveBy(delta))
            return false;
        QueueFocus(context);
        return true;
    }

    internal bool MoveBySourceRows(int delta)
    {
        ArgumentOutOfRangeException.ThrowIfZero(delta);
        return MoveBy(delta);
    }

    internal bool Move(Key navigationKey) =>
        navigationKey switch
        {
            Key.Left or Key.Up => MoveBy(-1),
            Key.Right or Key.Down => MoveBy(1),
            Key.Home => MoveToEnd(first: true),
            Key.End => MoveToEnd(first: false),
            _ => false,
        };

    internal bool Focus(TKey key, BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Activate(key, request: false))
            return false;
        QueueFocus(context);
        return true;
    }

    internal void RegisterTarget(TKey key, ElementIdentity identity) => _targets[key] = identity;

    internal void RegisterTarget(TKey key, BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var identity = context.Identity;
        RegisterTarget(key, identity);
        context.OnDispose(() => UnregisterTarget(key, identity));
    }

    internal void RegisterTarget(TKey key, BehaviorContext context, FocusTarget focusTarget)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(focusTarget);
        var identity = context.Identity;
        RegisterTarget(key, identity);
        _focusTargets[key] = focusTarget;
        if (_pendingFocus is { HasValue: true } pending && _comparer.Equals(pending.Key!, key))
            _ = context.Post(() => TransferFocus(key, focusTarget));
        context.OnDispose(() => UnregisterTarget(key, identity, focusTarget));
    }

    internal bool UnregisterTarget(TKey key, ElementIdentity identity)
    {
        if (!_targets.TryGetValue(key, out var current) || current != identity)
            return false;
        return _targets.Remove(key);
    }

    internal int RegisteredTargetCount => _targets.Count;
    internal int RetainedFocusTargetCount => _focusTargets.Count;

    internal bool RequestRoving()
    {
        var active = EffectiveRoving();
        if (!active.HasValue)
            return false;
        _requested(active.Key!);
        return true;
    }

    internal void CancelRoving()
    {
        CancelTransferredFocus();
        _pendingFocus = default;
        _roving.Value = default;
    }

    private bool MoveBy(int delta)
    {
        if (_boundaryMode == KeyedSelectionBoundaryMode.Clamp)
            return MoveClamped(delta);
        var enabled = EnabledItems();
        if (enabled.Count == 0)
            return false;
        var active = EffectiveRoving();
        var index = active.HasValue
            ? enabled.FindIndex(item => _comparer.Equals(_key(item), active.Key!))
            : -1;
        index =
            index < 0
                ? (delta > 0 ? 0 : enabled.Count - 1)
                : (index + delta + enabled.Count) % enabled.Count;
        var key = _key(enabled[index]);
        _roving.Value = new(true, key);
        if (_commitMode == KeyedSelectionCommitMode.OnNavigation)
            _requested(key);
        return true;
    }

    private bool MoveClamped(int delta)
    {
        var items = _items();
        if (items.Count == 0)
            return false;
        var direction = Math.Sign(delta);
        var active = EffectiveRoving();
        if (!active.HasValue)
            return ActivateFirstEligible(items, direction);
        var start = IndexOf(items, active.Key!);
        if (start < 0)
            return ActivateFirstEligible(items, direction);
        var target = Math.Clamp(start + delta, 0, items.Count - 1);
        if (target == start)
            return true;
        for (var index = target; index >= 0 && index < items.Count; index += direction)
            if (_enabled(items[index]))
                return SetRoving(_key(items[index]));
        for (var index = target - direction; index != start; index -= direction)
            if (_enabled(items[index]))
                return SetRoving(_key(items[index]));
        return true;
    }

    private bool ActivateFirstEligible(IReadOnlyList<TItem> items, int direction)
    {
        var index = direction > 0 ? 0 : items.Count - 1;
        var end = direction > 0 ? items.Count : -1;
        for (; index != end; index += direction)
            if (_enabled(items[index]))
                return SetRoving(_key(items[index]));
        return false;
    }

    private bool SetRoving(TKey key)
    {
        _roving.Value = new(true, key);
        if (_commitMode == KeyedSelectionCommitMode.OnNavigation)
            _requested(key);
        return true;
    }

    private bool MoveToEnd(bool first)
    {
        var enabled = EnabledItems();
        if (enabled.Count == 0)
            return false;
        var key = _key(enabled[first ? 0 : enabled.Count - 1]);
        _roving.Value = new(true, key);
        if (_commitMode == KeyedSelectionCommitMode.OnNavigation)
            _requested(key);
        return true;
    }

    private RovingKey EffectiveRoving()
    {
        var items = _items();
        if (_roving.Value is { HasValue: true } active)
        {
            var retained = Find(items, active.Key!);
            if (retained is not null && _enabled(retained))
                return active;
        }
        var applied = _applied();
        var selected = applied.HasValue ? Find(items, applied.Value) : null;
        if (selected is not null && _enabled(selected))
            return new(true, applied.Value);
        foreach (var item in items)
        {
            if (_enabled(item))
                return new(true, _key(item));
        }
        return default;
    }

    private List<TItem> EnabledItems()
    {
        var result = new List<TItem>();
        foreach (var item in _items())
        {
            if (_enabled(item))
                result.Add(item);
        }
        return result;
    }

    private TItem? Find(TKey key) => Find(_items(), key);

    private int IndexOf(IReadOnlyList<TItem> items, TKey key)
    {
        for (var index = 0; index < items.Count; index++)
            if (_comparer.Equals(_key(items[index]), key))
                return index;
        return -1;
    }

    private TItem? Find(IReadOnlyList<TItem> items, TKey key)
    {
        foreach (var item in items)
        {
            if (_comparer.Equals(_key(item), key))
                return item;
        }
        return default;
    }

    private bool FocusRoving(InputRouter router)
    {
        var active = EffectiveRoving();
        return active.HasValue
            && _targets.TryGetValue(active.Key!, out var target)
            && router.FocusSemantic(target);
    }

    private void QueueFocus(BehaviorContext context)
    {
        var active = EffectiveRoving();
        if (!active.HasValue)
            return;
        if (_boundaryMode != KeyedSelectionBoundaryMode.Clamp)
        {
            _ = context.Post(() => _ = FocusRoving(context.CompositionInput()));
            return;
        }
        CancelTransferredFocus();
        _pendingFocus = active;
        if (_focusTargets.TryGetValue(active.Key!, out var target))
            _ = context.Post(() => TransferFocus(active.Key!, target));
    }

    private void TransferFocus(TKey key, FocusTarget target)
    {
        if (
            _pendingFocus is not { HasValue: true } pending
            || !_comparer.Equals(pending.Key!, key)
            || !_focusTargets.TryGetValue(key, out var current)
            || !ReferenceEquals(current, target)
        )
            return;
        target.Request();
        _transferredFocus = target;
        _pendingFocus = default;
    }

    private void CancelTransferredFocus()
    {
        if (_transferredFocus is { } target && target.IsPending)
            target.Cancel();
        _transferredFocus = null;
    }

    private void UnregisterTarget(TKey key, ElementIdentity identity, FocusTarget focusTarget)
    {
        if (!UnregisterTarget(key, identity))
            return;
        if (
            _focusTargets.TryGetValue(key, out var retained)
            && ReferenceEquals(retained, focusTarget)
        )
            _focusTargets.Remove(key);
        if (ReferenceEquals(_transferredFocus, focusTarget))
            _transferredFocus = null;
    }

    private readonly record struct RovingKey(bool HasValue, TKey? Key);
}
