using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>Adds the standard Windows host to a Lucent application builder.</summary>
public static class WindowsApplicationBuilderExtensions
{
    /// <summary>Selects the Windows SDL, Skia, input, settings, and accessibility adapter.</summary>
    public static LucentApplicationBuilder UseWindows(
        this LucentApplicationBuilder builder,
        WindowsWindowOptions? window = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        window?.Validate();
        return builder.UseHost(
            window is null ? WindowsApplicationHost.Instance : new WindowsApplicationHost(window)
        );
    }

    private sealed class WindowsApplicationHost : IApplicationHost
    {
        internal static WindowsApplicationHost Instance { get; } = new();

        private readonly WindowsWindowOptions? _window;

        internal WindowsApplicationHost(WindowsWindowOptions? window = null) => _window = window;

        public int Run(ApplicationSession session) => WindowsBootstrap.Run(session, _window);
    }
}
