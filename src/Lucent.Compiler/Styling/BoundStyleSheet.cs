namespace Lucent.Compiler.Styling;

internal sealed record BoundStyleSheet(
    IReadOnlyList<BoundStyleRule> Rules)
{
    public static BoundStyleSheet Empty { get; } = new([]);
}

internal sealed record BoundStyleRule(
    string? TypeName,
    string? ClassName,
    string? PseudoClass,
    IReadOnlyList<BoundStyleDeclaration> Declarations);

internal sealed record BoundStyleDeclaration(
    string PropertyName,
    string Value);
