using Lucent.Core;
using Lucent.Platform.Windows;

namespace Lucent.Performance.Fixture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: Lucent.Performance.Fixture");
            return 2;
        }

        try
        {
            return LucentApplication
                .CreateBuilder()
                .UseWindows(
                    new WindowsWindowOptions
                    {
                        Width = FixedFixture.WindowWidth,
                        Height = FixedFixture.WindowHeight,
                        MinimumWidth = 320,
                        MinimumHeight = 200,
                    }
                )
                .SetTheme(static _ => ControlThemes.Light)
                .SetTitle("Lucent Performance Fixture " + FixedFixture.Version)
                .Build()
                .Run(FixedFixture.Create());
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Lucent performance fixture failed: " + error);
            return 1;
        }
    }
}
