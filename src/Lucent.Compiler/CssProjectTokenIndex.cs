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
    private readonly IReadOnlyList<StyleClassEntry> _catalogClasses;

    private CssProjectTokenIndex(
        IReadOnlyDictionary<string, string> documents,
        IReadOnlyList<CssNavigation> classes,
        IReadOnlyList<CssNavigation> resources,
        IReadOnlyList<StyleClassEntry> catalogClasses)
    {
        _documents = documents;
        _classes = classes;
        _resources = resources;
        _catalogClasses = catalogClasses;
    }

    internal static CssProjectTokenIndex Create(IEnumerable<CssProjectDocument> documents,
        IEnumerable<StyleClassEntry>? catalogClasses = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var source = documents
            .GroupBy(document => Path.GetFullPath(document.SourcePath), PathComparer)
            .ToDictionary(group => group.Key, group => group.Last(), PathComparer);
        var classes = new List<CssNavigation>();
        var resources = new List<CssNavigation>();
        foreach (var (path, document) in source)
        {
            var text = document.Text;
            if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                AddCssTokens(path, text, classes, resources);
            else if (path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase))
                AddLucentClasses(path, text, classes);
        }
        var entries = new List<StyleClassEntry>(catalogClasses ?? []);
        foreach (var (path, document) in source.Where(item => item.Key.EndsWith(".css", StringComparison.OrdinalIgnoreCase)))
            entries.AddRange(LiveClasses(path, document.Text, document.Origin));
        return new CssProjectTokenIndex(source.ToDictionary(item => item.Key, item => item.Value.Text, PathComparer), Distinct(classes), Distinct(resources), new StyleClassCatalog(entries).Entries);
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
            return _classes.Select(token => token.Name).Concat(_catalogClasses.Select(entry => entry.Name))
                .Distinct(StringComparer.Ordinal)
                .Where(name => name.StartsWith(@class.Name, StringComparison.OrdinalIgnoreCase))
                .Select(name => new LucentCompletionItem(name, LucentCompletionItemKind.Value,
                    "Lucent CSS class", name, "Class selector"))
                .OrderBy(item => item.Label, StringComparer.Ordinal).ToArray();
        return [];
    }

    internal IReadOnlyList<LucentCompletionItem> GetClassValueCompletions(
        string sourceText, int offset, string? receivingType)
    {
        if (!TryFindClassLiteralSegment(sourceText, offset, out var token)) return [];
        var used = token.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(name => !string.Equals(name, token.Active, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        return _catalogClasses
            .Where(entry => entry.Origin != StyleClassOrigin.LocalCss || entry.Definition is not null)
            .Where(entry => entry.Name.StartsWith(token.Active, StringComparison.OrdinalIgnoreCase) && !used.Contains(entry.Name))
            .OrderBy(entry => Rank(entry, receivingType))
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(group => group.OrderBy(entry => Rank(entry, receivingType)).ThenBy(entry => entry.Detail, StringComparer.Ordinal).First())
            .Select(entry => new LucentCompletionItem(entry.Name, LucentCompletionItemKind.Value,
                entry.Origin == StyleClassOrigin.LocalCss ? "Adjacent CSS" : entry.Origin == StyleClassOrigin.GlobalStyle ? "Global CSS" : "Active theme",
                entry.Name, entry.Detail, SortText: $"{Rank(entry, receivingType):D1}-{entry.Name}",
                ReplacementSpan: token.ActiveSpan))
            .ToArray();
    }

    private static int Rank(StyleClassEntry entry, string? receivingType)
    {
        var applicability = entry.ApplicableType is null ? 2 :
            string.Equals(entry.ApplicableType, receivingType, StringComparison.Ordinal) ? 0 : 1;
        var origin = entry.Origin switch
        {
            StyleClassOrigin.LocalCss => 0,
            StyleClassOrigin.GlobalStyle => 3,
            StyleClassOrigin.NativeTheme => 6,
            _ => 9,
        };
        return origin + applicability;
    }

    private static IEnumerable<StyleClassEntry> LiveClasses(string path, string text, StyleClassOrigin origin)
    {
        var (sheet, _) = StyleSheetParser.Parse(text, path);
        foreach (var rule in sheet.Rules)
        foreach (var name in rule.ClassNames.Distinct(StringComparer.Ordinal))
            yield return new StyleClassEntry(name, rule.TypeName, origin,
                new SourceIdentity(path, string.Empty, rule.SelectorOffset, rule.SelectorText.Length), rule.SelectorText);
    }

    private static bool TryFindClassLiteralSegment(string text, int offset, out (string Value, string Active, SourceSpan ActiveSpan) token)
    {
        token = default;
        var cursor = Math.Clamp(offset, 0, text.Length);
        var quote = cursor > 0 ? text.LastIndexOfAny(['"', '\''], cursor - 1) : -1;
        if (quote < 0 || (quote > 0 && text[quote - 1] == '\\')) return false;
        var end = text.IndexOf(text[quote], quote + 1);
        if (end < cursor || end < 0 || !IsClassValue(text, quote)) return false;
        // Interpolation expressions are C#, not literal class text.
        var interpolationOpen = text.LastIndexOf('{', cursor - 1);
        if (interpolationOpen > quote && interpolationOpen > text.LastIndexOf('}', cursor - 1)) return false;
        var start = cursor;
        while (start > quote + 1 && !char.IsWhiteSpace(text[start - 1])) start--;
        var finish = cursor;
        while (finish < end && !char.IsWhiteSpace(text[finish])) finish++;
        token = (text[(quote + 1)..end], text[start..finish], new SourceSpan(start, finish - start));
        return true;
    }

    private static bool IsClassValue(string text, int quote) =>
        text[..quote].TrimEnd().TrimEnd('$', '@').TrimEnd().EndsWith("Class:", StringComparison.Ordinal);

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
            if (text[index] != '.') continue;
            var cursor = index + 1;
            if (!CssClassName.TryRead(text, ref cursor, end, out var name, out var nameStart, out var nameLength)) continue;
            classes.Add(new CssNavigation(name, path, new SourceSpan(nameStart, nameLength)));
            index = cursor - 1;
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
        for (var marker = index - 1; marker >= 0; marker--)
        {
            if (text[marker] is '{' or '}' || char.IsWhiteSpace(text[marker])) break;
            if (text[marker] != '.') continue;
            var cursor = marker + 1;
            if (cursor == index) return new CssNavigation(string.Empty, string.Empty, new SourceSpan(cursor, 0));
            if (!CssClassName.TryRead(text, ref cursor, text.Length, out var name, out var start, out var length)) return null;
            if (index >= start && index <= start + length)
                return new CssNavigation(name, string.Empty, new SourceSpan(start, length));
            break;
        }
        return null;
    }

    private static bool IsSelectorPosition(string text, int offset) =>
        text.LastIndexOf('{', Math.Max(0, offset - 1)) <= text.LastIndexOf('}', Math.Max(0, offset - 1));
    private static bool IsIdentifierStart(char value) => CssClassName.IsStart(value);
    private static bool IsIdentifierPart(char value) => CssClassName.IsPart(value);
    private static IReadOnlyList<CssNavigation> Distinct(IEnumerable<CssNavigation> tokens) => tokens
        .OrderBy(token => token.SourcePath, PathComparer).ThenBy(token => token.Span.Start)
        .GroupBy(token => (token.Name, token.SourcePath, token.Span))
        .Select(group => group.First()).ToArray();
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal sealed record CssProjectDocument(string SourcePath, string Text,
    StyleClassOrigin Origin = StyleClassOrigin.LocalCss);
