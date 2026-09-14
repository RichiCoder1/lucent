using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Lucent.Lui.Compiler;

/// <summary>Stable identifiers owned by the shared LUI lint policy.</summary>
public static class LuiLintCatalog
{
    /// <summary>A keyed iteration uses a known unstable identity source.</summary>
    public const string UnstableKey = "LUI5001";

    /// <summary>A private named style has no symbol-bound use.</summary>
    public const string UnusedPrivateStyle = "LUI5002";

    /// <summary>A proven-equivalent default-content attribute should be element content.</summary>
    public const string DefaultContentPlacement = "LUI5003";

    /// <summary>Top-level declarations do not follow the configured organization policy.</summary>
    public const string DeclarationOrder = "LUI5004";

    /// <summary>A scoped lint-suppression directive is malformed or cannot attach safely.</summary>
    public const string InvalidSuppression = "LUI5005";

    /// <summary>Project-aware lint analysis could not be completed.</summary>
    public const string AnalysisUnavailable = "LUI5006";

    /// <summary>An unexpected failure prevented lint analysis.</summary>
    public const string AnalysisFailed = "LUI5007";

    private static readonly HashSet<string> Suppressible = new(StringComparer.Ordinal)
    {
        UnstableKey,
        UnusedPrivateStyle,
        DefaultContentPlacement,
        DeclarationOrder,
        "LUI2017",
    };

    /// <summary>Returns whether a rule can be named by <c>lui-lint-disable-next</c>.</summary>
    public static bool IsSuppressible(string id) => id is not null && Suppressible.Contains(id);
}

/// <summary>Completion state for one shared lint analysis.</summary>
public enum LuiLintAnalysisStatus
{
    /// <summary>Every requested syntax and semantic rule ran.</summary>
    Complete,

    /// <summary>Required syntax, binding, or project context was unavailable.</summary>
    Unavailable,

    /// <summary>An unexpected implementation failure prevented a trustworthy result.</summary>
    Failed,
}

/// <summary>Options for rules whose policy is not enabled unconditionally.</summary>
public sealed class LuiLintOptions
{
    /// <summary>Creates lint options. Default-content placement is enabled by default.</summary>
    public LuiLintOptions(
        LuiDeclarationOrder declarationOrder = LuiDeclarationOrder.None,
        bool defaultContentPlacement = true
    )
    {
        if (!Enum.IsDefined(typeof(LuiDeclarationOrder), declarationOrder))
            throw new ArgumentOutOfRangeException(nameof(declarationOrder));
        DeclarationOrder = declarationOrder;
        DefaultContentPlacement = defaultContentPlacement;
    }

    /// <summary>Optional top-level component/style ordering convention.</summary>
    public LuiDeclarationOrder DeclarationOrder { get; }

    /// <summary>Whether proven-equivalent default-content attributes are diagnosed.</summary>
    public bool DefaultContentPlacement { get; }
}

/// <summary>One independently applicable, behavior-preserving lint edit.</summary>
public sealed class LuiLintFix
{
    private readonly string analyzedSource;

    internal LuiLintFix(
        string diagnosticId,
        string title,
        string analyzedSource,
        LuiSpan diagnosticSpan,
        IReadOnlyList<LuiSourceEdit> edits
    )
    {
        DiagnosticId = diagnosticId;
        Title = title;
        this.analyzedSource = analyzedSource;
        DiagnosticSpan = diagnosticSpan;
        Edits = edits;
    }

    /// <summary>The rule whose diagnostic this edit resolves.</summary>
    public string DiagnosticId { get; }

    /// <summary>Short user-facing action title.</summary>
    public string Title { get; }

    /// <summary>The exact diagnostic occurrence resolved by this fix.</summary>
    public LuiSpan DiagnosticSpan { get; }

    /// <summary>Non-overlapping source edits measured against the analyzed snapshot.</summary>
    public IReadOnlyList<LuiSourceEdit> Edits { get; }

