using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler;

/// <summary>Bounded projection of ordinary declarations and one named component from an authored LUI source snapshot.</summary>
/// <remarks>This A0 surface accepts ordinary type and delegate declarations before the component in one file-scoped namespace. It preserves source offsets by masking projected declarations.</remarks>
public sealed class LuiAuthoredSourceProjection
{
    private LuiAuthoredSourceProjection(
        LuiDocumentSyntax document,
        string declarationsSource,
        string? earlyComponentDeclaration,
        IReadOnlyList<LuiDiagnostic> diagnostics
    )
    {
        Document = document;
        DeclarationsSource = declarationsSource;
        EarlyComponentDeclaration = earlyComponentDeclaration;
        Diagnostics = diagnostics;
    }

    /// <summary>The parsed component document with projected declarations replaced by same-length whitespace.</summary>
    public LuiDocumentSyntax Document { get; }

    /// <summary>Ordinary C# declarations that must be supplied to preparatory and final compilations.</summary>
    public string DeclarationsSource { get; }

    /// <summary>Named partial identity and defining <c>Create</c> declaration supplied before component lowering.</summary>
    public string? EarlyComponentDeclaration { get; }

    /// <summary>Projection and parser diagnostics measured against the authored source.</summary>
    public IReadOnlyList<LuiDiagnostic> Diagnostics { get; }

    /// <summary>Whether the bounded projection and component parse completed without errors.</summary>
    public bool Success =>
        Diagnostics.All(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

    /// <summary>Projects declarations and a named component without reading files or running generators.</summary>
    public static LuiAuthoredSourceProjection Project(string source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        var tokens = SyntaxFactory.ParseTokens(source).ToArray();
        var depth = 0;
        var componentStarts = new List<int>();
        var firstStyleStart = -1;
        for (var index = 0; index + 2 < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.IsKind(SyntaxKind.OpenBraceToken))
                depth++;
            else if (token.IsKind(SyntaxKind.CloseBraceToken))
                depth--;
            if (
                depth == 0
                && firstStyleStart < 0
                && token.ValueText == "style"
                && tokens[index + 1].IsKind(SyntaxKind.IdentifierToken)
            )
                firstStyleStart = token.SpanStart;
            if (
                depth != 0
                || token.ValueText != "component"
                || !tokens[index + 1].IsKind(SyntaxKind.IdentifierToken)
                || !tokens[index + 2].IsKind(SyntaxKind.OpenParenToken)
            )
                continue;
            var hasAccessibility =
                index > 0
                && tokens[index - 1].Kind()
                    is SyntaxKind.PublicKeyword
                        or SyntaxKind.InternalKeyword;
            componentStarts.Add(hasAccessibility ? tokens[index - 1].SpanStart : token.SpanStart);
        }

        if (componentStarts.Count > 1)
            return Failed(
                source,
                "LUI1029",
                "A LUI file may contain at most one component.",
                componentStarts[1],
                "component".Length
            );

        var componentStart = componentStarts.Count == 0 ? -1 : componentStarts[0];
        var supportOnly = componentStart < 0;
        var declarationsEnd = source.Length;
        if (firstStyleStart >= 0)
            declarationsEnd = firstStyleStart;
        if (componentStart >= 0)
            declarationsEnd = Math.Min(declarationsEnd, componentStart);

        var declarations = source.Substring(0, declarationsEnd);
        var tree = CSharpSyntaxTree.ParseText(
            declarations,
            new CSharpParseOptions(LanguageVersion.Preview)
        );
        var errors = tree.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
        {
            var diagnostic = errors[0];
            return Failed(
                source,
                "LUI1027",
                "Supporting declarations must be valid C#: "
                    + diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                diagnostic.Location.SourceSpan.Start,
                diagnostic.Location.SourceSpan.Length
            );
        }

        var unit = tree.GetCompilationUnitRoot();
        var typeMembers = unit
            .Members.SelectMany(static member =>
                member is FileScopedNamespaceDeclarationSyntax ns ? ns.Members : [member]
            )
            .ToArray();
        var unsupported = typeMembers.FirstOrDefault(static member =>
            member is not BaseTypeDeclarationSyntax and not DelegateDeclarationSyntax
        );
        if (unsupported is not null)
            return Failed(
                source,
                "LUI1028",
                "This projection supports ordinary types and delegates before the component in one file-scoped namespace.",
                unsupported.SpanStart,
                unsupported.Span.Length
            );

        var masked = source.ToCharArray();
        foreach (var member in typeMembers)
            for (var index = member.SpanStart; index < member.Span.End; index++)
                if (masked[index] is not '\r' and not '\n')
                    masked[index] = ' ';
        var document = LuiParser.Parse(new string(masked));
        if (supportOnly)
            document = WithoutRequiredComponentDiagnostic(document);
        var diagnostics = document.Diagnostics.ToArray();
        var early = diagnostics.Any(static value => value.Severity == DiagnosticSeverity.Error)
            ? null
            : EarlyDeclaration(document, null);
        return new LuiAuthoredSourceProjection(document, declarations, early, diagnostics);
    }

