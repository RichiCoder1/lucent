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
    private readonly FlatMotion[] _componentMotion;
    private readonly FlatMotion[] _authorMotion;
    private readonly Signal<VariantState> _variants;
    private readonly Signal<VariantState> _behaviorVariants;
    private readonly Dictionary<IProperty, IControlValue> _control = [];
    private readonly Dictionary<IProperty, object?> _delegatedTargets = [];
    private readonly Dictionary<IProperty, bool> _delegatedAnimatedPolicy = [];
    internal Element Element => _element;

    internal ElementPresentation(Element element, ThemeContext theme, Style component, Style author)
    {
        _element = element;
        _theme = theme;
        Validate(component, author);
        _variants = element.Scope.Signal(VariantState.None, element.Name + ".variants");
        _behaviorVariants = element.Scope.Signal(
            VariantState.None,
            element.Name + ".behavior-variants"
        );
        _component = Materialize(component.Flatten(), "component").ToArray();
        _author = Materialize(author.Flatten(), "author").ToArray();
        _componentMotion = MaterializeMotion(component.FlattenMotion(), "component-motion")
            .ToArray();
        _authorMotion = MaterializeMotion(author.FlattenMotion(), "author-motion").ToArray();
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

    internal ResolvedProperty<T> Resolve<T>(Property<T> property) => _element.Resolve(property);

    internal T ResolveValueLocal<T>(Property<T> property, bool hasInherited, T inherited)
    {
        ArgumentNullException.ThrowIfNull(property);
        var value = hasInherited && property.Inherits ? inherited : property.DefaultValue;
        var hasAssignment = false;
        var winnerKey = int.MinValue;
        var winnerSource = int.MinValue;
        var winnerOrdinal = int.MinValue;

        var conditional = false;
        foreach (var item in _component)
            if (
                ReferenceEquals(item.Assignment.Property, property)
                && (item.Condition != VariantState.None || item.Conditions.Length != 0)
            )
            {
                conditional = true;
                break;
            }
        if (!conditional)
            foreach (var item in _author)
                if (
                    ReferenceEquals(item.Assignment.Property, property)
                    && (item.Condition != VariantState.None || item.Conditions.Length != 0)
                )
                {
                    conditional = true;
                    break;
                }

        var active = conditional ? _variants.Value | _behaviorVariants.Value : VariantState.None;
        ResolveValueAssignments(
            property,
            _component,
            source: 0,
            active,
            ref value,
            ref hasAssignment,
            ref winnerKey,
            ref winnerSource,
            ref winnerOrdinal
        );
        ResolveValueAssignments(
            property,
            _author,
            source: 1,
            active,
            ref value,
            ref hasAssignment,
            ref winnerKey,
            ref winnerSource,
            ref winnerOrdinal
        );

        if (_control.TryGetValue(property, out var control))
            value = (T)control.Value!;

        return value;
    }

    private void ResolveValueAssignments<T>(
        Property<T> property,
        FlatAssignment[] assignments,
        int source,
        VariantState active,
        ref T value,
        ref bool hasAssignment,
        ref int winnerKey,
        ref int winnerSource,
        ref int winnerOrdinal
    )
    {
        foreach (var item in assignments)
        {
            if (!ReferenceEquals(item.Assignment.Property, property))
                continue;
            if (!item.Assignment.IsAvailable)
                continue;
            if ((active & item.Condition) != item.Condition)
                continue;

            var mounted = true;
            foreach (var read in item.MountedConditions)
                if (!read())
                {
                    mounted = false;
                    break;
                }
            if (!mounted)
                continue;

            // Read every available active assignment so losing bindings and theme tokens remain dependencies.
            var candidate = (T)item.Assignment.Resolve(_theme)!;
            var key = VariantOrder.Key(item.Condition);
            if (
                !hasAssignment
                || key > winnerKey
                || (
                    key == winnerKey
                    && (
                        source > winnerSource
                        || (source == winnerSource && item.Ordinal > winnerOrdinal)
                    )
                )
            )
            {
                value = candidate;
                winnerKey = key;
                winnerSource = source;
                winnerOrdinal = item.Ordinal;
            }
            hasAssignment = true;
        }
    }

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
        var winner = candidates[^1];
        return new(
            winner.Value,
            winner.Provenance,
            candidates.Take(candidates.Count - 1).Select(item => item.Provenance).ToArray()
        );
    }

    internal bool CommitPresentationTargets(MotionTimeline timeline) =>
        Commit(timeline, VisualProperties.Background)
        | Commit(timeline, VisualProperties.Opacity)
        | Commit(timeline, TypographyProperties.TextColor);

    internal void ValidatePresentationTargets()
    {
        Validate(VisualProperties.Background);
        Validate(VisualProperties.Opacity);
        Validate(TypographyProperties.TextColor);
    }

    private void Validate<T>(Property<T> property) =>
        MotionTimeline.ValidateTarget(property.Transition, _element.ResolveValue(property));

    private bool Commit<T>(MotionTimeline timeline, Property<T> property)
    {
        var local = HasLocalValue(property);
        var policy = ResolveMotion(property, out var policySource);
        var target = _element.ResolveValue(property);
        if (property.Inherits && !local)
        {
            if (policy is null)
            {
                _delegatedTargets.Remove(property);
                _delegatedAnimatedPolicy.Remove(property);
                timeline.Remove(_element, property);
                return false;
            }
            if (policy.Value.DurationMilliseconds != 0)
            {
                if (!_delegatedTargets.TryGetValue(property, out var previous))
                {
                    _delegatedTargets[property] = target;
                    _delegatedAnimatedPolicy[property] = true;
                    timeline.Remove(_element, property);
                    return false;
                }
                if (EqualityComparer<T>.Default.Equals((T)previous!, target))
                {
                    if (_delegatedAnimatedPolicy.GetValueOrDefault(property) == false)
                    {
                        _delegatedAnimatedPolicy[property] = true;
                        timeline.Remove(_element, property);
                        return false;
                    }
                    if (!timeline.Contains(_element, property))
                        return false;
                }
                else
                {
                    if (!timeline.Contains(_element, property))
                    {
                        var presented = _element.Parent is { } parent
                            ? _element.Composition.ReadPresentedValue(parent, property)
                            : (T)previous!;
                        timeline.SeedAcknowledged(
                            _element,
                            property,
                            (T)previous!,
                            presented,
                            policy.Value,
                            policySource,
                            _theme,
                            _theme.AppearanceGeneration,
                            timeline.IsAcknowledged(_element)
                        );
                    }
                    _delegatedTargets[property] = target;
                }
                _delegatedAnimatedPolicy[property] = true;
            }
            else
            {
                _delegatedTargets[property] = target;
                _delegatedAnimatedPolicy[property] = false;
            }
        }
        return timeline.Commit(
            _element,
            property,
            target,
            policy,
            policySource,
            _theme,
            _control.ContainsKey(property),
            _theme.IsReducedMotion || _theme.Appearance.Contrast == ThemeContrast.High,
            _theme.IsReducedMotion ? "reduced-motion"
                : _theme.Appearance.Contrast == ThemeContrast.High ? "high-contrast"
                : null,
            _theme.AppearanceGeneration
        );
    }

    private bool HasLocalValue<T>(Property<T> property)
    {
        if (_control.ContainsKey(property))
            return true;
        var active = _variants.Value | _behaviorVariants.Value;
        return _component
            .Concat(_author)
            .Any(item =>
                ReferenceEquals(item.Assignment.Property, property)
                && item.Assignment.IsAvailable
                && (active & item.Condition) == item.Condition
                && item.MountedConditions.All(read => read())
            );
    }

    private Motion? ResolveMotion<T>(Property<T> property, out string? policySource)
    {
        var active = _variants.Value | _behaviorVariants.Value;
        var found = false;
        var value = Motion.None;
        var winnerKey = int.MinValue;
        var winnerSource = int.MinValue;
        var winnerOrdinal = int.MinValue;
        policySource = null;
        foreach (
            var entry in _componentMotion
                .Select(item => (item, source: 0))
                .Concat(_authorMotion.Select(item => (item, source: 1)))
        )
        {
            var item = entry.item;
            if (
                !ReferenceEquals(item.Property, property)
                || (active & item.Condition) != item.Condition
                || !item.MountedConditions.All(read => read())
            )
                continue;
            var key = VariantOrder.Key(item.Condition);
            if (
                !found
                || key > winnerKey
                || (
                    key == winnerKey
                    && (
                        entry.source > winnerSource
                        || (entry.source == winnerSource && item.Ordinal > winnerOrdinal)
                    )
                )
            )
            {
                found = true;
                value = item.Motion;
                winnerKey = key;
                winnerSource = entry.source;
                winnerOrdinal = item.Ordinal;
                policySource =
                    (entry.source == 0 ? "component" : "author")
                    + "#"
                    + item.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + (item.Condition == VariantState.None ? "" : "[" + item.Condition + "]");
            }
        }
        return found ? value : null;
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
            .Concat(_componentMotion.Select(item => item.Property))
            .Concat(_authorMotion.Select(item => item.Property));

    internal static void Validate(Style component, Style author)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(author);
        var componentAssignments = component.Flatten().ToArray();
        var authorAssignments = author.Flatten().ToArray();
        var componentMotion = component.FlattenMotion().ToArray();
        var authorMotion = author.FlattenMotion().ToArray();
        ValidateProperties(
            componentAssignments
                .Concat(authorAssignments)
                .Select(item => item.Assignment.Property)
                .Concat(componentMotion.Select(item => item.Property))
                .Concat(authorMotion.Select(item => item.Property))
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

    private IEnumerable<FlatMotion> MaterializeMotion(
        IEnumerable<FlatMotion> policies,
        string group
    )
    {
        var mounted = new Dictionary<StyleCondition, MountedCondition>(
            ReferenceEqualityComparer.Instance
        );
        foreach (var item in policies)
        {
            Derived<bool>? parent = null;
            var conditions = item
                .Conditions.Select(condition =>
                {
                    if (!mounted.TryGetValue(condition, out var value))
                    {
                        var name = _element.Name + ".style-when." + group + "." + mounted.Count;
                        var enclosing = parent;
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
                            name + ".effect"
                        );
                    }
                    parent = value.Gate;
                    return (Func<bool>)(() => value.Published.Value);
                })
                .ToArray();
            yield return item with
            {
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

internal static class TransitionTypes
{
    internal static bool Matches(TransitionKind kind, Type type) =>
        kind switch
        {
            TransitionKind.Color => type == typeof(Color),
            TransitionKind.Opacity => type == typeof(float),
            TransitionKind.Brush => type == typeof(Brush),
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

    internal static int Key(VariantState state)
    {
        var key = BitOperations.PopCount((uint)state) * (1 << Order.Length);
        for (var index = 0; index < Order.Length; index++)
            if ((state & Order[index]) != VariantState.None)
                key |= 1 << (Order.Length - index - 1);
        return key;
    }
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
