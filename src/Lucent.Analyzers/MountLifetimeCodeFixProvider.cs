using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Formatting;

namespace Lucent.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MountLifetimeCodeFixProvider))]
public sealed class MountLifetimeCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [MountLifetimeAnalyzer.DroppedTemporaryId];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var invocation = root.FindNode(context.Diagnostics[0].Location.SourceSpan)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation?.Expression is not MemberAccessExpressionSyntax { Expression: ObjectCreationExpressionSyntax creation } ||
            invocation.Parent is not ExpressionStatementSyntax statement)
        {
            return;
        }
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null)
            return;
        var componentName = "__lucent_component";
        for (var suffix = 1; model.LookupSymbols(statement.SpanStart, name: componentName).Length > 0; suffix++)
            componentName = "__lucent_component" + suffix;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Retain and dispose the Lucent component",
                cancellationToken => ApplyUsingDeclaration(
                    context.Document,
                    statement,
                    creation,
                    componentName,
                    ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.ValueText,
                    cancellationToken),
                nameof(MountLifetimeCodeFixProvider)),
            context.Diagnostics[0]);
    }

    private static async Task<Document> ApplyUsingDeclaration(
        Document document,
        ExpressionStatementSyntax statement,
        ObjectCreationExpressionSyntax creation,
        string componentName,
        string mountMethod,
        CancellationToken cancellationToken)
    {
        var replacement = SyntaxFactory.Block(
                SyntaxFactory.ParseStatement($"using var {componentName} = {creation};"),
                SyntaxFactory.ParseStatement($"{componentName}.{mountMethod}();"))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return document.WithSyntaxRoot(root!.ReplaceNode(statement, replacement));
    }
}
