using System.Globalization;

namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Displays a normalized progress fraction, or an explicitly indeterminate status when the reader returns null.</summary>
    /// <remarks>The value is read-only, finite and between zero and one. Indeterminate presentation is static and schedules no animation.</remarks>
    [LucentComponent]
    public static ComponentRecipe ProgressBar(
        string label,
        Func<double?> value,
        Style? style = null
    )
    {
        label = Required(label, nameof(label));
        ArgumentNullException.ThrowIfNull(value);
        return ProgressBarContent(label, value, style);
    }

    internal static double? ValidateProgressFraction(double? value)
    {
        if (value is { } fraction && (!double.IsFinite(fraction) || fraction < 0 || fraction > 1))
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Progress must be a finite fraction from zero to one, or null for indeterminate."
            );
        return value;
    }

    internal static string ProgressBarLabel(string label, double? value) =>
        ValidateProgressFraction(value) is { } fraction
            ? label + " " + fraction.ToString("P0", CultureInfo.CurrentCulture)
            : label + " — in progress";

    internal static GridTracks ProgressBarColumns(double? value)
    {
        var fraction = (float)(ValidateProgressFraction(value) ?? 0);
        return GridTracks.Create(
            fraction == 0 ? GridTrack.Fixed(0) : GridTrack.Fraction(fraction),
            fraction == 1 ? GridTrack.Fixed(0) : GridTrack.Fraction(1 - fraction)
        );
    }

    [LucentComponent]
    internal static ComponentRecipe ProgressBarFrame(
        [DefaultContent] ComponentContent content,
        string label,
        Func<double?> value,
        Style? style = null
    )
    {
        return ComponentRecipe.Create(
            "progress-bar",
            (context, root) =>
            {
                var current = root.Scope.Derived(
                    () => ValidateProgressFraction(value()),
                    root.Name + ".fraction"
                );
                var layout = Style
                    .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                    .Set(LayoutProperties.Spacing, 8f);
                root.Present(context.Theme, layout, style);
                root.AttachBehaviors(new ProgressBarBehavior(label, current.Value));
                _ = root.Scope.Effect(
                    () => root.UpdateControlSemantics(ProgressBarSemantics(label, current.Value)),
                    root.Name + ".semantics"
                );
                context.Mount(root, content);
            }
        );
    }

    private static SemanticDeclaration ProgressBarSemantics(string label, double? value) =>
        new(
            SemanticRole.ProgressBar,
            label,
            value: value is { } fraction
                ? fraction.ToString("P0", CultureInfo.CurrentCulture)
                : null,
            range: value is { } amount ? new(amount, 0, 1, 0.01, 0.1, isReadOnly: true) : null
        );

    private sealed class ProgressBarBehavior(string label, double? value) : Behavior
    {
        public override string Name => "progress-bar";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) =>
            context.SetSemantics(ProgressBarSemantics(label, value));
    }
}