    private static LuiAuthoredSourceProjection Failed(
        string source,
        string id,
        string message,
        int start = 0,
        int length = 0
    )
    {
        var document = LuiParser.Parse(source);
        var diagnostics = document
            .Diagnostics.Concat([new LuiDiagnostic(id, message, new LuiSpan(start, length))])
            .ToArray();
        return new LuiAuthoredSourceProjection(document, String.Empty, null, diagnostics);
    }

    private static LuiDocumentSyntax WithoutRequiredComponentDiagnostic(
        LuiDocumentSyntax document
    ) =>
        new(
            document.Span,
            document.Source,
            document.Namespace,
            document.Usings,
            document.Component,
            document.Styles,
            document.Comments,
            document.TopLevel,
            document.Diagnostics.Where(static diagnostic => diagnostic.Id != "LUI1003").ToArray()
        );

    internal static string? EarlyDeclaration(
        LuiDocumentSyntax document,
        IReadOnlyDictionary<string, (string Type, bool HasSetter)>? stateSignatures
    )
    {
        var component = document.Component;
        if (component is null)
            return null;
        var builder = new StringBuilder();
        if (!String.IsNullOrWhiteSpace(document.Namespace))
            builder.Append("namespace ").Append(document.Namespace).AppendLine(";");
        foreach (var item in document.Usings)
            builder.Append("using ").Append(item).AppendLine(";");
        var setupParameter =
            component
                .Body.OfType<LuiMemberSyntax>()
                .SingleOrDefault(static member => member.Kind == LuiMemberKind.Setup)
                ?.SetupOwner?.Text
            ?? "context";
        builder
            .Append(component.Accessibility.Text is "public" ? "public" : "internal")
            .Append(" sealed partial class ")
            .Append(component.Name.Text)
            .AppendLine()
            .AppendLine("{")
            .AppendLine("    [global::Lucent.Core.LucentComponent]")
            .Append("    public static partial global::Lucent.Core.ComponentRecipe Create(")
            .Append(
                String.Join(", ", component.Parameters.Select(static item => item.DeclarationText))
            )
            .AppendLine(");")
            .AppendLine();
        foreach (var parameter in component.Parameters)
            builder
                .Append("    private partial ")
                .Append(parameter.TypeText)
                .Append(' ')
                .Append(parameter.Name.Text)
                .AppendLine(" { get; }");
        foreach (
            var field in component
                .Body.OfType<LuiMemberSyntax>()
                .Where(static member => member.Kind == LuiMemberKind.Field)
                .Select(static member => (FieldDeclarationSyntax)member.Declaration)
        )
        foreach (var variable in field.Declaration.Variables)
        {
            var name = variable.Identifier.ValueText;
            var refined = default((string Type, bool HasSetter));
            var hasRefinedSignature =
                stateSignatures is not null && stateSignatures.TryGetValue(name, out refined);
            if (!hasRefinedSignature && IsVar(field.Declaration.Type))
                continue;
            builder
                .Append("    private partial ")
                .Append(
                    hasRefinedSignature && IsVar(field.Declaration.Type)
                        ? refined.Type
                        : field.Declaration.Type.ToString()
                )
                .Append(' ')
                .Append(variable.Identifier.Text)
                .Append(" { get;");
            if (hasRefinedSignature ? refined.HasSetter : ProjectedStateHasSetter(field, variable))
                builder.Append(" set;");
            builder.AppendLine(" }");
        }
        foreach (
            var method in component
                .Body.OfType<LuiMemberSyntax>()
                .Where(static member => member.Kind == LuiMemberKind.Method)
                .Select(static member => (MethodDeclarationSyntax)member.Declaration)
        )
            builder.Append("    ").AppendLine(NamedPartialMethod(method, definition: true));
        builder
            .Append("    partial void Setup(global::Lucent.Core.ComponentContext ")
            .Append(setupParameter)
            .AppendLine(");")
            .AppendLine("}");
        return builder.ToString();
    }

