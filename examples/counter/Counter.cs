using Avalonia.Controls;

namespace Lucent.Examples.Counter;

internal sealed class Counter : ContentControl
{
    private readonly CounterComponent _component = new();

    public Counter()
    {
        Content = _component.Mount();
        DetachedFromVisualTree += (_, _) => _component.Dispose();
    }
}
