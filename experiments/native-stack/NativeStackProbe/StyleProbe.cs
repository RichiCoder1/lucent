internal readonly record struct UiColor(string Value);
internal readonly record struct UiLength(int Value);
internal readonly record struct UiOpacity(float Value);
internal readonly record struct UiTransform(float X, float Y, float Scale = 1);
internal readonly record struct UiTypography(int Size, int Weight = 400);
internal readonly record struct UiShadow(int Blur, int X = 0, int Y = 0, UiColor? Color = null);
internal enum UiAlignment { Start, Center, End, Stretch }

/// <summary>Immutable typed paint/layout values; composition is always explicit and ordered.</summary>
internal sealed record Style(UiColor? Background = null, UiColor? Foreground = null, UiLength? PaddingX = null, UiLength? Radius = null, UiOpacity? Opacity = null, UiTransform? Transform = null, UiLength? FocusRing = null, UiColor? Border = null, SemanticToken? BackgroundToken = null, SemanticToken? ForegroundToken = null, UiLength? Width = null, UiLength? Height = null, UiLength? Gap = null, UiAlignment? Alignment = null, UiTypography? Typography = null, UiShadow? Shadow = null, UiLength? BorderWidth = null, SemanticToken? BorderToken = null)
{
    public Style Resolve(ThemeLayer theme) => this with { Background = Background ?? (BackgroundToken is { } background ? theme.Get(background) : null), Foreground = Foreground ?? (ForegroundToken is { } foreground ? theme.Get(foreground) : null), Border = Border ?? (BorderToken is { } border ? theme.Get(border) : null), BackgroundToken = null, ForegroundToken = null, BorderToken = null };
}

internal static class Styles
{
    public static Style Compose(params Style[] styles) => styles.Aggregate(new Style(), (current, next) => new(
        next.BackgroundToken is not null ? null : next.Background ?? current.Background,
        next.ForegroundToken is not null ? null : next.Foreground ?? current.Foreground,
        next.PaddingX ?? current.PaddingX, next.Radius ?? current.Radius, next.Opacity ?? current.Opacity, next.Transform ?? current.Transform, next.FocusRing ?? current.FocusRing, next.BorderToken is not null ? null : next.Border ?? current.Border,
        next.BackgroundToken ?? (next.Background is not null ? null : current.BackgroundToken),
        next.ForegroundToken ?? (next.Foreground is not null ? null : current.ForegroundToken),
        next.Width ?? current.Width, next.Height ?? current.Height, next.Gap ?? current.Gap, next.Alignment ?? current.Alignment, next.Typography ?? current.Typography, next.Shadow ?? current.Shadow, next.BorderWidth ?? current.BorderWidth,
        next.BorderToken ?? (next.Border is not null ? null : current.BorderToken)));
}

/// <summary>Finite authoring sugar over <see cref="Style"/>; there is no selector language.</summary>
internal static class StyleUtilities
{
    public static Style Bg(this Style style, UiColor color) => style with { Background = color, BackgroundToken = null };
    public static Style Bg(this Style style, SemanticToken token) => style with { Background = null, BackgroundToken = token };
    public static Style Fg(this Style style, UiColor color) => style with { Foreground = color, ForegroundToken = null };
    public static Style Fg(this Style style, SemanticToken token) => style with { Foreground = null, ForegroundToken = token };
    public static Style Px(this Style style, int value) => style with { PaddingX = new(value) };
    public static Style Padding(this Style style, int value) => style.Px(value);
    public static Style Gap(this Style style, int value) => style with { Gap = new(value) };
    public static Style Size(this Style style, int width, int height) => style with { Width = new(width), Height = new(height) };
    public static Style Align(this Style style, UiAlignment value) => style with { Alignment = value };
    public static Style Type(this Style style, int size, int weight = 400) => style with { Typography = new(size, weight) };
    public static Style Rounded(this Style style, int value) => style with { Radius = new(value) };
    public static Style Border(this Style style, UiColor color, int width = 1) => style with { Border = color, BorderToken = null, BorderWidth = new(width) };
    public static Style Border(this Style style, SemanticToken token, int width = 1) => style with { Border = null, BorderToken = token, BorderWidth = new(width) };
    public static Style Shadow(this Style style, int blur, int x = 0, int y = 0, UiColor? color = null) => style with { Shadow = new(blur, x, y, color) };
    public static Style Opacity(this Style style, float value) => style with { Opacity = new(value) };
    public static Style Transform(this Style style, float x, float y, float scale = 1) => style with { Transform = new(x, y, scale) };
    public static Style Ring(this Style style, int value) => style with { FocusRing = new(value) };
}

