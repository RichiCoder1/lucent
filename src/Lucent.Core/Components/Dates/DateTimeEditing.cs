using System.Globalization;

namespace Lucent.Core;

/// <summary>Frozen Gregorian formatting, nullability and inclusive bounds for a date picker.</summary>
public sealed class DatePickerOptions
{
    /// <summary>Creates explicit date-entry policy.</summary>
    public DatePickerOptions(
        CultureInfo? culture = null,
        bool allowNull = false,
        DateOnly? minimum = null,
        DateOnly? maximum = null,
        Func<DateOnly>? today = null
    )
    {
        var clone = (CultureInfo)(culture ?? CultureInfo.CurrentCulture).Clone();
        if (clone.OptionalCalendars.Any(value => value is GregorianCalendar))
            clone.DateTimeFormat.Calendar = new GregorianCalendar();
        else
            throw new ArgumentException(
                "The culture must support the Gregorian calendar.",
                nameof(culture)
            );
        if (minimum is { } min && maximum is { } max && min > max)
            throw new ArgumentException("The minimum date cannot follow the maximum date.");
        Culture = CultureInfo.ReadOnly(clone);
        AllowNull = allowNull;
        Minimum = minimum;
        Maximum = maximum;
        Today = today ?? (static () => DateOnly.FromDateTime(DateTime.Today));
    }

    /// <summary>Gets the read-only culture used with its Gregorian calendar.</summary>
    public CultureInfo Culture { get; }

    /// <summary>Gets whether clearing can request a null date.</summary>
    public bool AllowNull { get; }

    /// <summary>Gets the inclusive minimum date.</summary>
    public DateOnly? Minimum { get; }

    /// <summary>Gets the inclusive maximum date.</summary>
    public DateOnly? Maximum { get; }

    /// <summary>Gets the owner-supplied current local date provider.</summary>
    public Func<DateOnly> Today { get; }

    internal string Format(DateOnly? value) => value?.ToString("d", Culture) ?? "";

    internal bool InRange(DateOnly value) =>
        (Minimum is null || value >= Minimum) && (Maximum is null || value <= Maximum);
}

/// <summary>Frozen formatting, nullability, inclusive bounds and stepping for a time picker.</summary>
public sealed class TimePickerOptions
{
    private readonly string _longPattern;
    private readonly string _fractionalPattern;
    private readonly string[] _parsePatterns;

    /// <summary>Creates explicit time-entry policy.</summary>
    public TimePickerOptions(
        CultureInfo? culture = null,
        bool allowNull = false,
        TimeOnly? minimum = null,
        TimeOnly? maximum = null,
        TimeSpan? step = null
    )
    {
        if (minimum is { } min && maximum is { } max && min > max)
            throw new ArgumentException("The minimum time cannot follow the maximum time.");
        var actualStep = step ?? TimeSpan.FromMinutes(1);
        if (actualStep <= TimeSpan.Zero || actualStep >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(step));
        Culture = CultureInfo.ReadOnly(
            (CultureInfo)(culture ?? CultureInfo.CurrentCulture).Clone()
        );
        AllowNull = allowNull;
        Minimum = minimum;
        Maximum = maximum;
        Step = actualStep;
        _longPattern = Culture.DateTimeFormat.LongTimePattern;
        var secondsEnd = _longPattern.LastIndexOf('s');
        _fractionalPattern =
            secondsEnd >= 0
                ? _longPattern.Insert(secondsEnd + 1, ".FFFFFFF")
                : _longPattern + ":ss.FFFFFFF";
        _parsePatterns =
        [
            Culture.DateTimeFormat.ShortTimePattern,
            _longPattern,
            _fractionalPattern,
        ];
    }

    /// <summary>Gets the read-only culture used for 12-hour or 24-hour formatting.</summary>
    public CultureInfo Culture { get; }

    /// <summary>Gets whether clearing can request a null time.</summary>
    public bool AllowNull { get; }

    /// <summary>Gets the inclusive minimum time.</summary>
    public TimeOnly? Minimum { get; }

    /// <summary>Gets the inclusive maximum time.</summary>
    public TimeOnly? Maximum { get; }

    /// <summary>
    /// Gets the positive step smaller than one day. Stepping clamps exactly to explicit bounds;
    /// at a default day boundary it retains the last reachable value on the configured step.
    /// </summary>
    public TimeSpan Step { get; }

