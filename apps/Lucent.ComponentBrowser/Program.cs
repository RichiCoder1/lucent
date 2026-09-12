using Lucent.Platform.Windows;

namespace Lucent.ComponentBrowser;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: Lucent.ComponentBrowser");
            return 2;
        }

        try
        {
            return LucentApplication
                .CreateBuilder()
                .UseWindows(
                    new WindowsWindowOptions
                    {
                        Width = 1280,
                        Height = 840,
                        MinimumWidth = 760,
                        MinimumHeight = 520,
                    }
                )
                .SetTitle("Lucent Component Browser")
                .Build()
                .Run(new ComponentBrowserLifecycle());
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Lucent Component Browser failed: {exception}");
            return 1;
        }
    }
}
