using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace IssueBrowser.Avalonia;

// Frozen GAUNTLET syntax-confound comparison only; the mounted app remains IssueBrowser.lui.
internal static class StraightCSharpFilterBar
{
    internal static StackPanel Create(IssueBrowserSession session) => new()
    {
        DataContext = session,
        Spacing = 12,
        Children =
        {
            new TextBlock { Classes = { "section-label" }, Text = "FILTERS" },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { Chip("Ada", nameof(IssueBrowserSession.AdaChecked), session.ToggleAda), Chip("Open", nameof(IssueBrowserSession.OpenChecked), session.ToggleOpen), Chip("High", nameof(IssueBrowserSession.HighChecked), session.ToggleHigh) }
            }
        }
    };

    private static CheckBox Chip(string label, string property, Action toggle)
    {
        var chip = new CheckBox { Content = label };
        chip.Bind(CheckBox.IsCheckedProperty, new Binding(property));
        chip.Click += (_, _) => toggle();
        return chip;
    }
}
