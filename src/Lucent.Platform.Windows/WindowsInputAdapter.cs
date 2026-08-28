using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Converts SDL's bounded command keys and mouse events on the host UI thread; text/IME events remain unhandled until M3.</summary>
internal sealed class WindowsInputAdapter : IDisposable
{
    private readonly InputRouter _router;
    private readonly Dictionary<int, (float X, float Y)> _pointers = [];
    private bool _disposed;
    private bool _repaintRequested;

    internal WindowsInputAdapter(Composition composition) => _router = (composition ?? throw new ArgumentNullException(nameof(composition))).Input;

    internal bool Dispatch(SDL.Event @event)
    {
        if (_disposed) return false;
        switch ((SDL.EventType)@event.Type)
        {
            case SDL.EventType.MouseMotion: return Pointer(@event.Motion.Which, PointerCommandKind.Move, @event.Motion.X, @event.Motion.Y, PointerButton.None);
            case SDL.EventType.MouseButtonDown:
                return Button(@event.Button, PointerCommandKind.Down);
            case SDL.EventType.MouseButtonUp:
                return Button(@event.Button, PointerCommandKind.Up);
            case SDL.EventType.KeyDown:
            case SDL.EventType.KeyUp:
                return Key(@event.Key);
            case SDL.EventType.WindowFocusLost:
                CancelPointers(); return false;
            default: return false;
        }
    }

    internal void CancelPointers()
    {
        if (_disposed) return;
        var pointers = _pointers.ToArray();
        if (pointers.Length == 0) return;
        List<Exception>? errors = null;
        try
        {
            foreach (var (pointer, point) in pointers)
                try { _ = _router.DispatchPointer(new(PointerCommandKind.Cancel, pointer, point.X, point.Y)); }
                catch (Exception error) { (errors ??= []).Add(error); }
        }
        finally
        {
            _pointers.Clear();
            _repaintRequested = true;
        }
        if (errors is { Count: > 0 }) throw new AggregateException("Windows pointer cancellation failed.", errors);
    }

    internal bool ConsumeRepaintRequest() { var requested = _repaintRequested; _repaintRequested = false; return requested; }

    public void Dispose()
    {
        if (_disposed) return;
        try { CancelPointers(); }
        finally { _disposed = true; }
    }

    private bool Button(SDL.MouseButtonEvent @event, PointerCommandKind kind)
    {
        if (MapButton(@event.Button) is not { } button) return false;
        return Pointer(@event.Which, kind, @event.X, @event.Y, kind == PointerCommandKind.Down ? button : PointerButton.None);
    }

    private bool Pointer(uint source, PointerCommandKind kind, float x, float y, PointerButton button)
    {
        if (source > int.MaxValue || !float.IsFinite(x) || !float.IsFinite(y)) return false;
        var pointer = (int)source;
        try { _ = _router.DispatchPointer(new(kind, pointer, x, y, button)); }
        finally
        {
            if (kind == PointerCommandKind.Down) _pointers[pointer] = (x, y);
            else if (kind == PointerCommandKind.Move && _pointers.ContainsKey(pointer)) _pointers[pointer] = (x, y);
            else if (kind is PointerCommandKind.Up or PointerCommandKind.Cancel) _pointers.Remove(pointer);
        }
        return true;
    }

    private bool Key(SDL.KeyboardEvent @event)
    {
        if (MapKey(@event.Key) is not { } key) return false;
        _ = _router.DispatchKey(new(@event.Down ? KeyCommandKind.Down : KeyCommandKind.Up, key, MapModifiers(@event.Mod), @event.Down && @event.Repeat));
        return true;
    }

    internal static Key? MapKey(SDL.Keycode key) => key switch
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
        _ => null
    };

    internal static PointerButton? MapButton(byte button) => button switch
    {
        1 => PointerButton.Primary,
        2 => PointerButton.Middle,
        3 => PointerButton.Secondary,
        _ => null
    };

    internal static KeyModifiers MapModifiers(SDL.Keymod modifiers)
    {
        var flags = (ushort)modifiers;
        var result = KeyModifiers.None;
        if ((flags & (ushort)SDL.Keymod.Shift) != 0) result |= KeyModifiers.Shift;
        if ((flags & (ushort)SDL.Keymod.Ctrl) != 0) result |= KeyModifiers.Control;
        if ((flags & (ushort)SDL.Keymod.Alt) != 0) result |= KeyModifiers.Alt;
        if ((flags & (ushort)SDL.Keymod.GUI) != 0) result |= KeyModifiers.Meta;
        return result;
    }
}