    internal static bool ProjectedStateHasSetter(
        FieldDeclarationSyntax field,
        VariableDeclaratorSyntax variable
    ) =>
        !field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
        && (
            field
                .AttributeLists.SelectMany(static list => list.Attributes)
                .Any(static attribute =>
                    attribute.Name.ToString() == "Once" && attribute.ArgumentList is null
                )
            || variable.Initializer?.Value is { } value && DefinitelyConstant(value)
            || variable.Initializer?.Value is MemberAccessExpressionSyntax member
                && member.Expression.ToString() == field.Declaration.Type.ToString()
        );

    private static bool DefinitelyConstant(ExpressionSyntax expression) =>
        expression switch
        {
            LiteralExpressionSyntax => true,
            DefaultExpressionSyntax => true,
            ParenthesizedExpressionSyntax value => DefinitelyConstant(value.Expression),
            PrefixUnaryExpressionSyntax value => DefinitelyConstant(value.Operand),
            BinaryExpressionSyntax value => DefinitelyConstant(value.Left)
                && DefinitelyConstant(value.Right),
            CastExpressionSyntax value => DefinitelyConstant(value.Expression),
            ConditionalExpressionSyntax value => DefinitelyConstant(value.Condition)
                && DefinitelyConstant(value.WhenTrue)
                && DefinitelyConstant(value.WhenFalse),
            CheckedExpressionSyntax value => DefinitelyConstant(value.Expression),
            InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }
            } => true,
            _ => false,
        };

    private static bool IsVar(TypeSyntax type) =>
        type is IdentifierNameSyntax { Identifier.ValueText: "var" };

    internal static string NamedPartialMethod(MethodDeclarationSyntax declaration, bool definition)
    {
        var modifiers = declaration.Modifiers;
        if (
            !modifiers.Any(static modifier =>
                modifier.IsKind(SyntaxKind.PublicKeyword)
                || modifier.IsKind(SyntaxKind.InternalKeyword)
                || modifier.IsKind(SyntaxKind.ProtectedKeyword)
                || modifier.IsKind(SyntaxKind.PrivateKeyword)
            )
        )
            modifiers = modifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PrivateKeyword));
        if (!modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
        if (definition)
            modifiers = SyntaxFactory.TokenList(
                modifiers.Where(static modifier => !modifier.IsKind(SyntaxKind.AsyncKeyword))
            );

        var parameters = declaration.ParameterList.Parameters;
        if (!definition)
            parameters = SyntaxFactory.SeparatedList(
                parameters.Select(static parameter =>
                    parameter.WithAttributeLists(default).WithDefault(null)
                )
            );
        var projected = declaration
            .WithModifiers(modifiers)
            .WithParameterList(declaration.ParameterList.WithParameters(parameters));
        if (definition)
            projected = projected
                .WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        else
            projected = projected.WithAttributeLists(default);
        return projected.NormalizeWhitespace().ToFullString();
    }
}
