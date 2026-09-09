using System.Runtime.InteropServices;
using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Converts SDL input on the host UI thread; Core receives only portable commands and text.</summary>
internal sealed class WindowsInputAdapter : IDisposable
{
    private readonly InputRouter _router;
    private readonly nint _window;
    private readonly WindowsClipboard? _clipboard;
    private readonly TextInputTransport _textInput;
    private readonly WindowsCoordinateScale? _coordinateScaleOverride;
    private readonly Dictionary<int, (float X, float Y)> _pointers = [];
    private readonly Dictionary<int, HashSet<PointerButton>> _pressedButtons = [];
    private bool _disposed;
    private bool _windowFocused = true;
    private bool _repaintRequested;
    private bool _imeCompositionActive;
    private ElementIdentity? _imeCompositionTarget;
    private bool _rejectQueuedTextUntilRefresh;

    internal WindowsInputAdapter(
        Composition composition,
        nint window = 0,
        WindowsClipboard? clipboard = null,
        TextInputTransport? textInput = null,
        WindowsCoordinateScale? coordinateScale = null
    )
    {
        _router = (composition ?? throw new ArgumentNullException(nameof(composition))).Input;
        _window = window;
        _clipboard = clipboard;
        _textInput = textInput ?? TextInputTransport.Sdl;
        _coordinateScaleOverride = coordinateScale;
    }

    internal bool WindowFocused => _windowFocused;
    internal (float X, float Y)? PointerPosition { get; private set; }
    internal long CaretActivity { get; private set; }

    internal bool Dispatch(SDL.Event @event)
    {
        if (_disposed)
            return false;
        if (
            (SDL.EventType)@event.Type
            is SDL.EventType.KeyDown
                or SDL.EventType.TextInput
                or SDL.EventType.TextEditing
                or SDL.EventType.MouseButtonDown
                or SDL.EventType.WindowFocusGained
        )
            CaretActivity++;
        switch ((SDL.EventType)@event.Type)
        {
            case SDL.EventType.MouseMotion:
                return Pointer(
                    @event.Motion.Which,
                    PointerCommandKind.Move,
                    @event.Motion.X,
                    @event.Motion.Y,
                    PointerButton.None,
                    MapModifiers(SDL.GetModState())
                );
            case SDL.EventType.MouseButtonDown:
                return Button(@event.Button, PointerCommandKind.Down);
            case SDL.EventType.MouseButtonUp:
                return Button(@event.Button, PointerCommandKind.Up);
            case SDL.EventType.MouseWheel:
                return Wheel(@event.Wheel);
            case SDL.EventType.KeyDown:
            case SDL.EventType.KeyUp:
                return Key(@event.Key);
            case SDL.EventType.TextInput:
                return DispatchText(
                    new(TextInputKind.Commit, Marshal.PtrToStringUTF8(@event.Text.Text) ?? "")
                );
            case SDL.EventType.TextEditing:
                return DispatchText(
                    new(
                        TextInputKind.Preedit,
                        Marshal.PtrToStringUTF8(@event.Edit.Text) ?? "",
                        @event.Edit.Start,
                        @event.Edit.Length
                    )
                );
            case SDL.EventType.WindowFocusLost:
                _windowFocused = false;
                _repaintRequested = true;
                PointerPosition = null;
                _router.ClearPointerHover();
                CleanupInput();
                return false;
            case SDL.EventType.WindowFocusGained:
                _windowFocused = true;
                _repaintRequested = true;
                SyncTextInput();
                return false;
            case SDL.EventType.WindowMouseLeave:
                PointerPosition = null;
                _router.ClearPointerHover();
                _repaintRequested = true;
                return false;
            default:
                return false;
        }
    }

