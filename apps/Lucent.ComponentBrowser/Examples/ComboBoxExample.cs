namespace Lucent.ComponentBrowser;

public sealed class ComboBoxExampleModel
{
    private readonly Signal<ComboBoxSelectedItem<string>?> _selection;
    private readonly Signal<string> _message;

    public ComboBoxExampleModel(ReactiveScope owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _selection = owner.Signal<ComboBoxSelectedItem<string>?>(
            null,
            "component-browser.combo-selection"
        );
        _message = owner.Signal(
            "Choose a result to keep the applied key controlled.",
            "component-browser.combo-message"
        );
    }

    public ComboBoxSelectedItem<string>? Selection => _selection.Value;

    public string Message => _message.Value;

    public ComboBoxOptions Options { get; } = new(debounce: TimeSpan.Zero);

    public IReadOnlyList<ChoiceItem<string>> Items { get; } =
    [
        new("alpha", "Alpha workspace"),
        new("beta", "Beta workspace"),
        new("gamma", "Gamma workspace"),
    ];

    public void SetSelection(string key)
    {
        var item = Items.FirstOrDefault(candidate => candidate.Key == key);
        if (item is null || !item.Enabled)
            return;

        _selection.Value = new(item.Key, item.Label);
        _message.Value = "Applied " + item.Label + ".";
    }

    public ValueTask<ComboBoxSuggestionResult<string>> Suggest(
        string query,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = query.Trim();
        var matches = Items
            .Where(item =>
                normalized.Length == 0
                || item.Label.Contains(normalized, StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        return ValueTask.FromResult(ComboBoxSuggestionResult.Success(matches));
    }
}
