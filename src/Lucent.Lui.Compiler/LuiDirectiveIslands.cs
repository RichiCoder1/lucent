using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Lui.Compiler;

internal static class LuiDirectiveIslands
{
    internal static IReadOnlyList<LuiSpan> Find(LuiDocumentSyntax document, string directivePrefix)
    {
        var spans = new List<LuiSpan>();
        foreach (var node in document.TopLevel)
        {
            if (node is LuiComponentSyntax component)
            {
                ScanParameters(component.Parameters);
                ScanBody(component.Body);
            }
            else if (node is LuiStyleSyntax style)
            {
                ScanParameters(style.Parameters);
                ScanStyles(style.Members);
            }
        }
        return spans;

        void ScanParameters(IEnumerable<LuiParameterSyntax> parameters)
        {
            foreach (var parameter in parameters)
                ScanText(parameter.DeclarationText, parameter.Span.Start);
        }

        void ScanBody(IEnumerable<LuiBodySyntax> body)
        {
            foreach (var node in body)
            {
                switch (node)
                {
                    case LuiMemberSyntax member:
                        ScanText(member.Text, member.Span.Start);
                        break;
                    case LuiRequirementSyntax requirement:
                        ScanText(requirement.Text, requirement.Span.Start);
                        break;
                    case LuiExpressionBodySyntax expression:
                        ScanExpression(expression.Text, expression.OpenBrace.Span.End);
                        break;
                    case LuiElementSyntax element:
                        foreach (var attribute in element.Attributes)
                            ScanValue(attribute.Value);
                        ScanBody(element.Children);
                        break;
                    case LuiIfSyntax conditional:
                        ScanValue(conditional.Condition);
                        ScanBody(conditional.ThenBody);
                        ScanBody(conditional.ElseBody);
                        break;
                    case LuiForEachSyntax loop:
                        ScanValue(loop.Source);
                        ScanValue(loop.Key);
                        ScanBody(loop.Body);
                        break;
                }
            }
        }

        void ScanValue(LuiValueSyntax value)
        {
            if (value is LuiExpressionSyntax expression)
                ScanExpression(expression.Text, expression.OpenBrace.Span.End);
            else if (value is LuiStyleWithSyntax styleWith)
            {
                if (styleWith.Tail is { } tail)
                    ScanText(tail.Text, tail.Span.Start);
                ScanStyles(styleWith.Members);
            }
        }

        void ScanStyles(IEnumerable<LuiStyleMemberSyntax> members)
        {
            foreach (var member in members)
            {
                switch (member)
                {
                    case LuiStyleAssignmentSyntax assignment:
                        ScanValue(assignment.Expression);
                        break;
                    case LuiStyleTransitionSyntax transition:
                        ScanValue(transition.Expression);
                        break;
                    case LuiVariantGroupSyntax group:
                        if (group.ConditionExpression is { } condition)
                            ScanValue(condition);
                        ScanStyles(group.Members);
                        break;
                }
            }
        }

        void ScanExpression(string text, int sourceStart) => ScanText(text, sourceStart);

        void ScanText(string text, int sourceStart)
        {
            foreach (
                var trivia in SyntaxFactory
                    .ParseTokens(text)
                    .SelectMany(token => token.LeadingTrivia.Concat(token.TrailingTrivia))
            )
            {
                if (
                    trivia.RawKind
                        == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia
                    && trivia.ToFullString().StartsWith(directivePrefix, StringComparison.Ordinal)
                )
                    spans.Add(new LuiSpan(sourceStart + trivia.SpanStart, trivia.Span.Length));
            }
        }
    }
}
