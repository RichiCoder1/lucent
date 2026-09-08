using System.ComponentModel;
using System.Runtime.InteropServices;
using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Runs one eligible Core menu through the standard Windows menu tracker.</summary>
/// <remarks>
/// This host is deliberately synchronous. Core creates the menu composition before entering
/// <see cref="TrackPopupMenuEx"/>; the selected command is returned to the owner thread and is
/// then revalidated by <see cref="ContextMenuRequest.InvokeStandardCommand"/>. Unsupported menus return
/// <see langword="false"/> so the caller can retain the Lucent-rendered popup path.
/// </remarks>
internal static partial class WindowsNativeMenuHost
{
    private const uint MiimState = 0x00000001;
    private const uint MiimId = 0x00000002;
    private const uint MiimSubMenu = 0x00000004;
    private const uint MiimFtype = 0x00000100;
    private const uint MiimString = 0x00000040;
    private const uint MftString = 0x0000;
    private const uint MftSeparator = 0x0800;
    private const uint MfsEnabled = 0x0000;
    private const uint MfsGray = 0x0003;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNoNotify = 0x0080;
    private const uint TpmReturnCommand = 0x0100;
    private const uint WmCancelMode = 0x001F;
    private const uint WmNull = 0x0000;
    private const int MaximumNativeDepth = 8;
    private const int MaximumNativeEntries = 512;

    /// <summary>
    /// Attempts one standard menu invocation. A return value of <see langword="false"/> means
    /// that the Core descriptor is not eligible and the caller should use Lucent hosting.
    /// </summary>
    internal static bool TryShow(nint ownerWindow, nint ownerHwnd, ContextMenuRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerWindow);
        ArgumentOutOfRangeException.ThrowIfZero(ownerHwnd);
        ArgumentNullException.ThrowIfNull(request);
        if (!request.IsValid || request.IsDismissed)
        {
            request.Dispose();
            return true;
        }

        // Mount and flush on the same graph owner that queued the request. StandardMenu is a
        // value snapshot; it never exposes the mounted Element tree or application callbacks.
        StandardMenuDescriptor? descriptor;
        try
        {
            request.CreateComposition().Flush();
            descriptor = request.StandardMenu;
        }
        catch
        {
            request.Dispose();
            throw;
        }
        if (descriptor is null)
            return false;
        if (!TryValidateNativeDescriptor(descriptor, out _))
            return false;

