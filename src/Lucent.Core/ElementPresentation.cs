using System.Numerics;
using System.Text;

namespace Lucent.Core;

internal sealed class ElementPresentation
{
    private readonly Element _element;
    private readonly ThemeContext _theme;
    internal ThemeContext Theme => _theme;
    private readonly FlatAssignment[] _component;
    private readonly FlatAssignment[] _author;
    private readonly Transition[] _transitions;
    private readonly Dictionary<IProperty, Signal<TransitionController.Sample?>> _samples;
    private readonly Signal<VariantState> _variants;
    private readonly Signal<VariantState> _behaviorVariants;
    private readonly Dictionary<IProperty, IControlValue> _control = [];
    internal Element Element => _element;

    internal ElementPresentation(
        Element element,
        ThemeContext theme,
        Style component,
        Style author,
        Transition[] transitions
    )
    {
        _element = element;
        _theme = theme;
        _transitions = [.. transitions];
        Validate(component, author, _transitions);
        _samples = _transitions.ToDictionary(
            item => item.Property,
            item =>
                element.Scope.Signal<TransitionController.Sample?>(
                    null,
                    element.Name + ".transition." + item.Property.Name
                )
        );
        _variants = element.Scope.Signal(VariantState.None, element.Name + ".variants");
        _behaviorVariants = element.Scope.Signal(
            VariantState.None,
            element.Name + ".behavior-variants"
        );
        _component = Materialize(component.Flatten(), "component").ToArray();
        _author = Materialize(author.Flatten(), "author").ToArray();
        foreach (var assignment in _component.Concat(_author).Select(item => item.Assignment))
            assignment.Prime(theme);
    }

    internal void SetVariants(VariantState variants)
    {
        _element.Composition.ThrowIfBehaviorAttachment();
        VariantStates.Validate(variants, nameof(variants), true);
        _variants.Value = variants;
    }

    internal bool SetBehaviorVariants(VariantState variants)
    {
        VariantStates.Validate(variants, nameof(variants), true);
        if (_behaviorVariants.Value == variants)
            return false;
        _behaviorVariants.Value = variants;
        return true;
    }

    internal bool IsActive(VariantState condition) =>
        ((_variants.Value | _behaviorVariants.Value) & condition) == condition;

