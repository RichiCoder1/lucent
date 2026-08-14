using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.CodeGeneration;

internal sealed record BoundComponentModel(
    string NamespaceName,
    string ComponentName,
    IReadOnlyList<string> Usings,
    IReadOnlyList<BoundStateModel> States,
    BoundControlModel Root);

internal sealed record BoundStateModel(
    string TypeName,
    string Name,
    string InitializerText,
    SourceSpan Span);

internal sealed record BoundControlModel(
    string Name,
    string TypeName,
    IReadOnlyList<BoundControlMember> Members,
    SourceSpan Span,
    BoundControlKind Kind = BoundControlKind.Unknown,
    BoundContentRoute? ContentRoute = null);

internal sealed record BoundContentRoute(
    string PropertyName,
    bool IsCollection);

internal enum BoundControlKind
{
    Unknown,
    Panel,
    Decorator,
    ContentControl,
    ItemsControl,
    Text,
}

internal abstract record BoundControlMember(SourceSpan Span);

internal sealed record BoundPropertyMember(
    string Name,
    string ExpressionText,
    SourceSpan ExpressionSpan,
    bool IsStringLiteral,
    bool IsInterpolated,
    SourceSpan Span,
    BoundNativeValueKind NativeValueKind = BoundNativeValueKind.None)
    : BoundControlMember(Span);

internal enum BoundNativeValueKind
{
    None,
    Thickness,
    CornerRadius,
}

internal sealed record BoundContentMember(
    string ExpressionText,
    SourceSpan ExpressionSpan,
    bool IsInterpolated,
    SourceSpan Span) : BoundControlMember(Span);

internal sealed record BoundEventMember(
    string Name,
    string EventName,
    string BodyText,
    SourceSpan BodySpan,
    string DelegateTypeName,
    string DelegateSenderTypeName,
    string EventArgsTypeName,
    string? SenderParameterName,
    string? EventArgsParameterName,
    SourceSpan Span) : BoundControlMember(Span);

internal sealed record BoundForEachMember(
    string ItemName,
    string SourceExpression,
    SourceSpan SourceExpressionSpan,
    string KeyExpression,
    SourceSpan KeyExpressionSpan,
    BoundControlModel Body,
    SourceSpan Span) : BoundControlMember(Span);
