namespace Lucent.Compiler.Parsing;

internal sealed class SourceDocument
{
    private readonly int[] _lineStarts;

    public SourceDocument(string text, string path)
    {
        Text = text;
        Path = path;

        var lineStarts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                lineStarts.Add(index + 1);
            }
        }

        _lineStarts = lineStarts.ToArray();
    }

    public string Text { get; }

    public string Path { get; }

    public (int Line, int Column) GetLineAndColumn(int position)
    {
        position = Math.Clamp(position, 0, Text.Length);
        var lineIndex = Array.BinarySearch(_lineStarts, position);
        if (lineIndex < 0)
        {
            lineIndex = ~lineIndex - 1;
        }

        return (lineIndex + 1, position - _lineStarts[lineIndex] + 1);
    }
}
