using System.Numerics;
using System.Text;

namespace Lucent.Core;

/// <summary>Names a typed presentation value, its fallback, inheritance rule, and optional fixed transition channel.</summary>
public sealed class Property<T> : IProperty, IPropertyDump
{
    /// <summary>Creates a property definition. Names are diagnostic identities and transition channels must match <typeparamref name="T"/>.</summary>
    public Property(
        string name,
        T defaultValue,
        bool inherits = false,
        TransitionKind transition = TransitionKind.None
    )
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        Name = name;
        DefaultValue = defaultValue;
        Inherits = inherits;
        Transition = transition;
        if (
            !Enum.IsDefined(transition)
            || (
                transition != TransitionKind.None && !TransitionTypes.Matches(transition, typeof(T))
            )
        )
            throw new ArgumentException(
                "The property type does not match its transition channel.",
                nameof(transition)
            );
    }

    /// <summary>Gets the stable diagnostic name used in deterministic dumps.</summary>
    public string Name { get; }

    /// <summary>Gets the value used when no applicable style or inherited value supplies this property.</summary>
    public T DefaultValue { get; }

    /// <summary>Gets whether an unset value is inherited from the nearest presented ancestor.</summary>
    public bool Inherits { get; }

    /// <summary>Gets the only transition channel permitted for this property.</summary>
    public TransitionKind Transition { get; }
    object? IProperty.DefaultValue => DefaultValue;

    ResolutionDump IPropertyDump.Dump(ElementPresentation presentation)
    {
        var value = presentation.Resolve(this);
        return new(value.Winner, value.Overridden, value.SuppressedTransition);
    }
}

internal interface IProperty
{
    string Name { get; }
    bool Inherits { get; }
    TransitionKind Transition { get; }
    object? DefaultValue { get; }
}

internal interface IPropertyDump
{
    ResolutionDump Dump(ElementPresentation presentation);
}

internal sealed record ResolutionDump(
    PropertyProvenance Winner,
    IReadOnlyList<PropertyProvenance> Overridden,
    PropertyProvenance? Suppressed
);

/// <summary>Interpolation channels supported by the bounded presentation timeline.</summary>
public enum TransitionKind
{
    /// <summary>Disallows transition sampling.</summary>
    None,

    /// <summary>Allows color interpolation.</summary>
    Color,

    /// <summary>Allows opacity interpolation.</summary>
    Opacity,

    /// <summary>Reserves transform interpolation.</summary>
    Transform,

    /// <summary>Reserves focus-ring interpolation.</summary>
    FocusRing,
}

/// <summary>Interaction-state bits used to select conditional style assignments.</summary>
[Flags]
public enum VariantState
{
    /// <summary>Applies no interaction variant.</summary>
    None = 0,

    /// <summary>Applies while pointer hover is active.</summary>
    Hover = 1,

    /// <summary>Applies while keyboard-visible focus is active.</summary>
    FocusVisible = 2,

    /// <summary>Applies while selected.</summary>
    Selected = 4,

    /// <summary>Applies during an active press.</summary>
    Pressed = 8,

    /// <summary>Applies when a control is invalid.</summary>
    Invalid = 16,

    /// <summary>Applies when input is disabled.</summary>
    Disabled = 32,
}

/// <summary>System color-scheme choices observed by a theme context.</summary>
public enum ThemeColorScheme
{
    /// <summary>Selects light-surface appearance.</summary>
    Light,

    /// <summary>Selects dark-surface appearance.</summary>
    Dark,
}

/// <summary>System contrast choices observed by a theme context.</summary>
public enum ThemeContrast
{
    /// <summary>Selects ordinary contrast.</summary>
    Normal,

    /// <summary>Selects high-contrast appearance.</summary>
    High,
}

/// <summary>Portable color-scheme and contrast preference used to choose theme values.</summary>
public readonly record struct ThemeAppearance(ThemeColorScheme ColorScheme, ThemeContrast Contrast)
{
    /// <summary>Provides the standard light, normal-contrast appearance.</summary>
    public static readonly ThemeAppearance Light = new(
        ThemeColorScheme.Light,
        ThemeContrast.Normal
    );

    /// <summary>Validates the value and throws when its fields are outside the supported contract.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(ColorScheme) || !Enum.IsDefined(Contrast))
            throw new ArgumentException(
                "Theme appearance requires finite color scheme and contrast values."
            );
    }
}

/// <summary>Names an immutable themed value with a fallback used when the active theme does not assign it.</summary>
public sealed class Token<T>
{
    /// <summary>Initializes a named token and its fallback value.</summary>
    public Token(string name, T fallback)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        Name = name;
        Fallback = fallback;
    }

    /// <summary>Gets the stable diagnostic name.</summary>
    public string Name { get; }

    /// <summary>Gets the value used when the active theme does not assign the token.</summary>
    public T Fallback { get; }
}

/// <summary>A named mutable collection of typed token assignments, observed through a <see cref="ThemeContext"/>.</summary>
public sealed class Theme
{
    private readonly Dictionary<object, object?> _values;

    /// <summary>Initializes an immutable named theme with no token assignments.</summary>
    public Theme(string name)
        : this(name, []) { }

    private Theme(string name, Dictionary<object, object?> values)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        Name = name;
        _values = values;
    }

    /// <summary>Gets the stable diagnostic name.</summary>
    public string Name { get; }

    /// <summary>Returns a new theme with the supplied token assignment.</summary>
    public Theme Set<T>(Token<T> token, T value)
    {
        ArgumentNullException.ThrowIfNull(token);
        var values = new Dictionary<object, object?>(_values) { [token] = value };
        return new(Name, values);
    }

    internal T Resolve<T>(Token<T> token) =>
        _values.TryGetValue(token, out var value) ? (T)value! : token.Fallback;

    internal bool Has<T>(Token<T> token) => _values.ContainsKey(token);
}

