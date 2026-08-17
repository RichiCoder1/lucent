namespace Lucent.Compiler.Styling;

internal sealed record BoundStyleSheet(
    IReadOnlyList<BoundStyleRule> Rules)
{
    public static BoundStyleSheet Empty { get; } = new([]);
}

internal sealed record BoundStyleRule(
    BoundStyleSelector Selector,
    IReadOnlyList<BoundStyleDeclaration> Declarations)
{
    public IReadOnlyList<string> TargetTypeNames { get; init; } = [];
    public string? TypeName => Selector.Terminal.TypeName;
    public string? ClassName => Selector.Terminal.Classes.FirstOrDefault();
    public string? PseudoClass => Selector.PseudoClass;
    public string? Name => Selector.Terminal.Name;
    public IReadOnlyList<string> ClassNames => Selector.Terminal.Classes;
    public string SelectorText => Selector.Text;
}

internal sealed record BoundStyleDeclaration(
    string PropertyName,
    string Value,
    int Offset = 0);

internal sealed record BoundStyleSelector(
    string Text,
    IReadOnlyList<BoundStyleSelectorPart> Parts,
    IReadOnlyList<BoundStyleCombinator> Combinators,
    string? PseudoClass)
{
    public BoundStyleSelectorPart Terminal => Parts[^1];
}

internal sealed record BoundStyleSelectorPart(
    string? TypeName,
    string? Name,
    IReadOnlyList<string> Classes)
{
    public string? ResolvedTypeName { get; init; }
}

internal enum BoundStyleCombinator
{
    Descendant,
    Child,
}
