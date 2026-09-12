using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lucent.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Lucent.Platform.Windows;

internal static unsafe class WindowsFilePickerNative
{
    private const int CanceledHResult = unchecked((int)0x800704C7);

    // CLSID_FileOpenDialog and CLSID_FileSaveDialog from the Windows SDK shobjidl.h.
    private static readonly Guid OpenClass = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid SaveClass = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");

    [ThreadStatic]
    private static ActiveDialog? _active;

    internal static FilePickerResult Show(
        nint owner,
        WindowsFilePickerRequest request,
        Func<bool> canceled
    )
    {
        if (canceled())
            return new(FilePickerStatus.Canceled);
        var initialized = PInvoke.CoInitializeEx(
            COINIT.COINIT_APARTMENTTHREADED | COINIT.COINIT_DISABLE_OLE1DDE
        );
        if (initialized.Failed)
            return new(
                FilePickerStatus.Failed,
                error: "The native picker requires a Windows STA owner thread."
            );
        IFileDialog* dialog = null;
        nuint timer = 0;
        try
        {
            if (_active is not null)
                return new(
                    FilePickerStatus.Failed,
                    error: "Another native picker is already open."
                );
            if (request.Kind == WindowsFilePickerKind.Save)
            {
                PInvoke
                    .CoCreateInstance(
                        SaveClass,
                        null,
                        CLSCTX.CLSCTX_INPROC_SERVER,
                        out IFileSaveDialog* save
                    )
                    .ThrowOnFailure();
                dialog = (IFileDialog*)save;
            }
            else
            {
                PInvoke
                    .CoCreateInstance(
                        OpenClass,
                        null,
                        CLSCTX.CLSCTX_INPROC_SERVER,
                        out IFileOpenDialog* open
                    )
                    .ThrowOnFailure();
                dialog = (IFileDialog*)open;
            }
            Configure(dialog, request);
            _active = new((nint)dialog, canceled);
            // Native Show pumps this owner-thread callback. Cancellation never calls a COM pointer
            // from a worker thread, and the timer exists only for the duration of the modal call.
            timer = PInvoke.SetTimer(default, 0, 50, &CheckCancellation);
            if (timer == 0)
                return new(
                    FilePickerStatus.Failed,
                    error: "Windows could not install picker cancellation."
                );
            if (canceled())
                return new(FilePickerStatus.Canceled);
            dialog->Show(new HWND(owner));
            if (canceled())
                return new(FilePickerStatus.Canceled);
            return ReadResult(dialog, request.Kind);
        }
        catch (COMException error) when (error.HResult == CanceledHResult)
        {
            return new(FilePickerStatus.Canceled);
        }
        catch (COMException)
        {
            return new(
                FilePickerStatus.Failed,
                error: "Windows could not complete the file selection. Try again."
            );
        }
        finally
        {
            if (timer != 0)
                _ = PInvoke.KillTimer(default, timer);
            if (dialog is not null)
            {
                _active = null;
                dialog->Release();
            }
            PInvoke.CoUninitialize();
        }
    }

    private static void Configure(IFileDialog* dialog, WindowsFilePickerRequest request)
    {
        FILEOPENDIALOGOPTIONS options;
        dialog->GetOptions(&options);
        options |=
            FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_NOCHANGEDIR;
        if (request.Kind == WindowsFilePickerKind.Folder)
            options |=
                FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS | FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST;
        if (request.Multiple)
            options |= FILEOPENDIALOGOPTIONS.FOS_ALLOWMULTISELECT;
        dialog->SetOptions(options);
        if (request.Title is { } title)
            fixed (char* value = title)
                dialog->SetTitle(value);
        if (request.SuggestedName is { } name)
            fixed (char* value = name)
                dialog->SetFileName(value);
        if (request.InitialDirectory is { } directory)
        {
            IShellItem* folder = null;
            var itemId = typeof(IShellItem).GUID;
            try
            {
                fixed (char* path = directory.LocalPath)
                    PInvoke
                        .SHCreateItemFromParsingName(path, null, &itemId, (void**)&folder)
                        .ThrowOnFailure();
                dialog->SetFolder(folder);
            }
            finally
            {
                if (folder is not null)
                    folder->Release();
            }
        }
        if (request.Filters.Count == 0)
            return;
        var specs = new COMDLG_FILTERSPEC[request.Filters.Count];
        try
        {
            for (var index = 0; index < specs.Length; index++)
            {
                var filter = request.Filters[index];
                specs[index].pszName = (char*)Marshal.StringToCoTaskMemUni(filter.Name);
                specs[index].pszSpec = (char*)Marshal.StringToCoTaskMemUni(FilterPattern(filter));
            }
            fixed (COMDLG_FILTERSPEC* pointer = specs)
                dialog->SetFileTypes((uint)specs.Length, pointer);
            dialog->SetFileTypeIndex(1);
            if (
                request.Kind == WindowsFilePickerKind.Save
                && request.Filters[0].Extensions[0] is var extension
                && extension != "*"
            )
                fixed (char* value = extension)
                    dialog->SetDefaultExtension(value);
        }
        finally
        {
            foreach (var spec in specs)
            {
                PInvoke.CoTaskMemFree((void*)spec.pszName.Value);
                PInvoke.CoTaskMemFree((void*)spec.pszSpec.Value);
            }
        }
    }

    internal static string FilterPattern(FilePickerFilter filter) =>
        string.Join(
            ';',
            filter.Extensions.Select(extension => extension == "*" ? "*.*" : "*." + extension)
        );

    private static FilePickerResult ReadResult(IFileDialog* dialog, WindowsFilePickerKind kind)
    {
        var selected = new List<FilePickerItem>();
        if (kind == WindowsFilePickerKind.Save)
        {
            IShellItem* item = null;
            try
            {
                dialog->GetResult(&item);
                selected.Add(ReadItem(item));
            }
            finally
            {
                if (item is not null)
                    item->Release();
            }
        }
        else
        {
            IShellItemArray* items = null;
            try
            {
                ((IFileOpenDialog*)dialog)->GetResults(&items);
                uint count;
                items->GetCount(&count);
                for (uint index = 0; index < count; index++)
                {
                    IShellItem* item = null;
                    try
                    {
                        items->GetItemAt(index, &item);
                        selected.Add(ReadItem(item));
                    }
                    finally
                    {
                        if (item is not null)
                            item->Release();
                    }
                }
            }
            finally
            {
                if (items is not null)
                    items->Release();
            }
        }
        return new(FilePickerStatus.Selected, selected);
    }

    private static FilePickerItem ReadItem(IShellItem* item)
    {
        PWSTR path = default;
        PWSTR name = default;
        try
        {
            item->GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &path);
            item->GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, &name);
            return new(new Uri(path.ToString(), UriKind.Absolute), name.ToString());
        }
        finally
        {
            PInvoke.CoTaskMemFree(path.Value);
            PInvoke.CoTaskMemFree(name.Value);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CheckCancellation(HWND window, uint message, nuint id, uint time)
    {
        try
        {
            if (_active is { } active && active.Canceled())
                ((IFileDialog*)active.Dialog)->Close(new(CanceledHResult));
        }
        catch (COMException)
        { /* Close may race the native dialog's own dismissal. */
        }
        catch (Exception)
        {
            Environment.FailFast("A native file picker cancellation callback failed.");
        }
    }

    private sealed record ActiveDialog(nint Dialog, Func<bool> Canceled);
}