/// <summary>Scope-owned graph state; direct properties never read its token signal.</summary>
public sealed class ThemeContext : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<Theme> _theme;
    private readonly Signal<bool> _reducedMotion;
    private readonly Signal<ThemeAppearance> _appearance;
    private readonly Signal<ControlPresentationMode> _presentationMode;
    private readonly Dictionary<object, ITokenSlot> _tokens = [];
    private bool _disposed;
    internal ReactiveGraph Graph { get; }
    internal ReactiveScope Scope => _scope;
    internal int TokenCount => _tokens.Count;

    /// <summary>Initializes scope-owned reactive theme, motion, and appearance state.</summary>
    public ThemeContext(
        ReactiveScope scope,
        Theme theme,
        bool reducedMotion = false,
        ThemeAppearance? appearance = null,
        ControlPresentationMode presentationMode = ControlPresentationMode.Standard
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(theme);
        var initialAppearance = appearance ?? ThemeAppearance.Light;
        initialAppearance.Validate();
        ValidatePresentationMode(presentationMode, nameof(presentationMode));
        _scope = scope;
        Graph = scope.Graph;
        _theme = scope.Signal(theme, "theme");
        _reducedMotion = scope.Signal(reducedMotion, "reduced-motion");
        _appearance = scope.Signal(initialAppearance, "theme-appearance");
        _presentationMode = scope.Signal(presentationMode, "control-presentation-mode");
        scope.Own(this);
    }

    /// <summary>Gets or changes the active theme; changing it reactively updates token readers.</summary>
    public Theme Theme
    {
        get => _theme.Value;
        set
        {
            _scope.CheckMutationGuard();
            var theme = value ?? throw new ArgumentNullException(nameof(value));
            _theme.Value = theme;
            foreach (var slot in _tokens.Values)
                slot.Update(theme);
        }
    }

    /// <summary>Gets or changes the motion preference observed by presentation transitions.</summary>
    public bool ReducedMotion
    {
        get => _reducedMotion.Value;
        set
        {
            _scope.CheckMutationGuard();
            _reducedMotion.Value = value;
        }
    }

    /// <summary>Portable system appearance. Authors choose typed theme/token values for each appearance.</summary>
    public ThemeAppearance Appearance
    {
        get => _appearance.Value;
        set
        {
            _scope.CheckMutationGuard();
            value.Validate();
            _appearance.Value = value;
        }
    }

    /// <summary>Gets or changes the composition-scoped stock control presentation mode.</summary>
    /// <remarks>Minimal mode changes decoration only; control behavior, semantics, geometry, and focus remain active.</remarks>
    public ControlPresentationMode PresentationMode
    {
        get => _presentationMode.Value;
        set
        {
            _scope.CheckMutationGuard();
            ValidatePresentationMode(value, nameof(value));
            _presentationMode.Value = value;
        }
    }

    internal Theme CurrentTheme => _theme.Value;
    internal bool IsReducedMotion => _reducedMotion.Value;

    internal void ValidateLive()
    {
        ObjectDisposedException.ThrowIf(_disposed || _scope.IsDisposed, typeof(ThemeContext));
    }

    internal T Token<T>(Token<T> token) => Slot(token).State.Value;

    internal bool IsThemed<T>(Token<T> token) => Slot(token).State.Themed;

    private TokenSlot<T> Slot<T>(Token<T> token) =>
        _tokens.TryGetValue(token, out var slot) ? (TokenSlot<T>)slot : Add(token);

    private TokenSlot<T> Add<T>(Token<T> token)
    {
        // A first read tracks this token slot, not every change to the theme that seeds it.
        var theme = Graph.Untracked(() => _theme.Value);
        var slot = new TokenSlot<T>(
            _scope,
            _scope.SignalForFramework(
                new TokenState<T>(theme.Resolve(token), theme.Has(token)),
                "token." + token.Name
            ),
            token
        );
        _tokens.Add(token, slot);
        _scope.RegisterFactoryRollback(() => Remove(token, slot));
        return slot;
    }

    private void Remove<T>(Token<T> token, TokenSlot<T> slot)
    {
        if (_tokens.TryGetValue(token, out var current) && ReferenceEquals(current, slot))
        {
            _tokens.Remove(token);
            slot.Dispose();
        }
    }

    /// <summary>Disposes token signals owned by this context; the containing scope also owns this context.</summary>
    public void Dispose()
    {
        _scope.CheckMutationGuard();
        if (_disposed)
            return;
        _disposed = true;
        foreach (var slot in _tokens.Values)
            slot.Dispose();
        _tokens.Clear();
        _appearance.Dispose();
        _presentationMode.Dispose();
        _reducedMotion.Dispose();
        _theme.Dispose();
    }

    private static void ValidatePresentationMode(
        ControlPresentationMode value,
        string parameterName
    )
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private interface ITokenSlot : IDisposable
    {
        void Update(Theme theme);
    }

    private sealed class TokenSlot<T>(
        ReactiveScope scope,
        Signal<TokenState<T>> signal,
        Token<T> token
    ) : ITokenSlot
    {
        internal TokenState<T> State => signal.Value;

        public void Update(Theme theme) =>
            signal.Value = new(theme.Resolve(token), theme.Has(token));

        public void Dispose()
        {
            scope.Detach(signal);
            signal.Dispose();
        }
    }
}

internal readonly record struct TokenState<T>(T Value, bool Themed);