internal enum SemanticToken { Background, Foreground, Card, CardForeground, Primary, PrimaryForeground, Muted, MutedForeground, Destructive, Border, Ring }
internal readonly record struct TokenValue(SemanticToken Token, UiColor Value);

/// <summary>Immutable token scope. A child overrides only its declared semantic keys.</summary>
internal sealed class ThemeLayer(ThemeLayer? parent, params TokenValue[] values)
{
    private readonly Dictionary<SemanticToken, UiColor> _values = values.ToDictionary(value => value.Token, value => value.Value);
    public UiColor Get(SemanticToken token) => _values.TryGetValue(token, out var value) ? value : parent?.Get(token) ?? throw new InvalidOperationException($"Missing semantic token: {token}.");
}

internal static class Themes
{
    public static ThemeLayer Light { get; } = new(null,
        new(SemanticToken.Background, new("#ffffff")), new(SemanticToken.Foreground, new("#09090b")), new(SemanticToken.Card, new("#ffffff")), new(SemanticToken.CardForeground, new("#09090b")), new(SemanticToken.Primary, new("#18181b")), new(SemanticToken.PrimaryForeground, new("#fafafa")), new(SemanticToken.Muted, new("#f4f4f5")), new(SemanticToken.MutedForeground, new("#71717a")), new(SemanticToken.Destructive, new("#ef4444")), new(SemanticToken.Border, new("#e4e4e7")), new(SemanticToken.Ring, new("#18181b")));
    public static ThemeLayer Dark { get; } = new(Light,
        new(SemanticToken.Background, new("#09090b")), new(SemanticToken.Foreground, new("#fafafa")), new(SemanticToken.Card, new("#18181b")), new(SemanticToken.CardForeground, new("#fafafa")), new(SemanticToken.Primary, new("#fafafa")), new(SemanticToken.PrimaryForeground, new("#18181b")), new(SemanticToken.Muted, new("#27272a")), new(SemanticToken.MutedForeground, new("#a1a1aa")), new(SemanticToken.Border, new("#27272a")), new(SemanticToken.Ring, new("#d4d4d8")));
}

[Flags]
internal enum StyleState { None = 0, Hover = 1, Pressed = 2, FocusVisible = 4, Selected = 8, Disabled = 16, Invalid = 32 }
internal sealed record VariantStyle(Style Base, Style? Hover = null, Style? Pressed = null, Style? FocusVisible = null, Style? Selected = null, Style? Disabled = null, Style? Invalid = null)
{
    // Active layers retain independent properties; collisions resolve in this documented order.
    public Style Resolve(StyleState state, ThemeLayer theme) => Styles.Compose(Base,
        state.HasFlag(StyleState.Selected) ? Selected ?? new() : new(),
        state.HasFlag(StyleState.FocusVisible) ? FocusVisible ?? new() : new(),
        state.HasFlag(StyleState.Hover) ? Hover ?? new() : new(),
        state.HasFlag(StyleState.Pressed) ? Pressed ?? new() : new(),
        state.HasFlag(StyleState.Invalid) ? Invalid ?? new() : new(),
        state.HasFlag(StyleState.Disabled) ? Disabled ?? new() : new()).Resolve(theme);
}

