using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lucent.Core;

namespace Lucent.Testing;

/// <summary>A deterministic fixed-metric shaper for tests that do not assert font-specific geometry.</summary>
public sealed class HeadlessTextShaper : ITextShaper
{
    /// <inheritdoc />
    public ShapedText Shape(TextMeasureRequest request)
    {
        request.Validate();
        if (request.Text.Length == 0)
            return new(
                Identity(request),
                0,
                0,
                [],
                [],
                false,
                request.InlineConstraint,
                request.BlockConstraint
            );

        var advance = request.FontSize * 0.6f;
        var glyphs = new List<ShapedGlyph>();
        var x = 0f;
        var offset = 0;
        foreach (var rune in request.Text.EnumerateRunes())
        {
            glyphs.Add(new(1, (uint)offset, x, 0, advance, 0, 0));
            x += advance;
            offset += rune.Utf16SequenceLength;
        }

        var run = new ShapedRun(
            Identity(request) + ":run",
            "Lucent Headless",
            (int)request.FontWeight,
            5,
            0,
            "lucent-headless-fixed-metrics",
            0,
            "lucent-headless-fixed-metrics#0",
            request.Direction,
            request.Language,
            request.FontSize,
            0,
            request.FontSize,
            -request.FontSize,
            0,
            x,
            glyphs
        );
        var line = new ParagraphLine(
            0,
            request.Text.Length,
            0,
            request.FontSize,
            -request.FontSize,
            0,
            0,
            x,
            0,
            false
        );
        return new(
            Identity(request),
            x,
            request.FontSize,
            [run],
            [line],
            request.InlineConstraint.IsBounded && x > request.InlineConstraint.Limit,
            request.InlineConstraint,
            request.BlockConstraint
        );
    }

    private static string Identity(TextMeasureRequest request) =>
        "headless:"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Text)))
        + ":"
        + request.FontSize.ToString("R", CultureInfo.InvariantCulture);
}
