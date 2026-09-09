using System.Collections.ObjectModel;

namespace Lucent.Lui.Compiler;

/// <summary>Compiler-owned framework symbol groups that receive implicit `.lui` resolution.</summary>
public static class LuiPropertyCatalog
{
    /// <summary>Fully qualified static types whose public <c>Property&lt;T&gt;</c> members are implicit style keys.</summary>
    public static IReadOnlyList<string> ImplicitStylePropertyTypeNames { get; } =
        new ReadOnlyCollection<string>([
            "Lucent.Core.LayoutProperties",
            "Lucent.Core.VisualProperties",
            "Lucent.Core.TypographyProperties",
            "Lucent.Core.InputProperties",
            "Lucent.Core.ScrollBarProperties",
            "Lucent.Core.ImageProperties",
        ]);

    /// <summary>Fully qualified property identities supported by the first transition authoring surface.</summary>
    public static IReadOnlyList<string> TransitionPropertyIdentities { get; } =
        new ReadOnlyCollection<string>([
            "global::Lucent.Core.VisualProperties.Background",
            "global::Lucent.Core.VisualProperties.Opacity",
            "global::Lucent.Core.TypographyProperties.TextColor",
        ]);

    /// <summary>Returns the value type required by a supported transition property identity.</summary>
    public static string? TransitionPropertyValueType(string identity) =>
        identity switch
        {
            "global::Lucent.Core.VisualProperties.Background" => "global::Lucent.Core.Brush",
            "global::Lucent.Core.VisualProperties.Opacity" => "float",
            "global::Lucent.Core.TypographyProperties.TextColor" => "global::Lucent.Core.Color",
            _ => null,
        };
}
