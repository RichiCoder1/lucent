using Lucent.Compiler.Parsing;

namespace Lucent.Compiler.Syntax;

public abstract record LucentSyntaxNode(SourceSpan Span);

public sealed record CompilationUnitSyntax(
    string NamespaceName,
    ComponentDeclarationSyntax Component,
    SourceSpan Span,
    IReadOnlyList<ComponentDeclarationSyntax>? Components = null,
    IReadOnlyList<UsingDirectiveSyntax>? Usings = null)
    : LucentSyntaxNode(Span)
{
    public IReadOnlyList<ComponentDeclarationSyntax> AllComponents =>
        Components ?? [Component];

    public IReadOnlyList<UsingDirectiveSyntax> AllUsings => Usings ?? [];
}

public sealed record UsingDirectiveSyntax(
    string Text,
    SourceSpan Span) : LucentSyntaxNode(Span);

public sealed record ComponentDeclarationSyntax(
    string Name,
    StateMemberSyntax? State,
    RenderMethodSyntax RenderMethod,
    SourceSpan Span,
    IReadOnlyList<StateMemberSyntax>? StateMembers = null,
    IReadOnlyList<LucentSyntaxNode>? Members = null,
    IReadOnlyList<ComputedMemberSyntax>? ComputedMembers = null,
    IReadOnlyList<ComponentParameterSyntax>? Parameters = null,
    IReadOnlyList<SlotDeclarationSyntax>? Slots = null,
    IReadOnlyList<OrdinaryMemberSyntax>? OrdinaryMembers = null)
    : LucentSyntaxNode(Span)
{
    public IReadOnlyList<StateMemberSyntax> AllStateMembers =>
        StateMembers ?? (State is null ? [] : [State]);

    public IReadOnlyList<ComputedMemberSyntax> AllComputedMembers =>
        ComputedMembers ?? [];

    public IReadOnlyList<ComponentParameterSyntax> AllParameters => Parameters ?? [];
    public IReadOnlyList<SlotDeclarationSyntax> AllSlots => Slots ?? [];
    public IReadOnlyList<OrdinaryMemberSyntax> AllOrdinaryMembers => OrdinaryMembers ?? [];
}

public sealed record ComponentParameterSyntax(
    string TypeName,
    string Name,
    string? DefaultValueText,
    SourceSpan Span,
    SourceSpan NameSpan,
    SourceSpan? DefaultValueSpan) : LucentSyntaxNode(Span);

public sealed record SlotDeclarationSyntax(
    string Name,
    SourceSpan Span,
    SourceSpan NameSpan) : LucentSyntaxNode(Span);

public sealed record OrdinaryMemberSyntax(
    string Text,
    string Name,
    SourceSpan Span,
    SourceSpan NameSpan) : LucentSyntaxNode(Span);

public sealed record StateMemberSyntax(
    string TypeName,
    string Name,
    int InitialValue,
    SourceSpan Span,
    string? InitializerText = null,
    SourceSpan? InitializerSpan = null) : LucentSyntaxNode(Span);

public sealed record ComputedMemberSyntax(
    string TypeName,
    string Name,
    string InitializerText,
    SourceSpan Span,
    SourceSpan InitializerSpan) : LucentSyntaxNode(Span);

public sealed record RenderMethodSyntax(
    UiElementSyntax Root,
    SourceSpan Span,
    IReadOnlyList<RenderStatementSyntax>? Statements = null,
    UiFragmentSyntax? Fragment = null) : LucentSyntaxNode(Span)
{
    public IReadOnlyList<RenderStatementSyntax> BodyStatements =>
        Statements ?? [new ReturnRenderStatementSyntax(Root, Root.Span)];

    public UiFragmentSyntax RenderedFragment =>
        Fragment ?? new UiFragmentSyntax([Root], Root.Span);
}

public sealed record UiFragmentSyntax(
    IReadOnlyList<UiElementSyntax> Roots,
    SourceSpan Span) : LucentSyntaxNode(Span);

public abstract record RenderStatementSyntax(SourceSpan Span) : LucentSyntaxNode(Span);

public sealed record ReturnRenderStatementSyntax(
    UiElementSyntax Root,
    SourceSpan Span) : RenderStatementSyntax(Span);

