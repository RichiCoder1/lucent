using System;

namespace Lucent.Lui.Compiler;

/// <summary>The three configurable dimensions of canonical source formatting.</summary>
public sealed class LuiFormattingOptions
{
    /// <summary>Creates immutable indentation, soft-width and structural line-ending settings.</summary>
    public LuiFormattingOptions(
        int indentSize = 4,
        bool useTabs = false,
        int lineWidth = 100,
        LuiLineEnding lineEnding = LuiLineEnding.Preserve,
        int? tabWidth = null
    )
    {
        if (indentSize < 1 || indentSize > 16)
            throw new ArgumentOutOfRangeException(nameof(indentSize));
        if (lineWidth < 20)
            throw new ArgumentOutOfRangeException(nameof(lineWidth));
        if (tabWidth is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(tabWidth));
        if (!Enum.IsDefined(typeof(LuiLineEnding), lineEnding))
            throw new ArgumentOutOfRangeException(nameof(lineEnding));
        IndentSize = indentSize;
        UseTabs = useTabs;
        LineWidth = lineWidth;
        LineEnding = lineEnding;
        TabWidth = tabWidth ?? indentSize;
    }

    /// <summary>Visual columns per indentation level.</summary>
    public int IndentSize { get; }

    /// <summary>Whether indentation uses tabs where they fit the requested columns.</summary>
    public bool UseTabs { get; }

    /// <summary>Soft line width; int.MaxValue means unlimited.</summary>
    public int LineWidth { get; }

    /// <summary>Structural newline convention; literal contents are preserved.</summary>
    public LuiLineEnding LineEnding { get; }

    /// <summary>Columns per tab stop.</summary>
    public int TabWidth { get; }

    internal string Padding(int level)
    {
        var columns = level * IndentSize;
        return UseTabs
            ? new string('\t', columns / TabWidth) + new string(' ', columns % TabWidth)
            : new string(' ', columns);
    }

    internal string Newline(string source) =>
        LineEnding switch
        {
            LuiLineEnding.Lf => "\n",
            LuiLineEnding.CrLf => "\r\n",
            LuiLineEnding.Cr => "\r",
            _ => source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n"
            : source.IndexOf('\n') >= 0 ? "\n"
            : source.IndexOf('\r') >= 0 ? "\r"
            : "\n",
        };
}
