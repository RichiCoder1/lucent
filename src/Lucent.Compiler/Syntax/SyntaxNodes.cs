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
    IReadOnlyList<ComputedMemberSyntax>? ComputedMembers = null)
    : LucentSyntaxNode(Span)
{
    public IReadOnlyList<StateMemberSyntax> AllStateMembers =>
        StateMembers ?? (State is null ? [] : [State]);

    public IReadOnlyList<ComputedMemberSyntax> AllComputedMembers =>
        ComputedMembers ?? [];
}

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
    IReadOnlyList<RenderStatementSyntax>? Statements = null) : LucentSyntaxNode(Span)
{
    public IReadOnlyList<RenderStatementSyntax> BodyStatements =>
        Statements ?? [new ReturnRenderStatementSyntax(Root, Root.Span)];
}

public abstract record RenderStatementSyntax(SourceSpan Span) : LucentSyntaxNode(Span);

public sealed record ReturnRenderStatementSyntax(
    UiElementSyntax Root,
    SourceSpan Span) : RenderStatementSyntax(Span);

public sealed record UiElementSyntax(
    string Name,
    IReadOnlyList<UiMemberSyntax> Members,
    SourceSpan Span) : LucentSyntaxNode(Span)
{
    public IEnumerable<UiPropertySyntax> Properties =>
        Members.OfType<UiPropertySyntax>();

    public IEnumerable<UiElementSyntax> Children =>
        Members.OfType<UiChildSyntax>().Select(child => child.Element);
}

public abstract record UiMemberSyntax(SourceSpan Span) : LucentSyntaxNode(Span);

public sealed record UiPropertySyntax(
    string Name,
    UiValueSyntax Value,
    SourceSpan Span) : UiMemberSyntax(Span);

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

public sealed record UiIfSyntax(
    string Condition,
    SourceSpan ConditionSpan,
    UiElementSyntax TrueRoot,
    UiElementSyntax? FalseRoot,
    SourceSpan Span) : UiMemberSyntax(Span);

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
