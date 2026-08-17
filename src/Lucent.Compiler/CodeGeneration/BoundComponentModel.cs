using Lucent.Compiler.Syntax;
using Lucent.Compiler.Semantics;

namespace Lucent.Compiler.CodeGeneration;

internal enum BoundReactiveSourceKind { State, Computed, Parameter }

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
    BoundControlModel Root,
    IReadOnlyList<BoundParameterModel>? Parameters = null,
    IReadOnlyList<BoundSlotModel>? Slots = null,
    IReadOnlyList<BoundRenderableModel>? FragmentRoots = null,
    IReadOnlyList<BoundOrdinaryMemberModel>? OrdinaryMembers = null)
{
    public IReadOnlyList<BoundParameterModel> AllParameters => Parameters ?? [];
    public IReadOnlyList<BoundSlotModel> AllSlots => Slots ?? [];
    public IReadOnlyList<BoundRenderableModel> Roots => FragmentRoots ?? [Root];
    public IReadOnlyList<BoundOrdinaryMemberModel> Members => OrdinaryMembers ?? [];
}

internal sealed record BoundParameterModel(
    string TypeName,
    string Name,
    string? DefaultValueText,
    SourceSpan Span);

internal sealed record BoundSlotModel(string Name, SourceSpan Span);

internal sealed record BoundOrdinaryMemberModel(string Text, string Name, SourceSpan Span);

internal abstract record BoundRenderableModel(SourceSpan Span);

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
    BoundContentRoute? ContentRoute = null) : BoundRenderableModel(Span);

internal sealed record BoundComponentInvocationModel(
    ComponentSymbol Component,
    IReadOnlyList<BoundComponentArgument> Arguments,
    IReadOnlyList<BoundSlotSupply> Slots,
    int SiteId,
    SourceSpan Span) : BoundRenderableModel(Span);

internal sealed record BoundComponentArgument(
    ComponentParameterSymbol Parameter,
    BoundCSharpIsland Expression,
    bool IsDefault);

internal sealed record BoundSlotSupply(
    ComponentSlotSymbol Slot,
    IReadOnlyList<BoundRenderableModel> Roots,
    SourceSpan Span);

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

internal sealed record BoundComponentChildMember(
    BoundComponentInvocationModel Invocation,
    SourceSpan Span) : BoundControlMember(Span);

internal sealed record BoundYieldMember(
    ComponentSlotSymbol Slot,
    SourceSpan Span) : BoundControlMember(Span);

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

internal sealed record BoundAttachedPropertyMember(
    string Name,
    string SetterTypeName,
    string SetterName,
    BoundCSharpIsland Expression,
    SourceSpan Span) : BoundControlMember(Span);

internal sealed record BoundNativeCollectionMember(
    string PropertyName,
    string AddTypeName,
    IReadOnlyList<BoundCSharpIsland> Elements,
    SourceSpan Span) : BoundControlMember(Span);

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
    BoundRenderableModel Body,
    SourceSpan Span) : BoundControlMember(Span)
{
    public SourceSpan SourceExpressionSpan => SourceExpression.Span;
    public SourceSpan KeyExpressionSpan => KeyExpression.Span;
}

internal sealed record BoundConditionalMember(
    int Id,
    BoundCSharpIsland Condition,
    IReadOnlyList<BoundRenderableModel> TrueRoots,
    IReadOnlyList<BoundRenderableModel>? FalseRoots,
    SourceSpan Span) : BoundControlMember(Span);

internal sealed record BoundAsyncBoundary(
    string SourceIdentifier,
    BoundCSharpIsland Condition,
    IReadOnlyList<BoundRenderableModel> ContentRoots,
    IReadOnlyList<BoundRenderableModel> FallbackRoots,
    string CatchName,
    SourceSpan Span) : BoundControlMember(Span);
