using System.Text;
using Lucent.Compiler;

namespace Lucent.Compiler.CodeGeneration;

internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private readonly List<GeneratedProvenance> _provenance = [];
    private int _indent;
    private readonly List<(string Path, SourceSpan Span)> _mappings = [];

    public IReadOnlyList<GeneratedProvenance> Provenance => _provenance;

    public void Indent() => _indent++;

    public void Unindent() => _indent--;

    /// <summary>Associates subsequently emitted non-directive text with one Lucent span.</summary>
    public void Map(string sourcePath, SourceSpan span)
    {
        _mappings.Clear();
        _mappings.Add((sourcePath, span));
    }

    /// <summary>Associates the current generated statement with another source origin.</summary>
    public void MapAdditional(string sourcePath, SourceSpan span)
    {
        if (!_mappings.Contains((sourcePath, span)))
            _mappings.Add((sourcePath, span));
    }

    /// <summary>Stops provenance without emitting a generated directive.</summary>
    public void Unmap() => _mappings.Clear();

    public void Line(string text = "")
    {
        var start = _builder.Length;
        if (text.Length > 0)
        {
            _builder.Append(' ', _indent * 4);
            _builder.Append(text);
            if (_mappings.Count > 0 && !text.StartsWith("#line", StringComparison.Ordinal))
            {
                foreach (var mapping in _mappings)
                    _provenance.Add(new GeneratedProvenance(mapping.Path, mapping.Span,
                        start, _builder.Length - start));
            }
        }

        _builder.Append('\n');
        if (text.Trim().Equals("#line default", StringComparison.Ordinal))
            Unmap();
    }

    public override string ToString() => _builder.ToString();
}

internal sealed record GeneratedProvenance(string SourcePath, SourceSpan SourceSpan,
    int GeneratedStart, int GeneratedLength);
