using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lucent.Lui.Compiler;

public enum LuiLineEnding { Preserve, Lf, CrLf }

/// <summary>Canonical structural formatter. C# island text is never reformatted.</summary>
public static class LuiFormatter
{
    public static string Format(string source, LuiLineEnding lineEnding = LuiLineEnding.Preserve)
    {
        var document = LuiParser.Parse(source); if (document.Diagnostics.Count != 0) return source;
        var newline = lineEnding == LuiLineEnding.CrLf ? "\r\n" : lineEnding == LuiLineEnding.Lf ? "\n" : source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        var output = new StringBuilder();
        foreach (var node in document.TopLevel)
        {
            if (node is LuiNamespaceSyntax ns) output.Append("namespace ").Append(ns.Value).Append(';').Append(newline);
            else if (node is LuiUsingSyntax use) output.Append("using ").Append(use.Value).Append(';').Append(newline);
            else if (node is LuiTopLevelCommentSyntax comment) output.Append(comment.Text).Append(newline);
            else if (node is LuiStyleSyntax style) { Style(output, style, 0, newline); output.Append(newline); }
            else if (node is LuiComponentSyntax component) Component(output, component, newline);
        }
        return output.ToString();
    }

    public static string FormatRange(string source, LuiSpan range, LuiLineEnding lineEnding = LuiLineEnding.Preserve)
    {
        if (range.Start < 0 || range.End > source.Length) throw new ArgumentOutOfRangeException(nameof(range));
        var document = LuiParser.Parse(source); if (document.Diagnostics.Count != 0) return source;
        LuiSyntaxNode? selected = Nodes(document).Where(node => node.Span.Start >= range.Start && node.Span.End <= range.End).OrderByDescending(node => node.Span.Length).FirstOrDefault();
        if (selected == null) return source;
        var newline = lineEnding == LuiLineEnding.CrLf ? "\r\n" : lineEnding == LuiLineEnding.Lf ? "\n" : source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        var output = new StringBuilder();
        if (selected is LuiTopLevelCommentSyntax comment) output.Append(comment.Text);
        else if (selected is LuiNamespaceSyntax ns) output.Append("namespace ").Append(ns.Value).Append(';');
        else if (selected is LuiUsingSyntax use) output.Append("using ").Append(use.Value).Append(';');
        else if (selected is LuiComponentSyntax component) Component(output, component, newline);
        else if (selected is LuiStyleSyntax style) Style(output, style, 0, newline);
        else Body(output, new[] { (LuiBodySyntax)selected }, 0, newline);
        if (output.Length >= newline.Length && output.ToString(output.Length - newline.Length, newline.Length) == newline) output.Length -= newline.Length;
        return source.Substring(0, selected.Span.Start) + output + source.Substring(selected.Span.End);
    }

