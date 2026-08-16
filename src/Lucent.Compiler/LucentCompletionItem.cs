namespace Lucent.Compiler;

public enum LucentCompletionItemKind
{
    Property,
    Event,
    Value,
    Keyword,
    Variable,
    Field,
    Method,
    Type,
}

public sealed record LucentCompletionItem(
    string Label,
    LucentCompletionItemKind Kind,
    string Detail,
    string InsertText,
    string? Documentation = null,
    bool IsSnippet = false);
