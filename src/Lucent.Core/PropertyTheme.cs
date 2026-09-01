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
    private bool _disposed;
    internal ReactiveGraph Graph { get; }
    internal ReactiveScope Scope => _scope;
    internal int TokenCount => _tokens.Count;
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
    internal void ValidateLive()
    {
        if (_disposed || _scope.IsDisposed) throw new ObjectDisposedException(nameof(ThemeContext));
    }
    internal T Token<T>(Token<T> token) => Slot(token).State.Value;
    internal bool IsThemed<T>(Token<T> token) => Slot(token).State.Themed;
    private TokenSlot<T> Slot<T>(Token<T> token) => _tokens.TryGetValue(token, out var slot) ? (TokenSlot<T>)slot : Add(token);
    private TokenSlot<T> Add<T>(Token<T> token)
    {
        var slot = new TokenSlot<T>(_scope, _scope.SignalForFramework(new TokenState<T>(_theme.Value.Resolve(token), _theme.Value.Has(token)), "token." + token.Name), token);
        _tokens.Add(token, slot);
        _scope.RegisterFactoryRollback(() => Remove(token, slot));
        return slot;
    }
    private void Remove<T>(Token<T> token, TokenSlot<T> slot)
    {
        if (_tokens.TryGetValue(token, out var current) && ReferenceEquals(current, slot)) { _tokens.Remove(token); slot.Dispose(); }
    }
    public void Dispose()
    {
        _scope.CheckMutationGuard();
        if (_disposed) return;
        _disposed = true;
        foreach (var slot in _tokens.Values) slot.Dispose();
        _tokens.Clear(); _appearance.Dispose(); _reducedMotion.Dispose(); _theme.Dispose();
    }
    private interface ITokenSlot : IDisposable { void Update(Theme theme); }
    private sealed class TokenSlot<T>(ReactiveScope scope, Signal<TokenState<T>> signal, Token<T> token) : ITokenSlot
    {
        internal TokenState<T> State => signal.Value;
        public void Update(Theme theme) => signal.Value = new(theme.Resolve(token), theme.Has(token));
        public void Dispose() { scope.Detach(signal); signal.Dispose(); }
    }
}

internal readonly record struct TokenState<T>(T Value, bool Themed);
