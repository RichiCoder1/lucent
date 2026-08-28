using System.Numerics;
using System.Text;

namespace Lucent.Core;

public sealed class Property<T> : IProperty, IPropertyDump
{
    public Property(string name, T defaultValue, bool inherits = false, TransitionKind transition = TransitionKind.None)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        Name = name; DefaultValue = defaultValue; Inherits = inherits; Transition = transition;
        if (!Enum.IsDefined(transition) || (transition != TransitionKind.None && !TransitionTypes.Matches(transition, typeof(T))))
            throw new ArgumentException("The property type does not match its transition channel.", nameof(transition));
    }
    public string Name { get; }
    public T DefaultValue { get; }
    public bool Inherits { get; }
    public TransitionKind Transition { get; }
    object? IProperty.DefaultValue => DefaultValue;
    ResolutionDump IPropertyDump.Dump(ElementPresentation presentation) { var value = presentation.Resolve(this); return new(value.Winner, value.Overridden, value.SuppressedTransition); }
}

internal interface IProperty { string Name { get; } bool Inherits { get; } TransitionKind Transition { get; } object? DefaultValue { get; } }
internal interface IPropertyDump { ResolutionDump Dump(ElementPresentation presentation); }
internal sealed record ResolutionDump(PropertyProvenance Winner, IReadOnlyList<PropertyProvenance> Overridden, PropertyProvenance? Suppressed);

public enum TransitionKind { None, Color, Opacity, Transform, FocusRing }
[Flags] public enum VariantState { None = 0, Hover = 1, FocusVisible = 2, Selected = 4, Pressed = 8, Invalid = 16, Disabled = 32 }
public enum ThemeColorScheme { Light, Dark }
public enum ThemeContrast { Normal, High }
public readonly record struct ThemeAppearance(ThemeColorScheme ColorScheme, ThemeContrast Contrast)
{
    public static readonly ThemeAppearance Light = new(ThemeColorScheme.Light, ThemeContrast.Normal);
    public void Validate()
    {
        if (!Enum.IsDefined(ColorScheme) || !Enum.IsDefined(Contrast))
            throw new ArgumentException("Theme appearance requires finite color scheme and contrast values.");
    }
}

public sealed class Token<T>
{
    public Token(string name, T fallback) { ReactiveGraph.ValidateName(name, nameof(name)); Name = name; Fallback = fallback; }
    public string Name { get; }
    public T Fallback { get; }
}

public sealed class Theme
{
    private readonly Dictionary<object, object?> _values;
    public Theme(string name) : this(name, []) { }
    private Theme(string name, Dictionary<object, object?> values) { ReactiveGraph.ValidateName(name, nameof(name)); Name = name; _values = values; }
    public string Name { get; }
    public Theme Set<T>(Token<T> token, T value) { ArgumentNullException.ThrowIfNull(token); var values = new Dictionary<object, object?>(_values) { [token] = value }; return new(Name, values); }
    internal T Resolve<T>(Token<T> token) => _values.TryGetValue(token, out var value) ? (T)value! : token.Fallback;
    internal bool Has<T>(Token<T> token) => _values.ContainsKey(token);
}

