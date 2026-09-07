namespace Lucent.Core;

/// <summary>A typed style value supplied directly or resolved from the mounted element's theme.</summary>
/// <remarks>Selecting a token preserves its identity for dependency tracking and diagnostic provenance.</remarks>
public readonly record struct StyleValue<T>
{
    internal StyleValue(T value, Token<T>? token)
    {
        Value = value;
        Token = token;
    }

    internal T Value { get; }
    internal Token<T>? Token { get; }

    /// <summary>Converts a concrete property value into a style value.</summary>
    public static implicit operator StyleValue<T>(T value) => StyleValue.FromValue(value);

    /// <summary>Converts a theme token into a style value without resolving it prematurely.</summary>
    public static implicit operator StyleValue<T>(Token<T> token) => StyleValue.FromToken(token);
}

/// <summary>Creates typed concrete or themed style values without ambiguous null conversions.</summary>
public static class StyleValue
{
    /// <summary>Creates a concrete value, including null when the property type permits it.</summary>
    public static StyleValue<T> FromValue<T>(T value) => new(value, null);

    /// <summary>Creates a themed value; a null token is rejected.</summary>
    public static StyleValue<T> FromToken<T>(Token<T> token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new(default!, token);
    }
}

internal sealed class ValueBindingAssignment<T> : IAssignment, IBindingAssignment
{
    private readonly Property<T> _property;
    private readonly Func<StyleValue<T>> _read;

    internal ValueBindingAssignment(Property<T> property, Func<StyleValue<T>> read)
    {
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _read = read ?? throw new ArgumentNullException(nameof(read));
    }

    public IProperty Property => _property;
    public bool IsAvailable => false;
    public string? TokenName => null;

    public bool IsThemed(ThemeContext theme) => false;

    public object? Resolve(ThemeContext theme) =>
        throw new InvalidOperationException("Bindings are materialized only by Present.");

    public void Prime(ThemeContext theme) { }

    public IAssignment Materialize(
        ElementPresentation presentation,
        VariantState condition,
        int ordinal
    ) => new BoundStyleValue<T>(presentation, _property, _read, condition, ordinal);
}

internal sealed class BoundStyleValue<T> : IAssignment
{
    private readonly Signal<Snapshot> _resolved;
    private readonly Signal<bool> _available;

    internal BoundStyleValue(
        ElementPresentation presentation,
        Property<T> property,
        Func<StyleValue<T>> read,
        VariantState condition,
        int ordinal
    )
    {
        Property = property;
        var scope = presentation.Element.Scope;
        var name = presentation.Element.Name + ".bind-value." + property.Name + "." + ordinal;
        _resolved = scope.Signal(new Snapshot(property.DefaultValue, null, false), name);
        _available = scope.Signal(false, name + ".available");
        _ = scope.Effect(
            () =>
            {
                if (!presentation.IsActive(condition))
                {
                    _available.Value = false;
                    return;
                }
                var selected = read();
                var token = selected.Token;
                var theme = presentation.Theme;
                _resolved.Value = token is null
                    ? new(selected.Value, null, false)
                    : new(theme.Token(token), token, theme.IsThemed(token));
                _available.Value = true;
            },
            name + ".effect"
        );
    }

    public IProperty Property { get; }
    public bool IsAvailable => _available.Value;
    public string? TokenName => _resolved.Value.Token?.Name;

    public bool IsThemed(ThemeContext theme) => _resolved.Value.Themed;

    public object? Resolve(ThemeContext theme) => _resolved.Value.Value;

    public void Prime(ThemeContext theme) { }

    private readonly record struct Snapshot(T Value, Token<T>? Token, bool Themed);
}
