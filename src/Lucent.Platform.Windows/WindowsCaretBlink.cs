using System.Runtime.InteropServices;
using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>Owner-thread caret presentation clock. It schedules no work without an active text caret.</summary>
internal sealed partial class WindowsCaretBlink
{
    private ElementIdentity? _target;
    private LayoutRect _bounds;
    private bool _active;
    private long _activity;
    private long _deadline;
    private int _interval;
    private bool _resetBeforePresent;

    internal WindowsCaretBlink(uint interval) => SetInterval(interval);

    internal bool Visible { get; private set; }

    internal bool SetInterval(uint interval, long now = 0)
    {
        var next =
            interval == uint.MaxValue ? 0
            : interval == 0 ? 500
            : (int)Math.Min(interval, int.MaxValue);
        if (next == _interval)
            return false;
        _interval = next;
        _resetBeforePresent = true;
        return Reset(now);
    }

    internal bool UpdateActivity(bool active, long activity, long now)
    {
        if (_active == active && _activity == activity)
            return false;
        _active = active;
        _activity = activity;
        _resetBeforePresent = true;
        return Reset(now);
    }

    internal void SetTarget(ElementIdentity? target, LayoutRect bounds, long now)
    {
        if (_target == target && _bounds == bounds)
            return;
        _target = target;
        _bounds = bounds;
        _resetBeforePresent = true;
        Reset(now);
    }

    internal int WaitMilliseconds(long now) =>
        !_active || _target is null || _interval == 0
            ? -1
            : (int)Math.Clamp(_deadline - now, 0, int.MaxValue);

    internal bool Tick(long now)
    {
        if (WaitMilliseconds(now) != 0)
            return false;
        Visible = !Visible;
        _deadline = now + _interval;
        return true;
    }

    internal void BeforePresent(long now)
    {
        if (_resetBeforePresent)
            Reset(now);
        else
            Tick(now);
        _resetBeforePresent = false;
    }

    internal void Presented(long now)
    {
        // A raster slower than the interval must not schedule back-to-back full uploads.
        if (WaitMilliseconds(now) == 0)
            _deadline = now + _interval;
    }

    private bool Reset(long now)
    {
        var visible = _active && _target is not null;
        var changed = visible != Visible;
        Visible = visible;
        _deadline = now + _interval;
        return changed;
    }

    [LibraryImport("user32.dll")]
    internal static partial uint GetCaretBlinkTime();
}
