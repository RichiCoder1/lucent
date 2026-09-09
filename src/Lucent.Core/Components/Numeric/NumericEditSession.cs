using System.Globalization;

namespace Lucent.Core;

/// <summary>Determines how a successfully parsed value outside the declared range is committed.</summary>
public enum NumericBoundsPolicy
{
    /// <summary>Retains the draft and reports the range error.</summary>
    Reject,

    /// <summary>Commits the nearest bound.</summary>
    Clamp,
}

/// <summary>Determines how an external applied value is reconciled with an active draft.</summary>
public enum NumericExternalChangePolicy
{
    /// <summary>Retains the draft and exposes a conflict until the user commits or cancels.</summary>
    ShowConflict,

    /// <summary>Adopts the new applied value and discards the active draft.</summary>
    Adopt,
}

/// <summary>Immutable decimal editing and range configuration.</summary>
public sealed class NumericEditOptions
{
    /// <summary>Creates validated decimal edit configuration.</summary>
    public NumericEditOptions(
        decimal increment,
        decimal? minimum = null,
        decimal? maximum = null,
        decimal? pageIncrement = null,
        bool allowNull = false,
        NumericBoundsPolicy boundsPolicy = NumericBoundsPolicy.Reject,
        NumericExternalChangePolicy externalChangePolicy = NumericExternalChangePolicy.ShowConflict,
        CultureInfo? culture = null,
        string? format = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(increment);
        if (minimum > maximum)
            throw new ArgumentException("The numeric minimum cannot exceed the maximum.");
        if (pageIncrement is <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageIncrement));
        if (!Enum.IsDefined(boundsPolicy))
            throw new ArgumentOutOfRangeException(nameof(boundsPolicy));
        if (!Enum.IsDefined(externalChangePolicy))
            throw new ArgumentOutOfRangeException(nameof(externalChangePolicy));

        Increment = increment;
        Minimum = minimum;
        Maximum = maximum;
        PageIncrement = pageIncrement ?? checked(increment * 10);
        AllowNull = allowNull;
        BoundsPolicy = boundsPolicy;
        ExternalChangePolicy = externalChangePolicy;
        Culture = CultureInfo.ReadOnly(
            (CultureInfo)(culture ?? CultureInfo.CurrentCulture).Clone()
        );
        Format = string.IsNullOrWhiteSpace(format) ? "G" : format;
        _ = 0m.ToString(Format, Culture);
    }

    /// <summary>Gets the ordinary step size.</summary>
    public decimal Increment { get; }

    /// <summary>Gets the optional inclusive lower bound.</summary>
    public decimal? Minimum { get; }

    /// <summary>Gets the optional inclusive upper bound.</summary>
    public decimal? Maximum { get; }

    /// <summary>Gets the page step size.</summary>
    public decimal PageIncrement { get; }

    /// <summary>Gets whether an empty draft may commit null.</summary>
    public bool AllowNull { get; }

    /// <summary>Gets the out-of-range commit policy.</summary>
    public NumericBoundsPolicy BoundsPolicy { get; }

    /// <summary>Gets the external change policy.</summary>
    public NumericExternalChangePolicy ExternalChangePolicy { get; }

    /// <summary>Gets the frozen parsing and formatting culture.</summary>
    public CultureInfo Culture { get; }

    /// <summary>Gets the decimal format applied after successful commit.</summary>
    public string Format { get; }
}

