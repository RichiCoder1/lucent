using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lucent.Lui.Compiler;

/// <summary>Line-ending policy used when formatting structurally valid <c>.lui</c> text.</summary>
public enum LuiLineEnding
{
    /// <summary>Reuse CRLF when present in the source; otherwise use LF.</summary>
    Preserve,

    /// <summary>Write LF line endings.</summary>
    Lf,

    /// <summary>Write CRLF line endings.</summary>
    CrLf,
}

/// <summary>Canonical structural formatter for <c>.lui</c> documents.</summary>
/// <remarks>C# island text is never reformatted; invalid input and ranges without a complete node are returned unchanged.</remarks>
public static class LuiFormatter
{
    /// <summary>Formats a structurally valid document, preserving its C# island text and applying the requested line endings.</summary>
    /// <param name="source">Complete <c>.lui</c> source text.</param>
    /// <param name="lineEnding">Line-ending policy for rewritten structure.</param>
    /// <returns>Formatted text, or the original text when parsing reports diagnostics.</returns>
    public static string Format(string source, LuiLineEnding lineEnding = LuiLineEnding.Preserve)
    {
        var document = LuiParser.Parse(source);
        if (document.Diagnostics.Count != 0)
            return source;
        var newline =
            lineEnding == LuiLineEnding.CrLf ? "\r\n"
            : lineEnding == LuiLineEnding.Lf ? "\n"
            : source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n"
            : "\n";
        var output = new StringBuilder();
        foreach (var node in document.TopLevel)
        {
            if (node is LuiNamespaceSyntax ns)
                output.Append("namespace ").Append(ns.Value).Append(';').Append(newline);
            else if (node is LuiUsingSyntax use)
                output.Append("using ").Append(use.Value).Append(';').Append(newline);
            else if (node is LuiTopLevelCommentSyntax comment)
                output.Append(comment.Text).Append(newline);
            else if (node is LuiStyleSyntax style)
            {
                Style(output, style, 0, newline);
                output.Append(newline);
            }
            else if (node is LuiComponentSyntax component)
                Component(output, component, newline);
        }
        return output.ToString();
    }

    /// <summary>Formats the largest complete syntax node contained by an authored source range.</summary>
    /// <param name="source">Complete <c>.lui</c> source text.</param>
    /// <param name="range">Half-open source range measured against <paramref name="source"/>.</param>
    /// <param name="lineEnding">Line-ending policy for rewritten structure.</param>
    /// <returns>Text with the selected node replaced, or the original text when invalid or unselectable.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="range"/> is outside <paramref name="source"/>.</exception>
    public static string FormatRange(
        string source,
        LuiSpan range,
        LuiLineEnding lineEnding = LuiLineEnding.Preserve
    )
    {
        if (range.Start < 0 || range.End > source.Length)
            throw new ArgumentOutOfRangeException(nameof(range));
        var document = LuiParser.Parse(source);
        if (document.Diagnostics.Count != 0)
            return source;
        LuiSyntaxNode? selected = Nodes(document)
            .Where(node => node.Span.Start >= range.Start && node.Span.End <= range.End)
            .OrderByDescending(node => node.Span.Length)
            .FirstOrDefault();
        if (selected == null)
            return source;
        var newline =
            lineEnding == LuiLineEnding.CrLf ? "\r\n"
            : lineEnding == LuiLineEnding.Lf ? "\n"
            : source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n"
            : "\n";
        var output = new StringBuilder();
        if (selected is LuiTopLevelCommentSyntax comment)
            output.Append(comment.Text);
        else if (selected is LuiNamespaceSyntax ns)
            output.Append("namespace ").Append(ns.Value).Append(';');
        else if (selected is LuiUsingSyntax use)
            output.Append("using ").Append(use.Value).Append(';');
        else if (selected is LuiComponentSyntax component)
            Component(output, component, newline);
        else if (selected is LuiStyleSyntax style)
            Style(output, style, 0, newline);
        else
        {
            var indent = SourceIndent(source, selected.Span.Start);
            Body(output, new[] { (LuiBodySyntax)selected }, indent, newline);
            var prefixLength = Math.Min(indent * 4, output.Length);
            if (
                prefixLength != 0
                && output.ToString(0, prefixLength).All(character => character == ' ')
            )
                output.Remove(0, prefixLength);
        }
        if (
            output.Length >= newline.Length
            && output.ToString(output.Length - newline.Length, newline.Length) == newline
        )
            output.Length -= newline.Length;
        return source.Substring(0, selected.Span.Start)
            + output
            + source.Substring(selected.Span.End);
    }

