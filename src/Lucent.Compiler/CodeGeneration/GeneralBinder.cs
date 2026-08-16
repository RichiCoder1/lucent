using Lucent.Compiler.Parsing;
using Lucent.Compiler.Semantics;
using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Compiler.CodeGeneration;

internal sealed class GeneralBinder
{
    private readonly DiagnosticBag _diagnostics;
    private readonly List<LucentSemanticSymbol> _symbols = [];
    private readonly List<CSharpIslandRequest> _requests = [];
    private readonly List<BoundIslandScope> _editorScopes = [];
    private readonly LucentProjectContext? _projectContext;
    private readonly ProjectSemanticCompilation? _semanticCompilation;
    private NativeSymbolResolver? _resolver;
    private ITypeSymbol? _dynamicType;
    private int _nextConditionalId;
    private IReadOnlyList<BoundReactiveSource> _sources = [];

    public GeneralBinder(
        DiagnosticBag diagnostics,
        LucentProjectContext? projectContext = null,
        ProjectSemanticCompilation? semanticCompilation = null)
    {
        _diagnostics = diagnostics;
        _projectContext = projectContext;
        _semanticCompilation = semanticCompilation;
    }

    public IReadOnlyList<LucentSemanticSymbol> Symbols => _symbols;
    public IReadOnlyList<BoundIslandScope> EditorScopes => _editorScopes;

    public BoundComponentModel? Bind(Lucent.Compiler.Syntax.CompilationUnitSyntax syntax)
    {
        var project = _semanticCompilation ?? new ProjectSemanticCompilation(
            syntax.NamespaceName,
            syntax.AllUsings.Select(directive => directive.Text).ToArray(),
            _projectContext);
        _resolver = new NativeSymbolResolver(project);
        _dynamicType = project.Compilation.DynamicType;

        foreach (var additional in syntax.AllComponents.Skip(1))
        {
            AddUnsupported(
                additional.Span,
                "The initial compiler emits one component per source file; split additional components into separate files.");
        }

        var component = syntax.Component;
        _sources = component.AllStateMembers
            .Select(state => (Name: state.Name, Kind: BoundReactiveSourceKind.State,
                Type: state.TypeName, state.Span))
            .Concat(component.AllComputedMembers.Select(computed =>
                (Name: computed.Name, Kind: BoundReactiveSourceKind.Computed,
                    Type: computed.TypeName, Span: computed.Span)))
            .OrderBy(source => source.Span.Start)
            .Select((source, id) => new BoundReactiveSource(
                id, source.Name, source.Kind, source.Type, source.Span))
            .ToArray();
        var states = component.AllStateMembers
            .Select(BindState)
            .Where(state => state is not null)
            .Cast<BoundStateModel>()
            .ToArray();
        var computed = component.AllComputedMembers
            .Select(BindComputed)
            .Where(candidate => candidate is not null)
            .Cast<BoundComputedModel>()
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

        var islands = new CSharpIslandBinder(project, _sources, _diagnostics).BindAll(_requests);
        var model = new BoundComponentModel(
                syntax.NamespaceName,
                component.Name,
                syntax.AllUsings.Select(directive => directive.Text).ToArray(),
                _sources,
                states.Select(state => state with { Initializer = Resolve(state.Initializer, islands) }).ToArray(),
                computed.Select(value => value with
                {
                    Factory = Resolve(value.Factory, islands),
                    InitialValue = Resolve(value.InitialValue, islands),
                }).ToArray(),
                FinalizeControl(root, islands));
        DetectComputedCycles(model);
        return HasErrors ? null : model;
    }

    private BoundStateModel BindState(StateMemberSyntax state)
    {
        var span = state.InitializerSpan ?? state.Span;
        return new(
            state.TypeName,
            state.Name,
            Request(
                state.InitializerText ?? state.InitialValue.ToString(),
                span,
                CSharpIslandKind.Expression,
                CSharpIslandRole.StateInitializer,
                _resolver!.ResolveTypeName(state.TypeName)),
            state.Span);
    }