    internal string Format(TimeOnly? value)
    {
        if (value is not { } time)
            return "";
        var pattern =
            time.Ticks % TimeSpan.TicksPerSecond != 0 || Step.Ticks % TimeSpan.TicksPerSecond != 0
                ? _fractionalPattern
            : time.Ticks % TimeSpan.TicksPerMinute != 0 || Step.Ticks % TimeSpan.TicksPerMinute != 0
                ? _longPattern
            : Culture.DateTimeFormat.ShortTimePattern;
        return time.ToString(pattern, Culture);
    }

    internal bool TryParse(string text, out TimeOnly value) =>
        TimeOnly.TryParseExact(
            text,
            _parsePatterns,
            Culture,
            DateTimeStyles.AllowWhiteSpaces,
            out value
        );

    internal bool InRange(TimeOnly value) =>
        (Minimum is null || value >= Minimum) && (Maximum is null || value <= Maximum);
}

/// <summary>Optional Field presentation and participation shared by date and time pickers.</summary>
public sealed class DateTimeFieldOptions
{
    /// <summary>Creates optional field presentation without duplicating picker value policy.</summary>
    public DateTimeFieldOptions(
        Func<string>? help = null,
        Func<ValidationState>? validation = null,
        FormSession? form = null,
        string? fieldId = null,
        FieldParticipation participation = FieldParticipation.WhenNotCollapsed,
        bool required = false,
        Func<bool>? enabled = null,
        Func<bool>? readOnly = null,
        Style? style = null
    )
    {
        if (!Enum.IsDefined(participation))
            throw new ArgumentOutOfRangeException(nameof(participation));
        Help = help;
        Validation = validation;
        Form = form;
        FieldId = fieldId;
        Participation = participation;
        Required = required;
        Enabled = enabled;
        ReadOnly = readOnly;
        Style = style;
    }

    /// <summary>Gets optional help content.</summary>
    public Func<string>? Help { get; }

    /// <summary>Gets optional application validation used when the draft parses successfully.</summary>
    public Func<ValidationState>? Validation { get; }

    /// <summary>Gets the optional form session.</summary>
    public FormSession? Form { get; }

    /// <summary>Gets the stable field identifier.</summary>
    public string? FieldId { get; }

    /// <summary>Gets retained-tree form participation.</summary>
    public FieldParticipation Participation { get; }

    /// <summary>Gets whether the visual label includes a required marker.</summary>
    public bool Required { get; }

    /// <summary>Gets the optional enabled reader.</summary>
    public Func<bool>? Enabled { get; }

    /// <summary>Gets the optional read-only reader.</summary>
    public Func<bool>? ReadOnly { get; }

    /// <summary>Gets the optional field style.</summary>
    public Style? Style { get; }
}

internal sealed class DateEditSession
{
    private readonly Func<DateOnly?> _applied;
    private readonly Action<DateOnly?> _request;
    private readonly DatePickerOptions _options;
    private readonly Signal<string> _draft;
    private readonly Signal<ValidationState> _validation;
    private readonly Signal<DateOnly?> _lastApplied;

    internal DateEditSession(
        ReactiveScope scope,
        Func<DateOnly?> applied,
        Action<DateOnly?> request,
        DatePickerOptions options,
        string name
    )
    {
        _applied = applied;
        _request = request;
        _options = options;
        var initial = applied();
        ValidateApplied(initial);
        _lastApplied = scope.Signal(initial, name + ".applied");
        _draft = scope.Signal(options.Format(initial), name + ".draft");
        _validation = scope.Signal(ValidationState.Valid, name + ".validation");
        _ = scope.Effect(Synchronize, name + ".synchronize");
    }

    internal string Draft => _draft.Value;
    internal ValidationState Validation => _validation.Value;
    internal DateOnly? Applied => _lastApplied.Value;
    internal DatePickerOptions Options => _options;

    internal void Edit(string value)
    {
        _draft.Value = value;
        _validation.Value = ValidationState.Valid;
    }

    internal void Cancel()
    {
        _draft.Value = _options.Format(_lastApplied.Value);
        _validation.Value = ValidationState.Valid;
    }

