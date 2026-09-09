using System.Security.Cryptography;
using Lucent.Core;

namespace Fixture.Library;

public static class AssetContract
{
    private static readonly byte[] BrandBytes =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"12\" viewBox=\"0 0 24 12\"><path d=\"M0 0h24v12H0z\"/></svg>"u8.ToArray();

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADUlEQVR42mNk+M/wHwAF/gL+3j37WQAAAABJRU5ErkJggg=="
    );

    private static readonly byte[] JpegBytes = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAf/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAHlAP/EABQQAQAAAAAAAAAAAAAAAAAAABD/2gAIAQEAAT8Af//EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQIBAT8Af//EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQMBAT8Af//Z"
    );

    private static readonly byte[] BinaryBytes =
    [
        0x00,
        0x01,
        0x02,
        0x10,
        0x20,
        0x40,
        0x7f,
        0x80,
        0xfe,
        0xff,
        0x0d,
        0x0a,
    ];

    public static void Run()
    {
        AssertImage(
            Fixture.Resources.Catalog.Images.Brand,
            AssetFormat.Svg,
            "images/brand.svg",
            BrandBytes,
            width: 24,
            height: 12,
            density: 1,
            relativeWidth: null,
            relativeHeight: null
        );
        AssertImage(
            Fixture.Resources.Catalog.Pixels.Tiny,
            AssetFormat.Png,
            "pixels/tiny.png",
            PngBytes,
            width: .5f,
            height: .5f,
            density: 2,
            relativeWidth: null,
            relativeHeight: null
        );
        AssertImage(
            Fixture.Branding.Catalog.Photos.TinyJpeg,
            AssetFormat.Jpeg,
            "photos/tiny.jpeg",
            JpegBytes,
            width: .5f,
            height: .5f,
            density: 2,
            relativeWidth: null,
            relativeHeight: null
        );

        var binary = Fixture.Binary.Catalog.Payload.Binary;
        Require(binary.Id.Domain == "Fixture.Library.Domain", "Binary domain changed.");
        Require(binary.Id.Path == "data/payload.bin", "Binary logical path changed.");
        Require(binary.Format == AssetFormat.Binary, "Binary accessor was emitted as an image.");
        Require(binary.Image is null, "Binary accessor unexpectedly carries image metadata.");
        AssertBytes(binary, BinaryBytes, "binary asset");

        Console.WriteLine("assets: PASS");
    }

    private static void AssertImage(
        ImageSource source,
        AssetFormat format,
        string path,
        byte[] expected,
        float width,
        float height,
        float density,
        float? relativeWidth,
        float? relativeHeight
    )
    {
        var asset =
            source.PackagedAsset
            ?? throw new InvalidOperationException("Image has no packaged asset.");
        Require(asset.Id.Domain == "Fixture.Library.Domain", $"Image domain changed for {path}.");
        Require(asset.Id.Path == path, $"Image logical path changed for {path}.");
        Require(asset.Format == format, $"Image format changed for {path}.");
        Require(asset.Image is not null, $"Image metadata is missing for {path}.");
        var metadata = source.Metadata;
        Require(metadata.Width == width, $"Image width changed for {path}: {metadata.Width}.");
        Require(metadata.Height == height, $"Image height changed for {path}: {metadata.Height}.");
        Require(
            metadata.Density == density,
            $"Image density changed for {path}: {metadata.Density}."
        );
        Require(metadata.RelativeWidth == relativeWidth, $"Relative width changed for {path}.");
        Require(metadata.RelativeHeight == relativeHeight, $"Relative height changed for {path}.");
        AssertBytes(asset, expected, path);
    }

    private static void AssertBytes(AssetReference asset, byte[] expected, string label)
    {
        Require(asset.ByteLength == expected.LongLength, $"Encoded length changed for {label}.");
        var expectedHash = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
        Require(asset.ContentHash == expectedHash, $"Encoded hash changed for {label}.");
        using var stream = asset.OpenRead();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        Require(
            copy.ToArray().AsSpan().SequenceEqual(expected),
            $"Encoded bytes changed for {label}."
        );
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