    /// <summary>Applies this fix only when every stored range is valid and non-overlapping.</summary>
    public string Apply(string source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (!StringComparer.Ordinal.Equals(source, analyzedSource))
            throw new InvalidOperationException(
                "The lint fix was computed for a different source snapshot."
            );
        var ordered = Edits.OrderByDescending(edit => edit.Span.Start).ToArray();
        var previousStart = source.Length;
        foreach (var edit in ordered)
        {
            if (
                edit.Span.Start < 0
                || edit.Span.End > source.Length
                || edit.Span.End > previousStart
            )
                throw new InvalidOperationException(
                    "The lint fix does not apply to this source snapshot."
                );
            source = source
                .Remove(edit.Span.Start, edit.Span.Length)
                .Insert(edit.Span.Start, edit.NewText);
            previousStart = edit.Span.Start;
        }
        return source;
    }
}

/// <summary>Immutable result from shared syntax and project-aware lint analysis.</summary>
public sealed class LuiLintResult
{
    internal LuiLintResult(
        LuiLintAnalysisStatus status,
        IReadOnlyList<LuiDiagnostic> diagnostics,
        IReadOnlyList<LuiLintFix> fixes
    )
    {
        Status = status;
        Diagnostics = diagnostics;
        Fixes = fixes;
    }

    /// <summary>Whether all requested rules produced a trustworthy result.</summary>
    public LuiLintAnalysisStatus Status { get; }

    /// <summary>Compiler and lint diagnostics, after valid scoped lint suppressions.</summary>
    public IReadOnlyList<LuiDiagnostic> Diagnostics { get; }

    /// <summary>Only fixes proven against the analyzed compilation and an edited rebind.</summary>
    public IReadOnlyList<LuiLintFix> Fixes { get; }
}

/// <summary>Shared project-aware LUI lint policy used by build, CLI, and editor hosts.</summary>
public static class LuiLintAnalyzer
{
    private const string DefaultContentAttribute = "Lucent.Core.DefaultContentAttribute";
    private static readonly char[] UnsafeScalarContent = ['<', '{'];