/// <summary>Bounded paint transition: opacity/transform values advance from the scheduler-owned clock only.</summary>
internal sealed class PaintTransition(FrameScheduler scheduler, TimeSpan duration, Action<float>? paint = null) : IDisposable
{
    private readonly FrameScheduler _scheduler = scheduler;
    private readonly TimeSpan _duration = duration;
    private readonly Action<float>? _paint = paint;
    private IDisposable? _subscription;
    private TimeSpan _started;
    public float Progress { get; private set; } = 1;
    public bool Active => _subscription is not null;
    public void Start(bool reducedMotion)
    {
        if (_duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(_duration), "Transition duration must be positive.");
        _subscription?.Dispose(); _subscription = null;
        _started = _scheduler.Clock.Now;
        Progress = reducedMotion ? 1 : 0;
        _paint?.Invoke(Progress);
        if (!reducedMotion) _subscription = _scheduler.Clock.Subscribe(Tick);
    }
    public void SetReducedMotion(bool reducedMotion)
    {
        if (!reducedMotion || !Active) return;
        Progress = 1; _subscription!.Dispose(); _subscription = null;
        _paint?.Invoke(Progress);
    }
    private void Tick(TimeSpan now)
    {
        Progress = Math.Min(1, (float)((now - _started).TotalMilliseconds / _duration.TotalMilliseconds));
        _paint?.Invoke(Progress);
        if (Progress == 1) { _subscription!.Dispose(); _subscription = null; }
    }
    public void Dispose() { _subscription?.Dispose(); _subscription = null; }
}

internal sealed record StyleCheckResult(bool Ok, bool ImmutableComposition, bool FiniteSurface, bool StatePrecedence, bool ThemeRetainsState, bool TokenDependenciesUpdate, bool ReducedAtStartup, bool ReducedMidAnimation, bool OneSchedulerClock, bool ClockDisposed, bool PaintOnlyAnimation, int IdleFrames);

