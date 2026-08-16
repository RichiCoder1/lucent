using Avalonia;
using Avalonia.Controls;
using Lucent.Examples.Todo;

namespace Lucent.Poc;

internal sealed class MainWindow : Window
{
    private readonly TodoComponent? _todo;

    public MainWindow()
    {
        if (Program.SmokeTestName is not null)
        {
            return;
        }

        _todo = new TodoComponent();
        Title = "Lucent Native Controls Todo";
        Width = 760;
        Height = 720;
        MinWidth = 560;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _todo.Mount();
    }

    protected override void OnClosed(EventArgs e)
    {
        _todo?.Dispose();
        base.OnClosed(e);
    }
}
