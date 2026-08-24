using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;

namespace ShadcnGallery;

public sealed class GalleryWindow : Window
{
    public TextBox FocusTarget { get; } = new() { Text = "Focused field" };

    public GalleryWindow()
    {
        Title = "Shadcn control gallery";
        Width = 900;
        Height = 700;
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children =
        {
            new TextBlock { Text = "Shadcn utilities (finite Avalonia catalog)", FontSize = 20 },
            UtilityEvidence(),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
            {
                Swatch("Primary", "Shadcn.Primary"), Swatch("Muted", "Shadcn.Muted"), Swatch("Accent", "Shadcn.Accent"),
                Swatch("Danger", "Shadcn.Destructive"), Swatch("Border", "Shadcn.Border"), Swatch("Input", "Shadcn.Input"), Swatch("Ring", "Shadcn.Ring")
            }},
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new Button { Content = "Primary" }, new Button { Content = "Secondary", Classes = { "secondary" } },
                new Button { Content = "Destructive", Classes = { "destructive" } }, new Button { Content = "Outline", Classes = { "outline" } },
                new Button { Content = "Ghost", Classes = { "ghost" } }, new Button { Content = "Link", Classes = { "link" } }, new Button { Content = "Disabled", IsEnabled = false }
            }},
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                StateButton("Hover", ":pointerover"), StateButton("Pressed", ":pressed"), StateButton("Focus visible", ":focus-visible")
            }},
            new Border { Classes = { "card" }, Child = new StackPanel { Spacing = 8, Children =
            {
                FocusTarget, new TextBox { PlaceholderText = "Empty field" }, new TextBox { Text = "Disabled", IsEnabled = false },
                InvalidField(), ValidationText(),
                FocusVisible(new CheckBox { Content = "Checked", IsChecked = true }), new CheckBox { Content = "Unchecked", IsChecked = false },
                FocusVisible(new RadioButton { Content = "Selected", IsChecked = true }), new RadioButton { Content = "Unselected", IsChecked = false },
                FocusVisible(new ComboBox { ItemsSource = new[] { "One", "Two", "Three" }, SelectedIndex = 0 }),
                FocusVisible(new ListBox { ItemsSource = new[] { "Selected", "Unselected" }, SelectedIndex = 0, Height = 70 }),
                FocusVisible(new TabControl { ItemsSource = new[] { new TabItem { Header = "First" }, new TabItem { Header = "Second" } } }),
                FocusVisible(new Menu { ItemsSource = new[] { new MenuItem { Header = "Menu", ItemsSource = new[] { new MenuItem { Header = "Menu item" } } } } }),
                new ToolTip { Content = "Visible tooltip treatment" }
            }}}
        }}};
    }

    private static Control UtilityEvidence()
    {
        var selected = new ListBoxItem { Classes = { "selected:bg-muted" }, Content = "Selected utility", IsSelected = true };
        var hover = StateButton("Hover utility", ":pointerover");
        hover.Classes.Add("hover:bg-primary");
        var focus = new TextBox { Text = "Focus utility", Classes = { "focus:border-ring" } };
        ((IPseudoClasses)focus.Classes).Add(":focus");
        var visibleFocus = new Button { Content = "Focus-visible utility", Classes = { "focus-visible:border-ring" } };
        ((IPseudoClasses)visibleFocus.Classes).Add(":focus-visible");
        var disabled = new Button { Content = "Disabled utility", IsEnabled = false, Classes = { "disabled:opacity-50" } };
        return new Border { Name = "UtilityCatalogEvidence", Classes = { "m-2", "m-4", "p-2", "p-4", "bg-muted", "border-border", "border", "border-2", "rounded", "rounded-lg", "overflow-hidden", "text-center-self", "items-center" }, Child = new StackPanel { Classes = { "gap-2", "gap-4" }, Children =
        {
            new TextBlock { Name = "UtilityTypographyEvidence", Classes = { "text-sm", "text-lg", "font-medium", "font-bold", "italic", "text-left", "text-center", "text-wrap", "text-nowrap", "leading-6" }, Text = "Typography: bold italic centered line-height" },
            new Border { Name = "UtilityConflictEvidence", Classes = { "p-2", "p-4", "w-24", "w-48", "h-8", "h-12", "bg-primary" }, Child = new TextBlock { Text = "Canonical conflict: p-4 / w-48 / h-12 win" } },
            hover, focus, visibleFocus, disabled,
            new CheckBox { Classes = { "checked:bg-primary" }, Content = "Checked utility", IsChecked = true },
            new ListBox { Height = 45, ItemsSource = new[] { selected } },
            new Button { Name = "UtilityForegroundEvidence", Classes = { "text-foreground" }, Content = "Templated foreground utility" },
            new Border { Name = "UtilityOpacityEvidence", Classes = { "opacity-50", "opacity-100" }, Child = new TextBlock { Text = "Opacity-100 wins" } },
            new Border { Name = "UtilityHiddenEvidence", Classes = { "hidden" }, Child = new TextBlock { Text = "Hidden utility remains captured structurally" } }
        } } };
    }

    private static Border Swatch(string name, string resource)
    {
        var dark = Application.Current?.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var color = resource switch
        {
            "Shadcn.Primary" => dark ? "#E5E5E5" : "#343434",
            "Shadcn.Muted" => dark ? "#404040" : "#F5F5F5",
            "Shadcn.Accent" => dark ? "#404040" : "#F5F5F5",
            "Shadcn.Destructive" => dark ? "#FF6467" : "#E7000B",
            "Shadcn.Border" or "Shadcn.Input" => dark ? "#1AFFFFFF" : "#E5E5E5",
            _ => dark ? "#737373" : "#ABABAB"
        };
        var swatch = new Border
        {
            Width = 78, Height = 34, Background = new SolidColorBrush(Color.Parse(color)),
            Child = new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        return swatch;
    }

    private static TextBox InvalidField()
    {
        var field = new TextBox { Text = "Invalid" };
        field.SetValue(DataValidationErrors.HasErrorsProperty, true);
        return field;
    }

    private static TextBlock ValidationText()
    {
        if (Application.Current?.TryFindResource("Shadcn.Destructive",
                Application.Current.RequestedThemeVariant, out var resource) != true || resource is not IBrush foreground)
            throw new InvalidOperationException("Shadcn.Destructive must resolve for validation text.");
        return new TextBlock { Text = "Invalid field (native validation state)", Foreground = foreground };
    }

    private static Button StateButton(string content, string state)
    {
        var button = new Button { Name = content.Replace(" ", string.Empty) + "Evidence", Content = content };
        return FocusVisible(button, state);
    }

    private static T FocusVisible<T>(T control, string state = ":focus-visible") where T : Control
    {
        ((IPseudoClasses)control.Classes).Add(state);
        return control;
    }
}
