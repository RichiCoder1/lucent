using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>Chooses how standard context menus are presented by the Windows host.</summary>
public enum WindowsMenuPresentation
{
    /// <summary>Use Lucent's retained, themed popup renderer.</summary>
    Lucent,

    /// <summary>Use a standard Windows menu when the Core menu descriptor is eligible.</summary>
    PreferNative,
}

/// <summary>Initial and minimum client-area dimensions in device-independent logical pixels.</summary>
public sealed record WindowsWindowOptions
{
    /// <summary>Gets the initial client width.</summary>
    public int Width { get; init; } = 800;

    /// <summary>Gets the initial client height.</summary>
    public int Height { get; init; } = 500;

    /// <summary>Gets the minimum client width; zero leaves it unrestricted.</summary>
    public int MinimumWidth { get; init; }

    /// <summary>Gets the minimum client height; zero leaves it unrestricted.</summary>
    public int MinimumHeight { get; init; }

    /// <summary>Gets the requested context-menu presentation.</summary>
    public WindowsMenuPresentation MenuPresentation { get; init; } = WindowsMenuPresentation.Lucent;

    /// <summary>
    /// Gets the artwork used by this window's title bar and taskbar icon.
    /// When omitted, the executable's generated <see cref="ApplicationIconDefaults.Current"/>
    /// artwork is used when one was declared.
    /// </summary>
    public ImageSource? Icon { get; init; }

    internal void Validate()
    {
        if (
            Width <= 0
            || Height <= 0
            || MinimumWidth < 0
            || MinimumHeight < 0
            || MinimumWidth > Width
            || MinimumHeight > Height
            || !Enum.IsDefined(MenuPresentation)
        )
            throw new ArgumentOutOfRangeException(
                nameof(WindowsWindowOptions),
                "Window dimensions must be positive and at least their nonnegative minimums, and menu presentation must be defined."
            );
    }

    internal static int ToWindowUnits(int logical, float dpiScale, float pixelDensity)
    {
        if (
            logical < 0
            || !float.IsFinite(dpiScale)
            || dpiScale <= 0
            || !float.IsFinite(pixelDensity)
            || pixelDensity <= 0
        )
            throw new ArgumentOutOfRangeException(nameof(logical));
        return checked((int)Math.Ceiling((double)logical * dpiScale / pixelDensity));
    }
}
