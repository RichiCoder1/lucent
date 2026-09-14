using System;
using System.Collections.Generic;

namespace Lucent.Lui.Compiler;

/// <summary>Line-ending policy used when formatting structurally valid <c>.lui</c> text.</summary>
public enum LuiLineEnding
{
    /// <summary>Reuse the source newline convention; use LF when none is present.</summary>
    Preserve,

    /// <summary>Write LF line endings.</summary>
    Lf,

    /// <summary>Write CRLF line endings.</summary>
    CrLf,

    /// <summary>Write CR line endings.</summary>
    Cr,
}

/// <summary>Canonical structural formatter for <c>.lui</c> documents.</summary>
/// <remarks>C# syntax and LUI share one layout policy. Invalid or unsupported source is returned unchanged; use the result API to distinguish it from clean source.</remarks>
public static partial class LuiFormatter
{
    /// <summary>Formats a structurally valid document, preserving its syntax and literal contents and applying the requested line endings.</summary>
    /// <param name="source">Complete <c>.lui</c> source text.</param>
    /// <param name="lineEnding">Line-ending policy for rewritten structure.</param>
    /// <returns>Formatted text, or the original text when parsing reports diagnostics.</returns>
    public static string Format(string source, LuiLineEnding lineEnding = LuiLineEnding.Preserve) =>
        FormatDocument(source, lineEnding).Text;

    /// <summary>Formats complete syntax boundaries contained by an authored source range.</summary>
    /// <param name="source">Complete <c>.lui</c> source text.</param>
    /// <param name="range">Half-open source range measured against <paramref name="source"/>.</param>
    /// <param name="lineEnding">Line-ending policy for rewritten structure.</param>
    /// <returns>Text with the selected node replaced, or the original text when invalid or unselectable.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="range"/> is outside <paramref name="source"/>.</exception>
    public static string FormatRange(
        string source,
        LuiSpan range,
        LuiLineEnding lineEnding = LuiLineEnding.Preserve
    ) => FormatSelection(source, range, lineEnding).Text;

    private static IEnumerable<LuiSyntaxNode> Nodes(LuiDocumentSyntax document)
    {
        foreach (var node in document.TopLevel)
        {
            yield return node;
            if (node is LuiComponentSyntax component)
                foreach (var body in Nodes(component.Body))
                    yield return body;
            else if (node is LuiStyleSyntax style)
                foreach (var member in Nodes(style.Members))
                    yield return member;
        }
    }

    private static IEnumerable<LuiSyntaxNode> Nodes(IReadOnlyList<LuiStyleMemberSyntax> members)
    {
        foreach (var member in members)
        {
            yield return member;
            if (member is LuiVariantGroupSyntax variant)
                foreach (var child in Nodes(variant.Members))
                    yield return child;
        }
    }

    private static IEnumerable<LuiSyntaxNode> Nodes(IReadOnlyList<LuiBodySyntax> body)
    {
        foreach (var node in body)
        {
            yield return node;
            if (node is LuiElementSyntax element)
                foreach (var child in Nodes(element.Children))
                    yield return child;
            else if (node is LuiIfSyntax conditional)
            {
                foreach (var child in Nodes(conditional.ThenBody))
                    yield return child;
                foreach (var child in Nodes(conditional.ElseBody))
                    yield return child;
            }
            else if (node is LuiForEachSyntax loop)
                foreach (var child in Nodes(loop.Body))
                    yield return child;
        }
    }
}
