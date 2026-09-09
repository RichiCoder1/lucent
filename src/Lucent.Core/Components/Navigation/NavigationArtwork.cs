namespace Lucent.Core;

internal static class NavigationArtwork
{
    internal static readonly ImageSource Collapsed = Source(
        "collapsed",
        "<path d='m8 5 6 5-6 5'/>"
    );
    internal static readonly ImageSource Expanded = Source("expanded", "<path d='m5 7 5 6 5-6'/>");

    private static ImageSource Source(string name, string geometry)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20' viewBox='0 0 20 20' fill='none' stroke='black' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>"
                + geometry
                + "</svg>"
        );
        return ImageSource.FromAsset(
            new AssetReference(
                new AssetId("Lucent.Core", "navigation/" + name + ".svg"),
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
                bytes.Length,
                AssetFormat.Svg,
                () => new MemoryStream(bytes, writable: false),
                new AssetImageMetadata(20, 20)
            )
        );
    }
}
