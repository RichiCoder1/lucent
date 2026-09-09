namespace Lucent.Core;

/// <summary>The finite outcome of validating one field draft.</summary>
public enum ValidationStatus
{
    /// <summary>The current draft satisfies the application rule.</summary>
    Valid,

    /// <summary>The current draft is awaiting a validation result.</summary>
    Pending,

    /// <summary>The current draft has one or more expected validation messages.</summary>
    Invalid,
}

/// <summary>Immutable validation state for one field draft.</summary>
public sealed class ValidationState
{
    private readonly IReadOnlyList<string> _messages;

    private ValidationState(
        ValidationStatus status,
        IEnumerable<string>? messages,
        long generation,
        Task? completion
    )
    {
        if (!Enum.IsDefined(status) || generation < 0)
            throw new ArgumentException("Validation state must be finite.");
        var copy = messages?.Select(Message).ToArray() ?? [];
        if (
            status == ValidationStatus.Invalid != (copy.Length != 0)
            || status == ValidationStatus.Pending != (generation != 0)
            || status != ValidationStatus.Pending && completion is not null
        )
            throw new ArgumentException(
                "Invalid validation requires messages and pending validation requires a positive generation."
            );
        Status = status;
        _messages = Array.AsReadOnly(copy);
        Generation = generation;
        Completion = completion;
    }

    /// <summary>A reusable valid state.</summary>
    public static ValidationState Valid { get; } = new(ValidationStatus.Valid, null, 0, null);

    /// <summary>Creates a pending state for the supplied latest-generation validation.</summary>
    public static ValidationState Pending(long generation, Task? completion = null) =>
        new(ValidationStatus.Pending, null, generation, completion);

    /// <summary>Creates an invalid state with immutable, nonempty messages.</summary>
    public static ValidationState Invalid(params ReadOnlySpan<string> messages) =>
        new(ValidationStatus.Invalid, messages.ToArray(), 0, null);

    /// <summary>Gets the finite validation outcome.</summary>
    public ValidationStatus Status { get; }

    /// <summary>Gets immutable application-supplied validation messages.</summary>
    public IReadOnlyList<string> Messages => _messages;

    /// <summary>Gets the positive pending generation, or zero for a settled state.</summary>
    public long Generation { get; }

    /// <summary>Gets an optional completion that a form may await before re-reading validation.</summary>
    public Task? Completion { get; }

    /// <summary>Gets whether this state is valid.</summary>
    public bool IsValid => Status == ValidationStatus.Valid;

    /// <summary>Gets whether this state is awaiting validation.</summary>
    public bool IsPending => Status == ValidationStatus.Pending;

    /// <summary>Gets whether this state contains expected validation errors.</summary>
    public bool IsInvalid => Status == ValidationStatus.Invalid;

    /// <summary>Applies a settled result only when it belongs to this pending generation.</summary>
    public bool TryComplete(long generation, ValidationState result, out ValidationState current)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsPending)
            throw new ArgumentException(
                "A pending validation cannot complete with another pending state.",
                nameof(result)
            );
        if (IsPending && Generation == generation)
        {
            current = result;
            return true;
        }
        current = this;
        return false;
    }

    private static string Message(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Validation messages must be nonempty.", nameof(message));
        return message;
    }
}
