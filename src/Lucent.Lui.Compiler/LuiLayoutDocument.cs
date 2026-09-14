using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Lucent.Lui.Compiler.Tests")]

namespace Lucent.Lui.Compiler;

// A bounded grouped document. Each group caches its flat width, so choosing a
// layout never searches an exponential set of alternatives. Children of a broken
// group can still choose their own compact layout.
internal sealed class LuiLayoutDocument
{
    private enum Kind
    {
        Text,
        Line,
        Concat,
        Indent,
        Group,
    }

    private readonly Kind kind;
    private readonly string text;
    private readonly LuiLayoutDocument[] children;
    private readonly bool forced;
    internal int FlatWidth { get; }

    private LuiLayoutDocument(
        Kind kind,
        string text,
        LuiLayoutDocument[] children,
        bool forced,
        int width
    )
    {
        this.kind = kind;
        this.text = text;
        this.children = children;
        this.forced = forced;
        FlatWidth = width;
    }

    internal static LuiLayoutDocument Text(string value) =>
        new(
            Kind.Text,
            value,
            [],
            false,
            value.IndexOfAny(['\r', '\n']) >= 0 ? int.MaxValue : value.Length
        );

    internal static LuiLayoutDocument Line { get; } = new(Kind.Line, " ", [], false, 1);
    internal static LuiLayoutDocument SoftLine { get; } = new(Kind.Line, "", [], false, 0);
    internal static LuiLayoutDocument HardLine { get; } =
        new(Kind.Line, "", [], true, int.MaxValue);

    internal static LuiLayoutDocument Concat(params LuiLayoutDocument[] items) =>
        new(
            Kind.Concat,
            "",
            items,
            false,
            (int)Math.Min(int.MaxValue, items.Sum(item => (long)item.FlatWidth))
        );

    internal static LuiLayoutDocument Join(
        LuiLayoutDocument separator,
        IEnumerable<LuiLayoutDocument> items
    )
    {
        var values = new List<LuiLayoutDocument>();
        foreach (var item in items)
        {
            if (values.Count != 0)
                values.Add(separator);
            values.Add(item);
        }
        return Concat(values.ToArray());
    }

    internal static LuiLayoutDocument Group(LuiLayoutDocument item) =>
        new(Kind.Group, "", [item], false, item.FlatWidth);

    internal static LuiLayoutDocument Indent(LuiLayoutDocument item) =>
        new(Kind.Indent, "", [item], false, item.FlatWidth);

    internal string Render(
        int width,
        string indent = "    ",
        string newline = "\n",
        int tabWidth = 4
    )
    {
        var result = new StringBuilder();
        var pending = new Stack<(LuiLayoutDocument Document, int Indent, bool Flat)>();
        pending.Push((this, 0, false));
        var column = 0;
        while (pending.Count != 0)
        {
            var (document, level, flat) = pending.Pop();
            switch (document.kind)
            {
                case Kind.Text:
                    result.Append(document.text);
                    foreach (var character in document.text)
                        column =
                            character is '\r' or '\n' ? 0
                            : character == '\t' ? column + tabWidth - column % tabWidth
                            : column + 1;
                    break;
                case Kind.Line:
                    if (flat && !document.forced)
                    {
                        result.Append(document.text);
                        column += document.text.Length;
                    }
                    else
                    {
                        result.Append(newline);
                        for (var index = 0; index < level; index++)
                            result.Append(indent);
                        column = level * (indent == "\t" ? tabWidth : indent.Length);
                    }
                    break;
                case Kind.Concat:
                    for (var index = document.children.Length - 1; index >= 0; index--)
                        pending.Push((document.children[index], level, flat));
                    break;
                case Kind.Indent:
                    pending.Push((document.children[0], level + 1, flat));
                    break;
                case Kind.Group:
                    pending.Push(
                        (document.children[0], level, flat || document.FlatWidth <= width - column)
                    );
                    break;
            }
        }
        return result.ToString();
    }
}
