using Lucent.Compiler.Parsing;
using Lucent.Compiler.Semantics;
using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Compiler.CodeGeneration;

internal sealed class GeneralBinder(
    DiagnosticBag diagnostics,
    LucentProjectContext? projectContext = null)
{
    private readonly List<LucentSemanticSymbol> _symbols = [];
    private NativeSymbolResolver? _resolver;

    public IReadOnlyList<LucentSemanticSymbol> Symbols => _symbols;

    public BoundComponentModel? Bind(Lucent.Compiler.Syntax.CompilationUnitSyntax syntax)
    {
        _resolver = new NativeSymbolResolver(
            syntax.NamespaceName,
            syntax.AllUsings.Select(directive => directive.Text).ToArray(),
            projectContext);

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
                syntax.AllUsings.Select(directive => directive.Text).ToArray(),
                states,
                root);
    }

    private static BoundStateModel BindState(StateMemberSyntax state) =>
        new(
            state.TypeName,
            state.Name,
            state.InitializerText ?? state.InitialValue.ToString(),
            state.Span);

    private BoundControlModel? BindControl(
        UiElementSyntax element,
        bool insideLoop = false)
    {
        var resolver = _resolver!;
        var resolvedControl = resolver.ResolveControl(element.Name);
        if (resolvedControl is null)
        {
            AddUnsupported(
                ControlNameSpan(element),
                $"Control '{element.Name}' could not be resolved to one accessible, concrete Avalonia Control in the project context.");
            return null;
        }

        _symbols.Add(resolver.ToSemanticSymbol(resolvedControl, ControlNameSpan(element)));
        var kind = ClassifyControl(resolver, resolvedControl);
        var contentRoute = resolvedControl.ContentRoute is null
            ? null
            : new BoundContentRoute(
                resolvedControl.ContentRoute.Property.Name,
                resolvedControl.ContentRoute.IsCollection);
        var members = new List<BoundControlMember>();
        var seenMembers = new HashSet<string>(StringComparer.Ordinal);
        var childCount = 0;
        var hasStructuralLoop = false;

        foreach (var member in element.Members)
        {
            switch (member)
            {
                case UiContentSyntax content:
                    BindImplicitContent(
                        element,
                        content,
                        resolvedControl,
                        seenMembers,
                        childCount,
                        members);
                    break;

                case UiPropertySyntax property:
                    BindProperty(
                        element,
                        property,
                        resolvedControl,
                        seenMembers,
                        childCount,
                        members);
                    break;

                case UiChildSyntax child:
                    if (hasStructuralLoop)
                    {
                        AddUnsupported(
                            child.Span,
                            $"Control '{element.Name}' cannot mix a keyed foreach with ordinary child controls in the initial compiler.");
                        continue;
                    }

                    var route = resolvedControl.ContentRoute;
                    if (route is null || !resolver.ContentAcceptsControl(route))
                    {
                        AddUnsupported(
                            child.Span,
                            $"Control '{element.Name}' does not accept a native Control through its Avalonia content property.");
                        continue;
                    }

                    if (!route.IsCollection && childCount > 0)
                    {
                        AddUnsupported(
                            child.Span,
                            $"Control '{element.Name}' can contain only one child through '{route.Property.Name}'.");
                        continue;
                    }

                    if (!route.IsCollection && seenMembers.Contains(route.Property.Name))
                    {
                        AddUnsupported(
                            child.Span,
                            $"Control '{element.Name}' cannot combine explicit '{route.Property.Name}' with a nested child.");
                        continue;
                    }

                    var boundChild = BindControl(child.Element, insideLoop);
                    if (boundChild is not null)
                    {
                        members.Add(new BoundChildMember(boundChild, child.Span));
                        childCount++;
                    }

                    break;

                case UiForEachSyntax loop:
                    if (insideLoop)
                    {
                        AddUnsupported(
                            loop.Span,
                            "Nested keyed foreach regions are not supported in the initial compiler.");
                        continue;
                    }

                    if (resolvedControl.ContentRoute is not { IsCollection: true } loopRoute ||
                        !resolver.ContentAcceptsControl(loopRoute))
                    {
                        AddUnsupported(
                            loop.Span,
                            $"Control '{element.Name}' cannot host a keyed foreach because its Avalonia content route is not a compatible collection.");
                        continue;
                    }

                    if (hasStructuralLoop || childCount > 0)
                    {
                        AddUnsupported(
                            loop.Span,
                            $"Control '{element.Name}' must dedicate its child region to one keyed foreach in the initial compiler.");
                        continue;
                    }

                    var boundBody = BindControl(loop.Body, insideLoop: true);
                    if (boundBody is not null)
                    {
                        members.Add(
                            new BoundForEachMember(
                                loop.ItemName,
                                loop.SourceExpression,
                                loop.SourceExpressionSpan,
                                loop.KeyExpression,
                                loop.KeyExpressionSpan,
                                boundBody,
                                loop.Span));
                        hasStructuralLoop = true;
                    }

                    break;
            }
        }

        return new BoundControlModel(
            element.Name,
            resolvedControl.TypeName,
            members,
            element.Span,
            kind,
            contentRoute);
    }

    private void BindImplicitContent(
        UiElementSyntax element,
        UiContentSyntax content,
        ResolvedNativeControl control,
        HashSet<string> seenMembers,
        int childCount,
        List<BoundControlMember> members)
    {
        var resolver = _resolver!;
        var route = control.ContentRoute;
        if (route is null || route.IsCollection || !resolver.ContentAcceptsString(route))
        {
            AddUnsupported(
                content.Span,
                $"Control '{element.Name}' does not have an implicit scalar content property that accepts a string.");
            return;
        }

        if (childCount > 0)
        {
            AddUnsupported(
                content.Span,
                $"Control '{element.Name}' cannot combine implicit content with a nested child.");
            return;
        }

        if (!seenMembers.Add(route.Property.Name))
        {
            AddUnsupported(
                content.Span,
                $"{element.Name} may contain only one '{route.Property.Name}' member.");
            return;
        }

        var contentValue = (StringValueSyntax)content.Value;
        members.Add(
            new BoundContentMember(
                contentValue.Text,
                contentValue.Span,
                contentValue.IsInterpolated,
                content.Span));
    }

    private void BindProperty(
        UiElementSyntax element,
        UiPropertySyntax property,
        ResolvedNativeControl control,
        HashSet<string> seenMembers,
        int childCount,
        List<BoundControlMember> members)
    {
        var resolver = _resolver!;
        var resolvedEvent = resolver.ResolveEvent(control, property.Name);
        if (resolvedEvent is not null)
        {
            if (!seenMembers.Add(resolvedEvent.Name))
            {
                AddUnsupported(
                    property.Span,
                    $"{element.Name} may contain only one '{property.Name}' member.");
                return;
            }

            var boundEvent = BindEvent(property, control, resolvedEvent);
            if (boundEvent is not null)
            {
                members.Add(boundEvent);
                _symbols.Add(
                    resolver.ToSemanticSymbol(
                        control,
                        resolvedEvent,
                        PropertyNameSpan(property)));
            }

            return;
        }

        if (property.Value is EventBlockValueSyntax)
        {
            AddUnsupported(
                property.Span,
                $"Member '{property.Name}' must be assigned an explicit C# lambda; bare event blocks are not supported.");
            return;
        }

        if (property.Name == "class")
        {
            if (!seenMembers.Add("class"))
            {
                AddUnsupported(property.Span, $"{element.Name} may contain only one 'class' member.");
                return;
            }

            members.Add(
                new BoundPropertyMember(
                    "class",
                    property.Value.Text,
                    property.Value.Span,
                    property.Value is StringValueSyntax,
                    property.Value is StringValueSyntax { IsInterpolated: true },
                    property.Span));
            return;
        }

        var resolvedProperty = resolver.ResolveProperty(control, property.Name);
        if (resolvedProperty is null)
        {
            AddUnsupported(
                PropertyNameSpan(property),
                $"Property '{property.Name}' could not be resolved on {control.TypeName}.");
            return;
        }

        if (resolvedProperty.Symbol.SetMethod is not { DeclaredAccessibility: Microsoft.CodeAnalysis.Accessibility.Public })
        {
            AddUnsupported(
                PropertyNameSpan(property),
                $"Property '{resolvedProperty.Name}' on {control.TypeName} is read-only.");
            return;
        }

        if (childCount > 0 &&
            string.Equals(
                resolvedProperty.Name,
                control.ContentRoute?.Property.Name,
                StringComparison.Ordinal))
        {
            AddUnsupported(
                property.Span,
                $"Control '{element.Name}' cannot combine explicit '{property.Name}' with a nested child.");
            return;
        }

        if (!seenMembers.Add(resolvedProperty.Name))
        {
            AddUnsupported(
                property.Span,
                $"{element.Name} may contain only one '{property.Name}' member.");
            return;
        }

        members.Add(
            new BoundPropertyMember(
                resolvedProperty.Name,
                property.Value.Text,
                property.Value.Span,
                property.Value is StringValueSyntax,
                property.Value is StringValueSyntax { IsInterpolated: true },
                property.Span,
                resolvedProperty.NativeValueKind));
        _symbols.Add(
            resolver.ToSemanticSymbol(
                control,
                resolvedProperty,
                PropertyNameSpan(property)));
    }

    private BoundEventMember? BindEvent(
        UiPropertySyntax property,
        ResolvedNativeControl control,
        ResolvedNativeEvent @event)
    {
        if (property.Value is EventBlockValueSyntax)
        {
            AddUnsupported(
                property.Span,
                $"Event '{property.Name}' must use an explicit lambda such as '(sender, e) => {{ ... }}'.");
            return null;
        }

        if (property.Value is not CSharpExpressionValueSyntax expression ||
            SyntaxFactory.ParseExpression(expression.Text) is not LambdaExpressionSyntax lambda)
        {
            AddUnsupported(
                property.Span,
                $"Event '{property.Name}' must use an explicit C# lambda.");
            return null;
        }

        var parameters = lambda switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized =>
                parenthesized.ParameterList.Parameters.ToArray(),
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            _ => [],
        };
        if (parameters.Length is not 0 and not 2)
        {
            AddUnsupported(
                property.Span,
                $"Event '{property.Name}' must use either '() => ...' or '(sender, e) => ...'; one-parameter handlers are ambiguous.");
            return null;
        }

        if (parameters.Any(parameter => parameter.Type is not null))
        {
            AddUnsupported(
                property.Span,
                $"Event '{property.Name}' lambda parameters are inferred from the resolved Avalonia event and must not declare explicit types.");
            return null;
        }

        var body = lambda.Body switch
        {
            BlockSyntax block => string.Join(
                Environment.NewLine,
                block.Statements.Select(statement => statement.ToFullString().TrimEnd())),
            ExpressionSyntax bodyExpression => bodyExpression.ToFullString().Trim() + ";",
            _ => string.Empty,
        };
        return new BoundEventMember(
            property.Name,
            @event.Name,
            body,
            property.Value.Span,
            @event.DelegateTypeName,
            @event.SenderTypeName,
            @event.EventArgsTypeName,
            parameters.Length == 2 ? parameters[0].Identifier.ValueText : null,
            parameters.Length == 2 ? parameters[1].Identifier.ValueText : null,
            property.Span);
    }

    private static BoundControlKind ClassifyControl(
        NativeSymbolResolver resolver,
        ResolvedNativeControl control)
    {
        var route = control.ContentRoute;
        if (route is null)
        {
            return BoundControlKind.Unknown;
        }

        if (route.IsCollection)
        {
            return route.Property.Name == "Items"
                ? BoundControlKind.ItemsControl
                : BoundControlKind.Panel;
        }

        if (resolver.ContentAcceptsControl(route))
        {
            return route.Property.Name == "Child"
                ? BoundControlKind.Decorator
                : BoundControlKind.ContentControl;
        }

        return BoundControlKind.Text;
    }

    private static SourceSpan ControlNameSpan(UiElementSyntax element) =>
        new(element.Span.Start, element.Name.Length);

    private static SourceSpan PropertyNameSpan(UiPropertySyntax property) =>
        new(property.Span.Start, property.Name.Length);

    private bool HasErrors =>
        diagnostics.Items.Any(diagnostic =>
            diagnostic.Severity == LucentDiagnosticSeverity.Error);

    private void AddUnsupported(SourceSpan span, string message) =>
        diagnostics.Add("LUC2001", message, span);
}

internal sealed record BoundChildMember(
    BoundControlModel Child,
    SourceSpan Span) : BoundControlMember(Span);
