using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Lui.Compiler;

/// <summary>Narrow lexical comparison independent of user-facing formatting policy.</summary>
public static class LuiSourceComparison
{
    /// <summary>Returns a key preserving token boundaries, comments, literal spelling and meaningful text; invalid syntax has no key.</summary>
    /// <remarks>This key admits only layout-whitespace changes. It is not a general semantic equivalence proof.</remarks>
    public static string? StructuralKey(string source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        var document = LuiParser.Parse(source);
        if (document.Diagnostics.Count != 0)
            return null;
        var key = new StringBuilder();
        foreach (var token in SyntaxFactory.ParseTokens(source))
        {
            Trivia(token.LeadingTrivia);
            Append(token.RawKind, token.Text);
            Trivia(token.TrailingTrivia);
        }
        if (document.Component is { } component)
            Body(component.Body);
        return key.ToString();

        void Append(int kind, string text) =>
            key.Append(kind).Append(':').Append(text.Length).Append(':').Append(text).Append(';');
        void Trivia(SyntaxTriviaList trivia)
        {
            foreach (var item in trivia)
                if (
                    !item.IsKind(SyntaxKind.WhitespaceTrivia)
                    && !item.IsKind(SyntaxKind.EndOfLineTrivia)
                )
                    Append(item.RawKind, item.ToFullString());
        }
        void Body(IReadOnlyList<LuiBodySyntax> nodes)
        {
            foreach (var node in nodes)
                switch (node)
                {
                    case LuiTextSyntax text:
                        Append(-1, text.Text);
                        break;
                    case LuiElementSyntax element:
                        Body(element.Children);
                        break;
                    case LuiIfSyntax conditional:
                        Body(conditional.ThenBody);
                        Body(conditional.ElseBody);
                        break;
                    case LuiForEachSyntax loop:
                        Body(loop.Body);
                        break;
                }
        }
    }
}
