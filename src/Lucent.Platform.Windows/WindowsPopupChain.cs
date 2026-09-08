using System.Diagnostics;
using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Owns the independent popup windows that mirror one Core menu request's active levels.</summary>
/// <remarks>
/// Core owns branch meaning and level lifetime. This class owns one SDL window per active level,
/// projects each level at its actual parent trigger, and routes keyboard input to the deepest
/// level that actually owns focus. Popup disposal is always deepest-first so a removed branch
/// cannot receive stale input.
/// </remarks>
internal sealed partial class WindowsPopupChain : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly nint _ownerWindow;
    private readonly nint _ownerHwnd;
    private readonly ContextMenuRequest _request;
    private readonly WindowsUiaDispatcher _uiaDispatcher;
    private readonly WindowsClipboard _clipboard;
    private readonly WindowsCursor _cursor;
    private readonly List<WindowsPopupHost> _levels = [];
    private readonly WindowsPopupSafeIntentSet _safeIntents = new();
    private readonly Dictionary<uint, PopupScreenPoint> _lastPointers = [];
    private WindowsPopupHost? _deferredPointerHost;
    private SDL.Event? _deferredPointerEvent;
    private uint? _deferredPointerParentWindowId;
    private WindowsPopupHost? _keyboardHost;
    private bool _levelsDirty;
    private readonly WindowsPopupFocusGate _focusGate = new();
    private bool _disposed;

    internal WindowsPopupChain(
        nint ownerWindow,
        nint ownerHwnd,
        ContextMenuRequest request,
        WindowsUiaDispatcher uiaDispatcher,
        WindowsClipboard clipboard,
        WindowsCursor cursor
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerWindow);
        ArgumentOutOfRangeException.ThrowIfZero(ownerHwnd);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(uiaDispatcher);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(cursor);
        _ownerWindow = ownerWindow;
        _ownerHwnd = ownerHwnd;
        _request = request;
        _uiaDispatcher = uiaDispatcher;
        _clipboard = clipboard;
        _cursor = cursor;
        _request.ActiveLevelsChanged += OnActiveLevelsChanged;
        try
        {
            _ = _request.CreateComposition();
            SynchronizeLevels();
        }
        catch (Exception error)
        {
            _request.ActiveLevelsChanged -= OnActiveLevelsChanged;
            List<Exception> cleanup = [];
            for (var i = _levels.Count - 1; i >= 0; i--)
                Capture(cleanup, _levels[i].Dispose);
            _levels.Clear();
            Capture(cleanup, _request.Dispose);
            if (cleanup.Count != 0)
                throw new AggregateException(
                    "Windows popup-chain construction failed during cleanup.",
                    [error, .. cleanup]
                );
            throw;
        }
    }

    internal IReadOnlyList<WindowsPopupHost> Levels => _levels;
    internal int LevelCount => _levels.Count;
    internal bool IsDismissed => _disposed || _request.IsDismissed;

    /// <summary>Gets the bounded wait needed to replay a deferred diagonal pointer sample.</summary>
    internal int SafeIntentWaitMilliseconds()
    {
        CheckThread();
        if (
            _disposed
            || _deferredPointerEvent is null
            || _deferredPointerParentWindowId is not { } parent
        )
            return -1;
        return _safeIntents.WaitMilliseconds(parent, Stopwatch.GetTimestamp());
    }

    /// <summary>Replays the last deferred pointer sample once the bounded intent grace expires.</summary>
    internal bool Tick()
    {
        CheckThread();
        if (
            _disposed
            || _deferredPointerEvent is not { } deferred
            || _deferredPointerParentWindowId is not { } parent
            || !_safeIntents.IsExpired(parent, Stopwatch.GetTimestamp())
        )
            return false;
        var host = _deferredPointerHost;
        _deferredPointerEvent = null;
        _deferredPointerHost = null;
        _deferredPointerParentWindowId = null;
        _safeIntents.Cancel(parent);
        if (host is null || host.IsDisposed)
            return false;
        var handled = host.Dispatch(deferred, dismissOnFocusLoss: false);
        if (_levelsDirty)
            SynchronizeLevels();
        return handled;
    }

    internal bool Dispatch(SDL.Event @event)
    {
        CheckThread();
        if (_disposed)
            return false;
        var type = (SDL.EventType)@event.Type;
        var windowId = WindowsPopupHost.EventWindowId(@event);
        if (type is SDL.EventType.WindowFocusLost or SDL.EventType.WindowFocusGained)
        {
            if (!OwnsWindow(windowId) && windowId != SDL.GetWindowID(_ownerWindow))
                return false;
            if (type == SDL.EventType.WindowFocusLost)
            {
                _focusGate.LostFocus();
                return true;
            }
            _focusGate.GainedFocus();
            return true;
        }

        WindowsPopupHost? host = null;
        if (
            type
                is SDL.EventType.KeyDown
                    or SDL.EventType.KeyUp
                    or SDL.EventType.TextInput
                    or SDL.EventType.TextEditing
            && windowId == SDL.GetWindowID(_ownerWindow)
        )
            host = _keyboardHost is { IsDisposed: false } focused
                ? focused
                : _levels.LastOrDefault();
        else
            host = _levels.LastOrDefault(level => level.WindowId == windowId);
        if (host is null)
            return false;

        if (type == SDL.EventType.MouseMotion)
        {
            var pointer = host.ToScreenPoint(@event.Motion.X, @event.Motion.Y);
            _lastPointers[host.WindowId] = pointer;
            _safeIntents.CancelForChild(host.WindowId);
            if (ShouldDeferPointer(host, pointer))
            {
                _deferredPointerHost = host;
                _deferredPointerEvent = @event;
                _deferredPointerParentWindowId = host.WindowId;
                return true;
            }
            _deferredPointerHost = null;
            _deferredPointerEvent = null;
            _deferredPointerParentWindowId = null;
        }

        var explicitPopupKeyboard =
            type
                is SDL.EventType.KeyDown
                    or SDL.EventType.KeyUp
                    or SDL.EventType.TextInput
                    or SDL.EventType.TextEditing
            && windowId != SDL.GetWindowID(_ownerWindow);
        var handled = host.Dispatch(@event, dismissOnFocusLoss: false);
        if (_levelsDirty)
            SynchronizeLevels();
        if (explicitPopupKeyboard && !host.IsDisposed && _levels.Contains(host))
        {
            _keyboardHost = host;
            PromoteKeyboardHostAfterKey(host, @event);
        }
        else if (type is SDL.EventType.KeyDown or SDL.EventType.KeyUp)
            PromoteKeyboardHostAfterKey(host, @event);
        else if (
            type
                is SDL.EventType.MouseButtonDown
                    or SDL.EventType.MouseButtonUp
                    or SDL.EventType.MouseMotion
            && !host.IsDisposed
            && _levels.Contains(host)
            && host.Composition.Input.FocusedElement is not null
        )
            _keyboardHost = host;
        return handled;
    }

    internal void Refresh()
    {
        CheckThread();
        if (_disposed)
            return;
        _ = Tick();
        SynchronizeLevels();
        foreach (var level in _levels)
            level.Refresh();
        SynchronizeLevels();
    }

    /// <summary>Resolves a pending focus loss after the SDL event batch has exposed internal child focus.</summary>
    internal void ResolveFocus()
    {
        CheckThread();
        if (_disposed)
            return;
        // A focus change between sibling levels raises another focus-gained event. If the native
        // foreground HWND is no longer one of our windows, the entire menu was externally dismissed.
        var foreground = GetForegroundWindow();
        if (
            _focusGate.ShouldDismiss(
                foreground == _ownerHwnd || _levels.Any(level => level.HwndHandle == foreground)
            )
        )
            Dismiss();
    }

    internal void SynchronizeLevels()
    {
        CheckThread();
        if (_disposed)
            return;
        if (!_levelsDirty && _levels.Count > 0 && !_request.IsDismissed)
            return;
        _levelsDirty = false;
        if (_request.IsDismissed)
            return;
        var active = _request.ActiveLevels.OrderBy(level => level.Depth).ToArray();
        var common = 0;
        while (
            common < _levels.Count
            && common < active.Length
            && SameLevel(_levels[common].Level, active[common])
        )
            common++;
        for (var i = 0; i < common; i++)
        {
            if (ReferenceEquals(_levels[i].Level, active[i]))
                continue;
            if (active[i].FocusFirst)
                _keyboardHost = _levels[i];
            else if (_keyboardHost is not null && _levels.IndexOf(_keyboardHost) >= i)
                _keyboardHost = i == 0 ? _levels[0] : _levels[i - 1];
        }
        for (var i = _levels.Count - 1; i >= common; i--)
        {
            if (ReferenceEquals(_deferredPointerHost, _levels[i]))
            {
                _deferredPointerHost = null;
                _deferredPointerEvent = null;
                _deferredPointerParentWindowId = null;
            }
            if (ReferenceEquals(_keyboardHost, _levels[i]))
                _keyboardHost = i == 0 ? null : _levels[i - 1];
            _lastPointers.Remove(_levels[i].WindowId);
            _levels[i].Dispose();
        }
        if (common < _levels.Count)
            _levels.RemoveRange(common, _levels.Count - common);
        for (var i = common; i < active.Length; i++)
        {
            var parent = i == 0 ? null : _levels[i - 1];
            parent?.Refresh();
            var popup = new WindowsPopupHost(
                _ownerWindow,
                parent?.WindowHandle ?? _ownerWindow,
                _request,
                active[i],
                _uiaDispatcher,
                _clipboard,
                _cursor,
                parent
            );
            _levels.Add(popup);
            if (active[i].FocusFirst || _keyboardHost is null)
                _keyboardHost = popup;
        }
        var now = Stopwatch.GetTimestamp();
        _safeIntents.Reconcile(
            _levels
                .Zip(_levels.Skip(1))
                .Select(pair =>
                    (
                        ParentWindowId: pair.First.WindowId,
                        ChildWindowId: pair.Second.WindowId,
                        Parent: pair.First.ScreenBounds,
                        Child: pair.Second.ScreenBounds,
                        OpensLeft: pair.Second.OpensLeft,
                        Pointer: LastPointer(pair.First)
                    )
                ),
            now
        );
        if (
            _deferredPointerParentWindowId is { } deferredParent
            && !_safeIntents.Contains(deferredParent)
        )
        {
            _deferredPointerHost = null;
            _deferredPointerEvent = null;
            _deferredPointerParentWindowId = null;
        }
        if (_keyboardHost is null && _levels.Count != 0)
            _keyboardHost = _levels[0];
    }

    internal void Dismiss()
    {
        CheckThread();
        if (_disposed)
            return;
        _request.Dismiss();
        _levelsDirty = true;
    }

    public void Dispose()
    {
        CheckThread();
        if (_disposed)
            return;
        _disposed = true;
        _request.ActiveLevelsChanged -= OnActiveLevelsChanged;
        List<Exception> errors = [];
        for (var i = _levels.Count - 1; i >= 0; i--)
            Capture(errors, _levels[i].Dispose);
        _levels.Clear();
        Capture(errors, () => _request.RestoreFocus());
        Capture(errors, _request.Dispose);
        if (errors.Count == 1)
            throw errors[0];
        if (errors.Count > 1)
            throw new AggregateException("Windows popup-chain cleanup failed.", errors);
    }

    private bool ShouldDeferPointer(WindowsPopupHost host, PopupScreenPoint pointer)
    {
        if (!_safeIntents.Contains(host.WindowId))
            return false;
        return _safeIntents.Observe(host.WindowId, pointer, Stopwatch.GetTimestamp());
    }

    private void PromoteKeyboardHostAfterKey(WindowsPopupHost host, SDL.Event @event)
    {
        if ((SDL.EventType)@event.Type != SDL.EventType.KeyDown)
            return;
        var key = WindowsInputAdapter.MapKey(@event.Key.Key);
        if (key is not (Key.Right or Key.Enter or Key.Space))
            return;
        var index = _levels.IndexOf(host);
        if (index < 0)
            return;
        var deepestFocused = -1;
        for (var i = _levels.Count - 1; i > index; i--)
            if (_levels[i].Composition.Input.FocusedElement is not null)
            {
                deepestFocused = i;
                break;
            }
        var target = WindowsPopupKeyboardRouting.SelectOwnerIndex(
            index,
            _levels.Count,
            deepestFocused,
            promoteFocusedChild: true
        );
        if (target > index)
            _keyboardHost = _levels[target];
    }

    private PopupScreenPoint? LastPointer(WindowsPopupHost host) =>
        _lastPointers.TryGetValue(host.WindowId, out var pointer) ? pointer : null;

    private void OnActiveLevelsChanged()
    {
        if (!_disposed)
            _levelsDirty = true;
    }

    private bool OwnsWindow(uint windowId) =>
        windowId != 0 && _levels.Any(level => level.WindowId == windowId);

    private static bool SameLevel(MenuLevelSnapshot? current, MenuLevelSnapshot next) =>
        current is not null
        && current.Depth == next.Depth
        && ReferenceEquals(current.Composition, next.Composition)
        && current.ParentTrigger == next.ParentTrigger;

    private static void Capture(List<Exception> errors, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Popup-chain access must remain on the SDL owner thread."
            );
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}

