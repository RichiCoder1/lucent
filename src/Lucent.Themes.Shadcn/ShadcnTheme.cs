using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Lucent.Themes.Shadcn;

/// <summary>New York/neutral resources layered after an explicitly installed Fluent theme.</summary>
public sealed class ShadcnTheme : Styles
{
    public ShadcnTheme() => AvaloniaXamlLoader.Load(this);
}
