namespace Lucent.Core;

internal static class DateTimeArtwork
{
    internal static readonly ImageSource Calendar = Source(
        "calendar",
        "<rect x='3' y='4' width='14' height='13' rx='2'/><path d='M3 8h14M7 2v4m6-4v4'/>"
    );
    internal static readonly ImageSource Decrease = Source("decrease", "<path d='M4 10h12'/>");
    internal static readonly ImageSource Increase = Source(
        "increase",
        "<path d='M4 10h12M10 4v12'/>"
    );
    internal static readonly ImageSource PreviousMonth = Source(
        "previous-month",
        "<path d='m12 5-5 5 5 5'/>"
    );
    internal static readonly ImageSource NextMonth = Source(
        "next-month",
        "<path d='m8 5 5 5-5 5'/>"
    );

    private static ImageSource Source(string name, string geometry)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20' viewBox='0 0 20 20' fill='none' stroke='black' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'>"
                + geometry
                + "</svg>"
        );
        return ImageSource.FromAsset(
            new AssetReference(
                new AssetId("Lucent.Core", "date-time/" + name + ".svg"),
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
                bytes.Length,
                AssetFormat.Svg,
                () => new MemoryStream(bytes, writable: false),
                new AssetImageMetadata(20, 20)
            )
        );
    }
}
