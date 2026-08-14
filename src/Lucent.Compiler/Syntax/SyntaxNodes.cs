namespace Lucent.Compiler.Syntax;

public abstract record LucentSyntaxNode(SourceSpan Span);

public sealed record CompilationUnitSyntax(
    string NamespaceName,
    ComponentDeclarationSyntax Component,
    SourceSpan Span) : LucentSyntaxNode(Span);

public sealed record ComponentDeclarationSyntax(
    string Name,
    StateMemberSyntax State,
    RenderMethodSyntax RenderMethod,
    SourceSpan Span) : LucentSyntaxNode(Span);

public sealed record StateMemberSyntax(
    string TypeName,
    string Name,
    int InitialValue,
    SourceSpan Span) : LucentSyntaxNode(Span);

public sealed record RenderMethodSyntax(
    UiElementSyntax Root,
    SourceSpan Span) : LucentSyntaxNode(Span);

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

public abstract record UiValueSyntax(string Text, SourceSpan Span)
    : LucentSyntaxNode(Span);

public sealed record StringValueSyntax(
    string Text,
    bool IsInterpolated,
    SourceSpan Span) : UiValueSyntax(Text, Span);

public sealed record EventBlockValueSyntax(
    string Text,
    SourceSpan Span) : UiValueSyntax(Text, Span);
