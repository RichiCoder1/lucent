using System;
using System.Collections.Generic;
using System.Linq;

namespace Lucent.Lui.Compiler;

internal static class LuiDocumentation
{
    private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

    internal static IReadOnlyList<LuiTopLevelCommentSyntax> ForComponent(
        LuiDocumentSyntax document,
        LuiComponentSyntax component
    )
    {
        var comments = new List<LuiTopLevelCommentSyntax>();
        foreach (var node in document.TopLevel)
        {
            if (ReferenceEquals(node, component))
                break;
            if (node is LuiTopLevelCommentSyntax comment && IsDocumentation(comment))
                comments.Add(comment);
            else
                comments.Clear();
        }
        return comments;
    }

    internal static string Indent(LuiTopLevelCommentSyntax comment)
    {
        var lines = comment.Text.Split(LineSeparators, StringSplitOptions.None);
        return String.Join("\n", lines.Select(line => "    " + line)) + "\n";
    }

    private static bool IsDocumentation(LuiTopLevelCommentSyntax comment) =>
        comment.Text.StartsWith("///", StringComparison.Ordinal)
        || comment.Text.StartsWith("/**", StringComparison.Ordinal);
}