    private BoundComputedModel? BindComputed(ComputedMemberSyntax computed)
    {
        var arguments = SyntaxFactory.ParseArgumentList(
            "(" + computed.InitializerText + ")");
        if (arguments.Arguments.Count != 2 ||
            arguments.Arguments[0].Expression is not LambdaExpressionSyntax)
        {
            AddUnsupported(
                computed.InitializerSpan,
                "Computed<T> requires a cancellation-token lambda and an initial value: new(ct => LoadAsync(ct), initialValue).");
            return null;
        }

        var factorySyntax = arguments.Arguments[0].Expression;
        var initialSyntax = arguments.Arguments[1].Expression;
        var baseStart = computed.InitializerSpan.Start - 1;
        var valueType = _resolver!.ResolveTypeName(computed.TypeName);
        var factoryType = _resolver.ResolveTypeName(
            $"global::System.Func<global::System.Threading.CancellationToken, " +
            $"global::System.Threading.Tasks.Task<{computed.TypeName}>>");
        return new BoundComputedModel(
            computed.TypeName,
            computed.Name,
            Request(
                factorySyntax.ToFullString().Trim(),
                new SourceSpan(baseStart + factorySyntax.SpanStart, factorySyntax.Span.Length),
                CSharpIslandKind.Expression,
                CSharpIslandRole.ComputedFactory,
                factoryType),
            Request(
                initialSyntax.ToFullString().Trim(),
                new SourceSpan(baseStart + initialSyntax.SpanStart, initialSyntax.Span.Length),
                CSharpIslandKind.Expression,
                CSharpIslandRole.ComputedInitialValue,
                valueType),
            computed.Span);
    }

    private BoundControlModel? BindControl(
        UiElementSyntax element,
        bool insideLoop = false,
        IReadOnlyList<BoundLocal>? locals = null,
        bool insideConditional = false)
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
        var hasStructuralRegion = false;

