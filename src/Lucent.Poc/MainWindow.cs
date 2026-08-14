using Avalonia;
using Avalonia.Controls;
using Lucent.Examples.Counter;

namespace Lucent.Poc;

internal sealed class MainWindow : Window
{
    private readonly CounterComponent _counter = new();

    public MainWindow()
    {
        Title = "Lucent POC";
        Width = 480;
        Height = 320;
        MinWidth = 360;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = new Border
        {
            Padding = new Thickness(32),
            Child = _counter.Mount(),
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _counter.Dispose();
        base.OnClosed(e);
    }
}