        nint menu = 0;
        try
        {
            menu = CreatePopupMenu();
            if (menu == 0)
                throw LastError("CreatePopupMenu");

            var commandIdentities = new Dictionary<nuint, SemanticIdentity>();
            nuint nextCommand = 1;
            BuildNativeMenu(menu, descriptor, ref nextCommand, commandIdentities, depth: 1);

            var point = AnchorInScreenPixels(ownerHwnd, request.Anchor);
            // The owner is already the foreground window for an input-originated context menu.
            // SetForegroundWindow is still required by the Win32 tracking contract before
            // notification/ownership-sensitive popup tracking; it is scoped to this HWND.
            _ = SetForegroundWindow(ownerHwnd);

            using var closeWatch = new NativeMenuCloseWatch(ownerWindow, ownerHwnd);
            var selected = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmNoNotify | TpmReturnCommand,
                point.X,
                point.Y,
                ownerHwnd,
                0
            );
            _ = PostMessage(ownerHwnd, WmNull, 0, 0);
            SemanticCommandResult? commandResult = null;
            if (selected != 0 && commandIdentities.TryGetValue((nuint)selected, out var identity))
                commandResult = request.InvokeStandardCommand(identity);
            if (Environment.GetEnvironmentVariable("LUCENT_MENU_DIAGNOSTICS") == "1")
                Console.Error.WriteLine(
                    $"Lucent native menu: selected={selected}; command={commandResult?.ToString() ?? "none"}."
                );
            return true;
        }
        finally
        {
            try
            {
                if (menu != 0)
                    _ = DestroyMenu(menu);
            }
            finally
            {
                try
                {
                    _ = request.RestoreFocus();
                }
                finally
                {
                    request.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Converts a Core label to Win32 menu text. A false result means that Lucent hosting should
    /// be retained because the label contains shortcut markup that cannot be represented
    /// literally by a standard menu item.
    /// </summary>
    internal static bool TryEscapeNativeLabel(string label, out string escaped)
    {
        if (
            label.Contains('\0')
            || label.Contains('\t')
            || label.Contains('\r')
            || label.Contains('\n')
        )
        {
            escaped = "";
            return false;
        }
        escaped = label.Replace("&", "&&", StringComparison.Ordinal);
        return true;
    }

    /// <summary>Validates a recursively projected descriptor before any native menu is created.</summary>
    /// <remarks>The bounded check keeps malformed or unexpectedly deep authoring on the Lucent path.</remarks>
    internal static bool TryValidateNativeDescriptor(
        StandardMenuDescriptor descriptor,
        out int leafCount
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var count = 0;
        leafCount = 0;
        var valid = Validate(descriptor, depth: 1);
        leafCount = count;
        return valid;

        bool Validate(StandardMenuDescriptor current, int depth)
        {
            if (depth > MaximumNativeDepth || current.Entries.Count == 0)
                return false;
            foreach (var entry in current.Entries)
            {
                switch (entry.Kind)
                {
                    case StandardMenuEntryKind.Separator:
                        if (
                            entry.Label is not null
                            || entry.Identity is not null
                            || entry.Submenu is not null
                        )
                            return false;
                        break;
                    case StandardMenuEntryKind.Command:
                        if (
                            entry.Identity is null
                            || entry.Label is null
                            || entry.Submenu is not null
                            || !TryEscapeNativeLabel(entry.Label, out _)
                        )
                            return false;
                        if (++count > MaximumNativeEntries)
                            return false;
                        break;
                    case StandardMenuEntryKind.Submenu:
                        if (
                            entry.Label is null
                            || entry.Submenu is null
                            || !TryEscapeNativeLabel(entry.Label, out _)
                            || !Validate(entry.Submenu, depth + 1)
                        )
                            return false;
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }
    }

    private static void BuildNativeMenu(
        nint menu,
        StandardMenuDescriptor descriptor,
        ref nuint nextCommand,
        Dictionary<nuint, SemanticIdentity> commandIdentities,
        int depth
    )
    {
        if (depth > MaximumNativeDepth)
            throw new InvalidOperationException("The native menu depth limit was exceeded.");
        uint position = 0;
        foreach (var entry in descriptor.Entries)
        {
            switch (entry.Kind)
            {
                case StandardMenuEntryKind.Separator:
                    InsertSeparator(menu, position++);
                    break;
                case StandardMenuEntryKind.Command:
                    if (nextCommand == 0)
                        throw new InvalidOperationException(
                            "The native menu command ID space was exhausted."
                        );
                    if (!TryEscapeNativeLabel(entry.Label!, out var label))
                        throw new InvalidOperationException(
                            "The standard menu label contains native shortcut markup."
                        );
                    InsertCommand(menu, position++, nextCommand, label, entry.Enabled);
                    commandIdentities.Add(nextCommand, entry.Identity!.Value);
                    nextCommand++;
                    break;
                case StandardMenuEntryKind.Submenu:
                    if (!TryEscapeNativeLabel(entry.Label!, out var submenuLabel))
                        throw new InvalidOperationException(
                            "The standard submenu label contains native shortcut markup."
                        );
                    var child = CreatePopupMenu();
                    if (child == 0)
                        throw LastError("CreatePopupMenu(submenu)");
                    try
                    {
                        BuildNativeMenu(
                            child,
                            entry.Submenu!,
                            ref nextCommand,
                            commandIdentities,
                            depth + 1
                        );
                        InsertSubmenu(menu, position++, submenuLabel, child, entry.Enabled);
                    }
                    catch
                    {
                        _ = DestroyMenu(child);
                        throw;
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "The Core menu descriptor contained an unknown entry kind."
                    );
            }
        }
    }

    private static unsafe void InsertCommand(
        nint menu,
        uint position,
        nuint command,
        string label,
        bool enabled
    )
    {
        fixed (char* text = label)
        {
            var info = new MenuItemInfo
            {
                cbSize = (uint)sizeof(MenuItemInfo),
                fMask = MiimState | MiimId | MiimFtype | MiimString,
                fType = MftString,
                fState = enabled ? MfsEnabled : MfsGray,
                wID = checked((uint)command),
                dwTypeData = (nint)text,
                cch = checked((uint)label.Length),
            };
            if (!InsertMenuItem(menu, position, true, ref info))
                throw LastError("InsertMenuItem(command)");
        }
    }

    private static unsafe void InsertSeparator(nint menu, uint position)
    {
        var info = new MenuItemInfo
        {
            cbSize = (uint)sizeof(MenuItemInfo),
            fMask = MiimFtype,
            fType = MftSeparator,
        };
        if (!InsertMenuItem(menu, position, true, ref info))
            throw LastError("InsertMenuItem(separator)");
    }

    private static unsafe void InsertSubmenu(
        nint menu,
        uint position,
        string label,
        nint submenu,
        bool enabled
    )
    {
        fixed (char* text = label)
        {
            var info = new MenuItemInfo
            {
                cbSize = (uint)sizeof(MenuItemInfo),
                fMask = MiimState | MiimFtype | MiimString | MiimSubMenu,
                fType = MftString,
                fState = enabled ? MfsEnabled : MfsGray,
                hSubMenu = submenu,
                dwTypeData = (nint)text,
                cch = checked((uint)label.Length),
            };
            if (!InsertMenuItem(menu, position, true, ref info))
                throw LastError("InsertMenuItem(submenu)");
        }
    }

    internal static (int X, int Y) AnchorInClientPixels(LayoutRect anchor, uint dpi)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        var scale = dpi / 96F;
        return (
            WindowsPopupHost.ToWindowUnits(anchor.X, scale, 1),
            WindowsPopupHost.ToWindowUnits(anchor.Y + anchor.Height, scale, 1)
        );
    }

    private static ScreenPoint AnchorInScreenPixels(nint ownerHwnd, LayoutRect anchor)
    {
        var dpi = global::Windows.Win32.PInvoke.GetDpiForWindow(
            new global::Windows.Win32.Foundation.HWND(ownerHwnd)
        );
        if (dpi == 0)
            throw new InvalidOperationException(
                "GetDpiForWindow returned zero for the owner window."
            );
        var client = AnchorInClientPixels(anchor, dpi);
        var point = new ScreenPoint { X = client.X, Y = client.Y };
        if (!ClientToScreen(ownerHwnd, ref point))
            throw LastError("ClientToScreen");
        return point;
    }

    private static Win32Exception LastError(string operation) =>
        new(Marshal.GetLastWin32Error(), operation + " failed.");

    private sealed class NativeMenuCloseWatch : IDisposable
    {
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private readonly uint _ownerWindowId;
        private readonly nint _ownerHwnd;
        private readonly SDL.EventFilter _watch;
        private bool _disposed;

        internal NativeMenuCloseWatch(nint ownerWindow, nint ownerHwnd)
        {
            _ownerWindowId = SDL.GetWindowID(ownerWindow);
            if (_ownerWindowId == 0)
                throw new InvalidOperationException($"SDL_GetWindowID owner: {SDL.GetError()}");
            _ownerHwnd = ownerHwnd;
            _watch = Watch;
            if (!SDL.AddEventWatch(_watch, 0))
                throw new InvalidOperationException(
                    $"SDL_AddEventWatch native menu: {SDL.GetError()}"
                );
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException(
                    "Native menu event watch must be removed on its owner thread."
                );
            _disposed = true;
            SDL.RemoveEventWatch(_watch, 0);
        }

        private bool Watch(nint _userdata, ref SDL.Event @event)
        {
            try
            {
                if (_disposed)
                    return true;
                var type = (SDL.EventType)@event.Type;
                if (
                    type == SDL.EventType.Quit
                    || type == SDL.EventType.WindowCloseRequested
                        && @event.Window.WindowID == _ownerWindowId
                )
                {
                    if (Environment.CurrentManagedThreadId == _ownerThread)
                        _ = EndMenu();
                    else
                        _ = PostMessage(_ownerHwnd, WmCancelMode, 0, 0);
                }
            }
            catch
            {
                // SDL invokes this delegate from unmanaged event delivery. Cancellation is
                // best-effort, but managed exceptions must never cross that boundary.
            }
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        internal uint cbSize;
        internal uint fMask;
        internal uint fType;
        internal uint fState;
        internal uint wID;
        internal nint hSubMenu;
        internal nint hbmpChecked;
        internal nint hbmpUnchecked;
        internal nint dwItemData;
        internal nint dwTypeData;
        internal uint cch;
        internal nint hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        internal int X;
        internal int Y;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreatePopupMenu", SetLastError = true)]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "InsertMenuItemW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InsertMenuItem(
        nint menu,
        uint item,
        [MarshalAs(UnmanagedType.Bool)] bool byPosition,
        ref MenuItemInfo info
    );

    [LibraryImport("user32.dll", EntryPoint = "DestroyMenu", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll", EntryPoint = "TrackPopupMenuEx", SetLastError = true)]
    private static partial int TrackPopupMenuEx(
        nint menu,
        uint flags,
        int x,
        int y,
        nint owner,
        nint parameters
    );

    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hwnd, ref ScreenPoint point);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "EndMenu", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndMenu();

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
