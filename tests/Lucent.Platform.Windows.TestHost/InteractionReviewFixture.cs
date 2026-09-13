using System.Globalization;
using Lucent.Core;
using Lucent.Platform.Windows;

namespace Lucent.Platform.Windows.TestHost;

/// <summary>Published public-control fixture for the bounded #279-283 interaction review.</summary>
internal static class InteractionReviewFixture
{
    internal static int Run()
    {
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "interaction-review-fixture");
            composition.ConfigureImages(
                new ImageCache(new Lucent.Renderer.Skia.SkiaImagePreparer())
            );
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var model = new InteractionReviewModel(composition.Root.Scope);
            composition.Mount(
                composition.Root,
                theme,
                LuiFixtures.Components.InteractionReviewFixtureView(model)
            );
            return WindowsBootstrap.Run("Lucent Interaction Review Fixture", composition, theme);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Lucent interaction review fixture: " + error);
            return 1;
        }
    }
}

internal sealed class InteractionReviewModel
{
    private readonly ReactiveScope _owner;
    private readonly Signal<string> _tooltipDialogStatus;
    private readonly Signal<string> _pendingStatus;
    private readonly Signal<string> _pendingSubmissionStatus;
    private readonly Signal<bool> _dateEnabled;
    private readonly Signal<DateOnly?> _dateValue;
    private readonly Signal<double> _sliderValue;
    private readonly Signal<int> _sliderCommits;
    private readonly Signal<int> _pagedSelection;
    private readonly Signal<decimal?> _numberValue;
    private readonly Signal<int> _numberRequests;
    private TaskCompletionSource? _pendingWrite;

    internal InteractionReviewModel(ReactiveScope owner)
    {
        _owner = owner;
        TooltipDialog = owner.Own(new DialogController<string>(owner, "tooltip-dialog"));
        PendingDialog = owner.Own(new DialogController<string>(owner, "pending-dialog"));
        _tooltipDialogStatus = owner.Signal("Tooltip dialog closed", "tooltip-dialog.status");
        _pendingStatus = owner.Signal("Pending dialog closed", "pending-dialog.status");
        _pendingSubmissionStatus = owner.Signal(
            "Pending submission idle",
            "pending-dialog.submission-status"
        );
        _dateEnabled = owner.Signal(true, "review-date.enabled");
        _dateValue = owner.Signal<DateOnly?>(new DateOnly(2024, 6, 15), "review-date.value");
        _sliderValue = owner.Signal(20d, "review-slider.value");
        _sliderCommits = owner.Signal(0, "review-slider.commits");
        _pagedSelection = owner.Signal(0, "review-list.selection");
        _numberValue = owner.Signal<decimal?>(1m, "review-number.value");
        _numberRequests = owner.Signal(0, "review-number.requests");
    }

    public DialogController<string> TooltipDialog { get; }
    public DialogController<string> PendingDialog { get; }
    public string TooltipDialogStatus => _tooltipDialogStatus.Value;
    public string PendingStatus => _pendingStatus.Value;
    public string PendingSubmissionStatus => _pendingSubmissionStatus.Value;
    public bool DateEnabled => _dateEnabled.Value;
    public DateOnly? DateValue => _dateValue.Value;
    public double SliderValue => _sliderValue.Value;
    public int PagedSelection => _pagedSelection.Value;
    public decimal? NumberValue => _numberValue.Value;
    public SliderOptions SliderOptions { get; } = new(0, 100, 1);
    public NumericEditOptions NumberOptions { get; } = new(1m, 0m, 2m);
    public IReadOnlyList<ChoiceItem<int>> PagedItems { get; } =
        Enumerable
            .Range(0, 20)
            .Select(index => new ChoiceItem<int>(index, "Paged choice " + index, index != 3))
            .ToArray();
    public DateTimeFieldOptions DateField => new(enabled: () => DateEnabled);
    public DatePickerOptions DateOptions { get; } =
        new(
            CultureInfo.GetCultureInfo("en-US"),
            minimum: new DateOnly(2024, 1, 1),
            maximum: new DateOnly(2024, 12, 31),
            today: static () => new DateOnly(2024, 6, 15)
        );

