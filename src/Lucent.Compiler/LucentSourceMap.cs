using System.Security.Cryptography;
using System.Text;
using Lucent.Compiler.CodeGeneration;

namespace Lucent.Compiler;

/// <summary>
/// Versioned, emitter-provenance mappings between Lucent and generated C#.
/// Scaffolding is intentionally absent: only writer spans explicitly marked by
/// <c>EmitLineMapping</c> participate.
/// </summary>
public sealed record LucentSourceMap(
    string LucentHash,
    string GeneratedHash,
    IReadOnlyList<LucentSourceMapEntry> Entries)
{
    internal static LucentSourceMap Create(
        string source,
        GeneratedCSharp emitted,
        string sourcePath,
        string? generatedPath = null)
    {
        // This is a logical generated-document identity, not a discovery step.
        // The caller that writes an output may provide its real path; editor-only
        // compilation uses the deterministic sibling name. Never inspect obj or
        // another project's files while answering a source-map request.
        generatedPath ??= Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(sourcePath))!,
            Path.GetFileNameWithoutExtension(sourcePath) + "Component.g.cs");
        var generatedStarts = LineStarts(emitted.Text);
        var sourceStarts = LineStarts(source);
        var generatedUri = NormalizeUri(generatedPath);
        var entries = emitted.Provenance
            .Where(mark => mark.GeneratedLength > 0 && mark.SourceSpan.Length >= 0)
            .Select(mark => new LucentSourceMapEntry(
                generatedUri,
                NormalizeUri(mark.SourcePath),
                Range(generatedStarts, emitted.Text.Length, mark.GeneratedStart, mark.GeneratedLength),
                Range(sourceStarts, source.Length, mark.SourceSpan.Start, mark.SourceSpan.Length)))
            .Distinct()
            .OrderBy(entry => entry.GeneratedUri, StringComparer.Ordinal)
            .ThenBy(entry => entry.GeneratedRange.StartLine)
            .ThenBy(entry => entry.GeneratedRange.StartCharacter)
            .ThenBy(entry => entry.GeneratedRange.EndLine)
            .ThenBy(entry => entry.GeneratedRange.EndCharacter)
            .ThenBy(entry => entry.LucentUri, StringComparer.Ordinal)
            .ThenBy(entry => entry.LucentRange.StartLine)
            .ThenBy(entry => entry.LucentRange.StartCharacter)
            .ToArray();
        return new LucentSourceMap(Hash(source), Hash(emitted.Text), entries);
    }

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
            if (text[index] == '\n') starts.Add(index + 1);
        return starts.ToArray();
    }

    private static LucentSourceMapRange Range(int[] starts, int textLength, int start, int length)
    {
        // The final line start alone is not the text length. Callers pass source
        // offsets derived from the actual buffers, so only clamp the lower bound
        // here and preserve half-open UTF-16 spans exactly.
        var boundedStart = Math.Clamp(start, 0, textLength);
        var boundedEnd = Math.Clamp(start + Math.Max(0, length), boundedStart, textLength);
        var (startLine, startCharacter) = Position(starts, boundedStart);
        var (endLine, endCharacter) = Position(starts, boundedEnd);
        return new LucentSourceMapRange(startLine, startCharacter, endLine, endCharacter);
    }

    private static (int Line, int Character) Position(int[] starts, int offset)
    {
        var index = Array.BinarySearch(starts, offset);
        if (index < 0) index = ~index - 1;
        index = Math.Max(0, index);
        return (index, offset - starts[index]);
    }

    private static string NormalizeUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record LucentSourceMapRange(int StartLine, int StartCharacter, int EndLine, int EndCharacter);

public sealed record LucentSourceMapEntry(
    string GeneratedUri,
    string LucentUri,
    LucentSourceMapRange GeneratedRange,
    LucentSourceMapRange LucentRange);