/// <summary>Scope-owned graph state; direct properties never read its token signal.</summary>
public sealed class ThemeContext : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<Theme> _theme;
    private readonly Signal<bool> _reducedMotion;
    private readonly Signal<ThemeAppearance> _appearance;
    private readonly Dictionary<object, ITokenSlot> _tokens = [];
    internal ReactiveGraph Graph { get; }
    public ThemeContext(ReactiveScope scope, Theme theme, bool reducedMotion = false, ThemeAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(theme);
        var initialAppearance = appearance ?? ThemeAppearance.Light;
        initialAppearance.Validate();
        _scope = scope; Graph = scope.Graph; _theme = scope.Signal(theme, "theme"); _reducedMotion = scope.Signal(reducedMotion, "reduced-motion"); _appearance = scope.Signal(initialAppearance, "theme-appearance"); scope.Own(this);
    }
    public Theme Theme { get => _theme.Value; set { _scope.CheckMutationGuard(); var theme = value ?? throw new ArgumentNullException(nameof(value)); _theme.Value = theme; foreach (var slot in _tokens.Values) slot.Update(theme); } }
    public bool ReducedMotion { get => _reducedMotion.Value; set { _scope.CheckMutationGuard(); _reducedMotion.Value = value; } }
    /// <summary>Portable system appearance. Authors choose typed theme/token values for each appearance.</summary>
    public ThemeAppearance Appearance { get => _appearance.Value; set { _scope.CheckMutationGuard(); value.Validate(); _appearance.Value = value; } }
    internal Theme CurrentTheme => _theme.Value;
    internal bool IsReducedMotion => _reducedMotion.Value;
    internal T Token<T>(Token<T> token) => Slot(token).State.Value;
    internal bool IsThemed<T>(Token<T> token) => Slot(token).State.Themed;
    private TokenSlot<T> Slot<T>(Token<T> token) => _tokens.TryGetValue(token, out var slot) ? (TokenSlot<T>)slot : Add(token);
    private TokenSlot<T> Add<T>(Token<T> token) { var slot = new TokenSlot<T>(_scope.Signal(new TokenState<T>(_theme.Value.Resolve(token), _theme.Value.Has(token)), "token." + token.Name), token); _tokens.Add(token, slot); return slot; }
    public void Dispose() { _scope.CheckMutationGuard(); _tokens.Clear(); _appearance.Dispose(); _reducedMotion.Dispose(); _theme.Dispose(); }
    private interface ITokenSlot { void Update(Theme theme); }
    private sealed class TokenSlot<T>(Signal<TokenState<T>> signal, Token<T> token) : ITokenSlot { internal TokenState<T> State => signal.Value; public void Update(Theme theme) => signal.Value = new(theme.Resolve(token), theme.Has(token)); }
}

internal readonly record struct TokenState<T>(T Value, bool Themed);

public sealed class Style
{
    private readonly Node[] _nodes;
    private Style(Node[] nodes) => _nodes = nodes;
    public static Style Empty { get; } = new([]);
    public Style Set<T>(Property<T> property, T value) => Add(new Assignment<T>(property, value));
    public Style Set<T>(Property<T> property, Token<T> token) => Add(new Assignment<T>(property, token));
    public Style When(VariantState when, Style style) { VariantStates.Validate(when, nameof(when), false); ArgumentNullException.ThrowIfNull(style); return new([.. _nodes, new VariantNode(when, style)]); }
    public static Style Compose(params Style[] styles)
    {
        ArgumentNullException.ThrowIfNull(styles); var nodes = new List<Node>();
        foreach (var style in styles) { ArgumentNullException.ThrowIfNull(style); nodes.AddRange(style._nodes); }
        return new([.. nodes]);
    }
    internal IEnumerable<FlatAssignment> Flatten()
    {
        var ordinal = 0;
        foreach (var assignment in Flatten(VariantState.None)) yield return assignment with { Ordinal = ordinal++ };
    }
    private IEnumerable<FlatAssignment> Flatten(VariantState condition)
    {
        foreach (var node in _nodes)
            if (node is AssignmentNode assignmentNode) yield return new FlatAssignment(assignmentNode.Assignment, condition, 0);
            else if (node is VariantNode variant)
                foreach (var assignment in variant.Style.Flatten(condition | variant.Condition)) yield return assignment;
    }
    private Style Add(IAssignment assignment) => new([.. _nodes, new AssignmentNode(assignment)]);
    private abstract record Node;
    private sealed record AssignmentNode(IAssignment Assignment) : Node;
    private sealed record VariantNode(VariantState Condition, Style Style) : Node;
}

