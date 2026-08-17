using Lucent.Compiler.CodeGeneration;
using Lucent.Compiler.Parsing;
using Lucent.Compiler.Semantics;

namespace Lucent.Compiler.Styling;

internal static class StyleSheetValidator
{
    private static readonly HashSet<string> PseudoClasses = new(StringComparer.Ordinal)
    {
        ":pointerover", ":pressed", ":focus", ":focus-visible", ":disabled",
        ":checked", ":unchecked", ":selected",
    };

    public static StyleSheetValidationResult Validate(
        BoundStyleSheet sheet,
        BoundComponentModel model,
        NativeSymbolResolver resolver,
        string path,
        string sourceText)
    {
        var diagnostics = new List<LucentDiagnostic>();
        var rules = new List<BoundStyleRule>(sheet.Rules.Count);
        var controls = model.Roots.SelectMany(Controls).ToArray();
        foreach (var parsedRule in sheet.Rules)
        {
            var rule = parsedRule with
            {
                Selector = parsedRule.Selector with
                {
                    Parts = parsedRule.Selector.Parts.Select(part => part.TypeName is null
                        ? part
                        : part with { ResolvedTypeName = resolver.ResolveControl(part.TypeName)?.TypeName })
                        .ToArray(),
                },
            };
            if (rule.PseudoClass is not null && !PseudoClasses.Contains(rule.PseudoClass))
            {
                Add(diagnostics, path, sourceText, rule.Declarations.FirstOrDefault()?.Offset ?? 0,
                    $"Avalonia pseudo-class '{rule.PseudoClass}' is not supported by the Lucent CSS subset.");
                rules.Add(rule);
                continue;
            }

            foreach (var part in rule.Selector.Parts)
            {
                if (part.TypeName is not null && resolver.ResolveControl(part.TypeName) is null)
                    Add(diagnostics, path, sourceText, rule.Declarations.FirstOrDefault()?.Offset ?? 0,
                        $"CSS selector target '{part.TypeName}' is not a resolvable Avalonia control.");
            }

            var candidates = controls.Where(control => MatchesTerminal(rule, control, resolver)).ToArray();
            var targetTypes = new List<string>();
            if (candidates.Length == 0 && rule.TypeName is not null)
            {
                var resolved = resolver.ResolveControl(rule.TypeName);
                if (resolved is not null)
                {
                    targetTypes.Add(resolved.TypeName);
                    ValidateDeclarations(rule, resolved, resolver, diagnostics, path, sourceText);
                }
                rules.Add(rule with { TargetTypeNames = targetTypes });
                continue;
            }

            if (candidates.Length == 0)
            {
                targetTypes.AddRange(CssPropertyCatalog.InferProjectedTargetTypes(rule.Declarations));
                if (targetTypes.Count == 0)
                {
                    Add(diagnostics, path, sourceText, rule.Declarations.FirstOrDefault()?.Offset ?? 0,
                        $"CSS selector terminal '{rule.SelectorText}' has no common native property owner.");
                }
                else
                {
                    foreach (var targetType in targetTypes)
                    {
                        var resolved = resolver.ResolveControl(targetType.Replace("global::", string.Empty,
                            StringComparison.Ordinal));
                        if (resolved is not null)
                            ValidateDeclarations(rule, resolved, resolver, diagnostics, path, sourceText);
                    }
                }
            }

            foreach (var control in candidates)
            {
                var resolved = resolver.ResolveControl(control.Name);
                if (resolved is not null)
                {
                    targetTypes.Add(resolved.TypeName);
                    ValidateDeclarations(rule, resolved, resolver, diagnostics, path, sourceText);
                }
            }
            rules.Add(rule with
            {
                TargetTypeNames = targetTypes.Distinct(StringComparer.Ordinal).ToArray(),
            });
        }
        return new StyleSheetValidationResult(new BoundStyleSheet(rules), diagnostics);
    }

