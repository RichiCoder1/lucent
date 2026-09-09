using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class JpegMemoryAdmissionTests
{
    [TestMethod]
    public void UsesPinnedBaselineStripEstimateForKnownRgbHeader()
    {
        var encoded = Header(0xC0, 600, 397);

        Assert.AreEqual(
            JpegMemoryProfile.BaselineRgb,
            JpegMemoryAdmission.ReadProfile(encoded, Info(600, 397))
        );
        Assert.AreEqual(
            34L * 600,
            JpegMemoryAdmission.EstimateScratchBytes(encoded, Info(600, 397))
        );
    }

    [TestMethod]
    public void UsesPinnedProgressiveCoefficientEstimateForKnownRgbHeader()
    {
        var encoded = Header(0xC2, 32, 23);

        Assert.AreEqual(
            JpegMemoryProfile.ProgressiveRgb,
            JpegMemoryAdmission.ReadProfile(encoded, Info(32, 23))
        );
        Assert.AreEqual(
            6L * 32 * 23 + 34 * 32,
            JpegMemoryAdmission.EstimateScratchBytes(encoded, Info(32, 23))
        );
    }

    [TestMethod]
    public void AcceptsStandardSubsamplingProfiles()
    {
        foreach (var sampling in new byte[] { 0x21, 0x22 })
        {
            var encoded = Header(0xC0, 650, 470, samplings: [sampling, 0x11, 0x11]);

            Assert.AreEqual(
                JpegMemoryProfile.BaselineRgb,
                JpegMemoryAdmission.ReadProfile(encoded, Info(650, 470))
            );
        }
    }

    [TestMethod]
    public void KeepsFourComponentCmykHeaderOnConservativeFallback()
    {
        var encoded = Header(0xC0, 600, 397, componentCount: 4);

        Assert.AreEqual(
            JpegMemoryProfile.Unknown,
            JpegMemoryAdmission.ReadProfile(encoded, Info(600, 397))
        );
        Assert.AreEqual(
            8L * 600 * 397,
            JpegMemoryAdmission.EstimateScratchBytes(encoded, Info(600, 397))
        );
    }

    [TestMethod]
    public void SequentialMultiscanRetainsFullSourceMemoryAdmission()
    {
        foreach (var scanComponents in new[] { 1, 2 })
        {
            var encoded = Header(0xC0, 6000, 6000, scanComponentCount: scanComponents);
            Assert.AreEqual(
                JpegMemoryProfile.Unknown,
                JpegMemoryAdmission.ReadProfile(encoded, Info(6000, 6000))
            );
            Assert.AreEqual(
                8L * 6000 * 6000,
                JpegMemoryAdmission.EstimateScratchBytes(encoded, Info(6000, 6000)),
                "A sequential multi-scan JPEG needs a full coefficient buffer, not only strips."
            );
        }
    }

    [TestMethod]
    public void KeepsMalformedOrMismatchedHeaderOnConservativeFallback()
    {
        var encoded = Header(0xC2, 650, 470);
        var truncated = encoded[..^2];
        var truncatedScan = encoded[..^1];
        var invalidScanLength = encoded.ToArray();
        var scanOffset = Array.IndexOf(encoded, (byte)0xDA) + 1;
        invalidScanLength[scanOffset] = 0;
        invalidScanLength[scanOffset + 1] = 1;

        Assert.AreEqual(
            JpegMemoryProfile.Unknown,
            JpegMemoryAdmission.ReadProfile(truncated, Info(650, 470))
        );
        Assert.AreEqual(
            JpegMemoryProfile.Unknown,
            JpegMemoryAdmission.ReadProfile(truncatedScan, Info(650, 470))
        );
        Assert.AreEqual(
            JpegMemoryProfile.Unknown,
            JpegMemoryAdmission.ReadProfile(invalidScanLength, Info(650, 470))
        );
        Assert.AreEqual(
            8L * 650 * 470,
            JpegMemoryAdmission.EstimateScratchBytes(truncated, Info(650, 470))
        );
        Assert.AreEqual(
            JpegMemoryProfile.Unknown,
            JpegMemoryAdmission.ReadProfile(encoded, Info(649, 470))
        );
    }

    [TestMethod]
    public void KeepsNonHuffmanAndInvalidSamplingOnConservativeFallback()
    {
        var arithmetic = Header(0xCA, 650, 470);
        var invalidSampling = Header(0xC2, 650, 470, sampling: 0x00);
        var unusualSampling = Header(0xC0, 650, 470, samplings: [0x41, 0x11, 0x11]);

        Assert.AreEqual(
            JpegMemoryProfile.Unknown,
            JpegMemoryAdmission.ReadProfile(arithmetic, Info(650, 470))
        );
        Assert.AreEqual(
            JpegMemoryProfile.Unknown,
            JpegMemoryAdmission.ReadProfile(invalidSampling, Info(650, 470))
        );
        Assert.AreEqual(
            JpegMemoryProfile.Unknown,
            JpegMemoryAdmission.ReadProfile(unusualSampling, Info(650, 470))
        );
    }

    private static SKImageInfo Info(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

    private static byte[] Header(
        byte sofMarker,
        int width,
        int height,
        int componentCount = 3,
        byte sampling = 0x11,
        byte[]? samplings = null,
        int? scanComponentCount = null
    )
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        var length = checked(8 + componentCount * 3);
        bytes.AddRange([0xFF, sofMarker, (byte)(length >> 8), (byte)length, 8]);
        bytes.AddRange([(byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width]);
        bytes.Add((byte)componentCount);
        for (var component = 0; component < componentCount; component++)
            bytes.AddRange([
                (byte)(component + 1),
                samplings is { Length: > 0 } ? samplings[component] : sampling,
                0,
            ]);
        var scanComponents = scanComponentCount ?? componentCount;
        bytes.AddRange([0xFF, 0xDA, 0x00, (byte)(6 + 2 * scanComponents), (byte)scanComponents]);
        for (var component = 0; component < scanComponents; component++)
            bytes.AddRange([(byte)(component + 1), 0]);
        bytes.AddRange([0, sofMarker == 0xC2 ? (byte)0 : (byte)63, 0, 0xFF, 0xD9]);
        return bytes.ToArray();
    }
}