    public string DateStatus =>
        DateEnabled
            ? $"Date enabled: {DateValue:yyyy-MM-dd}"
            : $"Date disabled: {DateValue:yyyy-MM-dd}";

    public string SliderStatus => $"Slider value {SliderValue:0}; commits {_sliderCommits.Value}";

    public string PagingStatus => $"Paged selection {PagedSelection}";

    public string NumberStatus => $"Number value {NumberValue:0}; requests {_numberRequests.Value}";

    public void OpenTooltipDialog()
    {
        _tooltipDialogStatus.Value = "Tooltip dialog open";
        _ = ObserveTooltipDialog(TooltipDialog.OpenAsync().AsTask());
    }

    public void OpenPendingDialog()
    {
        _pendingStatus.Value = "Pending dialog open";
        _ = ObservePendingDialog(PendingDialog.OpenAsync().AsTask());
    }

    public void StartPendingAcceptance()
    {
        if (!PendingDialog.IsOpen || PendingDialog.IsPending)
            return;
        _pendingSubmissionStatus.Value = "Pending write running";
        _pendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ObservePendingSubmission(
            PendingDialog
                .AcceptAsync("accepted", (_, _) => new ValueTask(_pendingWrite.Task))
                .AsTask()
        );
    }

    public void FailPendingWrite() =>
        _pendingWrite?.TrySetException(new InvalidOperationException("Expected fixture failure"));

    public void SetDateEnabled(bool value) => _dateEnabled.Value = value;

    public void SetDateValue(DateOnly? value) => _dateValue.Value = value;

    public void SetSliderValue(double value) => _sliderValue.Value = value;

    public void CommitSlider(double value)
    {
        _sliderValue.Value = value;
        _sliderCommits.Value++;
    }

    public void SetPagedSelection(int value) => _pagedSelection.Value = value;

    public void SetNumberValue(decimal? value)
    {
        _numberRequests.Value++;
        _numberValue.Value = value;
    }

    public void NoOp() => _ = _owner.IsDisposed;

    public ComponentRecipe ReviewMenu() =>
        Components.Menu([
            Components.MenuItem("First command", NoOp),
            Components.MenuSeparator(),
            Components.MenuItem("Second command", NoOp),
            Components.MenuItem("Unavailable command", NoOp, () => false),
            Components.MenuSubmenu(
                "More commands",
                () =>
                    Components.Menu([
                        Components.MenuItem("Nested first", NoOp),
                        Components.MenuSeparator(),
                        Components.MenuItem("Nested unavailable", NoOp, () => false),
                        Components.MenuItem("Nested second", NoOp),
                    ])
            ),
        ]);

    public bool FocusFirst(Composition popup) =>
        !_owner.IsDisposed && popup.Input.MoveFocus(FocusTraversalDirection.Next);

    private async Task ObserveTooltipDialog(Task<DialogResult<string>> operation)
    {
        var result = await operation.ConfigureAwait(false);
        _owner.Post(() =>
            _tooltipDialogStatus.Value =
                result.Kind == DialogResultKind.Canceled
                    ? "Tooltip dialog canceled"
                    : "Tooltip dialog accepted"
        );
    }

    private async Task ObservePendingDialog(Task<DialogResult<string>> operation)
    {
        var result = await operation.ConfigureAwait(false);
        _owner.Post(() =>
            _pendingStatus.Value =
                result.Kind == DialogResultKind.Canceled
                    ? "Pending dialog canceled"
                    : "Pending dialog accepted"
        );
    }

    private async Task ObservePendingSubmission(Task<DialogSubmissionResult> operation)
    {
        var result = await operation.ConfigureAwait(false);
        _owner.Post(() => _pendingSubmissionStatus.Value = "Pending submission " + result.Status);
    }
}