    /// <summary>Runs compiler diagnostics and lint rules against one trusted Roslyn compilation.</summary>
    public static LuiLintResult Analyze(
        LuiDocumentSyntax document,
        Compilation compilation,
        LuiFreshnessIdentity identity,
        LuiLintOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));
        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));
        if (identity is null)
            throw new ArgumentNullException(nameof(identity));
        options ??= new LuiLintOptions();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var compiled = LuiCompiler.Compile(document, compilation, identity);
            cancellationToken.ThrowIfCancellationRequested();
            return AnalyzeCompiled(
                document,
                compilation,
                identity,
                compiled,
                options,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(document, exception);
        }
    }

    /// <summary>Runs lint rules while reusing a current result already lowered by a build or editor host.</summary>
    public static LuiLintResult AnalyzeCompiled(
        LuiDocumentSyntax document,
        Compilation compilation,
        LuiFreshnessIdentity identity,
        LuiCompilationResult compiled,
        LuiLintOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));
        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));
        if (identity is null)
            throw new ArgumentNullException(nameof(identity));
        if (compiled is null)
            throw new ArgumentNullException(nameof(compiled));
        options ??= new LuiLintOptions();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var directives = LuiLintSuppressions.Parse(document);
            var diagnostics = new List<LuiDiagnostic>(directives.Diagnostics);
            if (document.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.AddRange(document.Diagnostics);
                diagnostics.Add(
                    new LuiDiagnostic(
                        LuiLintCatalog.AnalysisUnavailable,
                        "Lint analysis is incomplete because the document has syntax errors.",
                        document.Diagnostics[0].Span
                    )
                );
                return Result(LuiLintAnalysisStatus.Unavailable, diagnostics, [], directives);
            }

            var currentIdentity = LuiCompiler.Snapshot(identity, compilation);
            if (
                !compiled.Identity.Equals(currentIdentity)
                || !compiled.Map.Identity.Equals(compiled.Identity)
            )
            {
                diagnostics.Add(
                    new LuiDiagnostic(
                        LuiLintCatalog.AnalysisUnavailable,
                        "Project-aware lint analysis is incomplete because the compiled document is stale.",
                        document.Component?.Name.Span ?? new LuiSpan(0, 0)
                    )
                );
                return Result(LuiLintAnalysisStatus.Unavailable, diagnostics, [], directives);
            }
            diagnostics.AddRange(compiled.Diagnostics);
            AddDeclarationOrder(document, options.DeclarationOrder, diagnostics);

            if (!compiled.Success || compiled.Source is null)
            {
                var firstError = compiled.Diagnostics.FirstOrDefault(item =>
                    item.Severity == DiagnosticSeverity.Error
                );
                diagnostics.Add(
                    new LuiDiagnostic(
                        LuiLintCatalog.AnalysisUnavailable,
                        "Project-aware lint analysis is incomplete"
                            + (
                                firstError is null
                                    ? "."
                                    : " because " + firstError.Id + " blocked binding."
                            ),
                        firstError?.Span ?? document.Component?.Name.Span ?? new LuiSpan(0, 0)
                    )
                );
                return Result(LuiLintAnalysisStatus.Unavailable, diagnostics, [], directives);
            }

            var fixes = new List<LuiLintFix>();
            if (
                options.DefaultContentPlacement
                && !AddDefaultContentPlacement(
                    document,
                    compilation,
                    compiled,
                    currentIdentity,
                    diagnostics,
                    fixes,
                    cancellationToken
                )
            )
            {
                diagnostics.Add(
                    new LuiDiagnostic(
                        LuiLintCatalog.AnalysisUnavailable,
                        "Default-content placement analysis is incomplete because the bound projection is unavailable.",
                        document.Component?.Name.Span ?? new LuiSpan(0, 0)
                    )
                );
                return Result(LuiLintAnalysisStatus.Unavailable, diagnostics, [], directives);
            }
            return Result(LuiLintAnalysisStatus.Complete, diagnostics, fixes, directives);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(document, exception);
        }
    }

    private static LuiLintResult Failed(LuiDocumentSyntax document, Exception exception) =>
        new LuiLintResult(
            LuiLintAnalysisStatus.Failed,
            [
                new LuiDiagnostic(
                    LuiLintCatalog.AnalysisFailed,
                    "Lint analysis failed: " + exception.Message,
                    document.Component?.Name.Span ?? new LuiSpan(0, 0)
                ),
            ],
            []
        );

    private static LuiLintResult Result(
        LuiLintAnalysisStatus status,
        IEnumerable<LuiDiagnostic> diagnostics,
        IEnumerable<LuiLintFix> fixes,
        LuiLintSuppressions directives
    )
    {
        var kept = diagnostics
            .Where(diagnostic => !directives.IsSuppressed(diagnostic.Id, diagnostic.Span))
            .GroupBy(
                diagnostic =>
                    diagnostic.Id
                    + "\0"
                    + diagnostic.Span.Start
                    + "\0"
                    + diagnostic.Span.Length
                    + "\0"
                    + diagnostic.Message,
                StringComparer.Ordinal
            )
            .Select(group => group.First())
            .OrderBy(item => item.Span.Start)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        return new LuiLintResult(
            status,
            kept,
            fixes
                .Where(fix =>
                    kept.Any(diagnostic =>
                        diagnostic.Id == fix.DiagnosticId
                        && diagnostic.Span.Start == fix.DiagnosticSpan.Start
                        && diagnostic.Span.Length == fix.DiagnosticSpan.Length
                    )
                )
                .ToArray()
        );
    }

    private static void AddDeclarationOrder(
        LuiDocumentSyntax document,
        LuiDeclarationOrder policy,
        List<LuiDiagnostic> diagnostics
    )
    {
        if (policy == LuiDeclarationOrder.None || document.Component is null)
            return;
        LuiSyntaxNode? misplaced = policy switch
        {
            LuiDeclarationOrder.ComponentFirst => document
                .TopLevel.OfType<LuiStyleSyntax>()
                .FirstOrDefault(style => style.Span.Start < document.Component.Span.Start),
            LuiDeclarationOrder.StylesFirst => document
                .TopLevel.OfType<LuiStyleSyntax>()
                .FirstOrDefault(style => style.Span.Start > document.Component.Span.Start),
            _ => null,
        };
        if (misplaced is null)
            return;
        diagnostics.Add(
            new LuiDiagnostic(
                LuiLintCatalog.DeclarationOrder,
                policy == LuiDeclarationOrder.ComponentFirst
                    ? "The configured source policy places the component before named styles."
                    : "The configured source policy places named styles before the component.",
                misplaced.Span,
                DiagnosticSeverity.Warning
            )
        );
    }

    private static bool AddDefaultContentPlacement(
        LuiDocumentSyntax document,
        Compilation compilation,
        LuiCompilationResult compiled,
        LuiFreshnessIdentity identity,
        List<LuiDiagnostic> diagnostics,
        List<LuiLintFix> fixes,
        CancellationToken cancellationToken
    )
    {
        var originalBinding = ProjectionBinding.Create(compilation, compiled, identity);
        if (originalBinding is null)
            return false;
        var elements = Elements(document).ToArray();
        for (var index = 0; index < elements.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var element = elements[index];
            var invocation = originalBinding.Invocation(element);
            if (invocation is null)
                continue;
            var target = invocation.TargetMethod;
            var defaults = target.Parameters.Where(IsDefaultContent).ToArray();
            if (defaults.Length != 1)
                continue;
            var parameter = defaults[0];
            var attribute = element.Attributes.FirstOrDefault(item =>
                item.Name.Text.TrimStart('@') == parameter.Name
            );
            if (attribute is null)
                continue;
            var candidate = CreateContentFix(document.Source, element, attribute, parameter);
            if (candidate is null)
                continue;
            var editedSource = candidate.Apply(document.Source);
            var editedDocument = LuiParser.Parse(editedSource);
            if (editedDocument.Diagnostics.Count != 0)
                continue;
            var editedIdentity = WithDocumentVersion(
                identity,
                identity.DocumentVersion + ":lint-fix"
            );
            var editedCompilation = LuiCompiler.Compile(
                editedDocument,
                compilation,
                editedIdentity
            );
            if (!editedCompilation.Success || editedCompilation.Source is null)
                continue;
            var editedElements = Elements(editedDocument).ToArray();
            if (index >= editedElements.Length)
                continue;
            var editedBinding = ProjectionBinding.Create(
                compilation,
                editedCompilation,
                editedIdentity
            );
            var editedInvocation = editedBinding?.Invocation(editedElements[index]);
            if (!EquivalentInvocation(invocation, editedInvocation, parameter))
                continue;
            diagnostics.Add(
                new LuiDiagnostic(
                    LuiLintCatalog.DefaultContentPlacement,
                    "Default content parameter '"
                        + parameter.Name
                        + "' can be written between the element tags without changing its binding.",
                    attribute.Span,
                    DiagnosticSeverity.Warning
                )
            );
            fixes.Add(candidate);
        }
        return true;
    }

    private static LuiLintFix? CreateContentFix(
        string source,
        LuiElementSyntax element,
        LuiAttributeSyntax attribute,
        IParameterSymbol parameter
    )
    {
        if (
            element.Children.Count != 0
            || parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == "global::Lucent.Core.ComponentContent"
            || element
                .Attributes.SkipWhile(item => item != attribute)
                .Skip(1)
                .Any(item => item.Name.Text != "name")
        )
            return null;

        string body;
        switch (attribute.Value)
        {
            case LuiScalarSyntax scalar
                when parameter.Type.SpecialType == SpecialType.System_String
                    && scalar.Value.IndexOfAny(UnsafeScalarContent) < 0:
                body = scalar.Value;
                break;
            case LuiExpressionSyntax expression
                when !expression.Expression.IsKind(SyntaxKind.NullLiteralExpression)
                    && expression.Expression is not CollectionExpressionSyntax:
                body = "{" + expression.Text + "}";
                break;
            default:
                return null;
        }
        var attributeText = source.Substring(attribute.Span.Start, attribute.Span.Length);
        if (
            attributeText.Contains("//", StringComparison.Ordinal)
            || attributeText.Contains("/*", StringComparison.Ordinal)
        )
            return null;

        var removalStart = attribute.Span.Start;
        while (removalStart > element.Name.Span.End && Char.IsWhiteSpace(source[removalStart - 1]))
            removalStart--;
        var removalEnd = attribute.Span.End;
        var nextDelimiter = element.SelfClosing
            ? element.SelfClosingSlash.Span.Start
            : element.OpenCloseAngle.Span.Start;
        if (element.Attributes[element.Attributes.Count - 1] == attribute)
        {
            while (removalEnd < nextDelimiter && Char.IsWhiteSpace(source[removalEnd]))
                removalEnd++;
        }
        var edits = new List<LuiSourceEdit>
        {
            new LuiSourceEdit(LuiSpan.From(removalStart, removalEnd), String.Empty),
        };
        if (element.SelfClosing)
        {
            edits.Add(
                new LuiSourceEdit(
                    LuiSpan.From(
                        element.SelfClosingSlash.Span.Start,
                        element.OpenCloseAngle.Span.End
                    ),
                    ">" + body + "</" + element.Name.Text + ">"
                )
            );
        }
        else
        {
            edits.Add(new LuiSourceEdit(new LuiSpan(element.OpenCloseAngle.Span.End, 0), body));
        }
        return new LuiLintFix(
            LuiLintCatalog.DefaultContentPlacement,
            "Move default content between the element tags",
            source,
            attribute.Span,
            edits.ToArray()
        );
    }

    private static bool EquivalentInvocation(
        IInvocationOperation original,
        IInvocationOperation? edited,
        IParameterSymbol defaultParameter
    )
    {
        if (
            edited is null
            || !SameMethod(original.TargetMethod, edited.TargetMethod)
            || !EquivalentArguments(original, edited)
        )
            return false;
        var originalArgument = original.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Ordinal == defaultParameter.Ordinal
        );
        var editedArgument = edited.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Ordinal == defaultParameter.Ordinal
        );
        if (originalArgument is null || editedArgument is null)
            return false;
        return StringComparer.Ordinal.Equals(
                originalArgument.Value.Syntax.WithoutTrivia().ToFullString(),
                editedArgument.Value.Syntax.WithoutTrivia().ToFullString()
            )
            && originalArgument.Value.Kind == editedArgument.Value.Kind
            && StringComparer.Ordinal.Equals(
                originalArgument.Value.Type?.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                ),
                editedArgument.Value.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            )
            && SameConstant(
                originalArgument.Value.ConstantValue,
                editedArgument.Value.ConstantValue
            );
    }

    private static bool EquivalentArguments(
        IInvocationOperation original,
        IInvocationOperation edited
    )
    {
        var originalArguments = original
            .Arguments.Where(argument => !argument.IsImplicit)
            .ToArray();
        var editedArguments = edited.Arguments.Where(argument => !argument.IsImplicit).ToArray();
        return originalArguments.Length == editedArguments.Length
            && originalArguments
                .Zip(editedArguments, (left, right) => (left, right))
                .All(pair =>
                    pair.left.Parameter?.Ordinal == pair.right.Parameter?.Ordinal
                    && SameConversion(pair.left.InConversion, pair.right.InConversion)
                    && SameConversion(pair.left.OutConversion, pair.right.OutConversion)
                );
    }

    private static bool SameMethod(IMethodSymbol left, IMethodSymbol right) =>
        StringComparer.Ordinal.Equals(
            left.OriginalDefinition.GetDocumentationCommentId(),
            right.OriginalDefinition.GetDocumentationCommentId()
        )
        && left.Arity == right.Arity
        && left.TypeArguments.Select(TypeIdentity)
            .SequenceEqual(right.TypeArguments.Select(TypeIdentity), StringComparer.Ordinal)
        && TypeIdentity(left.ContainingType) == TypeIdentity(right.ContainingType)
        && TypeIdentity(left.ReturnType) == TypeIdentity(right.ReturnType)
        && left.Parameters.Select(ParameterIdentity)
            .SequenceEqual(right.Parameters.Select(ParameterIdentity), StringComparer.Ordinal);

    private static string ParameterIdentity(IParameterSymbol parameter) =>
        parameter.RefKind + ":" + TypeIdentity(parameter.Type);

    private static string TypeIdentity(ITypeSymbol type) =>
        type.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                    | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            )
        );

    private static bool SameConstant(Optional<object?> left, Optional<object?> right) =>
        left.HasValue == right.HasValue && (!left.HasValue || Equals(left.Value, right.Value));

    private static bool SameConversion(CommonConversion left, CommonConversion right) =>
        left.Exists == right.Exists
        && left.IsIdentity == right.IsIdentity
        && left.IsNumeric == right.IsNumeric
        && left.IsReference == right.IsReference
        && left.IsUserDefined == right.IsUserDefined
        && StringComparer.Ordinal.Equals(
            left.MethodSymbol?.OriginalDefinition.GetDocumentationCommentId(),
            right.MethodSymbol?.OriginalDefinition.GetDocumentationCommentId()
        );

    private static bool IsDefaultContent(IParameterSymbol parameter) =>
        parameter
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == DefaultContentAttribute
            );

    private static LuiFreshnessIdentity WithDocumentVersion(
        LuiFreshnessIdentity identity,
        string documentVersion
    ) =>
        new LuiFreshnessIdentity(
            identity.ProjectEpoch,
            identity.ProjectIdentity,
            identity.Document,
            documentVersion,
            identity.CompilationGeneration,
            identity.SiblingIndexGeneration,
            identity.LanguageVersion,
            identity.CompilerVersion,
            identity.ReferencesGeneration,
            identity.GlobalUsingsGeneration,
            identity.Options,
            identity.Defines,
            identity.RootNamespace
        );

    private static IEnumerable<LuiElementSyntax> Elements(LuiDocumentSyntax document) =>
        BodyElements(document.Component?.Body ?? Array.Empty<LuiBodySyntax>());

    private static IEnumerable<LuiElementSyntax> BodyElements(IEnumerable<LuiBodySyntax> body)
    {
        foreach (var node in body)
        {
            switch (node)
            {
                case LuiElementSyntax element:
                    yield return element;
                    foreach (var child in BodyElements(element.Children))
                        yield return child;
                    break;
                case LuiIfSyntax conditional:
                    foreach (var child in BodyElements(conditional.ThenBody))
                        yield return child;
                    foreach (var child in BodyElements(conditional.ElseBody))
                        yield return child;
                    break;
                case LuiForEachSyntax loop:
                    foreach (var child in BodyElements(loop.Body))
                        yield return child;
                    break;
            }
        }
    }

    private sealed class ProjectionBinding
    {
        private readonly SemanticModel model;
        private readonly LuiSourceMap map;

        private ProjectionBinding(SemanticModel model, LuiSourceMap map)
        {
            this.model = model;
            this.map = map;
        }

        internal static ProjectionBinding? Create(
            Compilation compilation,
            LuiCompilationResult result,
            LuiFreshnessIdentity identity
        )
        {
            if (result.Source is null)
                return null;
            var parseOptions =
                compilation
                    .SyntaxTrees.Select(tree => tree.Options)
                    .OfType<CSharpParseOptions>()
                    .FirstOrDefault()
                ?? CSharpParseOptions.Default;
            var tree = CSharpSyntaxTree.ParseText(result.Source, parseOptions, identity.HintName);
            var clean = compilation.RemoveSyntaxTrees(
                compilation.SyntaxTrees.Where(candidate =>
                    StringComparer.Ordinal.Equals(candidate.FilePath, identity.HintName)
                    || candidate.FilePath.StartsWith(
                        identity.HintName + ".",
                        StringComparison.Ordinal
                    )
                )
            );
            var bound = clean.AddSyntaxTrees(tree);
            return new ProjectionBinding(bound.GetSemanticModel(tree), result.Map);
        }

        internal IInvocationOperation? Invocation(LuiElementSyntax element)
        {
            var generated = map.Entries.FirstOrDefault(entry =>
                !entry.Hidden
                && entry.Kind == LuiMapKind.Symbol
                && entry.Source.Start == element.Name.Span.Start
                && entry.Source.Length == element.Name.Span.Length
            );
            if (generated is null)
                return null;
            var invocation = model
                .SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(candidate =>
                    candidate.Expression.SpanStart == generated.Generated.Start
                    && candidate.Expression.Span.Length == generated.Generated.Length
                );
            return invocation is null
                ? null
                : model.GetOperation(invocation) as IInvocationOperation;
        }
    }
}

