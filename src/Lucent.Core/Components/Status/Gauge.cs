using System.Globalization;

namespace Lucent.Core;

/// <summary>Finite read-only gauge range and the optional unit printed beside its value.</summary>
public sealed class GaugeOptions
{
    /// <summary>Creates a range with a positive finite span. Empty units are allowed.</summary>
    public GaugeOptions(double minimum = 0, double maximum = 100, string unit = "")
    {
        if (
            !double.IsFinite(minimum)
            || !double.IsFinite(maximum)
            || maximum <= minimum
            || !double.IsFinite(maximum - minimum)
        )
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                "Gauge bounds must have a positive finite span."
            );
        ArgumentNullException.ThrowIfNull(unit);
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit.Trim();
    }

    /// <summary>The inclusive lower bound.</summary>
    public double Minimum { get; }

    /// <summary>The inclusive upper bound.</summary>
    public double Maximum { get; }

    /// <summary>The display unit, or an empty string.</summary>
    public string Unit { get; }
}

public static partial class Components
{
    /// <summary>Shows a controlled value as a read-only ring and ordinary text, with a 96-DIP preferred box.</summary>
    /// <remarks>Null is empty. Nonfinite or out-of-range values show Unavailable without an arc or an invalid semantic range. No idle animation is scheduled.</remarks>
    [LucentComponent]
    public static AuthorRecipe<StyledAccessibleCapability> Gauge(
        string label,
        Func<double?> value,
        GaugeOptions? options = null,
        Style? style = null,
        Func<AriaMetadata?>? aria = null
    )
    {
        label = Required(label, nameof(label));
        ArgumentNullException.ThrowIfNull(value);
        options ??= new();
        var recipe = StockRecipe.Accessible(
            "gauge",
            (context, root) =>
            {
                var current = root.Scope.Derived(
                    () => GaugeSnapshot.Create(value(), options),
                    root.Name + ".value"
                );
                var preferred = Style
                    .Empty.Width(96)
                    .Height(96)
                    .MainShrink(1)
                    .MainAlignment(LayoutAlignment.Center)
                    .CrossAlignment(LayoutAlignment.Stretch)
                    .Clip(true);
                root.Present(context.Theme, preferred, style);
                DrawingBinding.Attach(
                    root,
                    new DrawingDescriptor(
                        new(96, 96),
                        recorder =>
                        {
                            var snapshot = current.Value;
                            var oval = new LayoutRect(3, 3, 90, 90);
                            var track = context.Theme.Token(ControlThemes.Border);
                            var accent = context.Theme.Token(ControlThemes.Accent);
                            // Monochrome palettes can share both brushes. Width keeps the value
                            // legible without introducing colors outside the selected theme.
                            recorder.StrokeEllipse(oval, track, Equals(track, accent) ? 2 : 6);
                            if (snapshot.Value is not null && snapshot.Fraction > 0)
                                recorder.Arc(
                                    oval,
                                    -90,
                                    (float)(snapshot.Fraction * 360),
                                    accent,
                                    6
                                );
                        }
                    )
                );
                root.AttachBehaviors(new GaugeBehavior(label, () => current.Value, options));
                context.Mount(
                    root,
                    Text(
                        () => current.Value.Display,
                        Style
                            .Empty.Set(LayoutProperties.MinWidth, 0f)
                            .Set(LayoutProperties.MainShrink, 1f)
                            .Set(LayoutProperties.CrossAlignment, LayoutAlignment.Center)
                            .Set(TypographyProperties.Overflow, TextOverflow.Ellipsis)
                    )
                );
            }
        );
        return aria is null ? recipe : recipe.Aria.Metadata(aria).End;
    }

    private sealed record GaugeSnapshot(double? Value, double Fraction, string Display)
    {
        internal static GaugeSnapshot Create(double? value, GaugeOptions options)
        {
            var suffix = options.Unit.Length == 0 ? "" : " " + options.Unit;
            if (value is null)
                return new(null, 0, "—" + suffix);
            if (!double.IsFinite(value.Value) || value < options.Minimum || value > options.Maximum)
                return new(null, 0, "Unavailable");
            return new(
                value,
                (value.Value - options.Minimum) / (options.Maximum - options.Minimum),
                value.Value.ToString("0.##", CultureInfo.CurrentCulture) + suffix
            );
        }
    }

    private sealed class GaugeBehavior(string label, Func<GaugeSnapshot> read, GaugeOptions options)
        : Behavior
    {
        public override string Name => "gauge";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) =>
            context.BindSemantics(() =>
            {
                var snapshot = read();
                return SemanticDeclaration
                    .Create(SemanticRole.ProgressBar, label)
                    .Value(snapshot.Display)
                    .Range(
                        snapshot.Value is { } value
                            ? new(value, options.Minimum, options.Maximum, 1, 1, isReadOnly: true)
                            : null,
                        false
                    )
                    .Build();
            });
    }
}
