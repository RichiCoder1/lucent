namespace Lucent.Lui.LanguageServer;

// LSP lines end at CRLF, CR or LF. Other Unicode separators remain UTF-16 characters.
internal sealed class ProtocolLineIndex
{
    private readonly int[] starts;
    private readonly int[] ends;
    private readonly int length;

    internal int Length => length;

    internal ProtocolLineIndex(string text)
    {
        length = text.Length;
        var lineStarts = new List<int> { 0 };
        var lineEnds = new List<int>();
        for (var offset = 0; offset < text.Length; offset++)
        {
            if (text[offset] is not ('\r' or '\n'))
                continue;
            lineEnds.Add(offset);
            if (text[offset] == '\r' && offset + 1 < text.Length && text[offset + 1] == '\n')
                offset++;
            lineStarts.Add(offset + 1);
        }
        lineEnds.Add(text.Length);
        starts = lineStarts.ToArray();
        ends = lineEnds.ToArray();
    }

    internal int Offset(int line, int character)
    {
        if (
            line < 0
            || line >= starts.Length
            || character < 0
            || character > ends[line] - starts[line]
        )
            throw new ArgumentOutOfRangeException(
                nameof(line),
                "The position is outside the document."
            );
        return starts[line] + character;
    }

    internal (int Line, int Character) Position(int offset)
    {
        if (offset < 0 || offset > length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var line = Array.BinarySearch(starts, offset);
        if (line < 0)
            line = ~line - 1;
        return (line, Math.Min(offset, ends[line]) - starts[line]);
    }

    internal int LineEnd(int line) => ends[line];
}