internal sealed class LuiLintSuppressions
{
    private const string Prefix = "lui-lint-disable-next";
    private readonly IReadOnlyList<(string Id, LuiSpan Target)> suppressions;

    private LuiLintSuppressions(
        IReadOnlyList<(string Id, LuiSpan Target)> suppressions,
        IReadOnlyList<LuiDiagnostic> diagnostics
    )
    {
        this.suppressions = suppressions;
        Diagnostics = diagnostics;
    }

    internal IReadOnlyList<LuiDiagnostic> Diagnostics { get; }

    internal bool IsSuppressed(string id, LuiSpan span) =>
        LuiLintCatalog.IsSuppressible(id)
        && suppressions.Any(item =>
            item.Id == id && item.Target.Start <= span.Start && span.End <= item.Target.End
        );

    internal static LuiLintSuppressions Parse(LuiDocumentSyntax document)
    {
        var suppressions = new List<(string Id, LuiSpan Target)>();
        var diagnostics = new List<LuiDiagnostic>();
        Scan(
            document.TopLevel,
            item => item is LuiTopLevelCommentSyntax comment ? comment.Text : null,
            RecurseTopLevel,
            suppressions,
            diagnostics
        );
        diagnostics.AddRange(
            LuiDirectiveIslands
                .Find(document, "// lui-lint-")
                .Select(span =>
                    Invalid(
                        "Lint suppressions inside C# syntax are unsupported; place the marker immediately before the complete C#-containing construct.",
                        span
                    )
                )
        );
        return new LuiLintSuppressions(suppressions.ToArray(), diagnostics.ToArray());

        void RecurseTopLevel(LuiSyntaxNode node)
        {
            if (node is LuiComponentSyntax component)
                ScanBody(component.Body);
            else if (node is LuiStyleSyntax style)
                ScanStyle(style.Members);
        }

        void ScanBody(IReadOnlyList<LuiBodySyntax> body)
        {
            Scan(
                body,
                item => item is LuiCommentSyntax comment ? comment.Text : null,
                node =>
                {
                    if (node is LuiElementSyntax element)
                        ScanBody(element.Children);
                    else if (node is LuiIfSyntax conditional)
                    {
                        ScanBody(conditional.ThenBody);
                        ScanBody(conditional.ElseBody);
                    }
                    else if (node is LuiForEachSyntax loop)
                        ScanBody(loop.Body);
                },
                suppressions,
                diagnostics
            );
        }

        void ScanStyle(IReadOnlyList<LuiStyleMemberSyntax> members)
        {
            Scan(
                members,
                item => item is LuiStyleCommentSyntax comment ? comment.Text : null,
                node =>
                {
                    if (node is LuiVariantGroupSyntax group)
                        ScanStyle(group.Members);
                },
                suppressions,
                diagnostics
            );
        }
    }

