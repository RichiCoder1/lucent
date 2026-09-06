namespace Lucent.Platform.Windows;

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

    internal void Validate()
    {
        if (
            Width <= 0
            || Height <= 0
            || MinimumWidth < 0
            || MinimumHeight < 0
            || MinimumWidth > Width
            || MinimumHeight > Height
        )
            throw new ArgumentOutOfRangeException(
                nameof(WindowsWindowOptions),
                "Window dimensions must be positive and at least their nonnegative minimums."
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
