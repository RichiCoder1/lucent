using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.CodeGeneration;

internal sealed record BoundComponentModel(
    string NamespaceName,
    string ComponentName,
    IReadOnlyList<BoundStateModel> States,
    BoundControlModel Root);

internal sealed record BoundStateModel(
    string TypeName,
    string Name,
    string InitializerText,
    SourceSpan Span);

internal sealed record BoundControlModel(
    string Name,
    IReadOnlyList<BoundControlMember> Members,
    SourceSpan Span);

internal abstract record BoundControlMember(SourceSpan Span);

internal sealed record BoundPropertyMember(
    string Name,
    string ExpressionText,
    SourceSpan ExpressionSpan,
    bool IsStringLiteral,
    bool IsInterpolated,
    SourceSpan Span) : BoundControlMember(Span);

internal sealed record BoundEventMember(
    string Name,
    string BodyText,
    SourceSpan BodySpan,
    bool IsExpression,
    SourceSpan Span) : BoundControlMember(Span);
