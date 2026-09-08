namespace Lucent.Core;

/// <summary>Scope-owned mutable state for a single control recipe.</summary>
internal sealed class ControlState
{
    private readonly ReactiveScope _scope;
    private readonly Signal<string> _label;
    private readonly Signal<bool> _selected;
    private readonly Signal<float?> _progress;

    internal ControlState(
        ReactiveScope scope,
        string name,
        string label,
        bool selected = false,
        float? progress = null
    )
    {
        _scope = scope;
        _label = scope.Signal(Required(label, nameof(label)), name + ".label");
        _selected = scope.Signal(selected, name + ".selected");
        if (progress is { } value)
            ValidateProgress(value);
        _progress = scope.Signal(progress, name + ".progress");
    }

    public string Label
    {
        get => _label.Value;
        set
        {
            Check();
            _label.Value = Required(value, nameof(value));
        }
    }
    public bool Selected
    {
        get => _selected.Value;
        set
        {
            Check();
            _selected.Value = value;
        }
    }
    public float Progress
    {
        get =>
            _progress.Value
            ?? throw new InvalidOperationException("This control has no progress value.");
        set
        {
            Check();
            ValidateProgress(value);
            _progress.Value = value;
        }
    }
    internal float? ProgressValue => _progress.Value;

    private void Check()
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, typeof(ControlState));
    }

    internal static void ValidateProgress(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    internal static string Required(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A control name or label is required.", parameter);
        return value;
    }
}