        foreach (var member in element.Members)
        {
            if (hasStructuralRegion && member is not UiPropertySyntax)
            {
                AddUnsupported(member.Span,
                    $"Control '{element.Name}' must dedicate its child region to one structural member.");
                continue;
            }

            switch (member)
            {
                case UiContentSyntax content:
                    BindImplicitContent(
                        element,
                        content,
                        resolvedControl,
                        seenMembers,
                        childCount,
                        members,
                        locals ?? []);
                    break;

                case UiPropertySyntax property:
                    BindProperty(
                        element,
                        property,
                        resolvedControl,
                        seenMembers,
                        childCount,
                        members,
                        locals ?? []);
                    break;

                case UiChildSyntax child:
                    if (hasStructuralRegion)
                    {
                        AddUnsupported(
                            child.Span,
                            $"Control '{element.Name}' cannot mix a structural region with ordinary child controls in this compiler subset.");
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

                    var boundChild = BindControl(child.Element, insideLoop, locals, insideConditional);
                    if (boundChild is not null)
                    {
                        members.Add(new BoundChildMember(boundChild, child.Span));
                        childCount++;
                    }

                    break;

                case UiForEachSyntax loop:
                    if (insideLoop || insideConditional)
                    {
                        AddUnsupported(
                            loop.Span,
                            "Keyed foreach regions are not supported inside another structural region.");
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

                    if (hasStructuralRegion || childCount > 0 ||
                        seenMembers.Contains(loopRoute.Property.Name))
                    {
                        AddUnsupported(
                            loop.Span,
                            $"Control '{element.Name}' must dedicate its child region to one keyed foreach in the initial compiler.");
                        continue;
                    }

                    var loopLocals = new[] { new BoundLocal(loop.ItemName, _dynamicType!) };
                    var boundBody = BindControl(loop.Body, insideLoop: true, loopLocals);
                    if (boundBody is not null)
                    {
                        members.Add(
                            new BoundForEachMember(
                                loop.ItemName,
                                Request(loop.SourceExpression, loop.SourceExpressionSpan,
                                    CSharpIslandKind.Expression, CSharpIslandRole.LoopSource, null),
                                Request(loop.KeyExpression, loop.KeyExpressionSpan,
                                    CSharpIslandKind.Expression, CSharpIslandRole.LoopKey, null,
                                    loopLocals),
                                boundBody,
                                loop.Span));
                        hasStructuralRegion = true;
                    }

                    break;

                case UiIfSyntax conditional:
                    if (insideLoop)
                    {
                        AddUnsupported(conditional.Span, "Conditionals inside keyed rows are not supported in this compiler subset.");
                        continue;
                    }
                    if (insideConditional)
                    {
                        AddUnsupported(conditional.Span, "Nested conditionals are not supported in this compiler subset.");
                        continue;
                    }
                    if (resolvedControl.ContentRoute is not { } conditionalRoute ||
                        !resolver.ContentAcceptsControl(conditionalRoute))
                    {
                        AddUnsupported(conditional.Span,
                            $"Control '{element.Name}' cannot host a conditional because it has no compatible native content route.");
                        continue;
                    }
                    if (hasStructuralRegion || childCount > 0 ||
                        seenMembers.Contains(conditionalRoute.Property.Name))
                    {
                        AddUnsupported(conditional.Span,
                            $"Control '{element.Name}' must dedicate its child region to one conditional.");
                        continue;
                    }

                    var conditionalId = _nextConditionalId++;
                    var trueRoot = BindControl(conditional.TrueRoot, locals: locals,
                        insideConditional: true);
                    var falseRoot = conditional.FalseRoot is null
                        ? null
                        : BindControl(conditional.FalseRoot, locals: locals,
                            insideConditional: true);
                    if (trueRoot is not null &&
                        (conditional.FalseRoot is null || falseRoot is not null))
                    {
                        members.Add(new BoundConditionalMember(
                            conditionalId,
                            Request(conditional.Condition, conditional.ConditionSpan,
                                CSharpIslandKind.Expression, CSharpIslandRole.Condition,
                                resolver.ResolveTypeName("bool"), locals),
                            trueRoot,
                            falseRoot,
                            conditional.Span));
                        hasStructuralRegion = true;
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
        List<BoundControlMember> members,
        IReadOnlyList<BoundLocal> locals)
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
                Request(contentValue.Text, contentValue.Span,
                    CSharpIslandKind.Expression, CSharpIslandRole.Content, route.ValueType, locals),
                contentValue.IsInterpolated,
                content.Span,
                route.ValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    private void BindProperty(
        UiElementSyntax element,
        UiPropertySyntax property,
        ResolvedNativeControl control,
        HashSet<string> seenMembers,
        int childCount,
        List<BoundControlMember> members,
        IReadOnlyList<BoundLocal> locals)
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

            var boundEvent = BindEvent(property, control, resolvedEvent, locals);
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

        if (property.Name == "Class")
        {
            if (!seenMembers.Add("Class"))
            {
                AddUnsupported(property.Span, $"{element.Name} may contain only one 'Class' member.");
                return;
            }

            members.Add(
                new BoundPropertyMember(
                    "Class",
                    Request(property.Value.Text, property.Value.Span,
                        CSharpIslandKind.Expression, CSharpIslandRole.Property, null, locals),
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
                Request(property.Value.Text, property.Value.Span,
                    CSharpIslandKind.Expression, CSharpIslandRole.Property,
                    resolvedProperty.NativeValueKind == BoundNativeValueKind.None
                        ? resolvedProperty.Symbol.Type
                        : null,
                    locals),
                property.Value is StringValueSyntax,
                property.Value is StringValueSyntax { IsInterpolated: true },
                property.Span,
                resolvedProperty.NativeValueKind,
                resolvedProperty.TypeName));
        _symbols.Add(
            resolver.ToSemanticSymbol(
                control,
                resolvedProperty,
                PropertyNameSpan(property)));
        _symbols.AddRange(resolver.GetValueSymbols(
            resolvedProperty,
            property.Value.Text,
            property.Value.Span.Start));
    }

    private BoundEventMember? BindEvent(
        UiPropertySyntax property,
        ResolvedNativeControl control,
        ResolvedNativeEvent @event,
        IReadOnlyList<BoundLocal> enclosingLocals)
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
        var bodySpan = lambda.Body is BlockSyntax bodyBlock
            ? new SourceSpan(
                expression.Span.Start + bodyBlock.OpenBraceToken.Span.End,
                bodyBlock.CloseBraceToken.SpanStart - bodyBlock.OpenBraceToken.Span.End)
            : new SourceSpan(
                expression.Span.Start + lambda.Body.SpanStart,
                lambda.Body.Span.Length);
        var locals = enclosingLocals.ToList();
        if (parameters.Length == 2)
        {
            locals.Add(new BoundLocal(parameters[0].Identifier.ValueText,
                _resolver!.ResolveTypeName(control.TypeName)!));
            locals.Add(new BoundLocal(parameters[1].Identifier.ValueText,
                _resolver.ResolveTypeName(@event.EventArgsTypeName)!));
        }
        _editorScopes.Add(new BoundIslandScope(property.Value.Span, locals));
        return new BoundEventMember(
            property.Name,
            @event.Name,
            Request(body, bodySpan, CSharpIslandKind.StatementBlock,
                CSharpIslandRole.EventBody, null, locals),
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

    private BoundCSharpIsland Request(
        string text,
        SourceSpan span,
        CSharpIslandKind kind,
        CSharpIslandRole role,
        ITypeSymbol? expectedType,
        IReadOnlyList<BoundLocal>? locals = null)
    {
        var id = _requests.Count;
        _requests.Add(new CSharpIslandRequest(id, text, span, kind, role, expectedType, locals ?? []));
        return new BoundCSharpIsland(text, $"__request:{id}", span, kind, [], []);
    }

    private static BoundCSharpIsland Resolve(
        BoundCSharpIsland island,
        IReadOnlyDictionary<int, BoundCSharpIsland> islands) =>
        island.LoweredText.StartsWith("__request:", StringComparison.Ordinal)
            ? islands[int.Parse(island.LoweredText[10..], System.Globalization.CultureInfo.InvariantCulture)]
            : island;

    private static BoundControlModel FinalizeControl(
        BoundControlModel control,
        IReadOnlyDictionary<int, BoundCSharpIsland> islands) =>
        control with
        {
            Members = control.Members.Select(member => member switch
            {
                BoundPropertyMember property => property with { Expression = Resolve(property.Expression, islands) },
                BoundContentMember content => content with { Expression = Resolve(content.Expression, islands) },
                BoundEventMember eventMember => eventMember with { Body = Resolve(eventMember.Body, islands) },
                BoundForEachMember loop => loop with
                {
                    SourceExpression = Resolve(loop.SourceExpression, islands),
                    KeyExpression = Resolve(loop.KeyExpression, islands),
                    Body = FinalizeControl(loop.Body, islands),
                },
                BoundConditionalMember conditional => conditional with
                {
                    Condition = Resolve(conditional.Condition, islands),
                    TrueRoot = FinalizeControl(conditional.TrueRoot, islands),
                    FalseRoot = conditional.FalseRoot is null
                        ? null
                        : FinalizeControl(conditional.FalseRoot, islands),
                },
                BoundChildMember child => child with { Child = FinalizeControl(child.Child, islands) },
                _ => member,
            }).ToArray(),
        };

    private void DetectComputedCycles(BoundComponentModel model)
    {
        var computedById = model.Computed.ToDictionary(
            computed => model.Sources.First(source => source.Name == computed.Name).Id);
        var visiting = new HashSet<int>();
        var visited = new HashSet<int>();
        void Visit(int id)
        {
            if (!visiting.Add(id))
            {
                return;
            }

            foreach (var dependency in computedById[id].Factory.Dependencies.Where(computedById.ContainsKey))
            {
                if (visiting.Contains(dependency))
                {
                    foreach (var read in computedById[id].Factory.ReactiveReads
                                 .Where(candidate => candidate.SourceId == dependency))
                    {
                        _diagnostics.Add("LUC2001", "Computed dependencies must not contain a self-reference or cycle.", read.Span);
                    }
                }
                else if (!visited.Contains(dependency))
                {
                    Visit(dependency);
                }
            }

            visiting.Remove(id);
            visited.Add(id);
        }

        foreach (var id in computedById.Keys)
        {
            Visit(id);
        }
    }

    private bool HasErrors =>
        _diagnostics.Items.Any(diagnostic =>
            diagnostic.Severity == LucentDiagnosticSeverity.Error);

    private void AddUnsupported(SourceSpan span, string message) =>
        _diagnostics.Add("LUC2001", message, span);
}

internal sealed record BoundChildMember(
    BoundControlModel Child,
    SourceSpan Span) : BoundControlMember(Span);
