using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace Lucent.Styles.Utilities;

// This finite table is the catalog's only source of truth. The embedded manifest
// is checked against it (including canonical order) on every package build.
internal sealed record UtilityDefinition(string Name, string Type, string Property, object Value,
    int Order, string Detail, string? Pseudo = null);

internal static class UtilitySpecification
{
    internal static readonly IReadOnlyList<UtilityDefinition> Entries =
    [
        new("m-2", "Control", "Margin", new Thickness(8), 10, "Control.Margin = 8"),
        new("m-4", "Control", "Margin", new Thickness(16), 11, "Control.Margin = 16"),
        new("p-2", "Border", "Padding", new Thickness(8), 20, "Border.Padding = 8"),
        new("p-4", "Border", "Padding", new Thickness(16), 21, "Border.Padding = 16"),
        new("w-24", "Control", "Width", 96d, 30, "Control.Width = 96"),
        new("w-48", "Control", "Width", 192d, 31, "Control.Width = 192"),
        new("h-8", "Control", "Height", 32d, 40, "Control.Height = 32"),
        new("h-12", "Control", "Height", 48d, 41, "Control.Height = 48"),
        new("gap-2", "StackPanel", "Spacing", 8d, 50, "StackPanel.Spacing = 8"),
        new("gap-4", "StackPanel", "Spacing", 16d, 51, "StackPanel.Spacing = 16"),
        new("text-sm", "TextBlock", "FontSize", 14d, 60, "TextBlock.FontSize = 14"),
        new("text-lg", "TextBlock", "FontSize", 18d, 61, "TextBlock.FontSize = 18"),
        new("font-medium", "TextBlock", "FontWeight", FontWeight.Medium, 70, "TextBlock.FontWeight = Medium"),
        new("font-bold", "TextBlock", "FontWeight", FontWeight.Bold, 71, "TextBlock.FontWeight = Bold"),
        new("italic", "TextBlock", "FontStyle", FontStyle.Italic, 80, "TextBlock.FontStyle = Italic"),
        new("text-left", "TextBlock", "TextAlignment", TextAlignment.Left, 90, "TextBlock.TextAlignment = Left"),
        new("text-center", "TextBlock", "TextAlignment", TextAlignment.Center, 91, "TextBlock.TextAlignment = Center"),
        new("text-wrap", "TextBlock", "TextWrapping", TextWrapping.Wrap, 100, "TextBlock.TextWrapping = Wrap"),
        new("text-nowrap", "TextBlock", "TextWrapping", TextWrapping.NoWrap, 101, "TextBlock.TextWrapping = NoWrap"),
        new("leading-6", "TextBlock", "LineHeight", 24d, 110, "TextBlock.LineHeight = 24"),
        new("bg-primary", "Border", "Background", Resource("Shadcn.Primary"), 120, "Border.Background = Shadcn.Primary"),
        new("bg-muted", "Border", "Background", Resource("Shadcn.Muted"), 121, "Border.Background = Shadcn.Muted"),
        new("text-foreground", "TemplatedControl", "Foreground", Resource("Shadcn.Foreground"), 130, "TemplatedControl.Foreground = Shadcn.Foreground"),
        new("border-border", "Border", "BorderBrush", Resource("Shadcn.Border"), 140, "Border.BorderBrush = Shadcn.Border"),
        new("border", "Border", "BorderThickness", new Thickness(1), 150, "Border.BorderThickness = 1"),
        new("border-2", "Border", "BorderThickness", new Thickness(2), 151, "Border.BorderThickness = 2"),
        new("rounded", "Border", "CornerRadius", new CornerRadius(4), 160, "Border.CornerRadius = 4"),
        new("rounded-lg", "Border", "CornerRadius", new CornerRadius(8), 161, "Border.CornerRadius = 8"),
        new("opacity-50", "Control", "Opacity", .5d, 170, "Control.Opacity = 0.5"),
        new("opacity-100", "Control", "Opacity", 1d, 171, "Control.Opacity = 1"),
        new("hidden", "Control", "IsVisible", false, 180, "Control.IsVisible = false"),
        new("overflow-hidden", "Control", "ClipToBounds", true, 190, "Control.ClipToBounds = true"),
        new("text-center-self", "Control", "HorizontalAlignment", HorizontalAlignment.Center, 200, "Control.HorizontalAlignment = Center"),
        new("items-center", "Control", "VerticalAlignment", VerticalAlignment.Center, 201, "Control.VerticalAlignment = Center"),
        new("hover:bg-primary", "Button", "Background", Resource("Shadcn.Primary"), 300, "Button:pointerover Background = Shadcn.Primary", ":pointerover"),
        new("focus:border-ring", "TextBox", "BorderBrush", Resource("Shadcn.Ring"), 301, "TextBox:focus BorderBrush = Shadcn.Ring", ":focus"),
        new("focus-visible:border-ring", "Button", "BorderBrush", Resource("Shadcn.Ring"), 302, "Button:focus-visible BorderBrush = Shadcn.Ring", ":focus-visible"),
        new("disabled:opacity-50", "Button", "Opacity", .5d, 303, "Button:disabled Opacity = 0.5", ":disabled"),
        new("checked:bg-primary", "ToggleButton", "Background", Resource("Shadcn.Primary"), 304, "ToggleButton:checked Background = Shadcn.Primary", ":checked"),
        new("selected:bg-muted", "ListBoxItem", "Background", Resource("Shadcn.Muted"), 305, "ListBoxItem:selected Background = Shadcn.Muted", ":selected"),
    ];

