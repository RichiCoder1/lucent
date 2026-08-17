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
    private readonly HashSet<string> _asyncBoundaryValueSources = new(StringComparer.Ordinal);
    private bool _componentHasAsyncBoundary;
    private IReadOnlyList<BoundIslandScope> _editorScopes = [];
    private readonly LucentProjectContext? _projectContext;
    private readonly ProjectSemanticCompilation? _semanticCompilation;
    private readonly ComponentIndex? _componentIndex;
    private readonly string? _sourcePath;
    private NativeSymbolResolver? _resolver;
    private ITypeSymbol? _dynamicType;
    private int _nextConditionalId;
    private int _nextComponentSiteId;
    private IReadOnlyList<BoundReactiveSource> _sources = [];
    private ComponentSymbol? _currentComponent;

    public GeneralBinder(
        DiagnosticBag diagnostics,
        LucentProjectContext? projectContext = null,
        ProjectSemanticCompilation? semanticCompilation = null,
        ComponentIndex? componentIndex = null,
        string? sourcePath = null)
    {
        _diagnostics = diagnostics;
        _projectContext = projectContext;
        _semanticCompilation = semanticCompilation;
        _componentIndex = componentIndex;
        _sourcePath = sourcePath;
    }

    public IReadOnlyList<LucentSemanticSymbol> Symbols => _symbols;
    public IReadOnlyList<BoundIslandScope> EditorScopes => _editorScopes;
    public IReadOnlyList<BoundEditorVariable> EditorVariables { get; private set; } = [];

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
        _componentHasAsyncBoundary = ContainsAsyncBoundary(component.RenderMethod.RenderedFragment.Roots);
        _currentComponent = _componentIndex?.Symbols.FirstOrDefault(symbol =>
            string.Equals(symbol.SourcePath, _sourcePath, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (_currentComponent is not null)
        {
            _symbols.Add(new LucentSemanticSymbol(_currentComponent.Name,
                LucentSemanticSymbolKind.Component, _currentComponent.DeclarationSpan,
                $"component {_currentComponent.NamespaceName}.{_currentComponent.Name}",
                Definition: new LucentDefinition(_currentComponent.SourcePath,
                    _currentComponent.DeclarationSpan)));
            _symbols.AddRange(component.AllParameters.Select(parameter =>
                new LucentSemanticSymbol(parameter.Name,
                    LucentSemanticSymbolKind.ComponentParameter, parameter.NameSpan,
                    $"{parameter.TypeName} {parameter.Name}")));
            _symbols.AddRange(component.AllSlots.Select(slot =>
                new LucentSemanticSymbol(slot.Name,
                    LucentSemanticSymbolKind.ComponentSlot, slot.NameSpan,
                    $"slot {slot.Name}")));
        }
        ValidateDeclarationNames(component);
        ValidateOrdinaryMembers(component.AllOrdinaryMembers);
        foreach (var parameter in component.AllParameters)
        {
            if (_resolver.ResolveTypeName(parameter.TypeName) is null)
            {
                AddUnsupported(parameter.Span,
                    $"Component parameter type '{parameter.TypeName}' could not be resolved.");
            }
        }
        _sources = component.AllParameters
            .Select(parameter => (Name: parameter.Name, Kind: BoundReactiveSourceKind.Parameter,
                Type: parameter.TypeName, parameter.Span))
            .Concat(component.AllStateMembers
            .Select(state => (Name: state.Name, Kind: BoundReactiveSourceKind.State,
                Type: state.TypeName, state.Span)))
            .Concat(component.AllComputedMembers.Select(computed =>
                (Name: computed.Name, Kind: BoundReactiveSourceKind.Computed,
                    Type: computed.TypeName, Span: computed.Span)))
            .OrderBy(source => source.Span.Start)
            .Select((source, id) => new BoundReactiveSource(
                id, source.Name, source.Kind, source.Type, source.Span))
            .ToArray();
        EditorVariables = _sources
            .Select(source => (Source: source, Type: _resolver.ResolveTypeName(source.ValueTypeName)))
            .Where(candidate => candidate.Type is not null)
            .Select(candidate => new BoundEditorVariable(
                candidate.Source.Name,
                candidate.Type!,
                candidate.Source.Kind switch
                {
                    BoundReactiveSourceKind.State => BoundEditorVariableKind.State,
                    BoundReactiveSourceKind.Computed => BoundEditorVariableKind.Computed,
                    _ => BoundEditorVariableKind.Parameter,
                },
                $"private readonly {candidate.Source.Kind}<{candidate.Source.ValueTypeName}> {candidate.Source.Name}"))
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

        if (component.RenderMethod.Root.Name == "Missing" && component.RenderMethod.Fragment is null)
        {
            return null;
        }

        var roots = component.RenderMethod.RenderedFragment.Roots
            .Select(element => BindRenderable(element))
            .Where(root => root is not null)
            .Cast<BoundRenderableModel>()
            .ToArray();
        var root = roots.OfType<BoundControlModel>().FirstOrDefault() ??
            new BoundControlModel("Missing", "global::Avalonia.Controls.Control", [],
                component.RenderMethod.RenderedFragment.Span);

        var islandBinding = new CSharpIslandBinder(
            project, _sources, _diagnostics, component.AllOrdinaryMembers).BindAll(_requests);
        _editorScopes = islandBinding.EditorScopes;
        var islands = islandBinding.Islands;
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
                FinalizeControl(root, islands),
                component.AllParameters.Select(parameter => new BoundParameterModel(
                    parameter.TypeName, parameter.Name, parameter.DefaultValueText, parameter.Span)).ToArray(),
                (_currentComponent?.Slots ?? []).Select(slot => new BoundSlotModel(slot.Name, slot.Span)).ToArray(),
                roots.Select(candidate => FinalizeRenderable(candidate, islands)).ToArray(),
                component.AllOrdinaryMembers.Select(member => new BoundOrdinaryMemberModel(
                    islandBinding.OrdinaryMembers?.GetValueOrDefault(member.Name) ?? member.Text,
                    member.Name, member.Span)).ToArray());
        foreach (var collection in EnumerateControls(model.Roots)
                     .SelectMany(control => control.Members.OfType<BoundNativeCollectionMember>()))
        {
            foreach (var element in collection.Elements.Where(element => element.Dependencies.Count > 0))
            {
                AddUnsupported(element.Span,
                    "Mount-only native collection elements cannot read reactive state or parameters.");
            }
        }
        foreach (var template in EnumerateControls(model.Roots)
                     .SelectMany(control => control.Members.OfType<BoundItemTemplateMember>()))
        {
            if (ContainsReactiveTemplateDependency(template.Root))
            {
                AddUnsupported(template.Span,
                    "ItemTemplate property values must depend on the typed item; component state, inputs, and computed values are not refreshed by Avalonia template realization.");
            }
        }
        DetectComputedCycles(model);
        return HasErrors ? null : model;

        static IEnumerable<BoundControlModel> EnumerateControls(IEnumerable<BoundRenderableModel> renderables)
        {
            foreach (var renderable in renderables)
            {
                if (renderable is BoundControlModel control)
                {
                    yield return control;
                    foreach (var member in control.Members)
                    {
                        foreach (var nested in EnumerateControlsFromMember(member))
                            yield return nested;
                    }
                }
                else if (renderable is BoundComponentInvocationModel invocation)
                {
                    foreach (var supply in invocation.Slots)
                        foreach (var nested in EnumerateControls(supply.Roots))
                            yield return nested;
                }
            }

            static IEnumerable<BoundControlModel> EnumerateControlsFromMember(BoundControlMember member) =>
                member switch
                {
                    BoundChildMember child => EnumerateControls([child.Child]),
                    BoundComponentChildMember child => EnumerateControls([child.Invocation]),
                    BoundAsyncBoundary boundary => EnumerateControls(
                        boundary.ContentRoots
                            .Concat(boundary.LoadingRoots ?? [])
                            .Concat(boundary.FallbackRoots)),
                    BoundConditionalMember conditional =>
                        EnumerateControls(conditional.TrueRoots.Concat(conditional.FalseRoots ?? [])),
                    BoundForEachMember loop => EnumerateControls([loop.Body]),
                    BoundItemTemplateMember template => EnumerateControls([template.Root]),
                    _ => [],
                };
        }

    static bool ContainsReactiveTemplateDependency(BoundControlModel control) =>
        control.Members.Any(member => member switch
        {
            BoundPropertyMember property => property.Expression.Dependencies.Count > 0,
            BoundAttachedPropertyMember attached => attached.Expression.Dependencies.Count > 0,
            BoundContentMember content => content.Expression.Dependencies.Count > 0,
            BoundNativeCollectionMember collection => collection.Elements.Any(element => element.Dependencies.Count > 0),
            BoundChildMember child => ContainsReactiveTemplateDependency(child.Child),
            _ => false,
        });
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

    private void ValidateDeclarationNames(ComponentDeclarationSyntax component)
    {
        var declarations = component.AllParameters.Select(parameter => (parameter.Name, parameter.NameSpan))
            .Concat(component.AllSlots.Select(slot => (slot.Name, slot.NameSpan)))
            .Concat(component.AllStateMembers.Select(state => (state.Name, state.Span)))
            .Concat(component.AllComputedMembers.Select(computed => (computed.Name, computed.Span)))
            .Concat(component.AllOrdinaryMembers.Select(member => (member.Name, member.NameSpan)))
            .ToArray();
        foreach (var declaration in declarations)
        {
            if (declaration.Name is "Mount" or "UpdateInputs" or "Dispose" ||
                declaration.Name.StartsWith("__lucent_", StringComparison.Ordinal))
            {
                AddUnsupported(declaration.Item2,
                    $"'{declaration.Name}' is reserved by the generated component contract.");
            }
        }

        foreach (var duplicate in declarations.GroupBy(item => item.Name, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1).SelectMany(group => group))
        {
            AddUnsupported(duplicate.Item2,
                $"Component member '{duplicate.Name}' collides with another declaration.");
        }

        foreach (var slot in component.AllSlots.Where(slot => slot.Name == "children"))
        {
            AddUnsupported(slot.NameSpan, "The implicit 'children' slot cannot be redeclared.");
        }

        foreach (var duplicate in component.RenderMethod.RenderedFragment.Roots
                     .SelectMany(AllYields).GroupBy(item => item.Name, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1).SelectMany(group => group))
        {
            AddUnsupported(duplicate.NameSpan,
                $"Slot '{duplicate.Name}' may have only one syntactic yield site.");
        }
    }

    private void ValidateOrdinaryMembers(IReadOnlyList<OrdinaryMemberSyntax> members)
    {
        foreach (var member in members)
        {
            var declaration = SyntaxFactory.ParseMemberDeclaration(member.Text);
            switch (declaration)
            {
                case FieldDeclarationSyntax field:
                    var isStatic = field.Modifiers.Any(modifier =>
                        modifier.IsKind(SyntaxKind.StaticKeyword) ||
                        modifier.IsKind(SyntaxKind.ConstKeyword));
                    if (!isStatic && !field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PrivateKeyword)))
                    {
                        AddUnsupported(member.NameSpan,
                            $"Instance field '{member.Name}' must be private.");
                    }
                    if (!isStatic && field.Declaration.Variables.Any(variable => variable.Initializer is null))
                    {
                        AddUnsupported(member.NameSpan,
                            $"Instance field '{member.Name}' must have an initializer.");
                    }
                    break;
                case MethodDeclarationSyntax method when
                    !method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PrivateKeyword)):
                    AddUnsupported(member.NameSpan,
                        $"Instance method '{member.Name}' must be private.");
                    break;
            }
        }
    }

    private static IEnumerable<UiYieldSyntax> AllYields(UiElementSyntax element)
    {
        foreach (var yield in element.Members.OfType<UiYieldSyntax>()) yield return yield;
        foreach (var child in element.Members.OfType<UiChildSyntax>())
            foreach (var yield in AllYields(child.Element)) yield return yield;
        foreach (var loop in element.Members.OfType<UiForEachSyntax>())
            foreach (var yield in AllYields(loop.Body)) yield return yield;
        foreach (var conditional in element.Members.OfType<UiIfSyntax>())
        {
            foreach (var root in conditional.TrueBranch.Roots)
                foreach (var yield in AllYields(root)) yield return yield;
            foreach (var root in conditional.FalseBranch?.Roots ?? [])
                foreach (var yield in AllYields(root)) yield return yield;
        }
        foreach (var boundary in element.Members.OfType<UiAsyncBoundarySyntax>())
        {
            foreach (var root in boundary.Content.Roots)
                foreach (var yield in AllYields(root)) yield return yield;
            foreach (var root in boundary.Loading?.Roots ?? [])
                foreach (var yield in AllYields(root)) yield return yield;
            foreach (var root in boundary.Fallback.Roots)
                foreach (var yield in AllYields(root)) yield return yield;
        }
    }

    private static int RenderableCardinality(IReadOnlyList<BoundRenderableModel> roots) =>
        roots.Sum(root => root is BoundComponentInvocationModel invocation
            ? invocation.Component.OutputCardinality
            : 1);

    private BoundRenderableModel? BindRenderable(
        UiElementSyntax element,
        bool insideLoop = false,
        IReadOnlyList<BoundLocal>? locals = null,
        bool insideConditional = false)
    {
        var components = _componentIndex?.Resolve(
            element.Name,
            _currentComponent?.NamespaceName ?? string.Empty,
            _semanticCompilation?.Imports ?? []) ?? [];
        var native = _resolver!.ResolveControl(element.Name);
        if (components.Count > 0 && native is not null)
        {
            AddUnsupported(ControlNameSpan(element),
                $"Renderable '{element.Name}' is ambiguous between a Lucent component and a native control.");
            return null;
        }
        if (components.Count > 1)
        {
            AddUnsupported(ControlNameSpan(element),
                $"Component name '{element.Name}' is ambiguous between imported namespaces.");
            return null;
        }
        if (components.Count == 1)
        {
            return BindComponentInvocation(element, components[0], locals ?? [], insideLoop);
        }

        return BindControl(element, insideLoop, locals, insideConditional);
    }

    private BoundComponentInvocationModel? BindComponentInvocation(
        UiElementSyntax element,
        ComponentSymbol component,
        IReadOnlyList<BoundLocal> locals,
        bool insideLoop = false)
    {
        _symbols.Add(new LucentSemanticSymbol(component.Name,
            LucentSemanticSymbolKind.Component, ControlNameSpan(element),
            $"component {component.NamespaceName}.{component.Name}",
            Definition: new LucentDefinition(component.SourcePath, component.DeclarationSpan)));
        var effective = new Dictionary<string, BoundComponentArgument>(StringComparer.Ordinal);
        var positional = 0;
        var sawNamed = false;
        foreach (var argument in element.AllArguments)
        {
            ComponentParameterSymbol? parameter;
            if (argument.Name is null)
            {
                if (sawNamed)
                {
                    AddUnsupported(argument.Span, "Positional component arguments must precede named arguments.");
                    continue;
                }
                parameter = positional < component.Parameters.Count ? component.Parameters[positional++] : null;
            }
            else
            {
                sawNamed = true;
                parameter = component.Parameters.FirstOrDefault(candidate => candidate.Name == argument.Name);
            }

            if (parameter is null)
            {
                AddUnsupported(argument.Span,
                    argument.Name is null ? "Too many component arguments." :
                    $"Component '{component.Name}' has no parameter named '{argument.Name}'.");
                continue;
            }
            if (effective.ContainsKey(parameter.Name))
            {
                AddUnsupported(argument.Span, $"Parameter '{parameter.Name}' is supplied more than once.");
                continue;
            }
            if (argument.Name is not null)
            {
                _symbols.Add(new LucentSemanticSymbol(parameter.Name,
                    LucentSemanticSymbolKind.ComponentParameter,
                    new SourceSpan(argument.Span.Start, argument.Name.Length),
                    $"{parameter.TypeName} {parameter.Name}"));
            }
            effective[parameter.Name] = new BoundComponentArgument(parameter,
                Request(argument.Text, argument.ExpressionSpan, CSharpIslandKind.Expression,
                    CSharpIslandRole.ComponentArgument,
                    _resolver!.ResolveTypeName(parameter.TypeName), locals), false);
        }

        foreach (var parameter in component.Parameters.Where(parameter => !effective.ContainsKey(parameter.Name)))
        {
            if (parameter.DefaultValueText is null)
            {
                AddUnsupported(ControlNameSpan(element),
                    $"Required component parameter '{parameter.Name}' is missing.");
                continue;
            }
            effective[parameter.Name] = new BoundComponentArgument(parameter,
                Request(parameter.BoundDefaultValueText ?? parameter.DefaultValueText, parameter.Span, CSharpIslandKind.Expression,
                    CSharpIslandRole.ComponentArgument,
                    _resolver!.ResolveTypeName(parameter.TypeName), locals), true);
        }

        var supplies = new List<BoundSlotSupply>();
        var children = element.Members.OfType<UiChildSyntax>().ToArray();
        if (children.Length > 0)
        {
            var slot = component.Slots.First(candidate => candidate.Name == "children");
            foreach (var child in children)
            {
                ValidateSlotStructure(child.Element, child.Span);
            }
            var boundRoots = children.Select(child => BindRenderable(
                    child.Element, locals: locals, insideLoop: insideLoop))
                .Where(item => item is not null).Cast<BoundRenderableModel>().ToArray();
            if (insideLoop && boundRoots.Any(HasNestedNativeSlotRoot))
            {
                AddUnsupported(element.Span,
                    "Nested keyed slot supplies must contain component roots only in this compiler subset.");
            }
            supplies.Add(new BoundSlotSupply(slot, boundRoots, element.Span));
        }
        foreach (var supply in element.Members.OfType<UiSlotSupplySyntax>())
        {
            var slot = component.Slots.FirstOrDefault(candidate => candidate.Name == supply.Name);
            if (slot is null)
            {
                AddUnsupported(supply.NameSpan,
                    $"Component '{component.Name}' has no slot named '{supply.Name}'.");
                continue;
            }
            if (supplies.Any(candidate => candidate.Slot.Name == slot.Name))
            {
                AddUnsupported(supply.NameSpan, $"Slot '{slot.Name}' is supplied more than once.");
                continue;
            }
            _symbols.Add(new LucentSemanticSymbol(slot.Name,
                LucentSemanticSymbolKind.ComponentSlot, supply.NameSpan,
                $"slot {slot.Name}",
                Definition: new LucentDefinition(component.SourcePath, slot.Span)));
            foreach (var root in supply.Fragment.Roots)
            {
                ValidateSlotStructure(root, supply.Span);
            }
            var boundRoots = supply.Fragment.Roots.Select(root => BindRenderable(
                    root, locals: locals, insideLoop: insideLoop))
                .Where(item => item is not null).Cast<BoundRenderableModel>().ToArray();
            if (insideLoop && boundRoots.Any(HasNestedNativeSlotRoot))
            {
                AddUnsupported(supply.Span,
                    "Nested keyed slot supplies must contain component roots only in this compiler subset.");
            }
            supplies.Add(new BoundSlotSupply(slot, boundRoots, supply.Span));
        }
        foreach (var invalid in element.Members.Where(member =>
                     member is not UiChildSyntax and not UiSlotSupplySyntax))
        {
            AddUnsupported(invalid.Span,
                "A component invocation body may contain only children and named slot supplies.");
        }

        return new BoundComponentInvocationModel(component,
            component.Parameters.Where(parameter => effective.ContainsKey(parameter.Name))
                .Select(parameter => effective[parameter.Name]).ToArray(),
            supplies, _nextComponentSiteId++, element.Span);

        void ValidateSlotStructure(UiElementSyntax root, SourceSpan span)
        {
            if (root.Members.Any(member => member is UiIfSyntax or UiForEachSyntax) ||
                root.Members.OfType<UiChildSyntax>().Any(child =>
                    HasStructuralMember(child.Element)))
            {
                AddUnsupported(span,
                    "Conditional and keyed structural regions are not supported inside delayed slot supplies; place them inside the yielded component host.");
            }
        }

        static bool HasStructuralMember(UiElementSyntax root) =>
            root.Members.Any(member => member is UiIfSyntax or UiForEachSyntax) ||
            root.Members.OfType<UiChildSyntax>().Any(child => HasStructuralMember(child.Element));

        static bool HasNestedNativeSlotRoot(BoundRenderableModel root) => root switch
        {
            BoundComponentInvocationModel invocation => invocation.Slots.Any(slot =>
                slot.Roots.Any(slotRoot => slotRoot is BoundControlModel ||
                    HasNestedNativeSlotRoot(slotRoot))),
            _ => false,
        };
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

        if (element.AllArguments.Count > 0)
        {
            AddUnsupported(element.AllArguments[0].Span,
                "Native controls do not accept invocation arguments.");
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

                case UiTemplateSyntax template:
                    BindItemTemplate(
                        element,
                        template,
                        resolvedControl,
                        seenMembers,
                        members);
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

                    var boundChild = BindRenderable(child.Element, insideLoop, locals, insideConditional);
                    if (boundChild is BoundControlModel boundControl)
                    {
                        members.Add(new BoundChildMember(boundControl, child.Span));
                        childCount++;
                    }
                    else if (boundChild is BoundComponentInvocationModel invocation)
                    {
                        if (!route.IsCollection && invocation.Component.OutputCardinality > 1)
                        {
                            AddUnsupported(child.Span,
                                $"Component '{invocation.Component.Name}' can produce {invocation.Component.OutputCardinality} roots, " +
                                $"but '{element.Name}' accepts only one child.");
                            continue;
                        }
                        members.Add(new BoundComponentChildMember(invocation, child.Span));
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

                    var loopLocals = new[]
                    {
                        new BoundLocal(loop.ItemName, _dynamicType!, loop.SourceExpression),
                    };
                    var boundBody = BindRenderable(loop.Body, insideLoop: true, loopLocals);
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

                case UiYieldSyntax yield:
                    var slot = _currentComponent?.Slots.FirstOrDefault(candidate => candidate.Name == yield.Name);
                    if (slot is null)
                    {
                        AddUnsupported(yield.NameSpan, $"Slot '{yield.Name}' is not declared by this component.");
                    }
                    else if (insideLoop)
                    {
                        AddUnsupported(yield.Span, "Slots cannot be yielded inside keyed loops.");
                    }
                    else
                    {
                        members.Add(new BoundYieldMember(slot, yield.Span));
                        childCount++;
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

                    var trueRoots = conditional.TrueBranch.Roots
                        .Select(root => BindRenderable(root, locals: locals, insideConditional: true))
                        .Where(root => root is not null).Cast<BoundRenderableModel>().ToArray();
                    var falseRoots = conditional.FalseBranch?.Roots
                        .Select(root => BindRenderable(root, locals: locals, insideConditional: true))
                        .Where(root => root is not null).Cast<BoundRenderableModel>().ToArray();
                    if (!conditionalRoute.IsCollection &&
                        (RenderableCardinality(trueRoots) > 1 ||
                         (falseRoots is not null && RenderableCardinality(falseRoots) > 1)))
                    {
                        if (RenderableCardinality(trueRoots) > 1)
                        {
                            AddUnsupported(conditional.TrueBranch.Span,
                                "A scalar content route cannot receive more than one conditional root.");
                        }
                        if (falseRoots is not null && RenderableCardinality(falseRoots) > 1 &&
                            conditional.FalseBranch is { } falseBranch)
                        {
                            AddUnsupported(falseBranch.Span,
                                "A scalar content route cannot receive more than one conditional root.");
                        }
                        continue;
                    }

                    var conditionalId = _nextConditionalId++;
                    if (trueRoots.Length == conditional.TrueBranch.Roots.Count &&
                        (conditional.FalseBranch is null || falseRoots?.Length == conditional.FalseBranch.Roots.Count))
                    {
                        members.Add(new BoundConditionalMember(
                            conditionalId,
                            Request(conditional.Condition, conditional.ConditionSpan,
                                CSharpIslandKind.Expression, CSharpIslandRole.Condition,
                                resolver.ResolveTypeName("bool"), locals),
                            trueRoots,
                            falseRoots,
                            conditional.Span));
                        seenMembers.Add(conditionalRoute.Property.Name);
                        hasStructuralRegion = true;
                    }
                    break;

                case UiAsyncBoundarySyntax boundary:
                    if (insideLoop || insideConditional)
                    {
                        AddUnsupported(boundary.Span, "Async boundaries cannot be nested inside another structural region.");
                        continue;
                    }
                    if (resolvedControl.ContentRoute is not { } boundaryRoute ||
                        !resolver.ContentAcceptsControl(boundaryRoute) || hasStructuralRegion || childCount > 0 ||
                        seenMembers.Contains(boundaryRoute.Property.Name))
                    {
                        AddUnsupported(boundary.Span,
                            $"Control '{element.Name}' must dedicate its content route to one async boundary.");
                        continue;
                    }
                    var source = _sources.FirstOrDefault(candidate => candidate.Name == boundary.SourceIdentifier &&
                        candidate.Kind == BoundReactiveSourceKind.Computed);
                    if (source is null)
                    {
                        AddUnsupported(boundary.SourceIdentifierSpan,
                            $"Async boundary source '{boundary.SourceIdentifier}' must be a declared Computed<T>.");
                        continue;
                    }
                    if (boundary.CatchType is not ("Exception" or "System.Exception" or "global::System.Exception"))
                    {
                        AddUnsupported(boundary.CatchTypeSpan, "Async boundary catches must use System.Exception.");
                        continue;
                    }
                    var exceptionType = _resolver!.ResolveTypeName("global::System.Exception");
                    var boundaryLocals = (locals ?? []).Concat([
                        new BoundLocal(boundary.CatchName, exceptionType!, $"__lucent_computed{char.ToUpperInvariant(boundary.SourceIdentifier[0])}{boundary.SourceIdentifier[1..]}.Error")
                    ]).ToArray();
                    IReadOnlyList<BoundRenderableModel> boundaryContent;
                    IReadOnlyList<BoundRenderableModel>? boundaryLoading = null;
                    IReadOnlyList<BoundRenderableModel> boundaryFallback;
                    _asyncBoundaryValueSources.Add(boundary.SourceIdentifier);
                    try
                    {
                        boundaryContent = boundary.Content.Roots
                            .Select(root => BindRenderable(root, locals: locals, insideConditional: true))
                            .Where(root => root is not null).Cast<BoundRenderableModel>().ToArray();
                    }
                    finally
                    {
                        _asyncBoundaryValueSources.Remove(boundary.SourceIdentifier);
                    }
                    if (boundary.Loading is { } loading)
                    {
                        boundaryLoading = loading.Roots
                            .Select(root => BindRenderable(root, locals: locals, insideConditional: true))
                            .Where(root => root is not null).Cast<BoundRenderableModel>().ToArray();
                    }
                    boundaryFallback = boundary.Fallback.Roots
                        .Select(root => BindRenderable(root, locals: boundaryLocals, insideConditional: true))
                        .Where(root => root is not null).Cast<BoundRenderableModel>().ToArray();
                    if (!boundaryRoute.IsCollection &&
                        (RenderableCardinality(boundaryContent) > 1 ||
                         (boundaryLoading is not null && RenderableCardinality(boundaryLoading) > 1) ||
                         RenderableCardinality(boundaryFallback) > 1))
                    {
                        AddUnsupported(boundary.Span, "An async boundary scalar content route accepts one root per branch.");
                        continue;
                    }
                    var boundaryId = _nextConditionalId++;
                    members.Add(new BoundAsyncBoundary(
                        boundary.SourceIdentifier,
                        source.Id,
                        boundaryId,
                        Request($"{boundary.SourceIdentifier}.Error is null", boundary.SourceIdentifierSpan,
                            CSharpIslandKind.Expression, CSharpIslandRole.Condition,
                            resolver.ResolveTypeName("bool"), locals),
                        boundaryContent,
                        boundaryLoading,
                        boundaryFallback,
                        boundary.CatchName,
                        boundary.Span));
                    seenMembers.Add(boundaryRoute.Property.Name);
                    hasStructuralRegion = true;
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

    private void BindItemTemplate(
        UiElementSyntax element,
        UiTemplateSyntax template,
        ResolvedNativeControl control,
        HashSet<string> seenMembers,
        List<BoundControlMember> members)
    {
        if (!string.Equals(template.Name, "ItemTemplate", StringComparison.Ordinal))
        {
            AddUnsupported(template.NameSpan,
                $"Only the Avalonia ItemTemplate property has first-class template syntax; use a C# expression for '{template.Name}'.");
            return;
        }

        var resolvedProperty = _resolver!.ResolveProperty(control, template.Name);
        if (resolvedProperty is null ||
            !_resolver.IsAssignableTo(
                resolvedProperty.Symbol.Type,
                "Avalonia.Controls.Templates.IDataTemplate"))
        {
            AddUnsupported(template.NameSpan,
                $"Property '{template.Name}' is not an Avalonia data-template property on {control.TypeName}.");
            return;
        }

        if (!seenMembers.Add(template.Name))
        {
            AddUnsupported(template.NameSpan,
                $"{element.Name} may contain only one '{template.Name}' member.");
            return;
        }

        var itemType = _resolver.ResolveTypeName(template.ItemTypeName);
        if (itemType is null)
        {
            AddUnsupported(template.ItemTypeSpan,
                $"Template item type '{template.ItemTypeName}' could not be resolved.");
            return;
        }

        if (HasTemplateStructure(template.Body.Roots))
        {
            AddUnsupported(template.Span,
                "ItemTemplate fragments currently support one native control tree only; nested templates, keyed, conditional, and slot regions are not supported in a recycled data template.");
            return;
        }

        var locals = new[] { new BoundLocal(template.ItemName, itemType) };
        var roots = template.Body.Roots
            .Select(root => BindRenderable(root, locals: locals))
            .Where(root => root is not null)
            .Cast<BoundRenderableModel>()
            .ToArray();
        if (roots.Length != 1)
        {
            AddUnsupported(template.Body.Span,
                "An ItemTemplate fragment must produce exactly one native control root.");
            return;
        }

        if (roots[0] is not BoundControlModel nativeRoot)
        {
            AddUnsupported(template.Body.Span,
                "ItemTemplate fragments cannot invoke Lucent components because Avalonia owns template realization and disposal.");
            return;
        }

        if (ContainsComponent(nativeRoot))
        {
            AddUnsupported(template.Body.Span,
                "ItemTemplate fragments cannot invoke Lucent components because Avalonia owns template realization and disposal.");
            return;
        }

        if (ContainsTemplateEvent(nativeRoot))
        {
            AddUnsupported(template.Body.Span,
                "ItemTemplate fragments cannot declare events because Avalonia owns template realization and no per-realization ComponentOwner is available.");
            return;
        }

        if (ContainsReactiveTemplateBinding(nativeRoot))
        {
            AddUnsupported(template.Body.Span,
                "ItemTemplate property values must depend on the typed item; component state, inputs, and computed values are not refreshed by Avalonia template realization.");
            return;
        }

        if (ContainsItemDependentCollection(nativeRoot, template.ItemName))
        {
            AddUnsupported(template.Body.Span,
                "Mount-only native collection members cannot depend on the ItemTemplate item.");
            return;
        }

        members.Add(new BoundItemTemplateMember(
            template.Name,
            itemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            template.ItemName,
            nativeRoot,
            template.Span));
        _symbols.Add(_resolver!.ToSemanticSymbol(control, resolvedProperty, template.NameSpan));

        static bool HasTemplateStructure(IEnumerable<UiElementSyntax> roots) =>
            roots.Any(HasTemplateStructureInElement);

        static bool HasTemplateStructureInElement(UiElementSyntax root) =>
            root.Members.Any(member => member is UiIfSyntax or UiForEachSyntax or
                UiAsyncBoundarySyntax or UiYieldSyntax or UiSlotSupplySyntax or UiTemplateSyntax) ||
            root.Members.OfType<UiChildSyntax>().Any(child => HasTemplateStructureInElement(child.Element));

        static bool ContainsComponent(BoundControlModel root) =>
            root.Members.Any(member => member switch
            {
                BoundComponentChildMember => true,
                BoundChildMember child => ContainsComponent(child.Child),
                _ => false,
            });

        static bool ContainsTemplateEvent(BoundControlModel root) =>
            root.Members.Any(member => member switch
            {
                BoundEventMember => true,
                BoundChildMember child => ContainsTemplateEvent(child.Child),
                _ => false,
            });

        bool ContainsReactiveTemplateBinding(BoundControlModel root) =>
            root.Members.Any(member => member switch
            {
                BoundPropertyMember property => IsReactiveTemplateExpression(property.Expression),
                BoundAttachedPropertyMember attached => IsReactiveTemplateExpression(attached.Expression),
                BoundContentMember content => IsReactiveTemplateExpression(content.Expression),
                BoundNativeCollectionMember collection => collection.Elements.Any(IsReactiveTemplateExpression),
                BoundChildMember child => ContainsReactiveTemplateBinding(child.Child),
                _ => false,
            });

        static bool ContainsItemDependentCollection(BoundControlModel root, string itemName) =>
            root.Members.Any(member => member switch
            {
                BoundNativeCollectionMember collection => collection.Elements.Any(element =>
                    ReferencesIdentifier(element.SourceText, itemName)),
                BoundChildMember child => ContainsItemDependentCollection(child.Child, itemName),
                _ => false,
            });

        static bool ReferencesIdentifier(string expression, string name) =>
            CSharpSyntaxTree.ParseText(expression).GetRoot().DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(identifier => identifier.Identifier.ValueText == name);

        bool IsReactiveTemplateExpression(BoundCSharpIsland expression) =>
            expression.Dependencies.Count > 0 ||
            CSharpSyntaxTree.ParseText(expression.SourceText).GetRoot().DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(identifier => _sources.Any(source => source.Name == identifier.Identifier.ValueText)) ||
            expression.LoweredText.Contains("__lucent_state", StringComparison.Ordinal) ||
            expression.LoweredText.Contains("__lucent_input", StringComparison.Ordinal) ||
            expression.LoweredText.Contains("__lucent_computed", StringComparison.Ordinal);
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
        if (property.Name.Contains('.', StringComparison.Ordinal))
        {
            var attached = resolver.ResolveAttachedProperty(control, property.Name);
            if (attached is null)
            {
                AddUnsupported(PropertyNameSpan(property),
                    $"Attached property '{property.Name}' could not be resolved for {control.TypeName}.");
                return;
            }

            if (!seenMembers.Add(property.Name))
            {
                AddUnsupported(property.Span, $"{element.Name} may contain only one '{property.Name}' member.");
                return;
            }

            var bound = new BoundAttachedPropertyMember(
                property.Name,
                attached.OwnerTypeName,
                attached.SetterName,
                Request(property.Value.Text, property.Value.Span,
                    CSharpIslandKind.Expression, CSharpIslandRole.Property,
                    attached.ValueType, locals),
                property.Span);
            members.Add(bound);
            _symbols.Add(resolver.ToSemanticSymbol(attached, PropertyNameSpan(property)));
            return;
        }
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
            var collection = resolver.ResolveMountCollection(control, property.Name);
            if (collection is not null)
            {
                if (property.Value is not CSharpExpressionValueSyntax expression ||
                    SyntaxFactory.ParseExpression(expression.Text) is not CollectionExpressionSyntax collectionSyntax)
                {
                    AddUnsupported(property.Value.Span,
                        $"Mount-only collection '{property.Name}' requires a C# collection expression.");
                    return;
                }

                var elements = new List<BoundCSharpIsland>();
                foreach (var collectionElement in collectionSyntax.Elements)
                {
                    if (collectionElement is not ExpressionElementSyntax expressionElement)
                    {
                        AddUnsupported(new SourceSpan(
                                property.Value.Span.Start + collectionElement.SpanStart,
                                Math.Max(1, collectionElement.Span.Length)),
                            "Spread collection elements are not supported for mount-only native collections.");
                        continue;
                    }

                    var span = new SourceSpan(
                        property.Value.Span.Start + expressionElement.Expression.SpanStart,
                        expressionElement.Expression.Span.Length);
                    elements.Add(Request(expressionElement.Expression.ToFullString().Trim(), span,
                        CSharpIslandKind.Expression, CSharpIslandRole.NativeCollectionElement,
                        collection.ElementType, locals));
                }

                if (seenMembers.Add(property.Name))
                {
                    members.Add(new BoundNativeCollectionMember(
                        resolvedProperty.Name, collection.ElementTypeName, elements, property.Span));
                }
                return;
            }
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

        var expressionText = property.Value.Text;
        if (property.Value is StringValueSyntax &&
            resolver.RequiresStringConstructor(resolvedProperty.Symbol.Type))
        {
            expressionText = $"new {resolvedProperty.TypeName}({expressionText})";
        }
        else if (property.Value is StringValueSyntax &&
            resolver.RequiresStringParse(resolvedProperty.Symbol.Type))
        {
            expressionText = $"{resolvedProperty.TypeName}.Parse({expressionText})";
        }

        members.Add(
            new BoundPropertyMember(
                resolvedProperty.Name,
                Request(expressionText, property.Value.Span,
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
        return new BoundEventMember(
            property.Name,
            @event.Name,
            Request(body, bodySpan, CSharpIslandKind.StatementBlock,
                CSharpIslandRole.EventBody, null, locals, expression.Span),
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

    private static bool ContainsAsyncBoundary(IEnumerable<UiElementSyntax> roots)
    {
        foreach (var root in roots)
        {
            foreach (var member in root.Members)
            {
                if (member is UiAsyncBoundarySyntax) return true;
                if (member is UiChildSyntax child && ContainsAsyncBoundary([child.Element])) return true;
                if (member is UiIfSyntax conditional &&
                    (ContainsAsyncBoundary(conditional.TrueBranch.Roots) ||
                     (conditional.FalseBranch is { } falseBranch && ContainsAsyncBoundary(falseBranch.Roots))))
                    return true;
            }
        }
        return false;
    }

    private BoundCSharpIsland Request(
        string text,
        SourceSpan span,
        CSharpIslandKind kind,
        CSharpIslandRole role,
        ITypeSymbol? expectedType,
        IReadOnlyList<BoundLocal>? locals = null,
        SourceSpan? editorSpan = null)
    {
        if (_componentHasAsyncBoundary &&
            role is CSharpIslandRole.Property or CSharpIslandRole.Content or
            CSharpIslandRole.Condition or CSharpIslandRole.LoopSource or
            CSharpIslandRole.LoopKey or CSharpIslandRole.ComponentArgument)
        {
            var expression = SyntaxFactory.ParseExpression(text);
            foreach (var access in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
                         .Where(access => access.Name.Identifier.ValueText == "Value" &&
                                          access.Expression is IdentifierNameSyntax))
            {
                var sourceName = ((IdentifierNameSyntax)access.Expression).Identifier.ValueText;
                if (_sources.Any(source => source.Name == sourceName && source.Kind == BoundReactiveSourceKind.Computed) &&
                    !_asyncBoundaryValueSources.Contains(sourceName))
                {
                    AddUnsupported(new SourceSpan(span.Start + access.SpanStart, access.Span.Length),
                        $"Computed source '{sourceName}.Value' must be read inside its try ({sourceName}) content branch.");
                }
            }
        }
        var id = _requests.Count;
        _requests.Add(new CSharpIslandRequest(
            id, text, span, kind, role, expectedType, locals ?? [], editorSpan ?? span));
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
                BoundAttachedPropertyMember attached => attached with { Expression = Resolve(attached.Expression, islands) },
                BoundNativeCollectionMember collection => collection with
                {
                    Elements = collection.Elements.Select(element => Resolve(element, islands)).ToArray(),
                },
                BoundContentMember content => content with { Expression = Resolve(content.Expression, islands) },
                BoundEventMember eventMember => eventMember with { Body = Resolve(eventMember.Body, islands) },
                BoundForEachMember loop => loop with
                {
                    SourceExpression = Resolve(loop.SourceExpression, islands),
                    KeyExpression = Resolve(loop.KeyExpression, islands),
                    Body = FinalizeRenderable(loop.Body, islands),
                },
                BoundAsyncBoundary boundary => boundary with
                {
                    Condition = Resolve(boundary.Condition, islands),
                    ContentRoots = boundary.ContentRoots.Select(root => FinalizeRenderable(root, islands)).ToArray(),
                    LoadingRoots = boundary.LoadingRoots?.Select(root => FinalizeRenderable(root, islands)).ToArray(),
                    FallbackRoots = boundary.FallbackRoots.Select(root => FinalizeRenderable(root, islands)).ToArray(),
                    TrueRoots = boundary.ContentRoots.Select(root => FinalizeRenderable(root, islands)).ToArray(),
                    FalseRoots = boundary.FallbackRoots.Select(root => FinalizeRenderable(root, islands)).ToArray(),
                },
                BoundConditionalMember conditional => conditional with
                {
                    Condition = Resolve(conditional.Condition, islands),
                    TrueRoots = conditional.TrueRoots.Select(root => FinalizeRenderable(root, islands)).ToArray(),
                    FalseRoots = conditional.FalseRoots?.Select(root => FinalizeRenderable(root, islands)).ToArray(),
                },
                BoundChildMember child => child with { Child = FinalizeControl(child.Child, islands) },
                BoundComponentChildMember child => child with
                {
                    Invocation = (BoundComponentInvocationModel)FinalizeRenderable(child.Invocation, islands),
                },
                BoundItemTemplateMember template => template with
                {
                    Root = FinalizeControl(template.Root, islands),
                },
                _ => member,
            }).ToArray(),
        };

    private static BoundRenderableModel FinalizeRenderable(
        BoundRenderableModel renderable,
        IReadOnlyDictionary<int, BoundCSharpIsland> islands) => renderable switch
        {
            BoundControlModel control => FinalizeControl(control, islands),
            BoundComponentInvocationModel component => component with
            {
                Arguments = component.Arguments.Select(argument => argument with
                {
                    Expression = Resolve(argument.Expression, islands),
                }).ToArray(),
                Slots = component.Slots.Select(slot => slot with
                {
                    Roots = slot.Roots.Select(root => FinalizeRenderable(root, islands)).ToArray(),
                }).ToArray(),
            },
            _ => renderable,
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
