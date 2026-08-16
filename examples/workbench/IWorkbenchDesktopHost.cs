using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Lucent.Examples.Workbench;

internal interface IWorkbenchDesktopHost
{
    Task<IReadOnlyList<IStorageFolder>> PickWorkspaceAsync(Window owner);
    Task SetClipboardTextAsync(TopLevel owner, string text);
    Task<TResult> ShowDialogAsync<TResult>(Window owner, Window dialog);
    void ShowOwnedWindow(Window owner, Window child);
}
