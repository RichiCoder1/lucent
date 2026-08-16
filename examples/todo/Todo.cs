using Avalonia.Controls;

namespace Lucent.Examples.Todo;

internal sealed class Todo : ContentControl
{
    private readonly TodoComponent _component = new();

    public Todo()
    {
        Content = _component.Mount();
        DetachedFromVisualTree += (_, _) => _component.Dispose();
    }
}
