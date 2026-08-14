using System.Text.Json;
using System.Text.RegularExpressions;
using Lucent.Compiler.Parsing;
using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.CodeGeneration;

internal sealed partial class CounterBinder(DiagnosticBag diagnostics)
{
    public BoundCounterModel? Bind(CompilationUnitSyntax syntax)
    {
        var state = syntax.Component.State;
        if (!string.Equals(state.TypeName, "int", StringComparison.Ordinal))
        {
            AddUnsupported(
                state.Span,
                "The initial compiler supports only State<int>.");
        }

        var root = syntax.Component.RenderMethod.Root;
        if (!string.Equals(root.Name, "Column", StringComparison.Ordinal))
        {
            AddUnsupported(
                root.Span,
                "The initial compiler requires Column as the rendered root.");
        }

        ValidateProperties(root, "class");
        var children = root.Children.ToArray();
        if (children.Length != 2 ||
            !string.Equals(children[0].Name, "Text", StringComparison.Ordinal) ||
            !string.Equals(children[1].Name, "Button", StringComparison.Ordinal))
        {
            AddUnsupported(
                root.Span,
                "The initial compiler requires a Column containing one Text followed by one Button.");
            return null;
        }

        var text = children[0];
        var button = children[1];
        ValidateProperties(text, "class", "text");
        ValidateProperties(button, "class", "text", "onClick");

        var rootClass = ReadOptionalClass(root);
        var countTextClass = ReadOptionalClass(text);
        var buttonClass = ReadOptionalClass(button);

        var textValue = GetRequiredProperty(text, "text")?.Value;
        if (textValue is not StringValueSyntax { IsInterpolated: true } interpolatedText)
        {
            AddUnsupported(
                text.Span,
                "The Counter Text requires an interpolated text property.");
            return null;
        }

        var stateReference = $"{state.Name}.Value";
        if (!interpolatedText.Text.Contains(
                $"{{{stateReference}}}",
                StringComparison.Ordinal))
        {
            AddUnsupported(
                interpolatedText.Span,
                $"The text expression must read {{{stateReference}}}.");
            return null;
        }

        var generatedTextExpression = interpolatedText.Text.Replace(
            stateReference,
            $"_{state.Name}",
            StringComparison.Ordinal);

        var buttonText = GetRequiredProperty(button, "text")?.Value;
        if (buttonText is not StringValueSyntax { IsInterpolated: false } buttonTextLiteral)
        {
            AddUnsupported(
                button.Span,
                "The Counter Button requires a string text property.");
            return null;
        }

        var clickValue = GetRequiredProperty(button, "onClick")?.Value;
        if (clickValue is not EventBlockValueSyntax eventBlock)
        {
            AddUnsupported(
                button.Span,
                "The Counter Button requires an onClick event block.");
            return null;
        }

        var eventPattern = new Regex(
            $@"^\s*{Regex.Escape(state.Name)}\.Update\s*\(\s*" +
            $@"{Regex.Escape(state.Name)}\.Value\s*\+\s*(?<increment>\d+)\s*\)\s*;\s*$",
            RegexOptions.CultureInvariant);
        var match = eventPattern.Match(eventBlock.Text);
        if (!match.Success ||
            !int.TryParse(match.Groups["increment"].Value, out var increment))
        {
            AddUnsupported(
                eventBlock.Span,
                $"The initial onClick form is {state.Name}.Update({state.Name}.Value + integer);.");
            return null;
        }

        return diagnostics.Items.Any(diagnostic =>
            diagnostic.Severity == LucentDiagnosticSeverity.Error)
            ? null
            : new BoundCounterModel(
                syntax.NamespaceName,
                syntax.Component.Name,
                state.Name,
                state.InitialValue,
                rootClass,
                generatedTextExpression,
                countTextClass,
                buttonTextLiteral.Text,
                buttonClass,
                increment);
    }

    private void ValidateProperties(
        UiElementSyntax element,
        params string[] allowedNames)
    {
        var allowed = allowedNames.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.Properties)
        {
            if (!allowed.Contains(property.Name))
            {
                AddUnsupported(
                    property.Span,
                    $"Property '{property.Name}' is not supported on {element.Name} in the initial compiler.");
            }
        }
    }

    private string? ReadOptionalClass(UiElementSyntax element)
    {
        var matches = element.Properties
            .Where(candidate =>
                string.Equals(candidate.Name, "class", StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length > 1)
        {
            AddUnsupported(
                element.Span,
                $"{element.Name} may contain only one 'class' property.");
            return null;
        }

        var property = matches[0];
        if (property.Value is not StringValueSyntax { IsInterpolated: false } value)
        {
            AddUnsupported(
                property.Span,
                "The initial compiler requires class values to be string literals.");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(value.Text);
        }
        catch (JsonException)
        {
            AddUnsupported(value.Span, "The class string literal is invalid.");
            return null;
        }
    }

    private UiPropertySyntax? GetRequiredProperty(
        UiElementSyntax element,
        string name)
    {
        var matches = element.Properties
            .Where(property =>
                string.Equals(property.Name, name, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length == 1)
        {
            return matches[0];
        }

        var message = matches.Length == 0
            ? $"{element.Name} requires a '{name}' property."
            : $"{element.Name} may contain only one '{name}' property.";
        AddUnsupported(element.Span, message);
        return null;
    }

    private void AddUnsupported(SourceSpan span, string message) =>
        diagnostics.Add("LUC2001", message, span);
}
