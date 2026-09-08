namespace Lucent.Core;

/// <summary>Tracks a bounded pointer corridor from a parent menu item toward an actually placed submenu.</summary>
public sealed class SubmenuPointerIntent
{
    private static readonly TimeSpan MaximumGrace = TimeSpan.FromMilliseconds(300);
    private readonly TimeSpan _grace;
    private LayoutRect _childBounds;
    private TimeSpan _started;
    private float _originX;
    private float _originY;
    private float _lastX;
    private int _direction;

    /// <summary>Initializes a tracker with the maximum 300 millisecond grace period.</summary>
    public SubmenuPointerIntent()
        : this(MaximumGrace) { }

    /// <summary>Initializes a tracker with a grace period no longer than 300 milliseconds.</summary>
    public SubmenuPointerIntent(TimeSpan grace)
    {
        if (grace <= TimeSpan.Zero || grace > MaximumGrace)
            throw new ArgumentOutOfRangeException(nameof(grace));
        _grace = grace;
    }

    /// <summary>Gets whether a valid corridor is being tracked.</summary>
    public bool IsActive => _direction != 0;

    /// <summary>Begins tracking from a pointer location toward the submenu's placed screen bounds.</summary>
    public void Begin(float x, float y, LayoutRect childBounds, TimeSpan timestamp)
    {
        ValidatePoint(x, y, timestamp);
        if (
            !float.IsFinite(childBounds.X)
            || !float.IsFinite(childBounds.Y)
            || !float.IsFinite(childBounds.Width)
            || !float.IsFinite(childBounds.Height)
            || childBounds.Width <= 0
            || childBounds.Height <= 0
        )
            throw new ArgumentException(
                "Submenu bounds must be finite and positive.",
                nameof(childBounds)
            );
        _direction =
            childBounds.X >= x ? 1
            : childBounds.X + childBounds.Width <= x ? -1
            : 0;
        _originX = _lastX = x;
        _originY = y;
        _childBounds = childBounds;
        _started = timestamp;
    }

    /// <summary>Returns whether a parent-level pointer selection should be deferred for this sample.</summary>
    public bool ShouldDefer(float x, float y, TimeSpan timestamp)
    {
        ValidatePoint(x, y, timestamp);
        if (!IsActive)
            return false;
        if (timestamp < _started || timestamp - _started > _grace || Contains(_childBounds, x, y))
        {
            Cancel();
            return false;
        }
        if (_direction > 0 ? x < _lastX : x > _lastX)
        {
            Cancel();
            return false;
        }
        _lastX = x;
        var edgeX = _direction > 0 ? _childBounds.X : _childBounds.X + _childBounds.Width;
        if (
            !InsideTriangle(
                x,
                y,
                _originX,
                _originY,
                edgeX,
                _childBounds.Y,
                edgeX,
                _childBounds.Y + _childBounds.Height
            )
        )
        {
            Cancel();
            return false;
        }
        return true;
    }

    /// <summary>Cancels deferral after the pointer enters the child or the host changes direction.</summary>
    public void Cancel() => _direction = 0;

    private static bool Contains(LayoutRect bounds, float x, float y) =>
        x >= bounds.X
        && x <= bounds.X + bounds.Width
        && y >= bounds.Y
        && y <= bounds.Y + bounds.Height;

    private static bool InsideTriangle(
        float x,
        float y,
        float ax,
        float ay,
        float bx,
        float by,
        float cx,
        float cy
    )
    {
        var first = Cross(x, y, ax, ay, bx, by);
        var second = Cross(x, y, bx, by, cx, cy);
        var third = Cross(x, y, cx, cy, ax, ay);
        return !(first < 0 || second < 0 || third < 0) || !(first > 0 || second > 0 || third > 0);
    }

    private static float Cross(float px, float py, float ax, float ay, float bx, float by) =>
        (px - bx) * (ay - by) - (ax - bx) * (py - by);

    private static void ValidatePoint(float x, float y, TimeSpan timestamp)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || timestamp < TimeSpan.Zero)
            throw new ArgumentException(
                "Pointer samples and timestamps must be finite and nonnegative."
            );
    }
}