    private static void Component(StringBuilder output, LuiComponentSyntax node, string nl)
    {
        output
            .Append(node.Accessibility.IsMissing ? "internal" : node.Accessibility.Text)
            .Append(" component ")
            .Append(node.Name.Text)
            .Append('(');
        for (var i = 0; i < node.Parameters.Count; i++)
        {
            if (i != 0)
                output.Append(", ");
            output.Append(node.Parameters[i].DeclarationText);
        }
        output.Append(") {").Append(nl);
        Body(output, node.Body, 1, nl);
        output.Append('}').Append(nl);
    }

    private static void Body(
        StringBuilder output,
        IReadOnlyList<LuiBodySyntax> body,
        int indent,
        string nl
    )
    {
        foreach (var node in body)
        {
            Pad(output, indent);
            if (node is LuiTextSyntax text)
                output.Append(text.Text).Append(nl);
            else if (node is LuiMemberSyntax member)
                output.Append(member.Text).Append(nl);
            else if (node is LuiExpressionBodySyntax expression)
                output.Append('{').Append(expression.Text).Append('}').Append(nl);
            else if (node is LuiCommentSyntax comment)
                output.Append(comment.Text).Append(nl);
            else if (node is LuiElementSyntax element)
                Element(output, element, indent, nl);
            else if (node is LuiIfSyntax conditional)
            {
                output.Append("if (").Append(conditional.Condition.Text).Append(") {").Append(nl);
                Body(output, conditional.ThenBody, indent + 1, nl);
                Pad(output, indent);
                output.Append('}');
                if (!conditional.ElseKeyword.IsMissing)
                {
                    output.Append(" else {").Append(nl);
                    Body(output, conditional.ElseBody, indent + 1, nl);
                    Pad(output, indent);
                    output.Append('}');
                }
                output.Append(nl);
            }
            else if (node is LuiForEachSyntax loop)
            {
                output
                    .Append("foreach (var ")
                    .Append(loop.Variable.Text)
                    .Append(" in ")
                    .Append(loop.Source.Text)
                    .Append(") keyed by ")
                    .Append(loop.Key.Text)
                    .Append(" {")
                    .Append(nl);
                Body(output, loop.Body, indent + 1, nl);
                Pad(output, indent);
                output.Append('}').Append(nl);
            }
        }
    }

    private static void Element(StringBuilder output, LuiElementSyntax node, int indent, string nl)
    {
        output.Append('<').Append(node.Name.Text);
        foreach (var attribute in node.Attributes)
        {
            output.Append(' ').Append(attribute.Name.Text).Append('=');
            Value(output, attribute.Value);
        }
        if (node.SelfClosing)
        {
            output.Append(" />").Append(nl);
            return;
        }
        if (node.Children.Count == 1 && node.Children[0] is LuiTextSyntax scalar)
        {
            output
                .Append('>')
                .Append(scalar.Text)
                .Append("</")
                .Append(node.CloseName.Text)
                .Append('>')
                .Append(nl);
            return;
        }
        if (node.Children.Count == 1 && node.Children[0] is LuiExpressionBodySyntax expression)
        {
            output
                .Append('>')
                .Append('{')
                .Append(expression.Text)
                .Append('}')
                .Append("</")
                .Append(node.CloseName.Text)
                .Append('>')
                .Append(nl);
            return;
        }
        output.Append('>').Append(nl);
        Body(output, node.Children, indent + 1, nl);
        Pad(output, indent);
        output.Append("</").Append(node.CloseName.Text).Append('>').Append(nl);
    }

