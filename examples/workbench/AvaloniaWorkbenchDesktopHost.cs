using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Lucent.Examples.Workbench;

internal sealed class AvaloniaWorkbenchDesktopHost : IWorkbenchDesktopHost
{
    public async Task<IReadOnlyList<IStorageFolder>> PickWorkspaceAsync(Window owner) =>
        await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open workspace",
        });

    public Task SetClipboardTextAsync(TopLevel owner, string text) =>
        owner.Clipboard is { } clipboard
            ? clipboard.SetTextAsync(text)
            : Task.FromException(new InvalidOperationException("The owner has no clipboard."));

    public Task<TResult> ShowDialogAsync<TResult>(Window owner, Window dialog) =>
        dialog.ShowDialog<TResult>(owner);

    public void ShowOwnedWindow(Window owner, Window child) => child.Show(owner);
}
