namespace Lucent.ComponentBrowser;

public sealed class NavigationExampleModel
{
    private static readonly Uri DocumentationUri = new(
        "https://github.com/RichiCoder1/lucent/blob/main/docs/COMPONENTS.md"
    );

    private readonly ReactiveScope _scope;
    private readonly IUriLauncher? _uriLauncher;
    private readonly CancellationToken _lifetime;
    private readonly Signal<string> _listSelection;
    private readonly Signal<string> _selectSelection;
    private readonly Signal<string> _tabSelection;
    private readonly Signal<bool> _disclosureExpanded;
    private readonly Signal<string> _linkMessage;
    private readonly Signal<string> _dialogMessage;

    public NavigationExampleModel(ReactiveScope owner, IUriLauncher? uriLauncher)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _scope = owner;
        _uriLauncher = uriLauncher;
        _listSelection = owner.Signal("overview", "component-browser.list-selection");
        _selectSelection = owner.Signal("keyboard", "component-browser.select-selection");
        _tabSelection = owner.Signal("overview", "component-browser.tab-selection");
        _disclosureExpanded = owner.Signal(false, "component-browser.disclosure-expanded");
        _linkMessage = owner.Signal(
            "No reference link invoked yet.",
            "component-browser.link-message"
        );
        _dialogMessage = owner.Signal("The dialog is closed.", "component-browser.dialog-message");
        Dialog = owner.Own(new DialogController<string>(owner, "component-browser-dialog"));
        var lifetime = owner.Own(new CancellationTokenSource());
        _lifetime = lifetime.Token;
        owner.OnDispose(lifetime.Cancel);
    }

    public IReadOnlyList<ChoiceItem<string>> ListItems { get; } =
    [
        new("overview", "Overview"),
        new("keyboard", "Keyboard behavior"),
        new("accessibility", "Accessibility contract"),
        new("disabled", "Disabled option", enabled: false),
    ];

    public IReadOnlyList<ChoiceItem<string>> SelectItems { get; } =
    [
        new("keyboard", "Keyboard behavior"),
        new("pointer", "Pointer behavior"),
        new("semantics", "Semantic output"),
    ];

    public IReadOnlyList<TabItem<string>> TabItems { get; } =
    [
        new(
            "overview",
            "Overview",
            () =>
                Lucent.Core.Components.Text(
                    "Tabs keep one keyed selection in application state and retain visited panels."
                )
        ),
        new(
            "keyboard",
            "Keyboard",
            () =>
                Lucent.Core.Components.Text(
                    "Manual and automatic activation are explicit policy choices."
                )
        ),
        new(
            "semantics",
            "Semantics",
            () =>
                Lucent.Core.Components.Text(
                    "The selected tab and its panel expose their relationship to accessibility clients."
                )
        ),
    ];

    public string ListSelection => _listSelection.Value;

    public string SelectSelection => _selectSelection.Value;

    public string TabSelection => _tabSelection.Value;

    public bool DisclosureExpanded => _disclosureExpanded.Value;

    public string LinkMessage => _linkMessage.Value;

    public string DialogMessage => _dialogMessage.Value;

    public DialogController<string> Dialog { get; }

    public void SetListSelection(string key)
    {
        if (ListItems.Any(item => item.Key == key && item.Enabled))
            _listSelection.Value = key;
    }

    public void SetSelectSelection(string key)
    {
        if (SelectItems.Any(item => item.Key == key && item.Enabled))
            _selectSelection.Value = key;
    }

    public void SetTabSelection(string key)
    {
        if (TabItems.Any(item => item.Key == key && item.Enabled))
            _tabSelection.Value = key;
    }

    public void SetDisclosureExpanded(bool expanded) => _disclosureExpanded.Value = expanded;

    public void SetLinkMessage() => _linkMessage.Value = "The typed Link action was invoked.";

    public void OpenDocumentation() => _ = ObserveDocumentationAsync();

    public bool FocusDialog(Composition popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        return Dialog.IsOpen && popup.Input.MoveFocus(FocusTraversalDirection.Next);
    }

    public void OpenDialog() => _ = ObserveDialogAsync(Dialog.OpenAsync(_lifetime).AsTask());

    public void CancelDialog() => Dialog.TryCancel();

    public void SubmitDialog()
    {
        if (!Dialog.IsOpen || Dialog.IsPending)
            return;

        _ = ObserveSubmissionAsync(
            Dialog.AcceptAsync("reviewed", static (_, _) => ValueTask.CompletedTask).AsTask()
        );
    }

    private async Task ObserveDocumentationAsync()
    {
        try
        {
            var result = _uriLauncher is null
                ? new UriLaunchResult(UriLaunchStatus.Unsupported)
                : await _uriLauncher.LaunchAsync(DocumentationUri, _lifetime).ConfigureAwait(false);
            _scope.Post(() =>
                _linkMessage.Value = result.Status switch
                {
                    UriLaunchStatus.Launched => "The system accepted the documentation link.",
                    UriLaunchStatus.Canceled => "Opening documentation was canceled.",
                    UriLaunchStatus.Denied => "The application's URI policy denied this link.",
                    UriLaunchStatus.Unsupported => "External links are unavailable on this host.",
                    _ => result.Error ?? "The system could not open documentation.",
                }
            );
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            _scope.Post(() => _linkMessage.Value = "The system could not open documentation.");
        }
    }

    private async Task ObserveDialogAsync(Task<DialogResult<string>> session)
    {
        try
        {
            var result = await session.ConfigureAwait(false);
            if (result.IsCanceled)
                _scope.Post(() => _dialogMessage.Value = "The dialog was canceled.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            _scope.Post(() => _dialogMessage.Value = "The dialog could not complete.");
        }
    }

    private async Task ObserveSubmissionAsync(Task<DialogSubmissionResult> submission)
    {
        try
        {
            var result = await submission.ConfigureAwait(false);
            _scope.Post(() =>
            {
                _dialogMessage.Value =
                    result.IsAccepted ? "The application action completed and the dialog closed."
                    : result.IsFailed ? result.FailureMessage ?? "The application action failed."
                    : "The application action did not complete.";
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            _scope.Post(() => _dialogMessage.Value = "The application action failed.");
        }
    }
}
