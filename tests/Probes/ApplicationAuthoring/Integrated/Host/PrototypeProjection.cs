using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal sealed record PrototypeProjection(string Declarations, string Component)
{
    internal static PrototypeProjection Parse(string source)
    {
        var tokens = SyntaxFactory.ParseTokens(source).ToArray();
        var depth = 0;
        var componentStart = -1;
        for (var index = 0; index + 2 < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.IsKind(SyntaxKind.OpenBraceToken))
                depth++;
            else if (token.IsKind(SyntaxKind.CloseBraceToken))
                depth--;
            if (
                depth != 0
                || token.ValueText != "component"
                || !tokens[index + 1].IsKind(SyntaxKind.IdentifierToken)
                || !tokens[index + 2].IsKind(SyntaxKind.OpenParenToken)
            )
                continue;
            componentStart =
                index > 0 && tokens[index - 1].IsKind(SyntaxKind.PublicKeyword)
                    ? tokens[index - 1].SpanStart
                    : token.SpanStart;
            break;
        }
        if (componentStart < 0)
            throw new InvalidOperationException(
                "Expected one component after supporting declarations."
            );
        var declarations = source[..componentStart];
        var tree = CSharpSyntaxTree.ParseText(
            declarations,
            new CSharpParseOptions(LanguageVersion.Preview)
        );
        var errors = tree.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            )
            .ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException(
                String.Join(Environment.NewLine, errors.AsEnumerable())
            );
        var unit = tree.GetCompilationUnitRoot();
        var types = unit
            .Members.SelectMany(static member =>
                member is FileScopedNamespaceDeclarationSyntax ns ? ns.Members : [member]
            )
            .ToArray();
        if (
            types.Any(static member =>
                member is not BaseTypeDeclarationSyntax and not DelegateDeclarationSyntax
            )
        )
            throw new InvalidOperationException(
                "The integrated projection supports ordinary types in one file-scoped namespace."
            );
        var masked = source.ToCharArray();
        foreach (var member in types)
            for (var index = member.SpanStart; index < member.Span.End; index++)
                if (masked[index] is not '\r' and not '\n')
                    masked[index] = ' ';
        var component = new string(masked);
        var parsed = LuiParser.Parse(component);
        if (parsed.Diagnostics.Count != 0)
            throw new InvalidOperationException(
                String.Join(
                    Environment.NewLine,
                    parsed.Diagnostics.Select(static diagnostic =>
                        diagnostic.Id + ": " + diagnostic.Message
                    )
                )
            );
        return new PrototypeProjection(declarations, component);
    }
}