    private static void Scan<T>(
        IReadOnlyList<T> nodes,
        Func<T, string?> commentText,
        Action<T> recurse,
        List<(string Id, LuiSpan Target)> suppressions,
        List<LuiDiagnostic> diagnostics
    )
        where T : LuiSyntaxNode
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var comment = commentText(node);
            if (comment is not null && TryDirective(comment, out var id, out var error))
            {
                var target = nodes
                    .Skip(index + 1)
                    .FirstOrDefault(candidate => commentText(candidate) is null);
                if (error is not null)
                    diagnostics.Add(Invalid(error, node.Span));
                else if (target is null)
                    diagnostics.Add(
                        Invalid("The lint suppression has no following construct.", node.Span)
                    );
                else
                    suppressions.Add((id!, target.Span));
            }
            recurse(node);
        }
    }

    private static bool TryDirective(string comment, out string? id, out string? error)
    {
        id = null;
        error = null;
        var text = comment.TrimStart();
        if (!text.StartsWith("//", StringComparison.Ordinal))
            return false;
        text = text.Substring(2).TrimStart();
        if (!text.StartsWith("lui-lint-", StringComparison.Ordinal))
            return false;
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "Malformed lint suppression; expected '// lui-lint-disable-next RULE: reason'.";
            return true;
        }
        var remainder = text.Substring(Prefix.Length).Trim();
        var colon = remainder.IndexOf(':');
        if (colon < 0)
        {
            error = "A lint suppression requires a rule ID and a nonempty reason.";
            return true;
        }
        id = remainder.Substring(0, colon).Trim();
        var reason = remainder.Substring(colon + 1).Trim();
        if (id.Length == 0 || reason.Length == 0)
        {
            error = "A lint suppression requires a rule ID and a nonempty reason.";
            return true;
        }
        if (id == "*" || !LuiLintCatalog.IsSuppressible(id))
        {
            error = "Unknown or unsupported lint rule '" + id + "'.";
            return true;
        }
        return true;
    }

    private static LuiDiagnostic Invalid(string message, LuiSpan span) =>
        new LuiDiagnostic(LuiLintCatalog.InvalidSuppression, message, span);
}