    internal static IEnumerable<Style> Generate() => Entries.OrderBy(entry => entry.Order).Select(Create);

    private static DynamicResourceExtension Resource(string key) => new(key);

    private static Style Create(UtilityDefinition entry)
    {
        Style style = entry.Type switch
        {
            "Border" => new Style(x => Select(x.Is<Border>(), entry)),
            "StackPanel" => new Style(x => Select(x.Is<StackPanel>(), entry)),
            "TextBlock" => new Style(x => Select(x.Is<TextBlock>(), entry)),
            "TemplatedControl" => new Style(x => Select(x.Is<TemplatedControl>(), entry)),
            "Button" => new Style(x => Select(x.Is<Button>(), entry)),
            "TextBox" => new Style(x => Select(x.Is<TextBox>(), entry)),
            "ToggleButton" => new Style(x => Select(x.Is<ToggleButton>(), entry)),
            "ListBoxItem" => new Style(x => Select(x.Is<ListBoxItem>(), entry)),
            _ => new Style(x => Select(x.Is<Control>(), entry)),
        };
        style.Setters.Add(new Setter(Property(entry), entry.Value));
        return style;
    }

    private static Selector Select(Selector selector, UtilityDefinition entry) =>
        entry.Pseudo is null ? selector.Class(entry.Name) : selector.Class(entry.Name).Class(entry.Pseudo);

    private static AvaloniaProperty Property(UtilityDefinition entry) => entry.Property switch
    {
        "Margin" => Layoutable.MarginProperty, "Padding" => Border.PaddingProperty,
        "Width" => Layoutable.WidthProperty, "Height" => Layoutable.HeightProperty,
        "Spacing" => StackPanel.SpacingProperty, "FontSize" => TextBlock.FontSizeProperty,
        "FontWeight" => TextBlock.FontWeightProperty, "FontStyle" => TextBlock.FontStyleProperty,
        "TextAlignment" => TextBlock.TextAlignmentProperty, "TextWrapping" => TextBlock.TextWrappingProperty,
        "LineHeight" => TextBlock.LineHeightProperty, "Background" => entry.Type == "Border" ? Border.BackgroundProperty : TemplatedControl.BackgroundProperty,
        "Foreground" => TemplatedControl.ForegroundProperty, "BorderBrush" => entry.Type == "Border" ? Border.BorderBrushProperty : TemplatedControl.BorderBrushProperty,
        "BorderThickness" => entry.Type == "Border" ? Border.BorderThicknessProperty : TemplatedControl.BorderThicknessProperty, "CornerRadius" => Border.CornerRadiusProperty,
        "Opacity" => Visual.OpacityProperty, "IsVisible" => Visual.IsVisibleProperty,
        "ClipToBounds" => Visual.ClipToBoundsProperty, "HorizontalAlignment" => Layoutable.HorizontalAlignmentProperty,
        "VerticalAlignment" => Layoutable.VerticalAlignmentProperty, _ => throw new InvalidOperationException(entry.Property),
    };
}