public sealed record UiElementSyntax(
    string Name,
    IReadOnlyList<UiMemberSyntax> Members,
    SourceSpan Span,
    IReadOnlyList<UiArgumentSyntax>? Arguments = null) : LucentSyntaxNode(Span)
{
    public IEnumerable<UiPropertySyntax> Properties =>
        Members.OfType<UiPropertySyntax>();

    public IEnumerable<UiElementSyntax> Children =>
        Members.OfType<UiChildSyntax>().Select(child => child.Element);

    public IReadOnlyList<UiArgumentSyntax> AllArguments => Arguments ?? [];
}

public sealed record UiArgumentSyntax(
    string? Name,
    string Text,
    SourceSpan Span,
    SourceSpan ExpressionSpan) : LucentSyntaxNode(Span);

public abstract record UiMemberSyntax(SourceSpan Span) : LucentSyntaxNode(Span);

public sealed record UiPropertySyntax(
    string Name,
    UiValueSyntax Value,
    SourceSpan Span,
    IReadOnlyList<SourceSpan>? NameSegments = null) : UiMemberSyntax(Span)
{
    public IReadOnlyList<SourceSpan> Segments => NameSegments ?? [new SourceSpan(Span.Start, Name.Length)];
}

public sealed record UiTemplateSyntax(
    string Name,
    string ItemTypeName,
    string ItemName,
    UiConditionalBranchSyntax Body,
    SourceSpan Span,
    SourceSpan NameSpan,
    SourceSpan ItemTypeSpan,
    SourceSpan ItemNameSpan) : UiMemberSyntax(Span);

public sealed record UiChildSyntax(
    UiElementSyntax Element,
    SourceSpan Span) : UiMemberSyntax(Span);

public sealed record UiContentSyntax(
    UiValueSyntax Value,
    SourceSpan Span) : UiMemberSyntax(Span);

public sealed record UiForEachSyntax(
    string ItemName,
    string SourceExpression,
    SourceSpan SourceExpressionSpan,
    string KeyExpression,
    SourceSpan KeyExpressionSpan,
    UiElementSyntax Body,
    SourceSpan Span) : UiMemberSyntax(Span);

public sealed record UiSlotSupplySyntax(
    string Name,
    UiFragmentSyntax Fragment,
    SourceSpan Span,
    SourceSpan NameSpan) : UiMemberSyntax(Span);

public sealed record UiYieldSyntax(
    string Name,
    SourceSpan Span,
    SourceSpan NameSpan) : UiMemberSyntax(Span);

public sealed record UiIfSyntax(
    string Condition,
    SourceSpan ConditionSpan,
    UiConditionalBranchSyntax TrueBranch,
    UiConditionalBranchSyntax? FalseBranch,
    SourceSpan Span) : UiMemberSyntax(Span)
{
    public UiElementSyntax TrueRoot => TrueBranch.Roots.FirstOrDefault() ??
        new UiElementSyntax("Missing", [], new SourceSpan(TrueBranch.Span.Start, 0));

    public UiElementSyntax? FalseRoot => FalseBranch is null
        ? null
        : FalseBranch.Roots.FirstOrDefault() ??
          new UiElementSyntax("Missing", [], new SourceSpan(FalseBranch.Span.Start, 0));
}

public sealed record UiAsyncBoundarySyntax(
    string SourceIdentifier,
    SourceSpan SourceIdentifierSpan,
    UiConditionalBranchSyntax Content,
    UiConditionalBranchSyntax Fallback,
    string CatchType,
    SourceSpan CatchTypeSpan,
    string CatchName,
    SourceSpan CatchNameSpan,
    SourceSpan Span) : UiMemberSyntax(Span);

public sealed record UiConditionalBranchSyntax(
    IReadOnlyList<UiElementSyntax> Roots,
    SourceSpan Span) : LucentSyntaxNode(Span);

public abstract record UiValueSyntax(
    string Text,
    SourceSpan Span,
    CSharpIslandKind IslandKind = CSharpIslandKind.Expression)
    : LucentSyntaxNode(Span);

public sealed record StringValueSyntax(
    string Text,
    bool IsInterpolated,
    SourceSpan Span)
    : UiValueSyntax(Text, Span);

public sealed record EventBlockValueSyntax(
    string Text,
    SourceSpan Span)
    : UiValueSyntax(Text, Span, CSharpIslandKind.StatementBlock);

public sealed record CSharpExpressionValueSyntax(
    string Text,
    SourceSpan Span)
    : UiValueSyntax(Text, Span);

public sealed record CSharpIslandSyntax(
    CSharpIslandKind Kind,
    string Text,
    SourceSpan Span,
    SourceSpan TerminatorSpan)
    : LucentSyntaxNode(Span);