    /// <summary>Internal control-only mutable channel; control-owned values are authoritative for their properties.</summary>
    internal void SetControl<T>(Property<T> property, T value)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (_control.TryGetValue(property, out var existing))
        {
            ((ControlValue<T>)existing).Value = value;
            return;
        }
        _control.Add(
            property,
            new ControlValue<T>(
                _element.Scope.Signal(value, _element.Name + ".control." + property.Name)
            )
        );
        // A newly introduced winning slot was absent from the previous projection's dependencies.
        // Paint-only values do not change hit geometry, text editing, or availability.
        if (
            !ReferenceEquals(property, VisualProperties.Background)
            && !ReferenceEquals(property, VisualProperties.Opacity)
            && !ReferenceEquals(property, TypographyProperties.TextColor)
        )
            _element.Composition.InvalidateInputProjection();
    }

    internal void Start<T>(Property<T> property, T value)
    {
        var spec =
            _transitions.SingleOrDefault(spec => ReferenceEquals(spec.Property, property))
            ?? throw new InvalidOperationException(
                "No transition specification exists for this property."
            );
        var slot = _samples[property];
        _element.Composition.Transitions.Start(
            _element,
            property,
            value,
            spec,
            sample => slot.Value = sample,
            () => slot.Value = null
        );
    }

    internal ResolvedProperty<T> Resolve<T>(Property<T> property) => _element.Resolve(property);

    internal ResolvedProperty<T> ResolveLocal<T>(
        Property<T> property,
        ResolvedProperty<T>? inherited
    )
    {
        ArgumentNullException.ThrowIfNull(property);
        var candidates = new List<(T Value, PropertyProvenance Provenance)>
        {
            (property.DefaultValue, new("default", 0)),
        };
        if (property.Inherits && inherited is not null)
            candidates.Add((inherited.Value, new("inherited", inherited.Winner.Ordinal)));
        var conditional = _component
            .Concat(_author)
            .Any(item =>
                ReferenceEquals(item.Assignment.Property, property)
                && (item.Condition != VariantState.None || item.Conditions.Length != 0)
            );
        var active = conditional ? _variants.Value | _behaviorVariants.Value : VariantState.None;
        var resolved =
            new List<(
                T Value,
                PropertyProvenance Provenance,
                VariantState Condition,
                int Source,
                int Ordinal
            )>();
        foreach (
            var item in _component
                .Select(item => (item, author: false))
                .Concat(_author.Select(item => (item, author: true)))
                .Where(entry =>
                    ReferenceEquals(entry.item.Assignment.Property, property)
                    && entry.item.Assignment.IsAvailable
                    && (active & entry.item.Condition) == entry.item.Condition
                    && entry.item.MountedConditions.All(read => read())
                )
        )
            resolved.Add(
                (
                    (T)item.item.Assignment.Resolve(_theme)!,
                    new(
                        (item.author ? "author" : "component")
                            + (item.item.Conditions.Length == 0 ? "" : ":when")
                            + (
                                item.item.Assignment.TokenName is { } token
                                    ? ":token:"
                                        + token
                                        + ":"
                                        + (
                                            item.item.Assignment.IsThemed(_theme)
                                                ? "theme"
                                                : "fallback"
                                        )
                                    : ""
                            ),
                        item.item.Ordinal,
                        item.item.Condition
                    ),
                    item.item.Condition,
                    item.author ? 1 : 0,
                    item.item.Ordinal
                )
            );
        foreach (
            var item in resolved
                .OrderBy(item => VariantOrder.Key(item.Condition))
                .ThenBy(item => item.Source)
                .ThenBy(item => item.Ordinal)
        )
            candidates.Add((item.Value, item.Provenance));
        if (_control.TryGetValue(property, out var control))
            candidates.Add(((T)control.Value!, new("control", 0)));
        PropertyProvenance? suppressed = null;
        if (_samples.TryGetValue(property, out var slot) && slot.Value is { } sample)
        {
            if (_theme.IsReducedMotion)
                suppressed = new("transition-suppressed", sample.Ordinal);
            else
                candidates.Add(((T)sample.Value!, new("transition", sample.Ordinal)));
        }
        var winner = candidates[^1];
        return new(
            winner.Value,
            winner.Provenance,
            candidates.Take(candidates.Count - 1).Select(item => item.Provenance).ToArray(),
            suppressed
        );
    }

    internal void AppendDump(StringBuilder dump)
    {
        dump.Append("  style variants=")
            .Append(_variants.Value | _behaviorVariants.Value)
            .Append(" reducedMotion=")
            .Append(_theme.IsReducedMotion ? "true" : "false")
            .Append('\n');
        foreach (var property in Properties().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var value = ((IPropertyDump)property).Dump(this);
            dump.Append("  property name=")
                .Append(Quote(property.Name))
                .Append(" winner=")
                .Append(Format(value.Winner))
                .Append(" overridden=[")
                .Append(string.Join(',', value.Overridden.Select(Format)))
                .Append(']');
            if (value.Suppressed is not null)
                dump.Append(" suppressed=").Append(Format(value.Suppressed));
            dump.Append('\n');
        }
    }

    private IEnumerable<IProperty> Properties() =>
        OwnProperties()
            .Concat(_element.AncestorProperties().Where(property => property.Inherits))
            .Distinct();

    internal IEnumerable<IProperty> OwnProperties() =>
        _component
            .Concat(_author)
            .Select(item => item.Assignment.Property)
            .Concat(_control.Keys)
            .Concat(_transitions.Select(item => item.Property));

    internal static void Validate(Style component, Style author, Transition[] transitions)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(transitions);
        var componentAssignments = component.Flatten().ToArray();
        var authorAssignments = author.Flatten().ToArray();
        ValidateProperties(
            componentAssignments
                .Concat(authorAssignments)
                .Select(item => item.Assignment.Property)
                .Concat(transitions.Select(item => item.Property))
        );
        if (transitions.GroupBy(item => item.Property).Any(group => group.Count() != 1))
            throw new ArgumentException(
                "Transition properties must be unique.",
                nameof(transitions)
            );
    }

    private static void ValidateProperties(IEnumerable<IProperty> properties)
    {
        var names = new Dictionary<string, IProperty>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (
                names.TryGetValue(property.Name, out var prior) && !ReferenceEquals(prior, property)
            )
                throw new ArgumentException(
                    "A presentation cannot contain distinct properties with the same name."
                );
            names[property.Name] = property;
        }
    }

    private IEnumerable<FlatAssignment> Materialize(
        IEnumerable<FlatAssignment> assignments,
        string group
    )
    {
        var mounted = new Dictionary<StyleCondition, MountedCondition>(
            ReferenceEqualityComparer.Instance
        );
        foreach (var item in assignments)
        {
            Derived<bool>? parent = null;
            var conditions = item
                .Conditions.Select(condition =>
                {
                    if (!mounted.TryGetValue(condition, out var value))
                    {
                        var name = _element.Name + ".style-when." + group + "." + mounted.Count;
                        var enclosing = parent;
                        // Read a lazy parent gate, rather than its published signal: a parent
                        // invalidated in the same batch must be refreshed before its child runs.
                        var gate = _element.Scope.Derived(
                            () =>
                                (enclosing?.Value ?? true)
                                && (
                                    condition.Variants == VariantState.None
                                    || IsActive(condition.Variants)
                                )
                                && _element.Composition.RunStyleCondition(condition.Read),
                            name + ".gate"
                        );
                        value = new(gate, _element.Scope.Signal(false, name));
                        mounted.Add(condition, value);
                        _ = _element.Scope.Effect(
                            () => value.Published.Value = gate.Value,
                            _element.Name
                                + ".style-when-effect."
                                + group
                                + "."
                                + (mounted.Count - 1)
                        );
                    }
                    parent = value.Gate;
                    return (Func<bool>)(() => value.Published.Value);
                })
                .ToArray();
            bool Active() =>
                (item.Condition == VariantState.None || IsActive(item.Condition))
                && conditions.All(read => read());
            yield return item with
            {
                Assignment = item.Assignment is IBindingAssignment binding
                    ? binding.Materialize(this, Active, item.Ordinal)
                    : item.Assignment,
                MountedConditions = conditions,
            };
        }
    }

    private sealed record MountedCondition(Derived<bool> Gate, Signal<bool> Published);

    private interface IControlValue
    {
        object? Value { get; }
    }

    private sealed class ControlValue<T>(Signal<T> signal) : IControlValue
    {
        public T Value
        {
            get => signal.Value;
            set => signal.Value = value;
        }
        object? IControlValue.Value => Value;
    }

    private string Format(PropertyProvenance value) =>
        DiagnosticText.Quote(value.Source)
        + "#"
        + value.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + (value.Condition == VariantState.None ? "" : "[" + value.Condition + "]");

    private static string Quote(string value) => DiagnosticText.Quote(value);
}