    private static void Style(StringBuilder output, LuiStyleSyntax style, int indent, string nl)
    {
        Pad(output, indent);
        output.Append("style ").Append(style.Name.Text);
        if (!style.OpenParameters.IsMissing)
        {
            output.Append('(');
            for (var index = 0; index < style.Parameters.Count; index++)
            {
                if (index != 0)
                    output.Append(", ");
                output.Append(style.Parameters[index].DeclarationText);
            }
            output.Append(')');
        }
        output.Append(" {").Append(nl);
        StyleMembers(output, style.Members, indent + 1, nl);
        Pad(output, indent);
        output.Append('}').Append(nl);
    }

    private static void StyleMembers(
        StringBuilder output,
        IReadOnlyList<LuiStyleMemberSyntax> members,
        int indent,
        string nl
    )
    {
        foreach (var member in members)
        {
            if (member is LuiStyleCommentSyntax comment)
            {
                Pad(output, indent);
                output.Append(comment.Text).Append(nl);
                continue;
            }
            if (member is LuiStyleAssignmentSyntax assignment)
            {
                Assignment(output, assignment, indent, nl);
                continue;
            }
            var group = (LuiVariantGroupSyntax)member;
            Pad(output, indent);
            output.Append("when ");
            if (group.ConditionExpression is not null)
                output.Append('(').Append(group.Condition.Text).Append(')');
            else
                output.Append(group.Condition.Text);
            output.Append(" {").Append(nl);
            StyleMembers(output, group.Members, indent + 1, nl);
            Pad(output, indent);
            output.Append('}').Append(nl);
        }
    }

    private static void Assignment(
        StringBuilder output,
        LuiStyleAssignmentSyntax assignment,
        int indent,
        string nl
    )
    {
        Pad(output, indent);
        output
            .Append(assignment.Property.Text)
            .Append(": ")
            .Append(assignment.Expression.Text)
            .Append(';')
            .Append(nl);
    }

    private static void Value(StringBuilder output, LuiValueSyntax value)
    {
        if (value is LuiScalarSyntax scalar)
            output.Append('"').Append(scalar.Value).Append('"');
        else if (value is LuiExpressionSyntax expression)
            output.Append('{').Append(expression.Text).Append('}');
        else if (value is LuiStyleWithSyntax style)
        {
            output.Append('{').Append(style.Name.Text).Append(" with ");
            if (style.Tail is { } tail)
            {
                output.Append(tail.Text).Append('}');
                return;
            }
            output.Append("{ ");
            InlineStyleMembers(output, style.Members);
            output.Append(" }}");
        }
    }

    private static void InlineStyleMembers(
        StringBuilder output,
        IReadOnlyList<LuiStyleMemberSyntax> members
    )
    {
        for (var index = 0; index < members.Count; index++)
        {
            if (index != 0)
                output.Append(' ');
            if (members[index] is LuiStyleAssignmentSyntax assignment)
            {
                output
                    .Append(assignment.Property.Text)
                    .Append(": ")
                    .Append(assignment.Expression.Text)
                    .Append(';');
                continue;
            }
            if (members[index] is LuiStyleCommentSyntax comment)
            {
                output.Append(comment.Text);
                continue;
            }
            var group = (LuiVariantGroupSyntax)members[index];
            output.Append("when ");
            if (group.ConditionExpression is not null)
                output.Append('(').Append(group.Condition.Text).Append(')');
            else
                output.Append(group.Condition.Text);
            output.Append(" { ");
            InlineStyleMembers(output, group.Members);
            output.Append(" }");
        }
    }

    private static void Pad(StringBuilder output, int count)
    {
        output.Append(' ', count * 4);
    }

    private static int SourceIndent(string source, int offset)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, offset - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var count = 0;
        for (var index = lineStart; index < offset; index++)
        {
            if (source[index] == ' ')
                count++;
            else if (source[index] == '\t')
                count += 4;
            else
                return 0;
        }
        return count / 4;
    }

    private static IEnumerable<LuiSyntaxNode> Nodes(LuiDocumentSyntax document)
    {
        foreach (var node in document.TopLevel)
        {
            yield return node;
            if (node is LuiComponentSyntax component)
                foreach (var body in Nodes(component.Body))
                    yield return body;
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
