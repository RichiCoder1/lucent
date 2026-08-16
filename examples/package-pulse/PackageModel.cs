namespace Lucent.Examples.PackagePulse;

public sealed record PackageInfo(
    string Id,
    string Name,
    string Version,
    // Test
    string Description);

public static class PackageCatalog
{
    private static readonly PackageInfo[] Packages =
    [
        new("lucent.ui", "Lucent.UI", "0.8.0", "Compiled declarative UI experiments for .NET."),
        new("avalonia", "Avalonia", "12.1.1", "Cross-platform UI framework for .NET."),
        new("reactive.core", "Reactive.Core", "2.1.4", "Small reactive primitives for application state."),
        new("community.toolkit", "CommunityToolkit.Mvvm", "8.4.0", "MVVM helpers and source generators."),
        new("humanizer", "Humanizer", "2.14.1", "Human-friendly text and date formatting."),
    ];

    public static PackageInfo[] Placeholders { get; } =
    [
        new("loading-1", "Loading package index…", "", "The first result will arrive shortly."),
        new("loading-2", "Preparing local metadata…", "", "Existing results remain visible during later searches."),
    ];

    public static async Task<PackageInfo[]> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        await Task.Delay(850, cancellationToken);
        if (query.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The simulated package feed is unavailable. Try another query.");
        }

        return Packages
            .Where(package =>
                package.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                package.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