    private static void Component(StringBuilder output, LuiComponentSyntax node, string nl)
    {
        output.Append(node.Accessibility.IsMissing ? "internal" : node.Accessibility.Text).Append(" component ").Append(node.Name.Text).Append('(');
        for (var i = 0; i < node.Parameters.Count; i++) { if (i != 0) output.Append(", "); output.Append(node.Parameters[i].TypeText).Append(' ').Append(node.Parameters[i].Name.Text); }
        output.Append(") {").Append(nl); Body(output, node.Body, 1, nl); output.Append('}').Append(nl);
    }
    private static void Body(StringBuilder output, IReadOnlyList<LuiBodySyntax> body, int indent, string nl)
    {
        foreach (var node in body)
        {
            Pad(output, indent);
            if (node is LuiTextSyntax text) output.Append(text.Text).Append(nl);
            else if (node is LuiCommentSyntax comment) output.Append(comment.Text).Append(nl);
            else if (node is LuiElementSyntax element) Element(output, element, indent, nl);
            else if (node is LuiIfSyntax conditional) { output.Append("if (").Append(conditional.Condition.Text).Append(") {").Append(nl); Body(output, conditional.ThenBody, indent + 1, nl); Pad(output, indent); output.Append('}'); if (!conditional.ElseKeyword.IsMissing) { output.Append(" else {").Append(nl); Body(output, conditional.ElseBody, indent + 1, nl); Pad(output, indent); output.Append('}'); } output.Append(nl); }
            else if (node is LuiForEachSyntax loop) { output.Append("foreach (var ").Append(loop.Variable.Text).Append(" in ").Append(loop.Source.Text).Append(") keyed by ").Append(loop.Key.Text).Append(" {").Append(nl); Body(output, loop.Body, indent + 1, nl); Pad(output, indent); output.Append('}').Append(nl); }
        }
    }
    private static void Element(StringBuilder output, LuiElementSyntax node, int indent, string nl)
    {
        output.Append('<').Append(node.Name.Text); foreach (var attribute in node.Attributes) { output.Append(' ').Append(attribute.Name.Text).Append('='); Value(output, attribute.Value); }
        if (node.SelfClosing) { output.Append(" />").Append(nl); return; }
        if (node.Children.Count == 1 && node.Children[0] is LuiTextSyntax scalar) { output.Append('>').Append(scalar.Text).Append("</").Append(node.CloseName.Text).Append('>').Append(nl); return; }
        output.Append('>').Append(nl); Body(output, node.Children, indent + 1, nl); Pad(output, indent); output.Append("</").Append(node.CloseName.Text).Append('>').Append(nl);
    }
    private static void Style(StringBuilder output, LuiStyleSyntax style, int indent, string nl)
    {
        Pad(output, indent); output.Append("style ").Append(style.Name.Text).Append(" {").Append(nl);
        foreach (var member in style.Members) { if (member is LuiStyleAssignmentSyntax styleAssignment) Assignment(output, styleAssignment, indent + 1, nl); else { var group = (LuiVariantGroupSyntax)member; Pad(output, indent + 1); output.Append("when ").Append(group.Name.Text).Append(" {").Append(nl); foreach (var groupAssignment in group.Assignments) Assignment(output, groupAssignment, indent + 2, nl); Pad(output, indent + 1); output.Append('}').Append(nl); } }
        Pad(output, indent); output.Append('}').Append(nl);
    }
    private static void Assignment(StringBuilder output, LuiStyleAssignmentSyntax assignment, int indent, string nl) { Pad(output, indent); output.Append(assignment.Property.Text).Append(": ").Append(assignment.Expression.Text).Append(';').Append(nl); }
    private static void Value(StringBuilder output, LuiValueSyntax value) { if (value is LuiScalarSyntax scalar) output.Append('"').Append(scalar.Value).Append('"'); else if (value is LuiExpressionSyntax expression) output.Append('{').Append(expression.Text).Append('}'); else if (value is LuiStyleWithSyntax style) { output.Append('{').Append(style.Name.Text).Append(" with { "); for (var i = 0; i < style.Assignments.Count; i++) { if (i != 0) output.Append("; "); output.Append(style.Assignments[i].Property.Text).Append(": ").Append(style.Assignments[i].Expression.Text); } output.Append(" }}"); } }
    private static void Pad(StringBuilder output, int count) { output.Append(' ', count * 4); }
    private static IEnumerable<LuiSyntaxNode> Nodes(LuiDocumentSyntax document)
    {
        foreach (var node in document.TopLevel)
        {
            yield return node;
            if (node is LuiComponentSyntax component) foreach (var body in Nodes(component.Body)) yield return body;
        }
    }
    private static IEnumerable<LuiSyntaxNode> Nodes(IReadOnlyList<LuiBodySyntax> body)
    {
        foreach (var node in body)
        {
            yield return node;
            if (node is LuiElementSyntax element) foreach (var child in Nodes(element.Children)) yield return child;
            else if (node is LuiIfSyntax conditional) { foreach (var child in Nodes(conditional.ThenBody)) yield return child; foreach (var child in Nodes(conditional.ElseBody)) yield return child; }
            else if (node is LuiForEachSyntax loop) foreach (var child in Nodes(loop.Body)) yield return child;
        }
    }
}