    private static void ValidateDeclarations(
        BoundStyleRule rule,
        ResolvedNativeControl control,
        NativeSymbolResolver resolver,
        List<LucentDiagnostic> diagnostics,
        string path,
        string sourceText)
    {
        foreach (var declaration in rule.Declarations)
        {
            if (declaration.PropertyName == "transition")
            {
                foreach (var candidate in declaration.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var property = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                    if (property is null || !CssPropertyCatalog.TryGet(property, out var definition) ||
                        resolver.ResolveProperty(control, definition.AvaloniaName) is null)
                    {
                        Add(diagnostics, path, sourceText, declaration.Offset,
                            $"CSS transition property '{property ?? candidate}' is not available on '{control.TypeName}'.");
                    }
                }
                continue;
            }

            if (!CssPropertyCatalog.TryGet(declaration.PropertyName, out var propertyDefinition)) continue;
            if (resolver.ResolveProperty(control, propertyDefinition.AvaloniaName) is null)
            {
                Add(diagnostics, path, sourceText, declaration.Offset,
                    $"CSS property '{declaration.PropertyName}' is not available on '{control.TypeName}'.");
            }
        }
    }

    private static bool MatchesTerminal(
        BoundStyleRule rule,
        BoundControlModel control,
        NativeSymbolResolver resolver)
    {
        var terminal = rule.Selector.Terminal;
        if (terminal.TypeName is not null)
        {
            var expected = resolver.ResolveControl(terminal.TypeName);
            var actual = resolver.ResolveControl(control.Name);
            if (expected is null || actual is null || !IsAssignableTo(actual.Symbol, expected.Symbol))
                return false;
        }
        if (terminal.Name is not null &&
            !control.Members.OfType<BoundPropertyMember>().Any(property =>
                property.Name == "Name" && property.ExpressionText.Trim('"') == terminal.Name))
            return false;
        var classes = control.Members.OfType<BoundPropertyMember>()
            .Where(property => property.Name == "Class" && property.IsStringLiteral)
            .SelectMany(property => property.ExpressionText.Trim('"').Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        return terminal.Classes.All(classes.Contains) ||
               control.Members.OfType<BoundPropertyMember>().Any(property =>
                   property.Name == "Class" && !property.IsStringLiteral);
    }

    private static bool IsAssignableTo(
        Microsoft.CodeAnalysis.INamedTypeSymbol actual,
        Microsoft.CodeAnalysis.INamedTypeSymbol expected)
    {
        for (var type = actual; type is not null; type = type.BaseType)
            if (Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(type, expected)) return true;
        return false;
    }

    private static IEnumerable<BoundControlModel> Controls(BoundRenderableModel root)
    {
        if (root is not BoundControlModel control) yield break;
        yield return control;
        foreach (var member in control.Members)
        {
            IEnumerable<BoundRenderableModel> children = member switch
            {
                BoundChildMember child => [child.Child],
                BoundConditionalMember conditional => conditional is BoundAsyncBoundary asyncBoundary
                    ? asyncBoundary.ContentRoots
                        .Concat(asyncBoundary.LoadingRoots ?? [])
                        .Concat(asyncBoundary.FallbackRoots)
                    : conditional.TrueRoots.Concat(conditional.FalseRoots ?? []),
                BoundForEachMember loop when loop.Body is not null => [loop.Body],
                _ => [],
            };
            foreach (var child in children)
                foreach (var nested in Controls(child))
                    yield return nested;
        }
    }

    private static void Add(List<LucentDiagnostic> diagnostics, string path, string sourceText, int offset, string message)
    {
        var span = new SourceSpan(Math.Clamp(offset, 0, sourceText.Length), 1);
        var (line, column) = new SourceDocument(sourceText, path).GetLineAndColumn(span.Start);
        diagnostics.Add(new LucentDiagnostic(
            "LUC4001", LucentDiagnosticSeverity.Error, message,
            span, line, column, path));
    }
}

internal sealed record StyleSheetValidationResult(
    BoundStyleSheet Sheet,
    IReadOnlyList<LucentDiagnostic> Diagnostics);
