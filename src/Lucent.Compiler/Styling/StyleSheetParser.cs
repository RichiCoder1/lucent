using System.Text.RegularExpressions;
using Lucent.Compiler.Parsing;

namespace Lucent.Compiler.Styling;

internal sealed partial class StyleSheetParser(string text, string path)
{
    private static readonly HashSet<string> SupportedProperties = new(StringComparer.Ordinal)
    {
        "background",
        "foreground",
        "opacity",
        "padding",
        "margin",
        "border-radius",
        "gap",
        "font-size",
        "font-weight",
        "width",
        "height",
        "min-width",
        "min-height",
        "max-width",
        "max-height",
        "horizontal-alignment",
        "vertical-alignment",
        "transition",
    };

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
            var match = SelectorPattern().Match(rule.Selector);
            if (!match.Success)
            {
                Add(
                    "LUC4001",
                    "The initial CSS subset supports one type selector, one class, and one Avalonia pseudo-class.",
                    rule.Offset);
                continue;
            }

            var declarations = new List<BoundStyleDeclaration>();
            foreach (var declaration in rule.Declarations)
            {
                if (!SupportedProperties.Contains(declaration.PropertyName))
                {
                    Add(
                        "LUC4001",
                        $"CSS property '{declaration.PropertyName}' is not supported by the initial Avalonia subset.",
                        rule.Offset);
                    continue;
                }

                var value = ResolveVariables(declaration.Value, rule.Offset);
                if (value is not null)
                {
                    declarations.Add(declaration with { Value = value });
                }
            }

            boundRules.Add(new BoundStyleRule(
                EmptyToNull(match.Groups["type"].Value),
                EmptyToNull(match.Groups["class"].Value.TrimStart('.')),
                EmptyToNull(match.Groups["pseudo"].Value),
                declarations));
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

            declarations.Add(new BoundStyleDeclaration(
                declaration[..colon].Trim(),
                declaration[(colon + 1)..].Trim()));
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

    [GeneratedRegex("^(?<type>[A-Za-z_][A-Za-z0-9_.]*)?(?<class>\\.[A-Za-z_][A-Za-z0-9_-]*)?(?<pseudo>:[A-Za-z_][A-Za-z0-9_-]*)?$")]
    private static partial Regex SelectorPattern();

    [GeneratedRegex(@"var\(\s*(?<name>--[A-Za-z_][A-Za-z0-9_-]*)\s*\)")]
    private static partial Regex VariablePattern();
}
