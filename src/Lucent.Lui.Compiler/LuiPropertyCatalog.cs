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
        ]);
}
