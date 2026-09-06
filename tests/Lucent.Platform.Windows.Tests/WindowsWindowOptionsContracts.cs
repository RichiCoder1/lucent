using Lucent.Core;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsWindowOptionsContracts
{
    [TestMethod]
    public void WindowConfigurationRejectsImpossibleBoundsBeforeStartingHost()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LucentApplication.CreateBuilder().UseWindows(new() { Width = 0 })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LucentApplication.CreateBuilder().UseWindows(new() { MinimumHeight = 501 })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LucentApplication.CreateBuilder().UseWindows(new() { MinimumWidth = -1 })
        );
        Assert.IsNotNull(
            LucentApplication
                .CreateBuilder()
                .UseWindows(
                    new()
                    {
                        Width = 1180,
                        Height = 760,
                        MinimumWidth = 480,
                        MinimumHeight = 520,
                    }
                )
        );
    }

    [TestMethod]
    public void LogicalWindowMinimumsRespectWindowsDpiAndSdlCoordinateDensity()
    {
        Assert.AreEqual(480, WindowsWindowOptions.ToWindowUnits(480, 1, 1));
        Assert.AreEqual(720, WindowsWindowOptions.ToWindowUnits(480, 1.5f, 1));
        Assert.AreEqual(960, WindowsWindowOptions.ToWindowUnits(480, 2, 1));
        Assert.AreEqual(480, WindowsWindowOptions.ToWindowUnits(480, 2, 2));
        Assert.AreEqual(722, WindowsWindowOptions.ToWindowUnits(481, 1.5f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowsWindowOptions.ToWindowUnits(480, float.NaN, 1)
        );
    }
}
