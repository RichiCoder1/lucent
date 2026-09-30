using Lucent.Core;
using Lucent.Platform.Windows;

namespace TemplateNamespace;

internal static class Program
{
    [STAThread]
    private static int Main() =>
        LucentApplication
            .CreateBuilder()
            .UseWindows(
                new WindowsWindowOptions
                {
                    Width = 640,
                    Height = 440,
                    MinimumWidth = 360,
                    MinimumHeight = 320,
                }
            )
            .SetTitle("LucentAppProject")
            .Build(Components.MainView())
            .Run();
}
