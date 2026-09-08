using Lucent.Core;
using Microsoft.Extensions.Time.Testing;

namespace Lucent.Testing;

/// <summary>Configures one isolated headless Lucent application.</summary>
public sealed class HeadlessApplicationOptions
{
    /// <summary>Gets or sets the diagnostic application title.</summary>
    public string Title { get; set; } = "Lucent headless test";

    /// <summary>Gets or sets the initial logical viewport.</summary>
    public LayoutViewport Viewport { get; set; } = new(1024, 768, 1);

    /// <summary>Gets or sets the appearance used to select the initial theme.</summary>
    public ThemeAppearance Appearance { get; set; } = ThemeAppearance.Light;

    /// <summary>Gets or sets the theme factory evaluated on the application owner thread.</summary>
    public Func<ThemeAppearance, Theme> ThemeFactory { get; set; } =
        appearance =>
            appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast
            : appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark
            : ControlThemes.Light;

    /// <summary>Gets or sets the text-shaper factory evaluated on the application owner thread.</summary>
    public Func<ITextShaper> TextShaperFactory { get; set; } =
        static () => new HeadlessTextShaper();

    /// <summary>Gets or sets the deterministic clock factory evaluated on the application owner thread.</summary>
    public Func<FakeTimeProvider> TimeProviderFactory { get; set; } = static () => new();

    /// <summary>Gets or sets the maximum work items processed while settling one operation.</summary>
    public int MaximumWorkItems { get; set; } = 10_000;

    internal HeadlessApplicationOptions Snapshot()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        Viewport.Validate();
        ArgumentNullException.ThrowIfNull(ThemeFactory);
        ArgumentNullException.ThrowIfNull(TextShaperFactory);
        ArgumentNullException.ThrowIfNull(TimeProviderFactory);
        if (MaximumWorkItems <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(MaximumWorkItems),
                "The headless work limit must be positive."
            );
        return new()
        {
            Title = Title,
            Viewport = Viewport,
            Appearance = Appearance,
            ThemeFactory = ThemeFactory,
            TextShaperFactory = TextShaperFactory,
            TimeProviderFactory = TimeProviderFactory,
            MaximumWorkItems = MaximumWorkItems,
        };
    }
}