internal readonly record struct PopupScreenPoint(int X, int Y);

internal readonly record struct PopupScreenRect(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Math.Max(0, Right - Left);
    internal int Height => Math.Max(0, Bottom - Top);

    internal bool Contains(PopupScreenPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
}

internal readonly record struct PopupPlacement(int OffsetX, int OffsetY, bool OpensLeft);

/// <summary>Separates focus changes between owned popup levels from a true external dismissal.</summary>
internal sealed class WindowsPopupFocusGate
{
    private bool _pendingLoss;

    internal void LostFocus() => _pendingLoss = true;

    internal void GainedFocus() => _pendingLoss = false;

    internal bool ShouldDismiss(bool focusWithinChain)
    {
        if (!_pendingLoss)
            return false;
        _pendingLoss = false;
        return !focusWithinChain;
    }
}

internal static class WindowsPopupPlacement
{
    internal static PopupPlacement Root(
        LayoutRect anchor,
        LayoutRect desired,
        float scale,
        float density
    ) =>
        new(
            WindowsPopupHost.ToWindowUnits(
                anchor.X - WindowsPopupHost.ShadowMargin,
                scale,
                density
            ),
            WindowsPopupHost.ToWindowUnits(
                anchor.Y + anchor.Height - WindowsPopupHost.ShadowMargin,
                scale,
                density
            ),
            false
        );

    internal static PopupPlacement Submenu(
        PopupScreenRect trigger,
        LayoutRect desired,
        PopupScreenRect parent,
        SDL.Rect usable,
        float scale,
        float density
    )
    {
        var width = WindowsPopupHost.ToWindowUnits(
            desired.Width + 2 * WindowsPopupHost.ShadowMargin,
            scale,
            density
        );
        var height = WindowsPopupHost.ToWindowUnits(
            desired.Height + 2 * WindowsPopupHost.ShadowMargin,
            scale,
            density
        );
        var work = new PopupScreenRect(
            checked((int)MathF.Round(usable.X * density)),
            checked((int)MathF.Round(usable.Y * density)),
            checked((int)MathF.Round((usable.X + usable.W) * density)),
            checked((int)MathF.Round((usable.Y + usable.H) * density))
        );
        var right = trigger.Right;
        var left = trigger.Left - width;
        var opensLeft = right + width > work.Right && left >= work.Left;
        var x = opensLeft ? left : right;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        var y = Math.Clamp(trigger.Top, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new(x - parent.Left, y - parent.Top, opensLeft);
    }
}

internal sealed class WindowsPopupSafeIntent
{
    internal const int MaximumGraceMilliseconds = 300;
    private readonly SubmenuPointerIntent _intent = new(
        TimeSpan.FromMilliseconds(MaximumGraceMilliseconds)
    );
    private long _deadline;

    internal void Arm(
        PopupScreenRect parent,
        PopupScreenRect child,
        bool opensLeft,
        PopupScreenPoint? pointer,
        long now
    )
    {
        var start = pointer ?? new(opensLeft ? parent.Left : parent.Right, parent.Top);
        _intent.Begin(
            start.X,
            start.Y,
            new LayoutRect(child.Left, child.Top, child.Width, child.Height),
            Timestamp(now)
        );
        _deadline = now + Stopwatch.Frequency * MaximumGraceMilliseconds / 1000;
    }

    internal bool Observe(PopupScreenPoint pointer, long now)
    {
        return _intent.ShouldDefer(pointer.X, pointer.Y, Timestamp(now));
    }

    internal bool IsExpired(long now) => _intent.IsActive && now >= _deadline;

    internal int WaitMilliseconds(long now)
    {
        if (!_intent.IsActive)
            return -1;
        var remaining = _deadline - now;
        if (remaining <= 0)
            return 0;
        var milliseconds = (remaining * 1000d / Stopwatch.Frequency);
        return Math.Clamp((int)Math.Ceiling(milliseconds), 1, MaximumGraceMilliseconds);
    }

    internal void Cancel() => _intent.Cancel();

    private static TimeSpan Timestamp(long timestamp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timestamp, nameof(timestamp));
        return TimeSpan.FromSeconds(timestamp / (double)Stopwatch.Frequency);
    }
}

/// <summary>Maintains one safe-intent corridor for every adjacent popup-level pair.</summary>
/// <remarks>
/// A pointer entering a child cancels only the corridor that led into that child. The child may
/// also be the parent of another active level, so its outgoing corridor remains eligible while
/// the pointer continues toward the deeper submenu.
/// </remarks>
internal sealed class WindowsPopupSafeIntentSet
{
    private readonly Dictionary<uint, Boundary> _boundaries = [];

    internal bool Contains(uint parentWindowId) => _boundaries.ContainsKey(parentWindowId);

    internal void Arm(
        uint parentWindowId,
        uint childWindowId,
        PopupScreenRect parent,
        PopupScreenRect child,
        bool opensLeft,
        PopupScreenPoint? pointer,
        long now
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(parentWindowId);
        ArgumentOutOfRangeException.ThrowIfZero(childWindowId);
        if (parentWindowId == childWindowId)
            throw new ArgumentException("A popup boundary must connect two distinct windows.");
        if (!_boundaries.TryGetValue(parentWindowId, out var boundary))
            boundary = new(childWindowId, new WindowsPopupSafeIntent());
        else
            boundary = boundary with { ChildWindowId = childWindowId };
        boundary.Intent.Arm(parent, child, opensLeft, pointer, now);
        _boundaries[parentWindowId] = boundary;
    }

    internal bool Observe(uint parentWindowId, PopupScreenPoint pointer, long now) =>
        _boundaries.TryGetValue(parentWindowId, out var boundary)
        && boundary.Intent.Observe(pointer, now);

    internal bool IsExpired(uint parentWindowId, long now) =>
        _boundaries.TryGetValue(parentWindowId, out var boundary) && boundary.Intent.IsExpired(now);

    internal int WaitMilliseconds(uint parentWindowId, long now) =>
        _boundaries.TryGetValue(parentWindowId, out var boundary)
            ? boundary.Intent.WaitMilliseconds(now)
            : -1;

    internal void Cancel(uint parentWindowId)
    {
        if (_boundaries.TryGetValue(parentWindowId, out var boundary))
            boundary.Intent.Cancel();
    }

    internal void CancelForChild(uint childWindowId)
    {
        foreach (var boundary in _boundaries.Values)
            if (boundary.ChildWindowId == childWindowId)
                boundary.Intent.Cancel();
    }

    /// <summary>
    /// Keeps an existing parent-child corridor and its deadline when deeper levels are added.
    /// Re-arm only when the boundary is new or its child window was replaced.
    /// </summary>
    internal void Reconcile(
        IEnumerable<(
            uint ParentWindowId,
            uint ChildWindowId,
            PopupScreenRect Parent,
            PopupScreenRect Child,
            bool OpensLeft,
            PopupScreenPoint? Pointer
        )> boundaries,
        long now
    )
    {
        ArgumentNullException.ThrowIfNull(boundaries);
        var desired = boundaries.ToArray();
        var desiredParents = desired.Select(item => item.ParentWindowId).ToHashSet();
        foreach (
            var parentWindowId in _boundaries
                .Keys.Where(id => !desiredParents.Contains(id))
                .ToArray()
        )
            _boundaries.Remove(parentWindowId);

        foreach (var boundary in desired)
        {
            if (
                _boundaries.TryGetValue(boundary.ParentWindowId, out var current)
                && current.ChildWindowId == boundary.ChildWindowId
            )
                continue;
            Arm(
                boundary.ParentWindowId,
                boundary.ChildWindowId,
                boundary.Parent,
                boundary.Child,
                boundary.OpensLeft,
                boundary.Pointer,
                now
            );
        }
    }

    internal void Clear() => _boundaries.Clear();

    private readonly record struct Boundary(uint ChildWindowId, WindowsPopupSafeIntent Intent);
}

/// <summary>Chooses an owner-thread keyboard level without treating hover-open levels as focused.</summary>
internal static class WindowsPopupKeyboardRouting
{
    internal static int SelectOwnerIndex(
        int currentIndex,
        int levelCount,
        int deepestFocusedIndex,
        bool promoteFocusedChild
    )
    {
        if (levelCount <= 0)
            return -1;
        var current = Math.Clamp(currentIndex, 0, levelCount - 1);
        return
            promoteFocusedChild && deepestFocusedIndex > current && deepestFocusedIndex < levelCount
            ? deepestFocusedIndex
            : current;
    }
}
