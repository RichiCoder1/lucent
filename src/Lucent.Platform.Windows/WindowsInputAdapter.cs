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
    private readonly Dictionary<int, (float X, float Y)> _pointers = [];
    private bool _disposed;
    private bool _windowFocused = true;
    private bool _repaintRequested;

    internal WindowsInputAdapter(
        Composition composition,
        nint window = 0,
        WindowsClipboard? clipboard = null,
        TextInputTransport? textInput = null
    )
    {
        _router = (composition ?? throw new ArgumentNullException(nameof(composition))).Input;
        _window = window;
        _clipboard = clipboard;
        _textInput = textInput ?? TextInputTransport.Sdl;
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
                    PointerButton.None
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
            SyncTextInput();
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
            kind == PointerCommandKind.Down ? button : PointerButton.None
        );
    }

    private bool Pointer(
        uint source,
        PointerCommandKind kind,
        float x,
        float y,
        PointerButton button
    )
    {
        if (source > int.MaxValue || !float.IsFinite(x) || !float.IsFinite(y))
            return false;
        var pointer = (int)source;
        PointerPosition = (x, y);
        try
        {
            _ = _router.DispatchPointer(new(kind, pointer, x, y, button));
        }
        finally
        {
            if (kind == PointerCommandKind.Down)
                _pointers[pointer] = (x, y);
            else if (kind == PointerCommandKind.Move && _pointers.ContainsKey(pointer))
                _pointers[pointer] = (x, y);
            else if (kind is PointerCommandKind.Up or PointerCommandKind.Cancel)
                _pointers.Remove(pointer);
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
        var direction = @event.Direction == SDL.MouseWheelDirection.Flipped ? -1f : 1f;
        const float logicalPixelsPerWheelUnit = 40f;
        var result = _router.DispatchWheel(
            new(
                @event.MouseX,
                @event.MouseY,
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
        _ = _router.DispatchKey(
            new(
                @event.Down ? KeyCommandKind.Down : KeyCommandKind.Up,
                key.Value,
                modifiers,
                @event.Down && @event.Repeat
            )
        );
        if (@event.Down)
            Clipboard();
        return true;
    }

    internal bool DispatchText(TextInputCommand command)
    {
        if (!_windowFocused)
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
            return result.Handled;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void Clipboard()
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
            var area = new SDL.Rect
            {
                X = (int)MathF.Round(caret.X),
                Y = (int)MathF.Round(caret.Y),
                W = Math.Max(1, (int)MathF.Ceiling(caret.Width)),
                H = Math.Max(1, (int)MathF.Ceiling(caret.Height)),
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
            StopTextInput();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        if (errors.Count != 0)
            throw new AggregateException("Windows input cleanup failed.", errors);
    }

    internal static Key? MapKey(SDL.Keycode key) =>
        key switch
        {
            SDL.Keycode.Tab => Core.Key.Tab,
            SDL.Keycode.Return or SDL.Keycode.KpEnter => Core.Key.Enter,
            SDL.Keycode.Space => Core.Key.Space,
            SDL.Keycode.Escape => Core.Key.Escape,
            SDL.Keycode.Left => Core.Key.Left,
            SDL.Keycode.Right => Core.Key.Right,
            SDL.Keycode.Up => Core.Key.Up,
            SDL.Keycode.Down => Core.Key.Down,
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

internal sealed class TextInputTransport(
    Func<nint, bool> active,
    Func<nint, bool> start,
    Func<nint, bool> stop,
    Func<nint, SDL.Rect, int, bool> setArea
)
{
    internal static TextInputTransport Sdl { get; } =
        new(SDL.TextInputActive, SDL.StartTextInput, SDL.StopTextInput, SetTextInputArea);
    internal Func<nint, bool> Active { get; } = active;
    internal Func<nint, bool> Start { get; } = start;
    internal Func<nint, bool> Stop { get; } = stop;
    internal Func<nint, SDL.Rect, int, bool> SetArea { get; } = setArea;

    private static bool SetTextInputArea(nint window, SDL.Rect area, int cursor) =>
        SDL.SetTextInputArea(window, in area, cursor);
}
