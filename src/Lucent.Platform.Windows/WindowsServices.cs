using System.Runtime.InteropServices;
using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows;

[Flags]
internal enum WindowsSettingsDiagnostic
{
    None = 0,
    UnknownTheme = 1,
    HighContrastReadFailed = 2,
    ReducedMotionReadFailed = 4,
}

internal readonly record struct WindowsSettingsSnapshot(
    ThemeColorScheme? ColorScheme,
    ThemeContrast? Contrast,
    bool? ReducedMotion,
    WindowsSettingsDiagnostic Diagnostics = WindowsSettingsDiagnostic.None
);

/// <summary>Reads Windows settings only on the window thread after SDL reports a settings/theme change.</summary>
internal sealed unsafe partial class WindowsSettings(Func<WindowsSettingsSnapshot> read)
{
    internal WindowsSettingsDiagnostic Diagnostics { get; private set; }
    internal string DiagnosticStatus => Diagnostics.ToString();

    internal WindowsSettings()
        : this(Read) { }

    internal bool Apply(ThemeContext? theme)
    {
        if (theme is null)
            return false;
        var next = read();
        Diagnostics = next.Diagnostics;
        var appearance = new ThemeAppearance(
            next.ColorScheme ?? theme.Appearance.ColorScheme,
            next.Contrast ?? theme.Appearance.Contrast
        );
        var changed =
            appearance != theme.Appearance
            || next.ReducedMotion is { } motion && motion != theme.ReducedMotion;
        if (!changed)
            return false;
        theme.Appearance = appearance;
        if (next.ReducedMotion is { } reducedMotion)
            theme.ReducedMotion = reducedMotion;
        return true;
    }

    private static WindowsSettingsSnapshot Read()
    {
        var diagnostics = WindowsSettingsDiagnostic.None;
        ThemeColorScheme? scheme = SDL.GetSystemTheme() switch
        {
            SDL.SystemTheme.Light => ThemeColorScheme.Light,
            SDL.SystemTheme.Dark => ThemeColorScheme.Dark,
            _ => null,
        };
        if (scheme is null)
            diagnostics |= WindowsSettingsDiagnostic.UnknownTheme;
        var contrast = new HighContrast { Size = (uint)sizeof(HighContrast) };
        var readContrast = SystemParametersInfo(0x0042, contrast.Size, ref contrast, 0);
        if (!readContrast)
            diagnostics |= WindowsSettingsDiagnostic.HighContrastReadFailed;
        ThemeContrast? highContrast = readContrast
            ? (contrast.Flags & 1) != 0
                ? ThemeContrast.High
                : ThemeContrast.Normal
            : null;
        var animations = 1;
        var readAnimations = SystemParametersInfo(0x1042, 0, out animations, 0);
        if (!readAnimations)
            diagnostics |= WindowsSettingsDiagnostic.ReducedMotionReadFailed;
        bool? reducedMotion = readAnimations ? animations == 0 : null;
        return new(scheme, highContrast, reducedMotion, diagnostics);
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref HighContrast value,
        uint flags
    );

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(
        uint action,
        uint parameter,
        out int value,
        uint flags
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        internal uint Size;
        internal uint Flags;
        internal nint Scheme;
    }
}

/// <summary>Clipboard operations never clear existing clipboard contents before a successful SDL write.</summary>
internal sealed class WindowsClipboard(
    Func<string?> read,
    Func<string, bool> write,
    Func<string> error
) : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;

    internal WindowsClipboard()
        : this(SDL.GetClipboardText, SDL.SetClipboardText, SDL.GetError) { }

    internal ClipboardReadResult Read()
    {
        if (Failure() is { } failure)
            return new(false, null, failure);
        try
        {
            return read() is { } text
                ? new(true, text, null)
                : new(false, null, Error("SDL clipboard read failed."));
        }
        catch (Exception error)
        {
            return new(false, null, error.Message);
        }
    }

    internal ClipboardWriteResult Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Failure() is { } failure)
            return new(false, failure);
        try
        {
            return write(text) ? new(true, null) : new(false, Error("SDL clipboard write failed."));
        }
        catch (Exception error)
        {
            return new(false, error.Message);
        }
    }

    public void Dispose() => _disposed = true;

    private string? Failure() =>
        _disposed ? "Clipboard lifetime ended."
        : Environment.CurrentManagedThreadId != _ownerThread
            ? "Clipboard access must remain on its owner UI thread."
        : null;

    private string Error(string fallback) =>
        string.IsNullOrWhiteSpace(error()) ? fallback : error();
}

internal readonly record struct ClipboardReadResult(bool Succeeded, string? Text, string? Error);

internal readonly record struct ClipboardWriteResult(bool Succeeded, string? Error);

/// <summary>Host-owned arrow and I-beam cursors, released before SDL shutdown.</summary>
internal sealed class WindowsCursor(
    Func<CursorIntent, nint> create,
    Func<nint, bool> set,
    Action<nint> destroy,
    Func<string> error
) : IDisposable
{
    private readonly Dictionary<CursorIntent, nint> _cursors = [];
    private CursorIntent? _active;
    private bool _disposed;

    internal WindowsCursor(
        Func<bool, nint> create,
        Func<nint, bool> set,
        Action<nint> destroy,
        Func<string> error
    )
        : this(intent => create(intent == CursorIntent.Text), set, destroy, error) { }

    internal WindowsCursor(
        Func<nint> create,
        Func<nint, bool> set,
        Action<nint> destroy,
        Func<string> error
    )
        : this((CursorIntent _) => create(), set, destroy, error) { }

    internal WindowsCursor()
        : this(
            (CursorIntent intent) =>
                SDL.CreateSystemCursor(
                    intent switch
                    {
                        CursorIntent.Text => SDL.SystemCursor.Text,
                        CursorIntent.Pointer => SDL.SystemCursor.Pointer,
                        _ => SDL.SystemCursor.Default,
                    }
                ),
            SDL.SetCursor,
            SDL.DestroyCursor,
            SDL.GetError
        ) { }

    internal bool Activate(bool text = false) =>
        Activate(text ? CursorIntent.Text : CursorIntent.Default);

    internal bool Activate(CursorIntent intent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(intent) || intent == CursorIntent.Auto)
            throw new ArgumentOutOfRangeException(nameof(intent));
        if (_active == intent)
            return true;
        if (!_cursors.TryGetValue(intent, out var cursor))
        {
            cursor = create(intent);
            if (cursor == 0)
                throw new InvalidOperationException("SDL_CreateSystemCursor: " + Error());
            _cursors.Add(intent, cursor);
        }
        if (!set(cursor))
            throw new InvalidOperationException("SDL_SetCursor: " + Error());
        _active = intent;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var handles = _cursors.Values.Distinct().ToArray();
        _cursors.Clear();
        _active = null;
        List<Exception>? errors = null;
        foreach (var cursor in handles)
            try
            {
                destroy(cursor);
            }
            catch (Exception failure)
            {
                (errors ??= []).Add(failure);
            }
        if (errors is not null)
            throw new AggregateException("Windows cursor cleanup failed.", errors);
    }

    private string Error() => string.IsNullOrWhiteSpace(error()) ? "unknown error" : error();
}
