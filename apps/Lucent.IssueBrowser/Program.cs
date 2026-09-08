using Lucent.Platform.Windows;

namespace Lucent.IssueBrowser;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0 && args is not ["--native-menus"])
        {
            Console.Error.WriteLine("Usage: Lucent.IssueBrowser [--native-menus]");
            return 2;
        }
        try
        {
            return LucentApplication
                .CreateBuilder()
                .UseWindows(
                    new WindowsWindowOptions
                    {
                        Width = 1120,
                        Height = 760,
                        MinimumWidth = 420,
                        MinimumHeight = 360,
                        MenuPresentation =
                            args.Length == 0
                                ? WindowsMenuPresentation.Lucent
                                : WindowsMenuPresentation.PreferNative,
                    }
                )
                .SetTitle("Lucent Issue Browser")
                .Build()
                .Run(IssueBrowserStructure.Create());
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Lucent startup failed: {exception.Message}");
            return 1;
        }
    }
}
