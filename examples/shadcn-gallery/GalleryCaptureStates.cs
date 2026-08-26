using Avalonia.Controls;
using Avalonia.Controls.Utils;
using Avalonia.VisualTree;

namespace ShadcnGallery;

internal static class GalleryCaptureStates
{
    internal static void Apply(Window window)
    {
        var controls = Descendants(window).ToArray();
        var utility = controls.First(control => control.Name == "UtilityCatalogEvidence");
        AddClasses(utility, "m-2", "m-4", "p-2", "p-4", "bg-muted", "border-border", "border", "border-2", "rounded", "rounded-lg", "overflow-hidden", "text-center-self", "items-center");
        AddClasses(utility.GetVisualDescendants().OfType<StackPanel>().First(), "gap-2", "gap-4");
        var selected = controls.OfType<ListBoxItem>().First(item => item.Name == "SelectedUtilityEvidence");
        AddClasses(selected, "selected:bg-muted");
        selected.IsSelected = true;
        AddClasses(controls.First(control => control.Name == "UtilityTypographyEvidence"), "text-sm", "text-lg", "font-medium", "font-bold", "italic", "text-left", "text-center", "text-wrap", "text-nowrap", "leading-6");
        AddClasses(controls.First(control => control.Name == "UtilityConflictEvidence"), "p-2", "p-4", "w-24", "w-48", "h-8", "h-12", "bg-primary");
        AddClasses(controls.First(control => control.Name == "UtilityForegroundEvidence"), "text-foreground");
        AddClasses(controls.First(control => control.Name == "UtilityOpacityEvidence"), "opacity-50", "opacity-100");
        AddClasses(controls.First(control => control.Name == "UtilityHiddenEvidence"), "hidden");
        var utilityButtons = controls.OfType<Button>().Where(button => button.Content is string content && content.EndsWith("utility", StringComparison.Ordinal)).ToArray();
        AddClasses(utilityButtons.First(button => (string)button.Content! == "Hover utility"), "hover:bg-primary");
        AddClasses(controls.OfType<TextBox>().First(box => box.Text == "Focus utility"), "focus:border-ring");
        AddClasses(utilityButtons.First(button => (string)button.Content! == "Focus-visible utility"), "focus-visible:border-ring");
        AddClasses(utilityButtons.First(button => (string)button.Content! == "Disabled utility"), "disabled:opacity-50");
        AddClasses(controls.OfType<CheckBox>().First(box => box.Content?.ToString() == "Checked utility"), "checked:bg-primary");
        ((IPseudoClasses)controls.OfType<Button>().First(button => button.Name == "HoverEvidence").Classes).Add(":pointerover");
        ((IPseudoClasses)controls.OfType<Button>().First(button => button.Name == "PressedEvidence").Classes).Add(":pressed");
        ((IPseudoClasses)controls.OfType<Button>().First(button => button.Name == "FocusVisibleEvidence").Classes).Add(":focus-visible");
        ((IPseudoClasses)controls.OfType<TextBox>().First(box => box.Name == "InvalidField").Classes).Add(":focus-visible");
        controls.OfType<TextBox>().First(box => box.Name == "InvalidField").SetValue(DataValidationErrors.HasErrorsProperty, true);
        foreach (var control in controls.Where(control => control.Focusable).Take(8))
            ((IPseudoClasses)control.Classes).Add(":focus-visible");
        ((IPseudoClasses)controls.First(control => control.Classes.Contains("hover:bg-primary")).Classes).Add(":pointerover");
        ((IPseudoClasses)controls.First(control => control.Classes.Contains("focus:border-ring")).Classes).Add(":focus");
        ((IPseudoClasses)controls.First(control => control.Classes.Contains("focus-visible:border-ring")).Classes).Add(":focus-visible");
    }

    private static void AddClasses(Control control, params string[] names)
    {
        foreach (var name in names) control.Classes.Add(name);
    }

    internal static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child)) yield return nested;
    }
}
