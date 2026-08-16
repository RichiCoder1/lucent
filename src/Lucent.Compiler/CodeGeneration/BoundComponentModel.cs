using Lucent.Compiler.Syntax;
using Lucent.Compiler.Semantics;

namespace Lucent.Compiler.CodeGeneration;

internal enum BoundReactiveSourceKind { State, Computed }

internal sealed record BoundReactiveSource(
    int Id,
    string Name,
    BoundReactiveSourceKind Kind,
    string ValueTypeName,
    SourceSpan Span);

internal sealed record BoundComponentModel(
    string NamespaceName,
    string ComponentName,
    IReadOnlyList<string> Usings,
    IReadOnlyList<BoundReactiveSource> Sources,
    IReadOnlyList<BoundStateModel> States,
    IReadOnlyList<BoundComputedModel> Computed,
    BoundControlModel Root);

internal sealed record BoundStateModel(
    string TypeName,
    string Name,
    BoundCSharpIsland Initializer,
    SourceSpan Span)
{
    public string InitializerText => Initializer.LoweredText;
}

internal sealed record BoundComputedModel(
    string TypeName,
    string Name,
    BoundCSharpIsland Factory,
    BoundCSharpIsland InitialValue,
    SourceSpan Span)
{
    public string FactoryText => Factory.LoweredText;
    public string InitialValueText => InitialValue.LoweredText;
}

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
    BoundCSharpIsland Expression,
    bool IsStringLiteral,
    bool IsInterpolated,
    SourceSpan Span,
    BoundNativeValueKind NativeValueKind = BoundNativeValueKind.None,
    string? TargetTypeName = null)
    : BoundControlMember(Span)
{
    public string ExpressionText => Expression.LoweredText;
    public SourceSpan ExpressionSpan => Expression.Span;
}

internal enum BoundNativeValueKind
{
    None,
    Thickness,
    CornerRadius,
}

internal sealed record BoundContentMember(
    BoundCSharpIsland Expression,
    bool IsInterpolated,
    SourceSpan Span,
    string? TargetTypeName = null) : BoundControlMember(Span)
{
    public string ExpressionText => Expression.LoweredText;
    public SourceSpan ExpressionSpan => Expression.Span;
}

internal sealed record BoundEventMember(
    string Name,
    string EventName,
    BoundCSharpIsland Body,
    string DelegateTypeName,
    string DelegateSenderTypeName,
    string EventArgsTypeName,
    string? SenderParameterName,
    string? EventArgsParameterName,
    SourceSpan Span) : BoundControlMember(Span)
{
    public string BodyText => Body.LoweredText;
    public SourceSpan BodySpan => Body.Span;
}

internal sealed record BoundForEachMember(
    string ItemName,
    BoundCSharpIsland SourceExpression,
    BoundCSharpIsland KeyExpression,
    BoundControlModel Body,
    SourceSpan Span) : BoundControlMember(Span)
{
    public SourceSpan SourceExpressionSpan => SourceExpression.Span;
    public SourceSpan KeyExpressionSpan => KeyExpression.Span;
}

internal sealed record BoundConditionalMember(
    int Id,
    BoundCSharpIsland Condition,
    BoundControlModel TrueRoot,
    BoundControlModel? FalseRoot,
    SourceSpan Span) : BoundControlMember(Span);
