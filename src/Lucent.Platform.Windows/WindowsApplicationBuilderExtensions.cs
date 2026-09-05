using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>Adds the standard Windows host to a Lucent application builder.</summary>
public static class WindowsApplicationBuilderExtensions
{
    /// <summary>Selects the Windows SDL, Skia, input, settings, and accessibility adapter.</summary>
    public static LucentApplicationBuilder UseWindows(this LucentApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseHost(WindowsApplicationHost.Instance);
    }

    private sealed class WindowsApplicationHost : IApplicationHost
    {
        internal static WindowsApplicationHost Instance { get; } = new();

        private WindowsApplicationHost() { }

        public int Run(ApplicationSession session) => WindowsBootstrap.Run(session);
    }
}
