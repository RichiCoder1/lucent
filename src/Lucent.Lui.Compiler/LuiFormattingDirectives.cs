using System;
using System.Collections.Generic;
using System.Linq;

namespace Lucent.Lui.Compiler;

// Scoped exceptions retain the authored slice, including its internal indentation.
// There is deliberately no formatter-off mode or unbounded suppression region.
internal sealed class LuiFormattingDirectives
{
    private readonly HashSet<LuiSyntaxNode> ignored = new();
    private readonly List<LuiDiagnostic> diagnostics = new();
    internal IReadOnlyList<LuiDiagnostic> Diagnostics => diagnostics;

    internal bool IsIgnored(LuiSyntaxNode node) =>
        ignored.Any(parent =>
            parent.Span.Start <= node.Span.Start && parent.Span.End >= node.Span.End
        );

    internal LuiFormattingDirectives(LuiDocumentSyntax document)
    {
        Visit(document.TopLevel);
        diagnostics.AddRange(
            LuiDirectiveIslands
                .Find(document, "// lui-format-")
                .Select(span => new LuiDiagnostic(
                    "LUI6003",
                    "Place the formatter directive before the complete C#-containing construct, not inside its C# syntax.",
                    span
                ))
        );
    }

    private void Visit<T>(IReadOnlyList<T> nodes)
        where T : LuiSyntaxNode
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (IsIgnored(node))
                continue;
            var comment = Comment(node);
            if (
                comment is not null
                && comment.StartsWith("// lui-format-", StringComparison.Ordinal)
            )
            {
                const string marker = "// lui-format-ignore:";
                if (
                    !comment.StartsWith(marker, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(comment.Substring(marker.Length))
                )
                    Invalid(
                        node,
                        "Use '// lui-format-ignore: reason' before a complete element or declaration."
                    );
                else
                {
                    var next = index + 1;
                    while (next < nodes.Count && Comment(nodes[next]) is not null)
                        next++;
                    if (
                        next == nodes.Count
                        || nodes[next] is LuiTextSyntax or LuiExpressionBodySyntax
                    )
                        Invalid(
                            node,
                            "A formatter ignore requires a following complete element or declaration in the same scope."
                        );
                    else
                        ignored.Add(nodes[next]);
                }
            }
            switch (node)
            {
                case LuiComponentSyntax component:
                    Visit(component.Body);
                    break;
                case LuiStyleSyntax style:
                    Visit(style.Members);
                    break;
                case LuiElementSyntax element:
                    Visit(element.Children);
                    break;
                case LuiIfSyntax conditional:
                    Visit(conditional.ThenBody);
                    Visit(conditional.ElseBody);
                    break;
                case LuiForEachSyntax loop:
                    Visit(loop.Body);
                    break;
                case LuiVariantGroupSyntax variant:
                    Visit(variant.Members);
                    break;
            }
        }
    }

    private void Invalid(LuiSyntaxNode node, string message) =>
        diagnostics.Add(new LuiDiagnostic("LUI6003", message, node.Span));

    private static string? Comment(LuiSyntaxNode node) =>
        node switch
        {
            LuiTopLevelCommentSyntax comment => comment.Text,
            LuiCommentSyntax comment => comment.Text,
            LuiStyleCommentSyntax comment => comment.Text,
            _ => null,
        };
}