/// <summary>Immutable eligible-property/duration specification. Values exist only in active timeline samples.</summary>
public sealed class Transition
{
    private Transition(IProperty property, int durationMilliseconds) { Property = property; DurationMilliseconds = durationMilliseconds; }
    internal IProperty Property { get; }
    public int DurationMilliseconds { get; }
    public static Transition For<T>(Property<T> property, int durationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!Enum.IsDefined(property.Transition) || property.Transition == TransitionKind.None) throw new ArgumentException("Only fixed transition channels are eligible.", nameof(property));
        if (durationMilliseconds is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        return new(property, durationMilliseconds);
    }
}

public sealed record ResolvedProperty<T>(T Value, PropertyProvenance Winner, IReadOnlyList<PropertyProvenance> Overridden, PropertyProvenance? SuppressedTransition = null);
public sealed record PropertyProvenance(string Source, int Ordinal, VariantState Condition = VariantState.None);

internal interface IAssignment { IProperty Property { get; } object? Resolve(ThemeContext theme); string? TokenName { get; } bool IsThemed(ThemeContext theme); void Prime(ThemeContext theme); }
internal sealed class Assignment<T> : IAssignment
{
    private readonly T? _value; private readonly Token<T>? _token;
    internal Assignment(Property<T> property, T value) { Property = property ?? throw new ArgumentNullException(nameof(property)); _value = value; }
    internal Assignment(Property<T> property, Token<T> token) { Property = property ?? throw new ArgumentNullException(nameof(property)); _token = token ?? throw new ArgumentNullException(nameof(token)); }
    public IProperty Property { get; }
    public string? TokenName => _token?.Name;
    public bool IsThemed(ThemeContext theme) => _token is not null && theme.IsThemed(_token);
    public object? Resolve(ThemeContext theme) => _token is null ? _value : theme.Token(_token);
    public void Prime(ThemeContext theme) { if (_token is not null) _ = theme.Token(_token); }
}
internal readonly record struct FlatAssignment(IAssignment Assignment, VariantState Condition, int Ordinal);

[Flags] public enum BehaviorOwnership { None = 0, Focus = 1, Action = 2, Semantics = 4 }
public enum BehaviorState { Focused, FocusVisible, Pressed, Selected }
public enum SemanticRole { Group, Text, Button, List, ListItem, Status }
[Flags] public enum SemanticAction { None = 0, Invoke = 1, SetValue = 2, Select = 4 }