    internal bool Commit()
    {
        var text = _draft.Value.Trim();
        if (text.Length == 0 && _options.AllowNull)
        {
            _request(null);
            return true;
        }
        if (
            !DateOnly.TryParseExact(
                text,
                _options.Culture.DateTimeFormat.ShortDatePattern,
                _options.Culture,
                DateTimeStyles.AllowWhiteSpaces,
                out var value
            )
        )
            return Reject("Enter a valid date.");
        if (!_options.InRange(value))
            return Reject("Enter a date within the allowed range.");
        _request(value);
        return true;
    }

    internal void Select(DateOnly value)
    {
        if (!_options.InRange(value))
            return;
        _draft.Value = _options.Format(value);
        _validation.Value = ValidationState.Valid;
        _request(value);
    }

    private bool Reject(string message)
    {
        _validation.Value = ValidationState.Invalid(message);
        return false;
    }

    private void Synchronize()
    {
        var value = _applied();
        ValidateApplied(value);
        if (value == _lastApplied.Value)
            return;
        _lastApplied.Value = value;
        _draft.Value = _options.Format(value);
        _validation.Value = ValidationState.Valid;
    }

    private void ValidateApplied(DateOnly? value)
    {
        if (value is null ? !_options.AllowNull : !_options.InRange(value.Value))
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}

internal sealed class TimeEditSession
{
    private readonly Func<TimeOnly?> _applied;
    private readonly Action<TimeOnly?> _request;
    private readonly TimePickerOptions _options;
    private readonly Signal<string> _draft;
    private readonly Signal<ValidationState> _validation;
    private TimeOnly? _lastApplied;

    internal TimeEditSession(
        ReactiveScope scope,
        Func<TimeOnly?> applied,
        Action<TimeOnly?> request,
        TimePickerOptions options,
        string name
    )
    {
        _applied = applied;
        _request = request;
        _options = options;
        _lastApplied = applied();
        ValidateApplied(_lastApplied);
        _draft = scope.Signal(options.Format(_lastApplied), name + ".draft");
        _validation = scope.Signal(ValidationState.Valid, name + ".validation");
        _ = scope.Effect(Synchronize, name + ".synchronize");
    }

    internal string Draft => _draft.Value;
    internal ValidationState Validation => _validation.Value;
    internal TimeOnly? Applied => _lastApplied;

    internal void Edit(string value)
    {
        _draft.Value = value;
        _validation.Value = ValidationState.Valid;
    }

    internal void Cancel()
    {
        _draft.Value = _options.Format(_lastApplied);
        _validation.Value = ValidationState.Valid;
    }

    internal bool Commit()
    {
        var text = _draft.Value.Trim();
        if (text.Length == 0 && _options.AllowNull)
        {
            _request(null);
            return true;
        }
        if (!_options.TryParse(text, out var value))
            return Reject("Enter a valid time.");
        if (!_options.InRange(value))
            return Reject("Enter a time within the allowed range.");
        _request(value);
        return true;
    }

    internal bool Step(int direction)
    {
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));
        var basis =
            _options.TryParse(_draft.Value, out var draft) && _options.InRange(draft)
                ? draft
                : _lastApplied ?? TimeOnly.MinValue;
        var ticks = basis.Ticks + direction * _options.Step.Ticks;
        var min = _options.Minimum?.Ticks ?? TimeOnly.MinValue.Ticks;
        var max = _options.Maximum?.Ticks ?? TimeOnly.MaxValue.Ticks;
        if (_options.Minimum is null && ticks < min)
            ticks = basis.Ticks;
        if (_options.Maximum is null && ticks > max)
            ticks = basis.Ticks;
        var value = new TimeOnly(Math.Clamp(ticks, min, max));
        _draft.Value = _options.Format(value);
        _validation.Value = ValidationState.Valid;
        _request(value);
        return true;
    }

    private bool Reject(string message)
    {
        _validation.Value = ValidationState.Invalid(message);
        return false;
    }

    private void Synchronize()
    {
        var value = _applied();
        ValidateApplied(value);
        if (value == _lastApplied)
            return;
        _lastApplied = value;
        _draft.Value = _options.Format(value);
        _validation.Value = ValidationState.Valid;
    }

    private void ValidateApplied(TimeOnly? value)
    {
        if (value is null ? !_options.AllowNull : !_options.InRange(value.Value))
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