/// <summary>Reactive decimal draft state with explicit parse, commit, cancel, and controlled-value reconciliation.</summary>
public sealed class NumericEditSession : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Func<decimal?> _readValue;
    private readonly Action<decimal?> _onValueRequested;
    private readonly Signal<string> _draft;
    private readonly Signal<ValidationState> _validation;
    private decimal? _lastApplied;
    private decimal? _pendingRequest;
    private bool _dirty;
    private bool _disposed;

    /// <summary>Creates a scope-owned numeric edit session over a controlled application value.</summary>
    public NumericEditSession(
        ReactiveScope owner,
        Func<decimal?> readValue,
        Action<decimal?> onValueRequested,
        NumericEditOptions options,
        string name = "number-field"
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        _readValue = readValue ?? throw new ArgumentNullException(nameof(readValue));
        _onValueRequested =
            onValueRequested ?? throw new ArgumentNullException(nameof(onValueRequested));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A numeric session name is required.", nameof(name));
        _scope = owner.CreateChild(name);
        _lastApplied = ReadApplied();
        _draft = _scope.Signal(Format(_lastApplied), name + ".draft");
        _validation = _scope.Signal(ValidationState.Valid, name + ".validation");
        _ = _scope.Effect(ReconcileApplied, name + ".applied");
    }

    /// <summary>Gets the validated immutable options.</summary>
    public NumericEditOptions Options { get; }

    /// <summary>Gets the raw draft currently shown by the editor.</summary>
    public string Draft => _draft.Value;

    /// <summary>Gets validation for the current draft or external-value conflict.</summary>
    public ValidationState Validation => _validation.Value;

    /// <summary>Gets the last application value observed by this session.</summary>
    public decimal? LastAppliedValue => _lastApplied;

    /// <summary>Replaces the raw draft without committing the application value.</summary>
    public void Edit(string draft)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("A numeric draft must be a single line.", nameof(draft));
        _draft.Value = draft;
        _dirty = true;
        _pendingRequest = null;
        _validation.Value = ValidateDraft(draft);
    }

    /// <summary>Attempts to commit the current draft, retaining malformed text on failure.</summary>
    public bool Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryParse(_draft.Value, out var parsed, out var message))
        {
            _validation.Value = ValidationState.Invalid(message!);
            return false;
        }
        if (parsed is { } value && Outside(value))
        {
            if (Options.BoundsPolicy == NumericBoundsPolicy.Reject)
            {
                _validation.Value = ValidationState.Invalid(RangeMessage());
                return false;
            }
            parsed = Clamp(value);
        }
        _draft.Value = Format(parsed);
        _validation.Value = ValidationState.Valid;
        _dirty = false;
        _pendingRequest = parsed;
        _onValueRequested(parsed);
        return true;
    }

    /// <summary>Restores the last applied application value without invoking the request callback.</summary>
    public void Cancel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _draft.Value = Format(_lastApplied);
        _validation.Value = ValidationState.Valid;
        _dirty = false;
        _pendingRequest = null;
    }

    /// <summary>Requests one checked step when the draft is currently valid.</summary>
    public bool Step(int direction, bool page = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (!TryParse(_draft.Value, out var parsed, out _) || parsed is null)
        {
            _validation.Value = ValidationState.Invalid("Enter a valid number before stepping.");
            return false;
        }
        decimal next;
        try
        {
            next = checked(
                parsed.Value + direction * (page ? Options.PageIncrement : Options.Increment)
            );
        }
        catch (OverflowException)
        {
            _validation.Value = ValidationState.Invalid("The requested numeric step overflowed.");
            return false;
        }
        next = Clamp(next);
        _draft.Value = Format(next);
        _validation.Value = ValidationState.Valid;
        _dirty = false;
        _pendingRequest = next;
        _onValueRequested(next);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _scope.Dispose();
    }

    private void ReconcileApplied()
    {
        var current = ReadApplied();
        if (current == _lastApplied)
            return;
        _lastApplied = current;
        if (
            _pendingRequest == current
            || !_dirty
            || Options.ExternalChangePolicy == NumericExternalChangePolicy.Adopt
        )
        {
            _draft.Value = Format(current);
            _validation.Value = ValidationState.Valid;
            _dirty = false;
            _pendingRequest = null;
            return;
        }
        _validation.Value = ValidationState.Invalid(
            "The value changed outside this active numeric draft."
        );
    }

    private ValidationState ValidateDraft(string draft) =>
        IsIntermediate(draft) ? ValidationState.Valid
        : TryParse(draft, out var value, out var message)
        && (value is null || !Outside(value.Value))
            ? ValidationState.Valid
        : ValidationState.Invalid(message ?? RangeMessage());

    private bool IsIntermediate(string draft)
    {
        var value = draft.Trim();
        var format = Options.Culture.NumberFormat;
        return value.Length == 0
            || string.Equals(value, format.NegativeSign, StringComparison.Ordinal)
            || string.Equals(value, format.PositiveSign, StringComparison.Ordinal)
            || string.Equals(value, format.NumberDecimalSeparator, StringComparison.Ordinal)
            || string.Equals(
                value,
                format.NegativeSign + format.NumberDecimalSeparator,
                StringComparison.Ordinal
            )
            || string.Equals(
                value,
                format.PositiveSign + format.NumberDecimalSeparator,
                StringComparison.Ordinal
            );
    }

    private bool TryParse(string draft, out decimal? value, out string? message)
    {
        var trimmed = draft.Trim();
        if (trimmed.Length == 0)
        {
            value = null;
            message = Options.AllowNull ? null : "A number is required.";
            return Options.AllowNull;
        }
        if (decimal.TryParse(trimmed, NumberStyles.Number, Options.Culture, out var parsed))
        {
            value = parsed;
            message = null;
            return true;
        }
        value = null;
        message = "Enter a valid number for " + Options.Culture.DisplayName + ".";
        return false;
    }

    private decimal? ReadApplied()
    {
        var value = _readValue();
        if (value is null && !Options.AllowNull)
            throw new InvalidOperationException("The controlled numeric value cannot be null.");
        if (value is { } applied && Outside(applied))
            throw new InvalidOperationException(
                "The controlled numeric value is outside the configured range."
            );
        return value;
    }

    private bool Outside(decimal value) => value < Options.Minimum || value > Options.Maximum;

    private decimal Clamp(decimal value) =>
        Options.Minimum is { } minimum && value < minimum ? minimum
        : Options.Maximum is { } maximum && value > maximum ? maximum
        : value;

    private string Format(decimal? value) =>
        value?.ToString(Options.Format, Options.Culture) ?? string.Empty;

    private string RangeMessage() =>
        $"Enter a value from {Options.Minimum?.ToString(Options.Format, Options.Culture) ?? "negative infinity"} to {Options.Maximum?.ToString(Options.Format, Options.Culture) ?? "positive infinity"}.";
}
