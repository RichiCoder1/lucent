using Microsoft.CodeAnalysis;

namespace Lucent.Lui.Compiler;

[System.Flags]
internal enum LuiRecipeCapabilities
{
    None = 0,
    Styled = 1,
    Accessible = 2,
}

internal static class LuiComponentReturnShape
{
    internal static bool TryGet(
        Compilation compilation,
        ITypeSymbol type,
        out LuiRecipeCapabilities capabilities
    )
    {
        capabilities = LuiRecipeCapabilities.None;
        if (
            SymbolEqualityComparer.Default.Equals(
                type,
                compilation.GetTypeByMetadataName("Lucent.Core.ComponentRecipe")
            )
        )
            return true;
        if (
            type is not INamedTypeSymbol { Arity: 1 } named
            || !SymbolEqualityComparer.Default.Equals(
                named.OriginalDefinition,
                compilation.GetTypeByMetadataName("Lucent.Core.AuthorRecipe`1")
            )
            || named.TypeArguments[0] is not INamedTypeSymbol marker
        )
            return false;

        if (
            SymbolEqualityComparer.Default.Equals(
                marker,
                compilation.GetTypeByMetadataName("Lucent.Core.StyledCapability")
            )
        )
            capabilities = LuiRecipeCapabilities.Styled;
        else if (
            SymbolEqualityComparer.Default.Equals(
                marker,
                compilation.GetTypeByMetadataName("Lucent.Core.AccessibleCapability")
            )
        )
            capabilities = LuiRecipeCapabilities.Accessible;
        else if (
            SymbolEqualityComparer.Default.Equals(
                marker,
                compilation.GetTypeByMetadataName("Lucent.Core.StyledAccessibleCapability")
            )
        )
            capabilities = LuiRecipeCapabilities.Styled | LuiRecipeCapabilities.Accessible;
        return capabilities != LuiRecipeCapabilities.None;
    }
}
