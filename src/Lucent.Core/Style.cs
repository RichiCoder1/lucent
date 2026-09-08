using System.Numerics;
using System.Text;

namespace Lucent.Core;

/// <summary>An immutable collection of layout, visual, text, and input settings for an element.</summary>
public sealed class Style
{
    private readonly Node[] _nodes;

    private Style(Node[] nodes) => _nodes = nodes;

    /// <summary>Gets an empty style with all properties at their defaults.</summary>
    public static Style Empty { get; } = new([]);

    /// <summary>Returns a new style that sets a property to an explicit value.</summary>
    public Style Set<T>(Property<T> property, T value) => Add(new Assignment<T>(property, value));

    /// <summary>Returns a new style that gets a property's value from a theme token.</summary>
    public Style Set<T>(Property<T> property, Token<T> token) =>
        Add(new Assignment<T>(property, token));

    /// <summary>Returns a new style that gets a property's value from a reader while the style applies.</summary>
    public Style Bind<T>(Property<T> property, Func<T> read) =>
        Add(new BindingAssignment<T>(property, read));

    /// <summary>Returns a style that selects a concrete value or theme token during construction.</summary>
    public Style SetValue<T>(Property<T> property, StyleValue<T> value) =>
        value.Token is { } token ? Set(property, token) : Set(property, value.Value);

    /// <summary>Returns a style that observes a concrete value or selected theme token while its variant applies.</summary>
    public Style BindValue<T>(Property<T> property, Func<StyleValue<T>> read) =>
        Add(new ValueBindingAssignment<T>(property, read));

    /// <summary>Returns a new style with the assignments from <paramref name="style"/> appended.</summary>
    public Style With(Style? style) => style is null ? this : new([.. _nodes, .. style._nodes]);

    /// <summary>Returns a new style whose nested settings apply only for the specified interaction states.</summary>
    public Style When(VariantState when, Style style)
    {
        VariantStates.Validate(when, nameof(when), false);
        ArgumentNullException.ThrowIfNull(style);
        return new([.. _nodes, new VariantNode(when, style)]);
    }

    /// <summary>Returns a new style whose nested settings apply while the reactive condition is true.</summary>
    public Style When(Func<bool> condition, Style style)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(style);
        return new([.. _nodes, new ConditionNode(condition, style)]);
    }

    /// <summary>Combines styles in order; settings in later styles override earlier settings.</summary>
    public static Style Compose(params Style[] styles)
    {
        ArgumentNullException.ThrowIfNull(styles);
        var nodes = new List<Node>();
        foreach (var style in styles)
        {
            ArgumentNullException.ThrowIfNull(style);
            nodes.AddRange(style._nodes);
        }
        return new([.. nodes]);
    }

    internal IEnumerable<FlatAssignment> Flatten()
    {
        var ordinal = 0;
        foreach (var assignment in Flatten(VariantState.None, []))
            yield return assignment with
            {
                Ordinal = ordinal++,
            };
    }

    private IEnumerable<FlatAssignment> Flatten(VariantState condition, StyleCondition[] conditions)
    {
        foreach (var node in _nodes)
            if (node is AssignmentNode assignmentNode)
                yield return new FlatAssignment(
                    assignmentNode.Assignment,
                    condition,
                    conditions,
                    [],
                    0
                );
            else if (node is VariantNode variant)
                foreach (
                    var assignment in variant.Style.Flatten(
                        condition | variant.Condition,
                        conditions
                    )
                )
                    yield return assignment;
            else if (node is ConditionNode conditional)
                foreach (
                    var assignment in conditional.Style.Flatten(
                        condition,
                        [.. conditions, new StyleCondition(conditional.Condition, condition)]
                    )
                )
                    yield return assignment;
    }

    private Style Add(IAssignment assignment) => new([.. _nodes, new AssignmentNode(assignment)]);

    private abstract record Node;

    private sealed record AssignmentNode(IAssignment Assignment) : Node;

    private sealed record VariantNode(VariantState Condition, Style Style) : Node;

    private sealed record ConditionNode(Func<bool> Condition, Style Style) : Node;
}

internal sealed class StyleCondition(Func<bool> read, VariantState variants)
{
    internal Func<bool> Read { get; } = read;
    internal VariantState Variants { get; } = variants;
}

