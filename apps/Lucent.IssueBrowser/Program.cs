using Lucent.Platform.Windows;

namespace Lucent.IssueBrowser;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            return LucentApplication
                .CreateBuilder()
                .UseWindows()
                .SetTitle("Lucent Issue Browser")
                .SetTheme(AppTheme.Create)
                .Build()
                .Run(IssueBrowserStructure.Create());
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Lucent M0 startup failed: {exception.Message}");
            return 1;
        }
    }
}
