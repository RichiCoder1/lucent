using Lucent.Compiler.Parsing;
using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.CodeGeneration;

internal sealed class GeneralBinder(DiagnosticBag diagnostics)
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProperties =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Column"] = ["class"],
            ["Text"] = ["class", "text"],
            ["Button"] = ["class", "text", "onClick"],
        };

    public BoundComponentModel? Bind(CompilationUnitSyntax syntax)
    {
        foreach (var additional in syntax.AllComponents.Skip(1))
        {
            AddUnsupported(
                additional.Span,
                "The initial compiler emits one component per source file; split additional components into separate files.");
        }

        var component = syntax.Component;
        var states = component.AllStateMembers
            .Select(BindState)
            .Where(state => state is not null)
            .Cast<BoundStateModel>()
            .ToArray();

        if (component.RenderMethod.Root.Name == "Missing")
        {
            return null;
        }

        var root = BindControl(component.RenderMethod.Root);
        if (root is null)
        {
            return null;
        }

        return HasErrors
            ? null
            : new BoundComponentModel(
                syntax.NamespaceName,
                component.Name,
                states,
                root);
    }

    private BoundStateModel? BindState(StateMemberSyntax state)
    {
        if (!string.Equals(state.TypeName, "int", StringComparison.Ordinal))
        {
            AddUnsupported(
                state.Span,
                "The initial compiler supports State<int> members.");
            return null;
        }

        return new BoundStateModel(
            state.TypeName,
            state.Name,
            state.InitializerText ?? state.InitialValue.ToString(),
            state.Span);
    }

    private BoundControlModel? BindControl(UiElementSyntax element)
    {
        if (!AllowedProperties.TryGetValue(element.Name, out var allowed))
        {
            AddUnsupported(
                element.Span,
                $"Control '{element.Name}' is not supported by the initial Avalonia projection.");
            return null;
        }

        var members = new List<BoundControlMember>();
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in element.Members)
        {
            switch (member)
            {
                case UiPropertySyntax property:
                    if (!seenProperties.Add(property.Name))
                    {
                        AddUnsupported(
                            property.Span,
                            $"{element.Name} may contain only one '{property.Name}' property.");
                        continue;
                    }

                    if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                    {
                        AddUnsupported(
                            property.Span,
                            $"Property '{property.Name}' is not supported on {element.Name}.");
                        continue;
                    }

                    if (property.Name == "onClick" &&
                        property.Value is EventBlockValueSyntax eventBlock)
                    {
                        if (!string.Equals(property.Name, "onClick", StringComparison.Ordinal))
                        {
                            AddUnsupported(
                                property.Span,
                                $"Block-valued property '{property.Name}' is not a supported event.");
                            continue;
                        }

                        members.Add(
                            new BoundEventMember(
                                property.Name,
                                eventBlock.Text,
                                eventBlock.Span,
                                IsExpression: false,
                                property.Span));
                    }
                    else if (property.Name == "onClick")
                    {
                        members.Add(
                            new BoundEventMember(
                                property.Name,
                                property.Value.Text,
                                property.Value.Span,
                                IsExpression: true,
                                property.Span));
                    }
                    else
                    {
                        members.Add(
                            new BoundPropertyMember(
                                property.Name,
                                property.Value.Text,
                                property.Value.Span,
                                property.Value is StringValueSyntax,
                                property.Value is StringValueSyntax { IsInterpolated: true },
                                property.Span));
                    }

                    break;

                case UiChildSyntax child:
                    var boundChild = BindControl(child.Element);
                    if (boundChild is not null)
                    {
                        members.Add(new BoundChildMember(boundChild, child.Span));
                    }

                    break;
            }
        }

        return new BoundControlModel(element.Name, members, element.Span);
    }

    private bool HasErrors =>
        diagnostics.Items.Any(diagnostic =>
            diagnostic.Severity == LucentDiagnosticSeverity.Error);

    private void AddUnsupported(SourceSpan span, string message) =>
        diagnostics.Add("LUC2001", message, span);
}

internal sealed record BoundChildMember(
    BoundControlModel Child,
    SourceSpan Span) : BoundControlMember(Span);
