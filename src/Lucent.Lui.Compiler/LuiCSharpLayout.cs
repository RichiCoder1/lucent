using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using D = Lucent.Lui.Compiler.LuiLayoutDocument;

namespace Lucent.Lui.Compiler;

// Syntax-only adapter: token contents are never rewritten. LUI supplies the
// enclosing indentation/width; this adapter owns only the C# island's syntax.
internal sealed class LuiCSharpLayout
{
    private readonly LuiFormattingOptions options;
    private readonly Dictionary<SyntaxNode, string> sources = new();

    internal LuiCSharpLayout(LuiFormattingOptions options) => this.options = options;

    internal D Expression(string source) => Format(SyntaxFactory.ParseExpression(source));

    internal D Parameters(string source) => Format(SyntaxFactory.ParseParameterList(source));

    internal D Declarations(string source) =>
        Format(
            CSharpSyntaxTree
                .ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))
                .GetRoot()
        );

    internal D Requirement(LuiRequirementSyntax requirement) =>
        D.Concat(
            D.Text(requirement.Keyword.Text + " "),
            Format(
                SyntaxFactory.ParseStatement(
                    requirement.Text.Substring(
                        requirement.Keyword.Span.End - requirement.Span.Start
                    )
                )
            )
        );

    internal D Member(LuiMemberSyntax member)
    {
        if (member.Kind != LuiMemberKind.Setup)
            return Format(SyntaxFactory.ParseMemberDeclaration(member.Text)!);
        var brace = member.Text.IndexOf('{');
        var header = member.Text.Substring(0, brace).TrimEnd();
        if (
            SyntaxFactory
                .ParseTokens(header)
                .SelectMany(token => token.LeadingTrivia.Concat(token.TrailingTrivia))
                .All(IsLayout)
        )
            header = "Setup(" + (member.SetupOwner?.Text ?? "") + ")";
        return D.Concat(
            D.Text(header),
            D.Text(" "),
            Format(SyntaxFactory.ParseStatement(member.Text.Substring(brace)))
        );
    }

    private D Format(SyntaxNode node)
    {
        if (node.ContainsDiagnostics)
            throw new InvalidOperationException(
                "The C# island cannot be safely parsed for formatting."
            );
        var normalized = node.NormalizeWhitespace(new string(' ', options.IndentSize), "\n");
        var authoredTokens = node.DescendantTokens().ToArray();
        var normalizedTokens = normalized.DescendantTokens().ToArray();
        var triviaReplacements = new Dictionary<SyntaxToken, SyntaxToken>();
        for (var index = 0; index < authoredTokens.Length; index++)
        {
            var authored = authoredTokens[index];
            var token = normalizedTokens[index];
            var leading = RestoreComments(authored.LeadingTrivia, token.LeadingTrivia);
            var trailing = RestoreComments(authored.TrailingTrivia, token.TrailingTrivia);
            if (index > 0)
            {
                var gap = authoredTokens[index - 1]
                    .TrailingTrivia.Concat(authored.LeadingTrivia)
                    .ToArray();
                if (
                    gap.All(IsLayout)
                    && gap.Count(item => item.IsKind(SyntaxKind.EndOfLineTrivia)) >= 2
                    && normalizedTokens[index - 1]
                        .TrailingTrivia.Concat(leading)
                        .Count(item => item.IsKind(SyntaxKind.EndOfLineTrivia)) == 1
                )
                    leading = leading.Insert(0, SyntaxFactory.EndOfLine("\n"));
            }
            triviaReplacements[token] = token
                .WithLeadingTrivia(leading)
                .WithTrailingTrivia(trailing);
        }
        normalized = normalized.ReplaceTokens(
            triviaReplacements.Keys,
            (token, _) => triviaReplacements[token]
        );
        var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
        foreach (var token in normalized.DescendantTokens())
        {
            // NormalizeWhitespace omits the separator after ')' for an is-pattern.
            // Supply syntax-specific spacing without touching interpolation contents.
            if (
                token.IsKind(SyntaxKind.IsKeyword)
                && token.Parent is IsPatternExpressionSyntax
                && token.LeadingTrivia.Count == 0
                && token.GetPreviousToken().TrailingTrivia.Count == 0
            )
                replacements[token] = token.WithLeadingTrivia(SyntaxFactory.Space);
            var sameLine =
                token.IsKind(SyntaxKind.OpenBraceToken)
                    && token.Parent is BlockSyntax or BaseTypeDeclarationSyntax
                || token.IsKind(SyntaxKind.ElseKeyword)
                || token.IsKind(SyntaxKind.CatchKeyword)
                || token.IsKind(SyntaxKind.FinallyKeyword);
            if (!sameLine)
                continue;
            var previous = token.GetPreviousToken();
            if (
                previous.RawKind == 0
                || !previous.TrailingTrivia.Concat(token.LeadingTrivia).All(IsLayout)
            )
                continue;
            replacements[previous] = previous.WithTrailingTrivia(SyntaxFactory.Space);
            replacements[token] = token.WithLeadingTrivia(default(SyntaxTriviaList));
        }
        normalized = normalized.ReplaceTokens(replacements.Keys, (token, _) => replacements[token]);
        var first = normalized.GetFirstToken();
        normalized = normalized.ReplaceToken(
            first,
            first.WithLeadingTrivia(first.LeadingTrivia.SkipWhile(IsLayout))
        );
        var last = normalized.GetLastToken();
        normalized = normalized.ReplaceToken(
            last,
            last.WithTrailingTrivia(last.TrailingTrivia.Reverse().SkipWhile(IsLayout).Reverse())
        );
        // Interpolated-string whitespace can include alignment/format content. Keep
        // the authored island intact, while retaining its normalized outer trivia.
        var originalStrings = node.DescendantNodesAndSelf()
            .OfType<InterpolatedStringExpressionSyntax>()
            .Where(item => !item.Ancestors().OfType<InterpolatedStringExpressionSyntax>().Any())
            .ToArray();
        var formattedStrings = normalized
            .DescendantNodesAndSelf()
            .OfType<InterpolatedStringExpressionSyntax>()
            .Where(item => !item.Ancestors().OfType<InterpolatedStringExpressionSyntax>().Any())
            .ToArray();
        if (formattedStrings.Length != 0)
        {
            var originals = formattedStrings
                .Select((item, index) => (item, originalStrings[index]))
                .ToDictionary(pair => pair.item, pair => pair.Item2);
            normalized = normalized.ReplaceNodes(
                formattedStrings,
                (item, _) => originals[item].WithTriviaFrom(item)
            );
        }
        return Node(normalized);
    }

    private D Node(SyntaxNode node) =>
        node switch
        {
            InterpolatedStringExpressionSyntax value => D.Text(value.ToFullString()),
            BinaryExpressionSyntax value
                when value
                    .OperatorToken.LeadingTrivia.Concat(value.OperatorToken.TrailingTrivia)
                    .All(IsLayout) => AtSourceIndent(
                value,
                D.Group(
                    D.Concat(
                        Format(value.Left),
                        D.Indent(
                            D.Concat(
                                D.Line,
                                D.Text(value.OperatorToken.Text + " "),
                                Format(value.Right)
                            )
                        )
                    )
                )
            ),
            ConditionalExpressionSyntax value
                when value
                    .QuestionToken.LeadingTrivia.Concat(value.QuestionToken.TrailingTrivia)
                    .Concat(value.ColonToken.LeadingTrivia)
                    .Concat(value.ColonToken.TrailingTrivia)
                    .All(IsLayout) => AtSourceIndent(
                value,
                D.Group(
                    D.Concat(
                        Format(value.Condition),
                        D.Indent(
                            D.Concat(
                                D.Line,
                                D.Text("? "),
                                Format(value.WhenTrue),
                                D.Line,
                                D.Text(": "),
                                Format(value.WhenFalse)
                            )
                        )
                    )
                )
            ),
            ArgumentListSyntax list => List(
                list.OpenParenToken,
                list.Arguments.Select(item => (SyntaxNode)item).ToArray(),
                list.Arguments.GetSeparators().ToArray(),
                list.CloseParenToken
            ),
            ParameterListSyntax list => List(
                list.OpenParenToken,
                list.Parameters.Select(item => (SyntaxNode)item).ToArray(),
                list.Parameters.GetSeparators().ToArray(),
                list.CloseParenToken
            ),
            BracketedArgumentListSyntax list => List(
                list.OpenBracketToken,
                list.Arguments.Select(item => (SyntaxNode)item).ToArray(),
                list.Arguments.GetSeparators().ToArray(),
                list.CloseBracketToken
            ),
            CollectionExpressionSyntax list => List(
                list.OpenBracketToken,
                list.Elements.Select(item => (SyntaxNode)item).ToArray(),
                list.Elements.GetSeparators().ToArray(),
                list.CloseBracketToken
            ),
            TupleExpressionSyntax list => List(
                list.OpenParenToken,
                list.Arguments.Select(item => (SyntaxNode)item).ToArray(),
                list.Arguments.GetSeparators().ToArray(),
                list.CloseParenToken
            ),
            _ => D.Concat(
                node.ChildNodesAndTokens()
                    .Select(item => item.IsNode ? Node(item.AsNode()!) : Token(item.AsToken()))
                    .ToArray()
            ),
        };

    private D List(
        SyntaxToken open,
        SyntaxNode[] items,
        SyntaxToken[] separators,
        SyntaxToken close
    )
    {
        if (items.Length == 0)
            return D.Concat(Token(open), Token(close));
        var contents = new List<D>();
        for (var index = 0; index < items.Length; index++)
        {
            var item = Format(items[index]);
            if (index < separators.Length)
            {
                var separator = separators[index];
                item = D.Concat(
                    item,
                    Trivia(separator.LeadingTrivia),
                    D.Text(separator.Text),
                    Trivia(TrimLayoutEnd(separator.TrailingTrivia))
                );
            }
            contents.Add(item);
        }
        var group = D.Group(
            D.Concat(
                D.Text(open.Text),
                D.Indent(
                    D.Concat(
                        D.SoftLine,
                        Trivia(TrimLayoutEnd(open.TrailingTrivia)),
                        D.Join(D.Line, contents)
                    )
                ),
                D.SoftLine,
                Trivia(close.LeadingTrivia.Where(item => !IsLayout(item))),
                D.Text(close.Text)
            )
        );
        // A list can occur inside a normalized method block. Its wrapping belongs
        // to that block's indentation, in addition to the enclosing LUI node.
        return D.Concat(
            Trivia(open.LeadingTrivia),
            AtSourceIndent(open.Parent!, group),
            Trivia(close.TrailingTrivia)
        );
    }

    private D AtSourceIndent(SyntaxNode node, D group)
    {
        var root = node;
        while (root.Parent is not null)
            root = root.Parent;
        if (!sources.TryGetValue(root, out var source))
            sources.Add(root, source = root.ToFullString());
        var start = node.SpanStart - root.FullSpan.Start;
        var lineStart = source.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        var columns = 0;
        while (lineStart + columns < start && source[lineStart + columns] == ' ')
            columns++;
        for (var depth = 0; depth < columns / options.IndentSize; depth++)
            group = D.Indent(group);
        return group;
    }

    private D Token(SyntaxToken token)
    {
        var leading = token.LeadingTrivia;
        // Roslyn normally stores block indentation on the following token rather
        // than with the preceding newline. Convert that indentation to the chosen
        // tab/space policy; never transform whitespace inside literal tokens.
        if (
            leading.Count > 0
            && leading[0].IsKind(SyntaxKind.WhitespaceTrivia)
            && token
                .GetPreviousToken()
                .TrailingTrivia.Any(item => item.IsKind(SyntaxKind.EndOfLineTrivia))
        )
        {
            var columns = leading[0].ToFullString().Length;
            return D.Concat(
                D.Text(options.Padding(columns / options.IndentSize)),
                Trivia(leading.Skip(1)),
                D.Text(token.Text),
                Trivia(token.TrailingTrivia)
            );
        }
        return D.Concat(Trivia(leading), D.Text(token.Text), Trivia(token.TrailingTrivia));
    }

    private D Trivia(IEnumerable<SyntaxTrivia> input)
    {
        var trivia = input.ToArray();
        var result = new List<D>();
        for (var index = 0; index < trivia.Length; index++)
        {
            var item = trivia[index];
            if (item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                var depth = 0;
                if (
                    index + 1 < trivia.Length
                    && trivia[index + 1].IsKind(SyntaxKind.WhitespaceTrivia)
                )
                    depth = trivia[++index].ToFullString().Length / options.IndentSize;
                var line = D.HardLine;
                for (var level = 0; level < depth; level++)
                    line = D.Indent(line);
                result.Add(line);
            }
            else
            {
                result.Add(D.Text(item.ToFullString()));
                if (item.IsKind(SyntaxKind.SingleLineCommentTrivia))
                    result.Add(D.BreakParent);
            }
        }
        return D.Concat(result.ToArray());
    }

    private static bool IsLayout(SyntaxTrivia item) =>
        item.IsKind(SyntaxKind.WhitespaceTrivia) || item.IsKind(SyntaxKind.EndOfLineTrivia);

    private static IEnumerable<SyntaxTrivia> TrimLayoutEnd(IEnumerable<SyntaxTrivia> input) =>
        input.Reverse().SkipWhile(IsLayout).Reverse();

    private static SyntaxTriviaList RestoreComments(
        SyntaxTriviaList original,
        SyntaxTriviaList normalized
    )
    {
        var preserved = new Queue<SyntaxTrivia>(original.Where(item => !IsLayout(item)));
        return SyntaxFactory.TriviaList(
            normalized.Select(item => IsLayout(item) ? item : preserved.Dequeue())
        );
    }
}
