using Microsoft.Windows.AppLifecycle;

namespace Lucent.Platform.Windows.Activation;

/// <summary>Explicit install/remove operations for an unpackaged application's protocol association.</summary>
/// <remarks>Call from an installer or explicit application setup command. Normal activation startup
/// never invokes these methods. MSIX packages declare the protocol in their manifest instead.</remarks>
public static class UnpackagedProtocolRegistration
{
    /// <summary>Registers the application's configured scheme for an absolute executable path.</summary>
    public static void Register(
        WindowsActivationOptions options,
        string executablePath,
        string logoResource,
        string displayName
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ValidateExecutablePath(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(logoResource);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        ActivationRegistrationManager.RegisterForProtocolActivation(
            options.Scheme,
            logoResource,
            displayName,
            executablePath
        );
    }

    /// <summary>Unregisters the application association during an explicit remove operation.</summary>
    public static void Unregister(WindowsActivationOptions options, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ValidateExecutablePath(executablePath);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        ActivationRegistrationManager.UnregisterForProtocolActivation(
            options.Scheme,
            executablePath
        );
    }

    private static void ValidateExecutablePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (
            !Path.IsPathFullyQualified(path)
            || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        )
            throw new ArgumentException(
                "Use an absolute executable path for protocol registration.",
                nameof(path)
            );
    }
}