public sealed class SemanticDeclaration
{
    public SemanticDeclaration(SemanticRole role, string name, bool enabled = true, bool focused = false, bool selected = false, SemanticAction actions = SemanticAction.None, string? value = null)
    { if (!Enum.IsDefined(role) || ((uint)actions & ~(uint)(SemanticAction.Invoke | SemanticAction.SetValue | SemanticAction.Select)) != 0) throw new ArgumentException("Semantic role/actions must be finite."); if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A semantic name is required.", nameof(name)); Role = role; Name = name; Enabled = enabled; Focused = focused; Selected = selected; Actions = actions; Value = value; }
    public SemanticRole Role { get; } public string Name { get; } public bool Enabled { get; } public bool Focused { get; } public bool Selected { get; } public SemanticAction Actions { get; } public string? Value { get; }
}

public readonly record struct SemanticIdentity(long CompositionEpoch, long ElementId, long Generation);
public sealed record SemanticSnapshot(SemanticIdentity Identity, SemanticRole Role, string Name, string? Value, bool Enabled, bool Focused, bool Selected, SemanticAction Actions, IReadOnlyList<SemanticSnapshot> Children);

public abstract class Behavior { public abstract string Name { get; } public virtual BehaviorOwnership Ownership => BehaviorOwnership.None; public abstract void Attach(BehaviorContext context); }

/// <summary>Behavior-only capability surface: identity, deterministic state, semantics, and scope-owned cleanup.</summary>
public sealed class BehaviorContext
{
    private readonly Composition _composition; private readonly ReactiveScope _scope; private readonly Dictionary<BehaviorState, bool> _state = []; private bool _attaching = true; private SemanticDeclaration? _semantic; private readonly Action _stateChanged;
    internal BehaviorContext(long elementId, Composition composition, ReactiveScope scope, Behavior behavior, Action stateChanged) { ElementId = elementId; _composition = composition; _scope = scope; Behavior = behavior; _stateChanged = stateChanged; }
    public long ElementId { get; }
    public Behavior Behavior { get; }
    public void OnDispose(Action cleanup) { CheckAttachment(); ArgumentNullException.ThrowIfNull(cleanup); _scope.OnDispose(() => _composition.RunBehaviorCleanup(cleanup)); }
    public T Own<T>(T value) where T : IDisposable { CheckAttachment(); ArgumentNullException.ThrowIfNull(value); _scope.Own(new GuardedDisposable(_composition, value)); return value; }
    /// <summary>Updates behavior-owned visual state. Router callbacks may call this after attachment.</summary>
    public void SetState(BehaviorState state, bool value) { _composition.CheckThread(); CheckLive(); if (!Enum.IsDefined(state)) throw new ArgumentException("Behavior state must be finite.", nameof(state)); if (_state.GetValueOrDefault(state) == value) return; _state[state] = value; _stateChanged(); }
    public void SetSemantics(SemanticDeclaration semantics) { CheckAttachment(); if (!Behavior.Ownership.HasFlag(BehaviorOwnership.Semantics)) throw new InvalidOperationException("Only semantic ownership can declare semantics."); _semantic = semantics ?? throw new ArgumentNullException(nameof(semantics)); }
    public void OnPointer(Action<PointerRoute> handler) { CheckInputOwnership(); _composition.Input.RegisterPointer(ElementId, _scope, handler); }
    public void OnKey(Action<KeyRoute> handler) { CheckInputOwnership(); _composition.Input.RegisterKey(ElementId, _scope, handler); }
    public void OnFocus(Action<FocusRoute> handler) { CheckFocusOwnership(); _composition.Input.RegisterFocus(ElementId, _scope, handler); }
    public void OnCaptureLost(Action<PointerCaptureLoss> handler) { CheckInputOwnership(); _composition.Input.RegisterCaptureLoss(ElementId, _scope, handler); }
    public void MakeFocusable(bool tabStop = true) { CheckFocusOwnership(); _composition.Input.RegisterFocusable(ElementId, _scope, tabStop, this); }
    internal SemanticDeclaration? Semantics => _semantic; internal IReadOnlyDictionary<BehaviorState, bool> State => _state; internal void Complete() => _attaching = false;
    private void CheckAttachment() { CheckLive(); if (!_attaching) throw new InvalidOperationException("Behavior registration is only valid while attaching."); }
    private void CheckLive() { if (_scope.IsDisposed) throw new ObjectDisposedException(Behavior.Name); }
    private void CheckInputOwnership() { CheckAttachment(); if ((Behavior.Ownership & (BehaviorOwnership.Action | BehaviorOwnership.Focus)) == 0) throw new InvalidOperationException("Input handlers require action or focus ownership."); }
    private void CheckFocusOwnership() { CheckAttachment(); if (!Behavior.Ownership.HasFlag(BehaviorOwnership.Focus)) throw new InvalidOperationException("Focus handlers require focus ownership."); }
    private sealed class GuardedDisposable(Composition composition, IDisposable value) : IDisposable
    {
        private IDisposable? _value = value;
        public void Dispose() { var value = Interlocked.Exchange(ref _value, null); if (value is not null) composition.RunBehaviorCleanup(value.Dispose); }
    }
}

internal sealed class ElementPresentation
{
    private readonly Element _element; private readonly ThemeContext _theme; private readonly FlatAssignment[] _component; private readonly FlatAssignment[] _author; private readonly Transition[] _transitions; private readonly Dictionary<IProperty, Signal<TransitionController.Sample?>> _samples; private readonly Signal<VariantState> _variants; private readonly Signal<VariantState> _behaviorVariants;
    internal ElementPresentation(Element element, ThemeContext theme, Style component, Style author, Transition[] transitions)
    {
        _element = element; _theme = theme; _component = component.Flatten().ToArray(); _author = author.Flatten().ToArray(); _transitions = [.. transitions];
        ValidateProperties(_component.Concat(_author).Select(item => item.Assignment.Property).Concat(_transitions.Select(item => item.Property)));
        if (_transitions.GroupBy(item => item.Property).Any(group => group.Count() != 1)) throw new ArgumentException("Transition properties must be unique.", nameof(transitions));
        foreach (var assignment in _component.Concat(_author).Select(item => item.Assignment)) assignment.Prime(theme);
        _samples = _transitions.ToDictionary(item => item.Property, item => element.Scope.Signal<TransitionController.Sample?>(null, element.Name + ".transition." + item.Property.Name));
        _variants = element.Scope.Signal(VariantState.None, element.Name + ".variants"); _behaviorVariants = element.Scope.Signal(VariantState.None, element.Name + ".behavior-variants");
    }
    internal void SetVariants(VariantState variants) { _element.Composition.ThrowIfBehaviorAttachment(); VariantStates.Validate(variants, nameof(variants), true); _variants.Value = variants; }
    internal void SetBehaviorVariants(VariantState variants) { VariantStates.Validate(variants, nameof(variants), true); _behaviorVariants.Value = variants; }
    internal void Start<T>(Property<T> property, T value)
    {
        var spec = _transitions.SingleOrDefault(spec => ReferenceEquals(spec.Property, property)) ?? throw new InvalidOperationException("No transition specification exists for this property.");
        var slot = _samples[property];
        _element.Composition.Transitions.Start(_element, property, value, spec, sample => slot.Value = sample, () => slot.Value = null);
    }
    internal ResolvedProperty<T> Resolve<T>(Property<T> property)
    {
        ArgumentNullException.ThrowIfNull(property); var candidates = new List<(T Value, PropertyProvenance Provenance)> { (property.DefaultValue, new("default", 0)) };
        if (property.Inherits && _element.Parent is not null) { var inherited = _element.Parent.Resolve(property); candidates.Add((inherited.Value, new("inherited", inherited.Winner.Ordinal))); }
        var active = _variants.Value | _behaviorVariants.Value;
        foreach (var item in _component.Select(item => (item, author: false)).Concat(_author.Select(item => (item, author: true)))
            .Where(entry => ReferenceEquals(entry.item.Assignment.Property, property) && (active & entry.item.Condition) == entry.item.Condition)
            .OrderBy(entry => VariantOrder.Key(entry.item.Condition)).ThenBy(entry => entry.author).ThenBy(entry => entry.item.Ordinal))
            candidates.Add(((T)item.item.Assignment.Resolve(_theme)!, new((item.author ? "author" : "component") + (item.item.Assignment.TokenName is { } token ? ":token:" + token + ":" + (item.item.Assignment.IsThemed(_theme) ? "theme" : "fallback") : ""), item.item.Ordinal, item.item.Condition)));
        PropertyProvenance? suppressed = null;
        if (_samples.TryGetValue(property, out var slot) && slot.Value is { } sample)
        {
            if (_theme.IsReducedMotion) suppressed = new("transition-suppressed", sample.Ordinal);
            else candidates.Add(((T)sample.Value!, new("transition", sample.Ordinal)));
        }
        var winner = candidates[^1]; return new(winner.Value, winner.Provenance, candidates.Take(candidates.Count - 1).Select(item => item.Provenance).ToArray(), suppressed);
    }
    internal void AppendDump(StringBuilder dump)
    {
        dump.Append("  style variants=").Append(_variants.Value | _behaviorVariants.Value).Append(" reducedMotion=").Append(_theme.IsReducedMotion ? "true" : "false").Append('\n');
        foreach (var property in Properties().OrderBy(item => item.Name, StringComparer.Ordinal))
        { var value = ((IPropertyDump)property).Dump(this); dump.Append("  property name=").Append(Quote(property.Name)).Append(" winner=").Append(Format(value.Winner)).Append(" overridden=[").Append(string.Join(',', value.Overridden.Select(Format))).Append("]"); if (value.Suppressed is not null) dump.Append(" suppressed=").Append(Format(value.Suppressed)); dump.Append('\n'); }
    }
    private IEnumerable<IProperty> Properties() => OwnProperties().Concat(_element.AncestorProperties().Where(property => property.Inherits)).Distinct();
    internal IEnumerable<IProperty> OwnProperties() => _component.Concat(_author).Select(item => item.Assignment.Property).Concat(_transitions.Select(item => item.Property));
    private static void ValidateProperties(IEnumerable<IProperty> properties)
    { var names = new Dictionary<string, IProperty>(StringComparer.Ordinal); foreach (var property in properties) { if (names.TryGetValue(property.Name, out var prior) && !ReferenceEquals(prior, property)) throw new ArgumentException("A presentation cannot contain distinct properties with the same name."); names[property.Name] = property; } }
    private string Format(PropertyProvenance value) => DiagnosticText.Quote(value.Source) + "#" + value.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) + (value.Condition == VariantState.None ? "" : "[" + value.Condition + "]");
    private static string Quote(string value) => DiagnosticText.Quote(value);
}