/// <summary>Specifies how long an eligible property takes to change between values.</summary>
public sealed class Transition
{
    private Transition(IProperty property, int durationMilliseconds)
    {
        Property = property;
        DurationMilliseconds = durationMilliseconds;
    }

    internal IProperty Property { get; }

    /// <summary>Gets the transition duration in milliseconds.</summary>
    public int DurationMilliseconds { get; }

    /// <summary>Creates a transition for a property that supports animation; duration must be from 1 through 500 milliseconds.</summary>
    public static Transition For<T>(Property<T> property, int durationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!Enum.IsDefined(property.Transition) || property.Transition == TransitionKind.None)
            throw new ArgumentException(
                "Only fixed transition channels are eligible.",
                nameof(property)
            );
        if (durationMilliseconds is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        return new(property, durationMilliseconds);
    }
}

/// <summary>Resolved presentation value with the winning and overridden style assignments.</summary>
public sealed record ResolvedProperty<T>(
    T Value,
    PropertyProvenance Winner,
    IReadOnlyList<PropertyProvenance> Overridden,
    PropertyProvenance? SuppressedTransition = null
);

/// <summary>Diagnostic origin of a style assignment participating in property resolution.</summary>
public sealed record PropertyProvenance(
    string Source,
    int Ordinal,
    VariantState Condition = VariantState.None
);

internal interface IAssignment
{
    IProperty Property { get; }
    bool IsAvailable { get; }
    object? Resolve(ThemeContext theme);
    string? TokenName { get; }
    bool IsThemed(ThemeContext theme);
    void Prime(ThemeContext theme);
}

internal interface IBindingAssignment
{
    IAssignment Materialize(ElementPresentation presentation, Func<bool> active, int ordinal);
}

internal sealed class Assignment<T> : IAssignment
{
    private readonly T? _value;
    private readonly Token<T>? _token;

    internal Assignment(Property<T> property, T value)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
        _value = value;
    }

    internal Assignment(Property<T> property, Token<T> token)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
        _token = token ?? throw new ArgumentNullException(nameof(token));
    }

    public IProperty Property { get; }
    public bool IsAvailable => true;
    public string? TokenName => _token?.Name;

    public bool IsThemed(ThemeContext theme) => _token is not null && theme.IsThemed(_token);

    public object? Resolve(ThemeContext theme) => _token is null ? _value : theme.Token(_token);

    public void Prime(ThemeContext theme)
    {
        if (_token is not null)
            _ = theme.Token(_token);
    }
}

internal sealed class BindingAssignment<T> : IAssignment, IBindingAssignment
{
    private readonly Property<T> _property;
    private readonly Func<T> _read;

    internal BindingAssignment(Property<T> property, Func<T> read)
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
        Func<bool> active,
        int ordinal
    ) => new BoundAssignment<T>(presentation, _property, _read, active, ordinal);
}

internal sealed class BoundAssignment<T> : IAssignment
{
    private readonly Signal<T> _value;
    private readonly Signal<bool> _available;

    internal BoundAssignment(
        ElementPresentation presentation,
        Property<T> property,
        Func<T> read,
        Func<bool> active,
        int ordinal
    )
    {
        Property = property;
        _value = presentation.Element.Scope.Signal(
            property.DefaultValue,
            presentation.Element.Name + ".bind." + property.Name + "." + ordinal
        );
        _available = presentation.Element.Scope.Signal(
            false,
            presentation.Element.Name + ".bind-available." + property.Name + "." + ordinal
        );
        _ = presentation.Element.Scope.Effect(
            () =>
            {
                if (active())
                {
                    _value.Value = read();
                    _available.Value = true;
                }
                else
                    _available.Value = false;
            },
            presentation.Element.Name + ".bind-effect." + property.Name + "." + ordinal
        );
    }

    public IProperty Property { get; }
    public bool IsAvailable => _available.Value;
    public string? TokenName => null;

    public bool IsThemed(ThemeContext theme) => false;

    public object? Resolve(ThemeContext theme) => _value.Value;

    public void Prime(ThemeContext theme) { }
}

internal readonly record struct FlatAssignment(
    IAssignment Assignment,
    VariantState Condition,
    StyleCondition[] Conditions,
    Func<bool>[] MountedConditions,
    int Ordinal
);
