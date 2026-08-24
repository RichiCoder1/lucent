using System.Text.RegularExpressions;
using Lucent.Compiler.Parsing;

namespace Lucent.Compiler.Styling;

internal sealed partial class StyleSheetParser(string text, string path)
{
    private readonly SourceDocument _source = new(text, path);
    private readonly List<LucentDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);

    public static (BoundStyleSheet Sheet, IReadOnlyList<LucentDiagnostic> Diagnostics) Parse(
        string text,
        string path)
    {
        var parser = new StyleSheetParser(text, path);
        return (parser.Parse(), parser._diagnostics);
    }

    private BoundStyleSheet Parse()
    {
        var rules = new List<(string Selector, int Offset, List<BoundStyleDeclaration> Declarations)>();
        var position = 0;
        while (position < text.Length)
        {
            SkipTrivia(ref position);
            if (position >= text.Length)
            {
                break;
            }

            var selectorStart = position;
            var openBrace = text.IndexOf('{', position);
            if (openBrace < 0)
            {
                Add("LUC4001", "Expected '{' after the CSS selector.", selectorStart);
                break;
            }

            var selector = text[selectorStart..openBrace].Trim();
            var closeBrace = FindCloseBrace(openBrace + 1);
            if (closeBrace < 0)
            {
                Add("LUC4001", "Expected '}' to close the CSS rule.", openBrace);
                break;
            }

            var declarations = ParseDeclarations(openBrace + 1, closeBrace);
            if (selector == ":root")
            {
                foreach (var declaration in declarations)
                {
                    if (!declaration.PropertyName.StartsWith("--", StringComparison.Ordinal))
                    {
                        Add("LUC4001", "Only CSS custom properties are supported in :root.", selectorStart);
                        continue;
                    }

                    _variables[declaration.PropertyName] = declaration.Value;
                }
            }
            else
            {
                rules.Add((selector, selectorStart, declarations));
            }

            position = closeBrace + 1;
        }

        var boundRules = new List<BoundStyleRule>();
        foreach (var rule in rules)
        {
            var selector = ParseSelector(rule.Selector);
            if (selector is null)
            {
                Add(
                    "LUC4001",
                    "CSS selectors support type, class, name, child (>) and descendant selectors with one terminal Avalonia pseudo-class.",
                    rule.Offset);
                continue;
            }

            var declarations = new List<BoundStyleDeclaration>();
            foreach (var declaration in rule.Declarations)
            {
                if (!CssPropertyCatalog.TryGet(declaration.PropertyName, out var definition))
                {
                    Add(
                        "LUC4001",
                        $"CSS property '{declaration.PropertyName}' is not supported by the Avalonia CSS catalog.",
                        rule.Offset);
                    continue;
                }

                var value = ResolveVariables(declaration.Value, rule.Offset);
                if (value is not null && !CssPropertyCatalog.TryValidateValue(definition, value, out var error))
                {
                    Add("LUC4001", error!, declaration.Offset);
                    continue;
                }
                if (value is not null)
                {
                    declarations.Add(declaration with { Value = value });
                }
            }

            boundRules.Add(new BoundStyleRule(
                new BoundStyleSelector(
                    rule.Selector,
                    selector.Parts,
                    selector.Combinators,
                    selector.PseudoClass),
                declarations,
                rule.Offset));
        }

        return new BoundStyleSheet(boundRules);
    }

    private List<BoundStyleDeclaration> ParseDeclarations(int start, int end)
    {
        var declarations = new List<BoundStyleDeclaration>();
        foreach (var segment in text[start..end].Split(';'))
        {
            var declaration = segment.Trim();
            if (declaration.Length == 0)
            {
                continue;
            }

            var colon = declaration.IndexOf(':');
            if (colon <= 0 || colon == declaration.Length - 1)
            {
                Add("LUC4001", "Expected a CSS declaration in the form 'name: value'.", start);
                continue;
            }

            var segmentStart = text.IndexOf(declaration, start, end - start, StringComparison.Ordinal);
            declarations.Add(new BoundStyleDeclaration(
                declaration[..colon].Trim(),
                declaration[(colon + 1)..].Trim(),
                Math.Max(start, segmentStart)));
        }

        return declarations;
    }

    private string? ResolveVariables(string value, int offset)
    {
        for (var depth = 0; depth < 8; depth++)
        {
            var match = VariablePattern().Match(value);
            if (!match.Success)
            {
                return value;
            }

            var name = match.Groups["name"].Value;
            if (!_variables.TryGetValue(name, out var replacement))
            {
                Add("LUC4001", $"CSS variable '{name}' is not defined in :root.", offset);
                return null;
            }

            value = value[..match.Index] + replacement + value[(match.Index + match.Length)..];
        }

        Add("LUC4001", "CSS variable expansion is cyclic or too deeply nested.", offset);
        return null;
    }

    private int FindCloseBrace(int start)
    {
        var inComment = false;
        for (var index = start; index < text.Length; index++)
        {
            if (!inComment && index + 1 < text.Length && text[index] == '/' && text[index + 1] == '*')
            {
                inComment = true;
                index++;
            }
            else if (inComment && index + 1 < text.Length && text[index] == '*' && text[index + 1] == '/')
            {
                inComment = false;
                index++;
            }
            else if (!inComment && text[index] == '}')
            {
                return index;
            }
        }

        return -1;
    }

    private void SkipTrivia(ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }

            if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
            {
                var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                position = end < 0 ? text.Length : end + 2;
                continue;
            }

            break;
        }
    }

    private void Add(string code, string message, int offset)
    {
        var span = new SourceSpan(Math.Clamp(offset, 0, text.Length), 1);
        var (line, column) = _source.GetLineAndColumn(span.Start);
        _diagnostics.Add(new LucentDiagnostic(
            code,
            LucentDiagnosticSeverity.Error,
            message,
            span,
            line,
            column,
            path));
    }

    private static string? EmptyToNull(string value) =>
        value.Length == 0 ? null : value;

    [GeneratedRegex(@"var\(\s*(?<name>--[A-Za-z_][A-Za-z0-9_-]*)\s*\)")]
    private static partial Regex VariablePattern();

    private static ParsedSelector? ParseSelector(string selector)
    {
        var text = selector.Trim();
        string? pseudo = null;
        var pseudoIndex = LastUnescapedColon(text);
        if (pseudoIndex >= 0)
        {
            pseudo = text[pseudoIndex..];
            text = text[..pseudoIndex].TrimEnd();
            if (!Regex.IsMatch(pseudo, "^:[A-Za-z_][A-Za-z0-9_-]*$")) return null;
        }

        var parts = new List<string>();
        var combinators = new List<BoundStyleCombinator>();
        var index = 0;
        var pendingWhitespace = false;
        while (index < text.Length)
        {
            var whitespace = false;
            while (index < text.Length && char.IsWhiteSpace(text[index])) { whitespace = true; index++; }
            if (index >= text.Length) break;
            if (text[index] == '>')
            {
                if (parts.Count == 0 || (combinators.Count == parts.Count)) return null;
                combinators.Add(BoundStyleCombinator.Child);
                index++;
                pendingWhitespace = false;
                continue;
            }
            if (parts.Count > 0 && combinators.Count < parts.Count)
                combinators.Add(pendingWhitespace || whitespace ? BoundStyleCombinator.Descendant : BoundStyleCombinator.Descendant);
            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != '>') index++;
            if (index == start) return null;
            parts.Add(text[start..index]);
            pendingWhitespace = whitespace;
        }
        if (parts.Count == 0 || combinators.Count != parts.Count - 1) return null;
        var selectors = new List<BoundStyleSelectorPart>();
        foreach (var part in parts)
        {
            var cursor = 0;
            string? type = null;
            string? name = null;
            if (cursor < part.Length && CssClassName.IsStart(part[cursor]))
            {
                var typeStart = cursor++;
                while (cursor < part.Length && CssClassName.IsPart(part[cursor])) cursor++;
                type = part[typeStart..cursor];
            }
            if (cursor < part.Length && part[cursor] == '#')
            {
                var nameStart = ++cursor;
                if (cursor >= part.Length || !CssClassName.IsStart(part[cursor])) return null;
                while (cursor < part.Length && CssClassName.IsPart(part[cursor])) cursor++;
                name = part[nameStart..cursor];
            }
            var classes = new List<string>();
            while (cursor < part.Length && part[cursor] == '.')
            {
                cursor++;
                if (!CssClassName.TryRead(part, ref cursor, part.Length, out var @class, out _, out _)) return null;
                classes.Add(@class);
            }
            if (cursor != part.Length || type is null && name is null && classes.Count == 0) return null;
            selectors.Add(new BoundStyleSelectorPart(
                type,
                name,
                classes));
        }

        var terminal = selectors[^1];
        return new ParsedSelector(
            terminal.TypeName,
            terminal.Name,
            terminal.Classes,
            pseudo,
            selectors,
            combinators);
    }

    private sealed record ParsedSelector(
        string? TypeName,
        string? Name,
        IReadOnlyList<string> ClassNames,
        string? PseudoClass,
        IReadOnlyList<BoundStyleSelectorPart> Parts,
        IReadOnlyList<BoundStyleCombinator> Combinators);

    private static int LastUnescapedColon(string value)
    {
        for (var index = value.Length - 1; index >= 0; index--)
            if (value[index] == ':' && (index == 0 || value[index - 1] != '\\')) return index;
        return -1;
    }
}
