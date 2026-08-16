using Avalonia.Controls;

namespace Lucent.Examples.PackagePulse;

internal sealed class PackagePulse : ContentControl
{
    private readonly PackagePulseComponent _component = new();

    public PackagePulse()
    {
        Content = _component.Mount();
        DetachedFromVisualTree += (_, _) => _component.Dispose();
    }
}