    internal void CancelPointers()
    {
        if (_disposed)
            return;
        var pointers = _pointers.ToArray();
        if (pointers.Length == 0)
            return;
        List<Exception>? errors = null;
        try
        {
            foreach (var (pointer, point) in pointers)
                try
                {
                    _ = _router.DispatchPointer(
                        new(PointerCommandKind.Cancel, pointer, point.X, point.Y)
                    );
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
        }
        finally
        {
            _pointers.Clear();
            _pressedButtons.Clear();
            _repaintRequested = true;
        }
        if (errors is { Count: > 0 })
            throw new AggregateException("Windows pointer cancellation failed.", errors);
    }

    internal bool ConsumeRepaintRequest()
    {
        var requested = _repaintRequested;
        _repaintRequested = false;
        return requested;
    }

    internal void RefreshTextInput()
    {
        if (!_disposed)
        {
            ReconcileImeComposition();
            SyncTextInput();
            _rejectQueuedTextUntilRefresh = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        try
        {
            CleanupInput();
        }
        finally
        {
            _disposed = true;
        }
    }

    private bool Button(SDL.MouseButtonEvent @event, PointerCommandKind kind)
    {
        if (MapButton(@event.Button) is not { } button)
            return false;
        return Pointer(
            @event.Which,
            kind,
            @event.X,
            @event.Y,
            kind is PointerCommandKind.Down or PointerCommandKind.Up ? button : PointerButton.None,
            MapModifiers(SDL.GetModState())
        );
    }

    private bool Pointer(
        uint source,
        PointerCommandKind kind,
        float x,
        float y,
        PointerButton button,
        KeyModifiers modifiers
    )
    {
        if (source > int.MaxValue || !float.IsFinite(x) || !float.IsFinite(y))
            return false;
        var pointer = (int)source;
        var scale = CoordinateScale;
        x = scale.WindowToLogical(x);
        y = scale.WindowToLogical(y);
        if (!float.IsFinite(x) || !float.IsFinite(y))
            return false;
        PointerPosition = (x, y);
        try
        {
            _ = _router.DispatchPointer(new(kind, pointer, x, y, button, modifiers));
            ReconcileImeComposition();
        }
        finally
        {
            if (kind == PointerCommandKind.Down)
            {
                _pointers[pointer] = (x, y);
                if (!_pressedButtons.TryGetValue(pointer, out var buttons))
                    _pressedButtons.Add(pointer, buttons = []);
                buttons.Add(button);
            }
            else if (kind == PointerCommandKind.Move && _pointers.ContainsKey(pointer))
                _pointers[pointer] = (x, y);
            else if (kind == PointerCommandKind.Up)
            {
                if (!_pressedButtons.TryGetValue(pointer, out var buttons))
                    _pointers.Remove(pointer);
                else if (buttons.Remove(button) && buttons.Count == 0)
                {
                    _pressedButtons.Remove(pointer);
                    _pointers.Remove(pointer);
                }
            }
            else if (kind == PointerCommandKind.Cancel)
            {
                _pressedButtons.Remove(pointer);
                _pointers.Remove(pointer);
            }
        }
        return true;
    }

    private bool Wheel(SDL.MouseWheelEvent @event)
    {
        if (
            !float.IsFinite(@event.MouseX)
            || !float.IsFinite(@event.MouseY)
            || !float.IsFinite(@event.X)
            || !float.IsFinite(@event.Y)
        )
            return false;
        var scale = CoordinateScale;
        var mouseX = scale.WindowToLogical(@event.MouseX);
        var mouseY = scale.WindowToLogical(@event.MouseY);
        if (!float.IsFinite(mouseX) || !float.IsFinite(mouseY))
            return false;
        var direction = @event.Direction == SDL.MouseWheelDirection.Flipped ? -1f : 1f;
        const float logicalPixelsPerWheelUnit = 40f;
        var result = _router.DispatchWheel(
            new(
                mouseX,
                mouseY,
                @event.X * direction * logicalPixelsPerWheelUnit,
                -@event.Y * direction * logicalPixelsPerWheelUnit
            )
        );
        _repaintRequested |= result.Handled;
        return true;
    }

    private bool Key(SDL.KeyboardEvent @event)
    {
        var modifiers = MapModifiers(@event.Mod);
        var key = MapKey(@event.Key) ?? MapShortcut(@event.Key, modifiers);
        if (key is null)
            return false;
        var command = new KeyCommand(
            @event.Down ? KeyCommandKind.Down : KeyCommandKind.Up,
            key.Value,
            modifiers,
            @event.Down && @event.Repeat
        );
        ReconcileImeComposition();
        if (_imeCompositionActive && TextInputCommand.IsImeOwnedKey(command))
        {
            if (@event.Down && key == Core.Key.Escape)
            {
                _ = CancelText();
            }
            // The native IME gets first ownership of editing/navigation keys
            // while a preedit is active.  Sending them to the editor would
            // mutate committed selection against DisplayText and can cause a
            // later commit to be applied twice.
            return true;
        }
        _ = _router.DispatchKey(command);
        ReconcileImeComposition();
        if (@event.Down)
            ProcessClipboardRequests();
        return true;
    }

    internal bool DispatchText(TextInputCommand command)
    {
        if (!_windowFocused)
            return false;
        ReconcileImeComposition();
        if (_rejectQueuedTextUntilRefresh)
            return false;
        if (command.Kind is TextInputKind.Commit or TextInputKind.Preedit)
        {
            if (!TextInputCommand.TryNormalizeMultiline(command.Text, out var text))
                return false;
            command = command with { Text = text };
        }
        try
        {
            var result = _router.DispatchText(command);
            _repaintRequested |= result.Handled;
            if (result.Handled)
            {
                if (command.Kind == TextInputKind.Preedit && command.Text.Length != 0)
                {
                    _imeCompositionActive = true;
                    _imeCompositionTarget = result.Target ?? _router.FocusedElement;
                }
                else
                {
                    if (command.Kind == TextInputKind.Cancel)
                        ClearNativeComposition();
                    _imeCompositionActive = false;
                    _imeCompositionTarget = null;
                }
            }
            ReconcileImeComposition();
            return result.Handled;
        }
        catch (Exception error) when (error is ObjectDisposedException or ArgumentException)
        {
            return false;
        }
    }

    private bool CancelText()
    {
        try
        {
            var result = _router.DispatchText(new(TextInputKind.Cancel, ""));
            _repaintRequested |= result.Handled;
            if (result.Handled)
            {
                ClearNativeComposition();
                _imeCompositionActive = false;
                _imeCompositionTarget = null;
            }
            return result.Handled;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void ReconcileImeComposition()
    {
        ElementIdentity? focused;
        bool coreActive;
        try
        {
            focused = _router.FocusedElement;
            coreActive = _router.HasTextComposition;
        }
        catch (ObjectDisposedException)
        {
            focused = null;
            coreActive = false;
        }
        if (!_imeCompositionActive)
        {
            if (coreActive)
            {
                _imeCompositionActive = true;
                _imeCompositionTarget = focused;
            }
            return;
        }

        if (coreActive && focused == _imeCompositionTarget)
            return;

        ClearNativeComposition();
        if (_imeCompositionActive && focused != _imeCompositionTarget)
            _rejectQueuedTextUntilRefresh = true;
        _imeCompositionActive = coreActive;
        _imeCompositionTarget = coreActive ? focused : null;
    }

    private void ClearNativeComposition()
    {
        if (_window == 0 || !_imeCompositionActive)
            return;
        if (!_textInput.ClearComposition(_window))
            throw new InvalidOperationException("SDL_ClearComposition: " + SDL.GetError());
        _repaintRequested = true;
    }

    /// <summary>Completes clipboard work requested by portable commands, including popup menu actions.</summary>
    internal void ProcessClipboardRequests()
    {
        if (_clipboard is null || !_router.TryTakeClipboardRequest(out var request))
            return;
        if (request.Operation == TextClipboardOperation.Paste)
        {
            var read = _clipboard.Read();
            _repaintRequested |= _router.CompleteClipboardRequest(
                request,
                read.Succeeded,
                read.Text
            );
        }
        else
        {
            var write = _clipboard.Write(request.Text!);
            _repaintRequested |= _router.CompleteClipboardRequest(request, write.Succeeded);
        }
    }

    private void SyncTextInput()
    {
        if (_window == 0)
            return;
        if (!_windowFocused)
        {
            StopTextInput();
            return;
        }
        if (_router.TryGetCaretGeometry(out var caret))
        {
            var scale = CoordinateScale;
            var area = new SDL.Rect
            {
                X = scale.LogicalToWindowRound(caret.X),
                Y = scale.LogicalToWindowRound(caret.Y),
                W = Math.Max(1, scale.LogicalToWindowCeiling(caret.Width)),
                H = Math.Max(1, scale.LogicalToWindowCeiling(caret.Height)),
            };
            if (!_textInput.Active(_window) && !_textInput.Start(_window))
                throw new InvalidOperationException("SDL_StartTextInput: " + SDL.GetError());
            if (!_textInput.SetArea(_window, area, 0))
                throw new InvalidOperationException("SDL_SetTextInputArea: " + SDL.GetError());
        }
        else
            StopTextInput();
    }

    private void StopTextInput()
    {
        if (_window != 0 && _textInput.Active(_window) && !_textInput.Stop(_window))
            throw new InvalidOperationException("SDL_StopTextInput: " + SDL.GetError());
    }

    private void CleanupInput()
    {
        var errors = new List<Exception>();
        try
        {
            // A Core-only focus or semantic cancellation may have ended the
            // preedit before the host began cleanup.  Reconcile once before
            // routing the portable cancel so the native IME is still reset.
            ReconcileImeComposition();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            CancelPointers();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            _ = CancelText();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            ClearNativeComposition();
            _imeCompositionActive = false;
            _imeCompositionTarget = null;
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            StopTextInput();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        if (errors.Count != 0)
            throw new AggregateException("Windows input cleanup failed.", errors);
    }

    private WindowsCoordinateScale CoordinateScale =>
        _coordinateScaleOverride ?? WindowsCoordinateScale.ForWindow(_window);

    internal static Key? MapKey(SDL.Keycode key) =>
        key switch
        {
            SDL.Keycode.F10 => Core.Key.F10,
            SDL.Keycode.Application => Core.Key.ContextMenu,
            SDL.Keycode.Tab => Core.Key.Tab,
            SDL.Keycode.Return or SDL.Keycode.KpEnter => Core.Key.Enter,
            SDL.Keycode.Space => Core.Key.Space,
            SDL.Keycode.Escape => Core.Key.Escape,
            SDL.Keycode.Left => Core.Key.Left,
            SDL.Keycode.Right => Core.Key.Right,
            SDL.Keycode.Up => Core.Key.Up,
            SDL.Keycode.Down => Core.Key.Down,
            SDL.Keycode.Pageup => Core.Key.PageUp,
            SDL.Keycode.Pagedown => Core.Key.PageDown,
            SDL.Keycode.Home => Core.Key.Home,
            SDL.Keycode.End => Core.Key.End,
            SDL.Keycode.Backspace => Core.Key.Backspace,
            SDL.Keycode.Delete => Core.Key.Delete,
            _ => null,
        };

    internal static Key? MapShortcut(SDL.Keycode key, KeyModifiers modifiers) =>
        (modifiers & KeyModifiers.Alt) != 0
        || (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0
            ? null
            : key switch
            {
                SDL.Keycode.A => Core.Key.A,
                SDL.Keycode.C => Core.Key.C,
                SDL.Keycode.F => Core.Key.F,
                SDL.Keycode.N => Core.Key.N,
                SDL.Keycode.S => Core.Key.S,
                SDL.Keycode.V => Core.Key.V,
                SDL.Keycode.X => Core.Key.X,
                SDL.Keycode.Y => Core.Key.Y,
                SDL.Keycode.Z => Core.Key.Z,
                _ => null,
            };

    internal static PointerButton? MapButton(byte button) =>
        button switch
        {
            1 => PointerButton.Primary,
            2 => PointerButton.Middle,
            3 => PointerButton.Secondary,
            _ => null,
        };

    internal static KeyModifiers MapModifiers(SDL.Keymod modifiers)
    {
        var flags = (ushort)modifiers;
        var result = KeyModifiers.None;
        if ((flags & (ushort)SDL.Keymod.Shift) != 0)
            result |= KeyModifiers.Shift;
        if ((flags & (ushort)SDL.Keymod.Ctrl) != 0)
            result |= KeyModifiers.Control;
        if ((flags & (ushort)SDL.Keymod.Alt) != 0)
            result |= KeyModifiers.Alt;
        if ((flags & (ushort)SDL.Keymod.GUI) != 0)
            result |= KeyModifiers.Meta;
        return result;
    }
}

/// <summary>Separates Windows' physical SDL coordinates, Core logical coordinates, and backing density.</summary>
/// <remarks>
/// SDL reports Windows pointer coordinates in the platform's native screen/window units. On Windows
/// those units are physical device pixels, while Lucent layout is in device-independent logical
/// pixels. The content scale converts between those spaces; pixel density only converts a logical
/// render size to a window-coordinate size when the backing buffer has a density other than one.
/// </remarks>
internal readonly record struct WindowsCoordinateScale
{
    internal WindowsCoordinateScale(float contentScale, float pixelDensity)
    {
        if (
            !float.IsFinite(contentScale)
            || contentScale <= 0
            || !float.IsFinite(pixelDensity)
            || pixelDensity <= 0
        )
            throw new ArgumentOutOfRangeException(nameof(contentScale));
        ContentScale = contentScale;
        PixelDensity = pixelDensity;
    }

    internal float ContentScale { get; }
    internal float PixelDensity { get; }

    internal float WindowToLogical(float windowCoordinate) =>
        windowCoordinate * PixelDensity / ContentScale;

    internal float LogicalToWindow(float logicalCoordinate) =>
        logicalCoordinate * ContentScale / PixelDensity;

    internal int LogicalToWindowRound(float logicalCoordinate) =>
        checked((int)MathF.Round(LogicalToWindow(logicalCoordinate)));

    internal int LogicalToWindowCeiling(float logicalCoordinate) =>
        checked((int)MathF.Ceiling(LogicalToWindow(logicalCoordinate)));

    /// <summary>Converts an SDL Windows event coordinate to a physical screen coordinate.</summary>
    /// <remarks>Windows SDL window/screen coordinates are already physical pixels; density is for the backing buffer.</remarks>
    internal static int WindowToScreenPixels(float windowCoordinate) =>
        checked((int)MathF.Round(windowCoordinate));

    /// <summary>Converts a Core logical edge to a physical screen coordinate for Windows UIA/placement.</summary>
    internal int LogicalToScreenPixels(float logicalCoordinate) =>
        checked((int)MathF.Round(logicalCoordinate * ContentScale));

    internal static WindowsCoordinateScale ForWindow(nint window)
    {
        if (window == 0)
            return new(1, 1);
        // Input cleanup can observe a late callback after SDL has released its native window.
        // Preserve the adapter's identity fallback for that teardown edge; the production
        // bootstrap creates and retains the SDL window before dispatch begins.
        if (SDL.GetWindowProperties(window) == 0)
            return new(1, 1);
        var scale = WindowsPopupHost.ScaleForWindow(window);
        var density = SDL.GetWindowPixelDensity(window);
        return new(scale, density);
    }
}

internal sealed class TextInputTransport(
    Func<nint, bool> active,
    Func<nint, bool> start,
    Func<nint, bool> stop,
    Func<nint, SDL.Rect, int, bool> setArea,
    Func<nint, bool>? clearComposition = null
)
{
    internal static TextInputTransport Sdl { get; } =
        new(
            SDL.TextInputActive,
            SDL.StartTextInput,
            SDL.StopTextInput,
            SetTextInputArea,
            SDL.ClearComposition
        );
    internal Func<nint, bool> Active { get; } = active;
    internal Func<nint, bool> Start { get; } = start;
    internal Func<nint, bool> Stop { get; } = stop;
    internal Func<nint, SDL.Rect, int, bool> SetArea { get; } = setArea;
    internal Func<nint, bool> ClearComposition { get; } = clearComposition ?? (_ => true);

    private static bool SetTextInputArea(nint window, SDL.Rect area, int cursor) =>
        SDL.SetTextInputArea(window, in area, cursor);
}