internal sealed class TransitionController
{
    private readonly Dictionary<(long Element, IProperty Property), Entry> _samples = []; private long _clock; private int _nextOrdinal;
    internal void Start<T>(Element element, Property<T> property, T value, Transition spec, Action<Sample> set, Action clear)
    {
        var sample = new Sample(value, checked(_clock + spec.DurationMilliseconds), ++_nextOrdinal); set(sample); _samples[(element.Id, property)] = new(sample, clear);
    }
    internal void Advance(int milliseconds) { if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds)); _clock = checked(_clock + milliseconds); Expire(); }
    internal void Remove(Element element) { foreach (var key in _samples.Keys.Where(key => key.Element == element.Id).ToArray()) { _samples[key].Clear(); _samples.Remove(key); } }
    private void Expire() { foreach (var item in _samples.Where(item => item.Value.Sample.ExpiresAt <= _clock).Select(item => item.Key).ToArray()) { _samples[item].Clear(); _samples.Remove(item); } }
    internal readonly record struct Sample(object? Value, long ExpiresAt, int Ordinal);
    private readonly record struct Entry(Sample Sample, Action Clear);
}

internal static class TransitionTypes
{
    internal static bool Matches(TransitionKind kind, Type type) => kind switch { TransitionKind.Color => type == typeof(uint), TransitionKind.Opacity or TransitionKind.FocusRing => type == typeof(float), TransitionKind.Transform => type == typeof(Matrix3x2), _ => true };
}
internal static class VariantStates
{
    private const VariantState All = VariantState.Hover | VariantState.FocusVisible | VariantState.Selected | VariantState.Pressed | VariantState.Invalid | VariantState.Disabled;
    internal static void Validate(VariantState state, string parameter, bool allowNone) { if ((!allowNone && state == VariantState.None) || ((uint)state & ~(uint)All) != 0) throw new ArgumentException("Variants must use the documented finite state set.", parameter); }
}
internal static class VariantOrder
{
    private static readonly VariantState[] Order = [VariantState.Disabled, VariantState.Invalid, VariantState.Pressed, VariantState.Selected, VariantState.FocusVisible, VariantState.Hover];
    internal static string Key(VariantState state) => BitOperations.PopCount((uint)state).ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + string.Concat(Order.Select(flag => state.HasFlag(flag) ? '1' : '0'));
}

internal static class DiagnosticText
{
    internal static string Quote(string value) => '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + '"';
}
