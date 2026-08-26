using Avalonia;

namespace Lucent.Examples.Workbench;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => args.Contains("--smoke-test", StringComparer.Ordinal)
        ? WorkbenchSmoke.Run(args)
        : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
