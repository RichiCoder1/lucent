using System.Numerics;
using System.Text;

namespace Lucent.Core;

public sealed class Style
{
    private readonly Node[] _nodes;

    private Style(Node[] nodes) => _nodes = nodes;

    public static Style Empty { get; } = new([]);

    public Style Set<T>(Property<T> property, T value) => Add(new Assignment<T>(property, value));

    public Style Set<T>(Property<T> property, Token<T> token) =>
        Add(new Assignment<T>(property, token));

    /// <summary>Reads a live value when this candidate's variant is active on a presented element.</summary>
    public Style Bind<T>(Property<T> property, Func<T> read) =>
        Add(new BindingAssignment<T>(property, read));

    /// <summary>Appends optional assignments; later assignments win.</summary>
    public Style With(Style? style) => style is null ? this : new([.. _nodes, .. style._nodes]);

    public Style When(VariantState when, Style style)
    {
        VariantStates.Validate(when, nameof(when), false);
        ArgumentNullException.ThrowIfNull(style);
        return new([.. _nodes, new VariantNode(when, style)]);
    }

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
        foreach (var assignment in Flatten(VariantState.None))
            yield return assignment with
            {
                Ordinal = ordinal++,
            };
    }

    private IEnumerable<FlatAssignment> Flatten(VariantState condition)
    {
        foreach (var node in _nodes)
            if (node is AssignmentNode assignmentNode)
                yield return new FlatAssignment(assignmentNode.Assignment, condition, 0);
            else if (node is VariantNode variant)
                foreach (var assignment in variant.Style.Flatten(condition | variant.Condition))
                    yield return assignment;
    }

    private Style Add(IAssignment assignment) => new([.. _nodes, new AssignmentNode(assignment)]);

    private abstract record Node;

    private sealed record AssignmentNode(IAssignment Assignment) : Node;

    private sealed record VariantNode(VariantState Condition, Style Style) : Node;
}

/// <summary>Immutable eligible-property/duration specification. Values exist only in active timeline samples.</summary>
public sealed class Transition
{
    private Transition(IProperty property, int durationMilliseconds)
    {
        Property = property;
        DurationMilliseconds = durationMilliseconds;
    }

    internal IProperty Property { get; }
    public int DurationMilliseconds { get; }

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

public sealed record ResolvedProperty<T>(
    T Value,
    PropertyProvenance Winner,
    IReadOnlyList<PropertyProvenance> Overridden,
    PropertyProvenance? SuppressedTransition = null
);

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
    IAssignment Materialize(ElementPresentation presentation, VariantState condition, int ordinal);
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
        VariantState condition,
        int ordinal
    ) => new BoundAssignment<T>(presentation, _property, _read, condition, ordinal);
}

internal sealed class BoundAssignment<T> : IAssignment
{
    private readonly Signal<T> _value;
    private readonly Signal<bool> _available;

    internal BoundAssignment(
        ElementPresentation presentation,
        Property<T> property,
        Func<T> read,
        VariantState condition,
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
                if (presentation.IsActive(condition))
                {
                    _value.Value = read();
                    _available.Value = true;
                }
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
    int Ordinal
);
