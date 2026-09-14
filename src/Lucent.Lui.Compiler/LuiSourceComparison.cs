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
        return StructuralKey(document);
    }

    internal static string? StructuralKey(LuiDocumentSyntax document)
    {
        if (document.Diagnostics.Count != 0)
            return null;
        var source = document.Source;
        var key = new StringBuilder();
        // Scalar body text follows LUI rules, not C# lexing (for example https://).
        // Mask it before lexing structural/island tokens, then compare its exact value.
        var textNodes = new List<LuiTextSyntax>();
        if (document.Component is { } component)
            Collect(component.Body);
        var lexical = new StringBuilder(source);
        for (var index = textNodes.Count - 1; index >= 0; index--)
            lexical
                .Remove(textNodes[index].Span.Start, textNodes[index].Span.Length)
                .Insert(textNodes[index].Span.Start, " __luiText" + index + " ");
        foreach (var token in SyntaxFactory.ParseTokens(lexical.ToString()))
        {
            Trivia(token.LeadingTrivia);
            Append(token.RawKind, token.Text);
            Trivia(token.TrailingTrivia);
        }
        foreach (var text in textNodes)
            Append(-1, text.Text);
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
                {
                    var text = item.ToFullString();
                    // Roslyn includes the terminating line ending inside XML
                    // documentation trivia. It is layout, unlike literal token text.
                    if (item.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
                    Append(item.RawKind, text);
                }
        }
        void Collect(IReadOnlyList<LuiBodySyntax> nodes)
        {
            foreach (var node in nodes)
                switch (node)
                {
                    case LuiTextSyntax text:
                        textNodes.Add(text);
                        break;
                    case LuiElementSyntax element:
                        Collect(element.Children);
                        break;
                    case LuiIfSyntax conditional:
                        Collect(conditional.ThenBody);
                        Collect(conditional.ElseBody);
                        break;
                    case LuiForEachSyntax loop:
                        Collect(loop.Body);
                        break;
                }
        }
    }
}
