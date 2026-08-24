using Lucent.Compiler.Parsing;
using Lucent.Compiler.Styling;
using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Compiler;

/// <summary>Project-scoped CSS selector and resource tokens for editor requests.</summary>
public sealed class CssProjectTokenIndex
{
    private readonly IReadOnlyDictionary<string, string> _documents;
    private readonly IReadOnlyList<CssNavigation> _classes;
    private readonly IReadOnlyList<CssNavigation> _resources;
    private readonly IReadOnlyList<string> _catalogClasses;

    private CssProjectTokenIndex(
        IReadOnlyDictionary<string, string> documents,
        IReadOnlyList<CssNavigation> classes,
        IReadOnlyList<CssNavigation> resources,
        IReadOnlyList<string> catalogClasses)
    {
        _documents = documents;
        _classes = classes;
        _resources = resources;
        _catalogClasses = catalogClasses;
    }

    public static CssProjectTokenIndex Create(IEnumerable<CssProjectDocument> documents,
        IEnumerable<string>? catalogClasses = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var source = documents
            .GroupBy(document => Path.GetFullPath(document.SourcePath), PathComparer)
            .ToDictionary(group => group.Key, group => group.Last().Text, PathComparer);
        var classes = new List<CssNavigation>();
        var resources = new List<CssNavigation>();
        foreach (var (path, text) in source)
        {
            if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                AddCssTokens(path, text, classes, resources);
            else if (path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase))
                AddLucentClasses(path, text, classes);
        }
        return new CssProjectTokenIndex(source, Distinct(classes), Distinct(resources),
            (catalogClasses ?? []).Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<LucentCompletionItem> GetCompletions(string sourcePath, int offset)
    {
        if (!TryGetDocument(sourcePath, out var text)) return [];
        if (FindResource(text, offset) is { } resource)
            return _resources.Where(token => token.Name.StartsWith(resource.Name, StringComparison.OrdinalIgnoreCase))
                .Select(token => new LucentCompletionItem(token.Name, LucentCompletionItemKind.Value,
                    "Avalonia resource", token.Name, "Known resource() key"))
                .OrderBy(item => item.Label, StringComparer.Ordinal).ToArray();
        if (FindClass(text, offset) is { } @class && IsSelectorPosition(text, offset))
            return _classes.Select(token => token.Name).Concat(_catalogClasses)
                .Distinct(StringComparer.Ordinal)
                .Where(name => name.StartsWith(@class.Name, StringComparison.OrdinalIgnoreCase))
                .Select(name => new LucentCompletionItem(name, LucentCompletionItemKind.Value,
                    "Lucent CSS class", name, "Class selector"))
                .OrderBy(item => item.Label, StringComparer.Ordinal).ToArray();
        return [];
    }

    public CssNavigation? GetDefinition(string sourcePath, int offset)
    {
        if (!TryGetDocument(sourcePath, out var text)) return null;
        if (FindResource(text, offset) is { } resource)
            return _resources.FirstOrDefault(token => string.Equals(token.Name, resource.Name, StringComparison.Ordinal));
        if (FindClass(text, offset) is { } @class)
            return _classes.Where(token => string.Equals(token.Name, @class.Name, StringComparison.Ordinal))
                .OrderBy(token => token.SourcePath.EndsWith(".lui", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(token => token.SourcePath, PathComparer)
                .ThenBy(token => token.Span.Start)
                .FirstOrDefault();
        return null;
    }

    public bool TryGetDocumentText(string sourcePath, out string text) =>
        TryGetDocument(sourcePath, out text);

    private bool TryGetDocument(string path, out string text) =>
        _documents.TryGetValue(Path.GetFullPath(path), out text!);

    private static void AddCssTokens(string path, string text, List<CssNavigation> classes, List<CssNavigation> resources)
    {
        // This parse is the grammar/validation authority; source scanning below only
        // locates exact spans for the already accepted selector/declaration shapes.
        var (sheet, _) = StyleSheetParser.Parse(text, path);
        foreach (var rule in sheet.Rules)
        {
            var selectorStart = text.IndexOf(rule.SelectorText, rule.SelectorOffset, StringComparison.Ordinal);
            if (selectorStart >= 0)
                AddSelectorClasses(path, text, selectorStart, rule.SelectorText.Length, classes);
            foreach (var declaration in rule.Declarations)
                AddDeclarationResources(path, text, declaration.Offset, resources);
        }
    }

    private static void AddSelectorClasses(string path, string text, int start, int length, List<CssNavigation> classes)
    {
        var end = Math.Min(text.Length, start + length);
        for (var index = start; index < end; index++)
        {
            if (text[index] != '.' || index + 1 >= end || !IsIdentifierStart(text[index + 1])) continue;
            var nameStart = ++index;
            while (index < end && IsIdentifierPart(text[index])) index++;
            classes.Add(new CssNavigation(text[nameStart..index], path, new SourceSpan(nameStart, index - nameStart)));
            index--;
        }
    }

    private static void AddDeclarationResources(string path, string text, int start, List<CssNavigation> resources)
    {
        var end = text.IndexOf(';', Math.Clamp(start, 0, text.Length));
        if (end < 0) end = text.Length;
        var index = Math.Clamp(start, 0, text.Length);
        while (index < end)
        {
            var marker = text.IndexOf("resource", index, end - index, StringComparison.OrdinalIgnoreCase);
            if (marker < 0) break;
            var cursor = marker + "resource".Length;
            while (cursor < end && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor >= end || text[cursor++] != '(') { index = cursor; continue; }
            while (cursor < end && char.IsWhiteSpace(text[cursor])) cursor++;
            var quote = cursor < end && text[cursor] is '\'' or '"' ? text[cursor++] : '\0';
            var nameStart = cursor;
            while (cursor < end && (quote != '\0' ? text[cursor] != quote : text[cursor] != ')')) cursor++;
            if (cursor == nameStart || (quote != '\0' && (cursor >= end || text[cursor] != quote))) { index = cursor; continue; }
            var name = text[nameStart..cursor];
            if (CssPropertyCatalog.TryGetResourceKey($"resource(\"{name}\")", out var key))
                resources.Add(new CssNavigation(key, path, new SourceSpan(nameStart, name.Length)));
            index = cursor + 1;
        }
    }

    private static void AddLucentClasses(string path, string text, List<CssNavigation> classes)
    {
        var parser = new Parser(text, path);
        var unit = parser.Parse();
        foreach (var component in unit.AllComponents)
            AddElementClasses(component.RenderMethod.RenderedFragment.Roots, text, classes, path);
    }

    private static void AddElementClasses(IEnumerable<UiElementSyntax> elements, string source, List<CssNavigation> classes, string path)
    {
        foreach (var element in elements)
        {
            foreach (var property in element.Properties.Where(property => string.Equals(property.Name, "Class", StringComparison.Ordinal) && property.Value is StringValueSyntax))
                AddClassLiteral(path, source, (StringValueSyntax)property.Value, classes);
            foreach (var child in element.Children) AddElementClasses([child], source, classes, path);
            foreach (var member in element.Members)
            {
                switch (member)
                {
                    case UiIfSyntax conditional:
                        AddElementClasses(conditional.TrueBranch.Roots, source, classes, path);
                        if (conditional.FalseBranch is not null) AddElementClasses(conditional.FalseBranch.Roots, source, classes, path);
                        break;
                    case UiAsyncBoundarySyntax boundary:
                        AddElementClasses(boundary.Content.Roots, source, classes, path);
                        if (boundary.Loading is not null) AddElementClasses(boundary.Loading.Roots, source, classes, path);
                        AddElementClasses(boundary.Fallback.Roots, source, classes, path);
                        break;
                    case UiForEachSyntax loop:
                        AddElementClasses([loop.Body], source, classes, path);
                        break;
                    case UiTemplateSyntax template:
                        AddElementClasses(template.Body.Roots, source, classes, path);
                        break;
                    case UiSlotSupplySyntax slot:
                        AddElementClasses(slot.Fragment.Roots, source, classes, path);
                        break;
                }
            }
        }
    }

    private static void AddClassLiteral(string path, string source, StringValueSyntax value, List<CssNavigation> classes)
    {
        if (SyntaxFactory.ParseExpression(value.Text) is not LiteralExpressionSyntax { Token.Value: string }) return;
        var start = value.Span.Start;
        var end = value.Span.End;
        if (end - start < 2 || source[start] is not '\'' and not '"') return;
        start++;
        end--;
        while (start < end)
        {
            while (start < end && char.IsWhiteSpace(source[start])) start++;
            var tokenStart = start;
            while (start < end && !char.IsWhiteSpace(source[start])) start++;
            if (start > tokenStart)
                classes.Add(new CssNavigation(source[tokenStart..start], path, new SourceSpan(tokenStart, start - tokenStart)));
        }
    }

    private static CssNavigation? FindResource(string text, int offset)
    {
        var tokens = new List<CssNavigation>();
        AddDeclarationResources(string.Empty, text, 0, tokens);
        return tokens.FirstOrDefault(token => offset >= token.Span.Start && offset <= token.Span.End);
    }

    private static CssNavigation? FindClass(string text, int offset)
    {
        var index = Math.Clamp(offset, 0, text.Length);
        var start = index;
        while (start > 0 && IsIdentifierPart(text[start - 1])) start--;
        if (start == 0 || text[start - 1] != '.') return null;
        var end = index;
        while (end < text.Length && IsIdentifierPart(text[end])) end++;
        return new CssNavigation(text[start..end], string.Empty, new SourceSpan(start, end - start));
    }

    private static bool IsSelectorPosition(string text, int offset) =>
        text.LastIndexOf('{', Math.Max(0, offset - 1)) <= text.LastIndexOf('}', Math.Max(0, offset - 1));
    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';
    private static bool IsIdentifierPart(char value) => IsIdentifierStart(value) || char.IsDigit(value) || value == '-';
    private static IReadOnlyList<CssNavigation> Distinct(IEnumerable<CssNavigation> tokens) => tokens
        .OrderBy(token => token.SourcePath, PathComparer).ThenBy(token => token.Span.Start)
        .GroupBy(token => (token.Name, token.SourcePath, token.Span))
        .Select(group => group.First()).ToArray();
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

public sealed record CssProjectDocument(string SourcePath, string Text);
