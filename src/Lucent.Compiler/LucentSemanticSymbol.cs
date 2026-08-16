namespace Lucent.Compiler;

public enum LucentSemanticSymbolKind
{
    NativeControl,
    NativeProperty,
    NativeEvent,
    NativeValue,
    Expression,
    Component,
    ComponentParameter,
    ComponentSlot,
}

public sealed record LucentDefinition(
    string SourcePath,
    SourceSpan Span);

public sealed record LucentSemanticSymbol(
    string Name,
    LucentSemanticSymbolKind Kind,
    SourceSpan ReferenceSpan,
    string Display,
    string? Documentation = null,
    LucentDefinition? Definition = null);
