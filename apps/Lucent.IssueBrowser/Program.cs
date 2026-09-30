using Lucent.Platform.Windows;

namespace Lucent.IssueBrowser;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!TryOptions(args, out var nativeMenus, out var restoreNavigation))
        {
            Console.Error.WriteLine(
                "Usage: Lucent.IssueBrowser [--native-menus] [--restore-navigation]"
            );
            return 2;
        }
        try
        {
            var persistence = restoreNavigation
                ? new IssueBrowserNavigationPersistence(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Lucent",
                        "IssueBrowser",
                        "navigation-v1.json"
                    )
                )
                : null;
            var builder = LucentApplication
                .CreateBuilder()
                .UseWindows(
                    new WindowsWindowOptions
                    {
                        Width = 1120,
                        Height = 760,
                        MinimumWidth = 420,
                        MinimumHeight = 360,
                        MenuPresentation = nativeMenus
                            ? WindowsMenuPresentation.PreferNative
                            : WindowsMenuPresentation.Lucent,
                    }
                )
                .SetTitle("Lucent Issue Browser");
            if (persistence is not null)
                persistence.Configure(builder);
            return builder.Build().Run(IssueBrowserStructure.CreateHosted(persistence));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Lucent startup failed: {exception.Message}");
            return 1;
        }
    }

    internal static bool TryOptions(string[] args, out bool nativeMenus, out bool restoreNavigation)
    {
        nativeMenus = false;
        restoreNavigation = false;
        foreach (var argument in args)
        {
            if (argument == "--native-menus" && !nativeMenus)
                nativeMenus = true;
            else if (argument == "--restore-navigation" && !restoreNavigation)
                restoreNavigation = true;
            else
                return false;
        }
        return true;
    }
}
