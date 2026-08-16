using Avalonia.Controls;

namespace Lucent.Examples.Workbench;

internal sealed class SettingsDialog : Window
{
    private readonly SettingsPaneComponent _component = new();

    public SettingsDialog()
    {
        Title = "Settings";
        Width = 420;
        Height = 280;
        var roots = _component.Mount();
        Content = roots.Count == 1 ? roots[0] : throw new InvalidOperationException("Settings pane must have one root.");
        Closed += (_, _) => _component.Dispose();
    }
}
