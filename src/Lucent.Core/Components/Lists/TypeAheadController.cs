namespace Lucent.Core;

internal sealed class TypeAheadController<TKey> : IDisposable
    where TKey : notnull
{
    private static readonly TimeSpan ResetDelay = TimeSpan.FromMilliseconds(700);
    private readonly Func<ChoiceItem<TKey>[]> _items;
    private readonly KeyedSelectionPolicy<TKey, ChoiceItem<TKey>> _policy;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private ITimer? _timer;
    private string _buffer = string.Empty;
    private long _generation;
    private bool _disposed;

    internal TypeAheadController(
        Func<ChoiceItem<TKey>[]> items,
        KeyedSelectionPolicy<TKey, ChoiceItem<TKey>> policy,
        TimeProvider? timeProvider
    )
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal bool Search(string text, BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_disposed || string.IsNullOrEmpty(text) || text.Any(char.IsControl))
            return false;
        _buffer += text;
        var generation = checked(++_generation);
        ReplaceTimer(context, generation);
        var items = _items();
        var start = -1;
        if (_policy.TryGetRoving(out var active))
            start = Array.FindIndex(
                items,
                item => EqualityComparer<TKey>.Default.Equals(item.Key, active)
            );
        for (var step = 1; step <= items.Length; step++)
        {
            var index = (start + step + items.Length) % items.Length;
            var item = items[index];
            if (
                item.Enabled
                && item.Label.StartsWith(_buffer, StringComparison.CurrentCultureIgnoreCase)
            )
                return _policy.Focus(item.Key, context);
        }
        return true;
    }

    private void ReplaceTimer(BehaviorContext context, long generation)
    {
        ITimer? prior;
        lock (_gate)
        {
            if (_disposed)
                return;
            prior = _timer;
            _timer = _timeProvider.CreateTimer(
                _state =>
                {
                    try
                    {
                        _ = context.Post(() => Reset(generation));
                    }
                    catch (ObjectDisposedException) { }
                },
                null,
                ResetDelay,
                Timeout.InfiniteTimeSpan
            );
        }
        prior?.Dispose();
    }

    private void Reset(long generation)
    {
        if (_disposed || generation != _generation)
            return;
        _buffer = string.Empty;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose()
    {
        ITimer? timer;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
        _buffer = string.Empty;
    }
}
