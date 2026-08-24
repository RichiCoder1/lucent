using Avalonia.Styling;

namespace Lucent.Styles.Utilities;

/// <summary>Optional finite Avalonia utility styles. Install explicitly in <c>Application.Styles</c>.</summary>
public sealed class LucentStyles : global::Avalonia.Styling.Styles
{
    public LucentStyles()
    {
        foreach (var style in UtilitySpecification.Generate()) Add(style);
    }
}
