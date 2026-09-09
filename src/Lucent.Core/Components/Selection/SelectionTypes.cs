namespace Lucent.Core;

/// <summary>The applied state of a checkbox.</summary>
public enum CheckState
{
    /// <summary>The checkbox is cleared.</summary>
    Off,

    /// <summary>The checkbox is checked.</summary>
    On,

    /// <summary>The checkbox represents a mixed aggregate.</summary>
    Mixed,
}

/// <summary>Determines how user activation advances a checkbox from its applied state.</summary>
public enum CheckStateCycle
{
    /// <summary>Requests On from Off or Mixed, then alternates between On and Off.</summary>
    Binary,

    /// <summary>Cycles Off, On, Mixed, then Off.</summary>
    TriState,
}

/// <summary>Determines whether a radio group exposes an at-least-one selection requirement.</summary>
public enum RadioSelectionRequirement
{
    /// <summary>The application may apply no matching selected key.</summary>
    Optional,

    /// <summary>The application is expected to apply one matching selected key.</summary>
    Required,
}

/// <summary>An explicit optional applied key for keyed selection controls.</summary>
public readonly struct SelectedKey<TKey> : IEquatable<SelectedKey<TKey>>
    where TKey : notnull
{
    private readonly TKey? _value;

    internal SelectedKey(TKey value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        HasValue = true;
    }

    /// <summary>Gets whether this state contains a key.</summary>
    public bool HasValue { get; }

    /// <summary>Gets the selected key.</summary>
    public TKey Value =>
        HasValue
            ? _value!
            : throw new InvalidOperationException("The applied selection has no key.");

    /// <inheritdoc />
    public bool Equals(SelectedKey<TKey> other) =>
        HasValue == other.HasValue
        && (!HasValue || EqualityComparer<TKey>.Default.Equals(_value!, other._value!));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SelectedKey<TKey> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HasValue ? EqualityComparer<TKey>.Default.GetHashCode(_value!) : 0;

    /// <summary>Compares two optional selected keys.</summary>
    public static bool operator ==(SelectedKey<TKey> left, SelectedKey<TKey> right) =>
        left.Equals(right);

    /// <summary>Compares two optional selected keys.</summary>
    public static bool operator !=(SelectedKey<TKey> left, SelectedKey<TKey> right) =>
        !left.Equals(right);
}

/// <summary>Creates explicit optional applied keys for keyed selection controls.</summary>
public static class SelectedKey
{
    /// <summary>Creates an applied state with no selected key.</summary>
    public static SelectedKey<TKey> None<TKey>()
        where TKey : notnull => default;

    /// <summary>Creates an applied state containing <paramref name="value"/>.</summary>
    public static SelectedKey<TKey> Some<TKey>(TKey value)
        where TKey : notnull => new(value);
}

/// <summary>One keyed option in a <c>RadioGroup</c> component.</summary>
public sealed class RadioOption<TKey>
    where TKey : notnull
{
    /// <summary>Creates a keyed radio option.</summary>
    public RadioOption(TKey key, string label, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Key = key;
        Label = label;
        Enabled = enabled;
    }

    /// <summary>Gets the stable application key.</summary>
    public TKey Key { get; }

    /// <summary>Gets the visible and accessible label.</summary>
    public string Label { get; }

    /// <summary>Gets whether the option can be focused and selected.</summary>
    public bool Enabled { get; }
}
