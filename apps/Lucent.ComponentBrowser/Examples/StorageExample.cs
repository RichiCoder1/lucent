namespace Lucent.ComponentBrowser;

public sealed class StorageExampleModel
{
    private readonly ReactiveScope _scope;
    private readonly IFilePicker _filePicker;
    private readonly CancellationToken _lifetime;
    private readonly Signal<string> _message;
    private readonly Signal<string> _location;

    public StorageExampleModel(ReactiveScope owner, IFilePicker filePicker)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(filePicker);
        _scope = owner;
        _filePicker = filePicker;
        _message = owner.Signal(
            "No native picker request yet.",
            "component-browser.storage-message"
        );
        _location = owner.Signal(
            "A selected name and location will appear here.",
            "component-browser.storage-location"
        );
        var lifetime = owner.Own(new CancellationTokenSource());
        _lifetime = lifetime.Token;
        owner.OnDispose(lifetime.Cancel);
    }

    public string Message => _message.Value;

    public string Location => _location.Value;

    public void OpenFiles() =>
        ObservePicker(
            "Open files",
            _filePicker.OpenFilesAsync(
                new OpenFileOptions(
                    Title: "Open Lucent examples",
                    Filters: [new FilePickerFilter("Lucent source", ["lui"])],
                    AllowMultiple: true
                ),
                _lifetime
            )
        );

    public void SaveDestination() =>
        ObservePicker(
            "Save destination",
            _filePicker.SaveFileAsync(
                new SaveFileOptions(
                    Title: "Choose a destination",
                    SuggestedName: "component-example.lui",
                    Filters: [new FilePickerFilter("Lucent source", ["lui"])]
                ),
                _lifetime
            )
        );

    public void ChooseFolder() =>
        ObservePicker(
            "Choose folder",
            _filePicker.PickFolderAsync(
                new PickFolderOptions("Choose an examples folder"),
                _lifetime
            )
        );

    private void ObservePicker(string operation, ValueTask<FilePickerResult> result) =>
        _ = ObservePickerAsync(operation, result.AsTask());

    private async Task ObservePickerAsync(string operation, Task<FilePickerResult> resultTask)
    {
        try
        {
            var result = await resultTask.ConfigureAwait(false);
            _scope.Post(() => ApplyResult(operation, result));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            _scope.Post(() =>
            {
                _message.Value = operation + " failed.";
                _location.Value = "The native picker could not complete.";
            });
        }
    }

    private void ApplyResult(string operation, FilePickerResult result)
    {
        switch (result.Status)
        {
            case FilePickerStatus.Selected:
                var selected = result.Items[0];
                _message.Value =
                    operation
                    + " selected "
                    + result.Items.Count
                    + " item"
                    + (result.Items.Count == 1 ? "" : "s")
                    + ".";
                _location.Value = selected.Name + " · " + selected.Location;
                break;
            case FilePickerStatus.Canceled:
                _message.Value = operation + " was canceled.";
                _location.Value = "No location selected.";
                break;
            case FilePickerStatus.Unsupported:
                _message.Value = operation + " is unavailable on this host.";
                _location.Value = "The picker returned an unsupported capability.";
                break;
            case FilePickerStatus.Failed:
                _message.Value = operation + " failed.";
                _location.Value = result.Error ?? "The native picker could not complete.";
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(result),
                    result.Status,
                    "Unknown file picker status."
                );
        }
    }
}
