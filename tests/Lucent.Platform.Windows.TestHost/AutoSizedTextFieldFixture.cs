using Lucent.Platform.Windows;

namespace Lucent.Platform.Windows.TestHost;

/// <summary>Published .lui fixture for the default intrinsic TextField sizing contract.</summary>
internal static class AutoSizedTextFieldFixture
{
    internal static int Run()
    {
        try
        {
            return LucentApplication
                .CreateBuilder()
                .UseWindows()
                .SetTitle("Lucent Autosized TextField Fixture")
                .SetTheme(_ => ControlThemes.Light)
                .Build()
                .Run(LuiFixtures.Components.AutoSizedTextFieldFixtureView());
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Lucent autosized text-field fixture: " + error.Message);
            return 1;
        }
    }
}
