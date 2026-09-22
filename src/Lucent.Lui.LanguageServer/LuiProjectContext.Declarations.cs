using Lucent.Lui.Compiler;
using Lucent.Lui.Preparation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.LanguageServer;

internal sealed partial class LuiProjectContext
{
    private static LuiNavigationTarget? NamedComponentDeclaration(
        PublishedDocument document,
        ISymbol symbol
    )
    {
        var type = symbol switch
        {
            INamedTypeSymbol named => named,
            IMethodSymbol { Name: "Create" } factory => factory.ContainingType,
            _ => null,
        };
        if (type is null)
            return null;
        var identity = type.ToDisplayString();
        LuiNavigationTarget? target = null;
        foreach (var (path, source) in document.AuthoredSources)
        {
            if (!path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase))
                continue;
            var syntax = LuiAuthoredSourceProjection.Project(source).Document;
            if (syntax.Component is not { } component)
                continue;
            var componentName = component.Name.Text.TrimStart('@');
            var candidate = String.IsNullOrEmpty(syntax.Namespace)
                ? componentName
                : syntax.Namespace + "." + componentName;
            if (!String.Equals(candidate, identity, StringComparison.Ordinal))
                continue;
            if (target is not null)
                return null;
            target = new(new Uri(path), component.Name.Span, source);
        }
        return target;
    }

    private static LuiEditorDiagnostic? MappedEditorDiagnostic(
        Diagnostic diagnostic,
        string path,
        SourceText source
    )
    {
        var span = AuthoredSpan(diagnostic.Location.GetMappedLineSpan(), path, source);
        return span is { } value
            ? new LuiEditorDiagnostic(
                diagnostic.Id,
                diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                value,
                diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => 1,
                    DiagnosticSeverity.Warning => 2,
                    DiagnosticSeverity.Info => 3,
                    _ => 4,
                },
                diagnostic.Descriptor.Category
            )
            : null;
    }

    private static LuiSignatureHelp? OrdinarySignatureHelp(SemanticDocument semantic)
    {
        if (semantic.Position < 0)
            return null;
        var token = semantic
            .Tree.GetRoot()
            .FindToken(Math.Min(semantic.Position, semantic.Tree.Length - 1));
        var argumentList = token
            .Parent?.AncestorsAndSelf()
            .OfType<ArgumentListSyntax>()
            .FirstOrDefault();
        if (argumentList?.Parent is not { } call)
            return null;
        var info = semantic.Model.GetSymbolInfo(call);
        var bound = info.Symbol as IMethodSymbol;
        IEnumerable<ISymbol> group = call is InvocationExpressionSyntax invocation
            ? semantic.Model.GetMemberGroup(invocation.Expression)
            : info.CandidateSymbols;
        var methods = group
            .OfType<IMethodSymbol>()
            .Concat(bound is null ? [] : [bound])
            .Distinct(SymbolEqualityComparer.Default)
            .OfType<IMethodSymbol>()
            .ToArray();
        if (methods.Length == 0)
            return null;
        var active = Math.Max(
            0,
            Array.FindIndex(methods, method => SymbolEqualityComparer.Default.Equals(method, bound))
        );
        var index = argumentList
            .Arguments.GetSeparators()
            .Count(separator => separator.SpanStart < semantic.Position);
        if (
            index < argumentList.Arguments.Count
            && argumentList.Arguments[index].NameColon is { } named
        )
            index =
                methods[active]
                    .Parameters.FirstOrDefault(parameter =>
                        parameter.Name == named.Name.Identifier.ValueText
                    )
                    ?.Ordinal
                ?? index;
        return new LuiSignatureHelp(
            methods
                .Select(method => new LuiSignature(
                    SymbolText(method),
                    method.Parameters.Select(parameter => SymbolText(parameter)).ToArray(),
                    Documentation(method)
                ))
                .ToArray(),
            active,
            Math.Min(index, Math.Max(0, methods[active].Parameters.Length - 1))
        );
    }

    private static SyntaxTree? DeclarationTree(Compilation compilation, string logicalPath) =>
        PreparationTree(compilation, LuiPreparationEngine.DeclarationsHintName(logicalPath));

    private static SyntaxTree? ComponentDeclarationTree(
        Compilation compilation,
        string logicalPath
    ) =>
        PreparationTree(
            compilation,
            LuiPreparationEngine.ComponentDeclarationsHintName(logicalPath)
        );

    private static SyntaxTree? PreparationTree(Compilation compilation, string hintName) =>
        compilation.SyntaxTrees.FirstOrDefault(tree =>
            String.Equals(tree.FilePath, hintName, StringComparison.Ordinal)
        );

    private static LuiSpan? AuthoredSpan(
        FileLinePositionSpan mapped,
        string path,
        SourceText source
    )
    {
        if (
            !mapped.IsValid
            || !SameFile(mapped.Path, new Uri(path))
            || mapped.StartLinePosition.Line >= source.Lines.Count
            || mapped.EndLinePosition.Line >= source.Lines.Count
        )
            return null;
        var startLine = source.Lines[mapped.StartLinePosition.Line];
        var endLine = source.Lines[mapped.EndLinePosition.Line];
        if (
            mapped.StartLinePosition.Character > startLine.Span.Length
            || mapped.EndLinePosition.Character > endLine.Span.Length
        )
            return null;
        var start = startLine.Start + mapped.StartLinePosition.Character;
        var end = endLine.Start + mapped.EndLinePosition.Character;
        return end >= start ? new LuiSpan(start, end - start) : null;
    }

    private static LuiCompilationResult DeclarationResult(
        SyntaxTree tree,
        string path,
        string source,
        LuiFreshnessIdentity identity
    )
    {
        var text = tree.GetText();
        var authored = SourceText.From(source);
        var entries = new List<LuiMapEntry>();
        void Add(TextSpan span, LuiMapKind kind)
        {
            var mapped = AuthoredSpan(tree.GetMappedLineSpan(span), path, authored);
            if (
                mapped is { } value
                && value.Length == span.Length
                && source
                    .AsSpan(value.Start, value.Length)
                    .SequenceEqual(text.ToString(span).AsSpan())
            )
                entries.Add(new(value, new LuiSpan(span.Start, span.Length), kind, false));
        }
        foreach (var line in text.Lines)
            if (line.Span.Length > 0)
                Add(line.Span, LuiMapKind.Expression);
        foreach (var token in tree.GetRoot().DescendantTokens())
            if (token.IsKind(SyntaxKind.IdentifierToken))
                Add(token.Span, LuiMapKind.Symbol);
        return new(identity, text.ToString(), new LuiSourceMap(identity, entries), []);
    }

    private static LuiCompilationResult ComponentDeclarationResult(
        SyntaxTree tree,
        string source,
        LuiDocumentSyntax syntax,
        LuiFreshnessIdentity identity
    )
    {
        var entries = new List<LuiMapEntry>();
        if (syntax.Component is { } component)
        {
            void AddSymbol(LuiSpan authored, SyntaxToken generated)
            {
                if (
                    authored.Start >= 0
                    && authored.End <= source.Length
                    && authored.Length == generated.Span.Length
                    && source
                        .AsSpan(authored.Start, authored.Length)
                        .SequenceEqual(generated.Text.AsSpan())
                )
                    entries.Add(
                        new(
                            authored,
                            new LuiSpan(generated.SpanStart, generated.Span.Length),
                            LuiMapKind.Symbol,
                            false
                        )
                    );
            }

            foreach (
                var declaration in tree.GetRoot()
                    .DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .Where(item => item.Identifier.ValueText == component.Name.Text.TrimStart('@'))
            )
            {
                AddSymbol(component.Name.Span, declaration.Identifier);

                var authoredMethods = component
                    .Body.OfType<LuiMemberSyntax>()
                    .Where(item => item.Kind == LuiMemberKind.Method)
                    .Select(item =>
                        (Member: item, Method: (MethodDeclarationSyntax)item.Declaration)
                    )
                    .ToArray();
                foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
                {
                    var authored = authoredMethods.SingleOrDefault(item =>
                        SameMethodIdentity(item.Method, method)
                    );
                    if (authored.Member is null)
                        continue;
                    AddSymbol(
                        new LuiSpan(
                            authored.Member.Span.Start + authored.Method.Identifier.SpanStart,
                            authored.Method.Identifier.Span.Length
                        ),
                        method.Identifier
                    );
                    for (var index = 0; index < method.ParameterList.Parameters.Count; index++)
                    {
                        var sourceParameter = authored.Method.ParameterList.Parameters[index];
                        AddSymbol(
                            new LuiSpan(
                                authored.Member.Span.Start + sourceParameter.Identifier.SpanStart,
                                sourceParameter.Identifier.Span.Length
                            ),
                            method.ParameterList.Parameters[index].Identifier
                        );
                    }
                }

                static bool SameMethodIdentity(
                    MethodDeclarationSyntax authored,
                    MethodDeclarationSyntax generated
                ) =>
                    authored.Identifier.ValueText == generated.Identifier.ValueText
                    && authored.TypeParameterList?.Parameters.Count
                        == generated.TypeParameterList?.Parameters.Count
                    && authored.ParameterList.Parameters.Count
                        == generated.ParameterList.Parameters.Count
                    && authored
                        .ParameterList.Parameters.Zip(generated.ParameterList.Parameters)
                        .All(pair =>
                            SyntaxFactory.AreEquivalent(pair.First.Type, pair.Second.Type)
                            && pair.First.Modifiers.Select(token => token.ValueText)
                                .SequenceEqual(
                                    pair.Second.Modifiers.Select(token => token.ValueText),
                                    StringComparer.Ordinal
                                )
                        );

                var authoredProperties = component
                    .Parameters.Select(parameter => (parameter.Name.Text, parameter.Name.Span))
                    .Concat(
                        component
                            .Body.OfType<LuiMemberSyntax>()
                            .Where(item => item.Kind == LuiMemberKind.Field)
                            .SelectMany(item =>
                                (
                                    (FieldDeclarationSyntax)item.Declaration
                                ).Declaration.Variables.Select(variable =>
                                    (
                                        variable.Identifier.ValueText,
                                        new LuiSpan(
                                            item.Span.Start + variable.Identifier.SpanStart,
                                            variable.Identifier.Span.Length
                                        )
                                    )
                                )
                            )
                    )
                    .ToLookup(item => item.Item1, item => item.Item2, StringComparer.Ordinal);
                foreach (var property in declaration.Members.OfType<PropertyDeclarationSyntax>())
                foreach (var authored in authoredProperties[property.Identifier.ValueText])
                    AddSymbol(authored, property.Identifier);
            }
        }
        return new(identity, source, new LuiSourceMap(identity, entries), []);
    }

    private static SemanticDocument? DeclarationSemantic(Snapshot snapshot, int offset)
    {
        if (
            !snapshot.NamedComponents
            || DeclarationTree(snapshot.Compilation, snapshot.Document.LogicalPath) is not { } tree
        )
            return null;
        var result = DeclarationResult(
            tree,
            snapshot.Document.Path,
            snapshot.Text.ToString(),
            snapshot.Identity
        );
        var entry = result
            .Map.FromSource(new LuiSpan(offset, 0))
            .OrderBy(item => item.Kind == LuiMapKind.Symbol ? 0 : 1)
            .ThenBy(item => item.Source.Length)
            .FirstOrDefault();
        if (entry is null)
            return null;
        var document = new PublishedDocument(
            result,
            new Uri(
                "lucent-lui://declarations/"
                    + result.Identity.MapIdentity
                    + "/"
                    + result.Identity.HintName
            ),
            snapshot.Text,
            snapshot.Uri,
            snapshot.Index,
            snapshot.Compilation,
            snapshot.Epoch,
            snapshot.Syntax,
            snapshot.Freshness,
            true,
            snapshot.AuthoredSources
        );
        return new(
            document,
            tree,
            snapshot.Compilation.GetSemanticModel(tree),
            entry.Generated.Start + offset - entry.Source.Start,
            entry
        );
    }

    private static IEnumerable<LuiDocumentSymbol> DeclarationSymbols(SyntaxNode parent)
    {
        foreach (var member in parent.ChildNodes())
        {
            if (member is BaseNamespaceDeclarationSyntax)
            {
                foreach (var nested in DeclarationSymbols(member))
                    yield return nested;
                continue;
            }
            var (name, kind) = member switch
            {
                BaseTypeDeclarationSyntax type => (
                    type.Identifier,
                    type switch
                    {
                        EnumDeclarationSyntax => 10,
                        InterfaceDeclarationSyntax => 11,
                        StructDeclarationSyntax => 23,
                        RecordDeclarationSyntax record
                            when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => 23,
                        _ => 5,
                    }
                ),
                DelegateDeclarationSyntax item => (item.Identifier, 12),
                MethodDeclarationSyntax item => (item.Identifier, 6),
                ConstructorDeclarationSyntax item => (item.Identifier, 9),
                PropertyDeclarationSyntax item => (item.Identifier, 7),
                EventDeclarationSyntax item => (item.Identifier, 24),
                EnumMemberDeclarationSyntax item => (item.Identifier, 22),
                _ => (default(SyntaxToken), 0),
            };
            if (kind != 0)
                yield return new(
                    name.ValueText,
                    kind,
                    new(member.SpanStart, member.Span.Length),
                    new(name.SpanStart, name.Span.Length),
                    DeclarationSymbols(member).ToArray()
                );
            else if (member is BaseFieldDeclarationSyntax field)
                foreach (var variable in field.Declaration.Variables)
                    yield return new(
                        variable.Identifier.ValueText,
                        8,
                        new(variable.SpanStart, variable.Span.Length),
                        new(variable.Identifier.SpanStart, variable.Identifier.Span.Length),
                        []
                    );
        }
    }
}
