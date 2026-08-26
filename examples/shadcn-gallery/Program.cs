using Avalonia;

namespace ShadcnGallery;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var dark = args.Contains("--dark", StringComparer.Ordinal);
        if (args.Contains("--capture", StringComparer.Ordinal))
            GalleryCapture.Run(dark);
        else
            AppBuilder.Configure(() => new GalleryApp(dark)).UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args);
    }
}
