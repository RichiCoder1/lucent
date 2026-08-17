namespace Lucent.Compiler.Styling;

using System.Globalization;
using System.Text.RegularExpressions;

internal enum CssValueKind
{
    Brush,
    Double,
    PositiveDouble,
    NonNegativeDouble,
    Thickness,
    CornerRadius,
    BoxShadows,
    FontFamily,
    FontStyle,
    FontWeight,
    TextAlignment,
    TextWrapping,
    HorizontalAlignment,
    VerticalAlignment,
    HorizontalContentAlignment,
    VerticalContentAlignment,
    Boolean,
    Cursor,
    Transition,
}

internal sealed record CssPropertyDefinition(
    string CssName,
    string AvaloniaName,
    CssValueKind ValueKind,
    string? TransitionType,
    bool SupportsResource = true,
    IReadOnlyList<string>? ProjectedTargetTypes = null);

internal static class CssPropertyCatalog
{
    private static readonly IReadOnlyDictionary<string, CssPropertyDefinition> Definitions =
        new Dictionary<string, CssPropertyDefinition>(StringComparer.Ordinal)
        {
            ["background"] = new("background", "Background", CssValueKind.Brush, "BrushTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.Border", "global::Avalonia.Controls.Panel", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["foreground"] = new("foreground", "Foreground", CssValueKind.Brush, "BrushTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.TextBlock", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["opacity"] = new("opacity", "Opacity", CssValueKind.Double, "DoubleTransition"),
            ["padding"] = new("padding", "Padding", CssValueKind.Thickness, "ThicknessTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.Border", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["margin"] = new("margin", "Margin", CssValueKind.Thickness, "ThicknessTransition"),
            ["border-color"] = new("border-color", "BorderBrush", CssValueKind.Brush, "BrushTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.Border", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["border-width"] = new("border-width", "BorderThickness", CssValueKind.Thickness, "ThicknessTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.Border", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["border-radius"] = new("border-radius", "CornerRadius", CssValueKind.CornerRadius, "CornerRadiusTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.Border", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["box-shadow"] = new("box-shadow", "BoxShadow", CssValueKind.BoxShadows, "BoxShadowsTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.Border"]),
            ["gap"] = new("gap", "Spacing", CssValueKind.Double, "DoubleTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.StackPanel"]),
            ["font-family"] = new("font-family", "FontFamily", CssValueKind.FontFamily, null,
                ProjectedTargetTypes: ["global::Avalonia.Controls.TextBlock", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["font-size"] = new("font-size", "FontSize", CssValueKind.PositiveDouble, "DoubleTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.TextBlock", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["font-style"] = new("font-style", "FontStyle", CssValueKind.FontStyle, null,
                ProjectedTargetTypes: ["global::Avalonia.Controls.TextBlock", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["font-weight"] = new("font-weight", "FontWeight", CssValueKind.FontWeight, null,
                ProjectedTargetTypes: ["global::Avalonia.Controls.TextBlock", "global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["text-align"] = new("text-align", "TextAlignment", CssValueKind.TextAlignment, null,
                ProjectedTargetTypes: ["global::Avalonia.Controls.TextBlock"]),
            ["text-wrap"] = new("text-wrap", "TextWrapping", CssValueKind.TextWrapping, null,
                ProjectedTargetTypes: ["global::Avalonia.Controls.TextBlock"]),
            ["line-height"] = new("line-height", "LineHeight", CssValueKind.NonNegativeDouble, "DoubleTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.TextBlock"]),
            ["letter-spacing"] = new("letter-spacing", "LetterSpacing", CssValueKind.Double, "DoubleTransition",
                ProjectedTargetTypes: ["global::Avalonia.Controls.TextBlock"]),
            ["width"] = new("width", "Width", CssValueKind.NonNegativeDouble, "DoubleTransition"),
            ["height"] = new("height", "Height", CssValueKind.NonNegativeDouble, "DoubleTransition"),
            ["min-width"] = new("min-width", "MinWidth", CssValueKind.NonNegativeDouble, "DoubleTransition"),
            ["min-height"] = new("min-height", "MinHeight", CssValueKind.NonNegativeDouble, "DoubleTransition"),
            ["max-width"] = new("max-width", "MaxWidth", CssValueKind.NonNegativeDouble, "DoubleTransition"),
            ["max-height"] = new("max-height", "MaxHeight", CssValueKind.NonNegativeDouble, "DoubleTransition"),
            ["horizontal-alignment"] = new("horizontal-alignment", "HorizontalAlignment", CssValueKind.HorizontalAlignment, null),
            ["vertical-alignment"] = new("vertical-alignment", "VerticalAlignment", CssValueKind.VerticalAlignment, null),
            ["horizontal-content-alignment"] = new("horizontal-content-alignment", "HorizontalContentAlignment", CssValueKind.HorizontalContentAlignment, null,
                ProjectedTargetTypes: ["global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["vertical-content-alignment"] = new("vertical-content-alignment", "VerticalContentAlignment", CssValueKind.VerticalContentAlignment, null,
                ProjectedTargetTypes: ["global::Avalonia.Controls.Primitives.TemplatedControl"]),
            ["visibility"] = new("visibility", "IsVisible", CssValueKind.Boolean, null),
            ["clip-to-bounds"] = new("clip-to-bounds", "ClipToBounds", CssValueKind.Boolean, null),
            ["cursor"] = new("cursor", "Cursor", CssValueKind.Cursor, null),
            ["transition"] = new("transition", "Transitions", CssValueKind.Transition, null, false),
        };

    public static IReadOnlyCollection<CssPropertyDefinition> All => Definitions.Values.ToArray();

    public static bool TryGet(string name, out CssPropertyDefinition definition) =>
        Definitions.TryGetValue(name, out definition!);

    public static bool TryGetResourceKey(string value, out string key)
    {
        key = string.Empty;
        if (!value.StartsWith("resource(", StringComparison.Ordinal) || !value.EndsWith(')'))
            return false;

        var candidate = value[9..^1].Trim();
        if (candidate.Length >= 2 && candidate[0] is '\'' or '"')
        {
            if (candidate[^1] != candidate[0]) return false;
            candidate = candidate[1..^1];
        }

        key = candidate;
        return key.Length > 0;
    }

    public static IReadOnlyList<string> InferProjectedTargetTypes(
        IReadOnlyList<BoundStyleDeclaration> declarations)
    {
        var constrained = declarations
            .SelectMany(ProjectedDefinitions)
            .Where(definition => definition.ProjectedTargetTypes is { Count: > 0 })
            .Select(definition => definition.ProjectedTargetTypes!)
            .ToArray();
        if (constrained.Length == 0) return ["global::Avalonia.Controls.Control"];
        return constrained.Skip(1).Aggregate(
                constrained[0].ToHashSet(StringComparer.Ordinal),
                (targets, next) =>
                {
                    targets.IntersectWith(next);
                    return targets;
                })
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool SupportsProjectedTarget(
        string targetType,
        BoundStyleDeclaration declaration) =>
        ProjectedDefinitions(declaration).All(definition =>
            definition.ProjectedTargetTypes is not { Count: > 0 } targets || targets.Contains(targetType));

    private static IEnumerable<CssPropertyDefinition> ProjectedDefinitions(
        BoundStyleDeclaration declaration)
    {
        if (declaration.PropertyName != "transition")
        {
            if (TryGet(declaration.PropertyName, out var definition)) yield return definition;
            yield break;
        }

        foreach (var candidate in declaration.Value.Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var property = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (property is not null && TryGet(property, out var definition)) yield return definition;
        }
    }

    public static bool TryValidateValue(
        CssPropertyDefinition definition,
        string value,
        out string? error)
    {
        error = null;
        if (value.StartsWith("resource(", StringComparison.Ordinal) && value.EndsWith(')'))
        {
            if (!definition.SupportsResource)
                error = $"CSS property '{definition.CssName}' does not accept dynamic resources.";
            else if (!TryGetResourceKey(value, out _))
                error = "CSS resource keys must be non-empty and use matching quotes.";
            return error is null;
        }

        bool Number(string candidate, bool nonNegative = false, bool positive = false)
        {
            var raw = candidate.EndsWith("px", StringComparison.OrdinalIgnoreCase)
                ? candidate[..^2]
                : candidate;
            if (candidate.Contains('%', StringComparison.Ordinal) ||
                !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                !double.IsFinite(parsed) || (nonNegative && parsed < 0) || (positive && parsed <= 0))
                return false;
            return true;
        }

        bool AnyNumbers(string candidate, int min, int max, bool nonNegative = false) =>
            candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                is var values && values.Length >= min && values.Length <= max &&
                values.All(item => Number(item, nonNegative));

        switch (definition.ValueKind)
        {
            case CssValueKind.Brush:
                if (!Regex.IsMatch(value, "^(#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|transparent|black|white)$"))
                    error = $"CSS value '{value}' is not a supported brush.";
                break;
            case CssValueKind.Double:
                if (!Number(value)) error = $"CSS value '{value}' must be a finite number.";
                break;
            case CssValueKind.PositiveDouble:
                if (!Number(value, positive: true)) error = $"CSS value '{value}' must be a positive finite number.";
                break;
            case CssValueKind.NonNegativeDouble:
                if (!Number(value, nonNegative: true) &&
                    !((definition.CssName is "width" or "height" or "max-width" or "max-height") &&
                      value is "auto" or "none"))
                    error = $"CSS value '{value}' must be a non-negative finite number.";
                break;
            case CssValueKind.Thickness:
                if (!AnyNumbers(value, 1, 4, true)) error = $"CSS thickness '{value}' is invalid.";
                break;
            case CssValueKind.CornerRadius:
                if (!AnyNumbers(value, 1, 4, true)) error = $"CSS corner radius '{value}' is invalid.";
                break;
            case CssValueKind.BoxShadows:
                if (!ValidateBoxShadows(value, (candidate, nonNegative) => Number(candidate, nonNegative), out error)) { }
                break;
            case CssValueKind.FontFamily:
                if (string.IsNullOrWhiteSpace(value)) error = "CSS font family cannot be empty.";
                break;
            case CssValueKind.FontStyle:
                if (value is not ("normal" or "italic")) error = $"CSS font style '{value}' is invalid.";
                break;
            case CssValueKind.FontWeight:
                if (value is not ("normal" or "medium" or "semibold" or "bold" or "400" or "500" or "600" or "700" or "800"))
                    error = $"CSS font weight '{value}' is invalid.";
                break;
            case CssValueKind.TextAlignment:
                if (value is not ("start" or "left" or "center" or "right" or "end" or "justify"))
                    error = $"CSS text alignment '{value}' is invalid.";
                break;
            case CssValueKind.TextWrapping:
                if (value is not ("wrap" or "nowrap")) error = $"CSS text wrapping '{value}' is invalid.";
                break;
            case CssValueKind.HorizontalAlignment:
                if (value is not ("left" or "center" or "right" or "stretch")) error = $"CSS horizontal alignment '{value}' is invalid.";
                break;
            case CssValueKind.VerticalAlignment:
                if (value is not ("top" or "center" or "bottom" or "stretch")) error = $"CSS vertical alignment '{value}' is invalid.";
                break;
            case CssValueKind.HorizontalContentAlignment:
            case CssValueKind.VerticalContentAlignment:
                if (value is not ("left" or "center" or "right" or "top" or "bottom" or "stretch"))
                    error = $"CSS content alignment '{value}' is invalid.";
                break;
            case CssValueKind.Boolean:
                if (value is not ("true" or "false")) error = $"CSS boolean '{value}' is invalid.";
                break;
            case CssValueKind.Cursor:
                if (value is not ("arrow" or "hand" or "ibeam" or "wait" or "cross" or "help" or "none"))
                    error = $"CSS cursor '{value}' is invalid.";
                break;
            case CssValueKind.Transition:
                if (!TryValidateTransitions(value, out error)) { }
                break;
        }
        return error is null;
    }

    public static bool TryValidateTransitions(string value, out string? error)
    {
        error = null;
        foreach (var candidate in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is < 2 or > 3 || !TryGet(parts[0], out var property) || property.TransitionType is null)
            {
                error = $"CSS transition property '{parts.FirstOrDefault() ?? candidate}' is not animatable by the catalog.";
                return false;
            }
            var duration = parts[1];
            var raw = duration.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ? duration[..^2] :
                duration.EndsWith('s') ? duration[..^1] : string.Empty;
            var multiplier = duration.EndsWith('s') && !duration.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ? 1000 : 1;
            if (raw.Length == 0 || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number) || number < 0 || number * multiplier > 86_400_000)
            {
                error = $"CSS transition duration '{duration}' is invalid.";
                return false;
            }
            if (parts.Length == 3 && parts[2] is not ("linear" or "ease-in" or "ease-out" or "ease-in-out"))
            {
                error = $"CSS easing '{parts[2]}' is invalid.";
                return false;
            }
        }
        return true;
    }

    private static bool ValidateBoxShadows(string value, Func<string, bool, bool> number, out string? error)
    {
        error = null;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 4 || !number(parts[0], false) || !number(parts[1], false) ||
            !number(parts[2], true) || (parts.Length == 4 && !Regex.IsMatch(parts[3], "^(#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|transparent|black|white)$")))
            error = $"CSS box shadow '{value}' is invalid.";
        return error is null;
    }
}
