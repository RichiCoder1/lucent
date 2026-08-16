using Avalonia.Controls;

namespace Lucent.Examples.Workbench;

internal sealed class GeneratedPreviewWindow : Window
{
    private readonly GeneratedPreviewPaneComponent _component = new();

    public GeneratedPreviewWindow()
    {
        Title = "Generated C# preview";
        Width = 720;
        Height = 480;
        var roots = _component.Mount();
        Content = roots.Count == 1 ? roots[0] : throw new InvalidOperationException("Preview pane must have one root.");
        Closed += (_, _) => _component.Dispose();
    }
}