internal static class StyleProbe
{
    public static StyleCheckResult Run()
    {
        var baseStyle = new Style().Bg(SemanticToken.Card).Fg(SemanticToken.CardForeground).Padding(2).Gap(1).Size(40, 20).Align(UiAlignment.Center).Type(14, 600).Border(new UiColor("#111111")).Rounded(4).Shadow(2, 1, 1).Opacity(.9f).Transform(1, 2).Ring(1);
        var later = new Style().Px(4).Rounded(6);
        var composed = Styles.Compose(baseStyle, later);
        var immutable = baseStyle.PaddingX == new UiLength(2) && composed.PaddingX == new UiLength(4) && composed.Radius == new UiLength(6);
        var finiteSurface = composed.Width == new UiLength(40) && composed.Height == new UiLength(20) && composed.Gap == new UiLength(1) && composed.Alignment == UiAlignment.Center && composed.Typography == new UiTypography(14, 600) && composed.Border == new UiColor("#111111") && composed.Shadow == new UiShadow(2, 1, 1) && composed.Opacity == new UiOpacity(.9f) && composed.Transform == new UiTransform(1, 2) && composed.FocusRing == new UiLength(1);

        var variants = new VariantStyle(baseStyle, new Style().Bg(SemanticToken.Muted), new Style().Bg(new UiColor("#111111")), new Style().Ring(2), new Style().Fg(SemanticToken.PrimaryForeground), new Style().Opacity(.5f), new Style(Border: new("#ef4444")));
        var state = StyleState.Selected | StyleState.FocusVisible | StyleState.Hover | StyleState.Pressed | StyleState.Invalid | StyleState.Disabled;
        var resolved = variants.Resolve(state, Themes.Light);
        var precedence = resolved.Background == new UiColor("#111111") && resolved.Foreground == Themes.Light.Get(SemanticToken.PrimaryForeground) && resolved.FocusRing == new UiLength(2) && resolved.Border == new UiColor("#ef4444") && resolved.Opacity == new UiOpacity(.5f);

        var graph = new ReactiveGraph();
        using var scope = graph.Scope();
        var theme = scope.Signal(Themes.Light, "style.theme");
        var visualState = scope.Signal(StyleState.Selected, "style.state");
        var scene = new RetainedScene(); var snapshots = new ProjectedSnapshots(); var projection = new SceneProjection(scene, snapshots);
        var element = new StableElement(new("style.button"), new(0, 0, 10, 10), new("#000000", "#ffffff", 4), new("button", "Style proof"));
        var scheduler = new FrameScheduler(() => { });
        UiColor before = default, after = default;
        _ = scope.Effect(() =>
        {
            var current = variants.Resolve(visualState.Value, theme.Value);
            before = after; after = current.Background!.Value;
            element.Style = new(current.Background!.Value.Value, current.Foreground!.Value.Value, current.Radius?.Value ?? 0);
            element.Mark(DirtyFacet.Paint); scheduler.Projected(projection.Project([element]));
        }, "style.paint");
        graph.Drain(); scheduler.Pump();
        theme.Value = Themes.Dark; graph.Drain(); scheduler.Pump();
        var themeRetainsState = visualState.Value == StyleState.Selected && after == Themes.Dark.Get(SemanticToken.Card);
        var tokenUpdates = before == Themes.Light.Get(SemanticToken.Card) && after != before;

        var reduced = scope.Signal(true, "motion.reduced");
        var animationPaints = 0;
        using var transition = new PaintTransition(scheduler, TimeSpan.FromMilliseconds(100), progress =>
        {
            animationPaints++;
            element.Style = element.Style with { Opacity = progress, TranslateX = progress * 4, Scale = .9f + progress * .1f };
            element.Mark(DirtyFacet.Paint);
            scheduler.Projected(projection.Project([element]));
        });
        _ = scope.Effect(() => transition.SetReducedMotion(reduced.Value), "motion.preference");
        graph.Drain(); transition.Start(reduced.Value);
        var reducedAtStartup = !transition.Active && transition.Progress == 1;
        reduced.Value = false; graph.Drain(); transition.Start(reduced.Value); scheduler.Clock.Tick(TimeSpan.FromMilliseconds(40)); scheduler.Pump();
        var activeBeforeToggle = transition.Active && transition.Progress is > 0 and < 1;
        reduced.Value = true; graph.Drain(); scheduler.Pump();
        var reducedMidAnimation = activeBeforeToggle && !transition.Active && transition.Progress == 1;
        var idleBefore = scheduler.PresentCalls; scheduler.Clock.Tick(TimeSpan.FromMilliseconds(40)); scheduler.Pump();
        var idleFrames = scheduler.PresentCalls - idleBefore;
        var oneClock = scheduler.Clock.TickCalls == 1;
        var disposalPaints = 0; var disposableTransition = new PaintTransition(scheduler, TimeSpan.FromMilliseconds(100), _ => disposalPaints++);
        disposableTransition.Start(false); var beforeDispose = disposalPaints; disposableTransition.Dispose(); scheduler.Clock.Tick(TimeSpan.FromMilliseconds(40));
        var activeTransitionDisposed = !disposableTransition.Active && disposalPaints == beforeDispose;
        var invalidDurationRejected = false;
        try { new PaintTransition(scheduler, TimeSpan.Zero).Start(false); } catch (ArgumentOutOfRangeException) { invalidDurationRejected = true; }
        scheduler.Clock.Dispose();
        var clockDisposed = scheduler.Clock.Disposed && activeTransitionDisposed;
        var command = scene.Commands.Single();
        var paintOnly = animationPaints == 4 && command.Opacity == 1 && command.TranslateX == 4 && command.Scale == 1 && element.Dirty == DirtyFacet.None && scene.Count == 1;
        Check(immutable && finiteSurface && precedence && themeRetainsState && tokenUpdates && reducedAtStartup && reducedMidAnimation && oneClock && clockDisposed && invalidDurationRejected && paintOnly && idleFrames == 0, "Style self-check failed.");
        return new(true, immutable, finiteSurface, precedence, themeRetainsState, tokenUpdates, reducedAtStartup, reducedMidAnimation, oneClock, clockDisposed, paintOnly, idleFrames);
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