internal sealed class TransitionController
{
    private readonly Dictionary<(long Element, IProperty Property), Entry> _samples = [];
    private long _clock;
    private int _nextOrdinal;

    internal void Start<T>(
        Element element,
        Property<T> property,
        T value,
        Transition spec,
        Action<Sample> set,
        Action clear
    )
    {
        var sample = new Sample(value, checked(_clock + spec.DurationMilliseconds), ++_nextOrdinal);
        set(sample);
        _samples[(element.Id, property)] = new(sample, clear);
    }

    internal void Advance(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds, nameof(milliseconds));
        _clock = checked(_clock + milliseconds);
        Expire();
    }

    internal void Remove(Element element)
    {
        foreach (var key in _samples.Keys.Where(key => key.Element == element.Id).ToArray())
        {
            _samples[key].Clear();
            _samples.Remove(key);
        }
    }

    private void Expire()
    {
        foreach (
            var item in _samples
                .Where(item => item.Value.Sample.ExpiresAt <= _clock)
                .Select(item => item.Key)
                .ToArray()
        )
        {
            _samples[item].Clear();
            _samples.Remove(item);
        }
    }

    internal readonly record struct Sample(object? Value, long ExpiresAt, int Ordinal);

    private readonly record struct Entry(Sample Sample, Action Clear);
}

internal static class TransitionTypes
{
    internal static bool Matches(TransitionKind kind, Type type) =>
        kind switch
        {
            TransitionKind.Color => type == typeof(Color),
            TransitionKind.Opacity or TransitionKind.FocusRing => type == typeof(float),
            TransitionKind.Transform => type == typeof(Matrix3x2),
            _ => true,
        };
}

internal static class VariantStates
{
    private const VariantState All =
        VariantState.Hover
        | VariantState.FocusVisible
        | VariantState.Selected
        | VariantState.Pressed
        | VariantState.Invalid
        | VariantState.Disabled;

    internal static void Validate(VariantState state, string parameter, bool allowNone)
    {
        if ((!allowNone && state == VariantState.None) || ((uint)state & ~(uint)All) != 0)
            throw new ArgumentException(
                "Variants must use the documented finite state set.",
                parameter
            );
    }
}

internal static class VariantOrder
{
    private static readonly VariantState[] Order =
    [
        VariantState.Disabled,
        VariantState.Invalid,
        VariantState.Pressed,
        VariantState.Selected,
        VariantState.FocusVisible,
        VariantState.Hover,
    ];

    internal static string Key(VariantState state) =>
        BitOperations
            .PopCount((uint)state)
            .ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
        + string.Concat(Order.Select(flag => state.HasFlag(flag) ? '1' : '0'));
}

internal static class DiagnosticText
{
    internal static string Quote(string value) =>
        '"'
        + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
        + '"';
}
