using Lucent.Compiler.Parsing;
using Lucent.Compiler.Semantics;
using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using LucentCompilationUnitSyntax = Lucent.Compiler.Syntax.CompilationUnitSyntax;

namespace Lucent.Compiler;

internal static class EditorIntelligence
{
    public static IReadOnlyList<LucentCompletionItem> GetCompletions(
        string sourceText,
        int offset,
        ComponentSemanticAnalysis analysis)
    {
        var syntax = analysis.Syntax;
        var resolver = analysis.Resolver;
        if (IsComponentExpressionPosition(analysis, offset))
        {
            return GetExpressionCompletions(sourceText, offset, analysis);
        }

        var element = EnumerateElements(syntax.Component.RenderMethod.Root)
            .Where(candidate =>
                offset >= candidate.Span.Start &&
                offset <= candidate.Span.End)
            .OrderBy(candidate => candidate.Span.Length)
            .FirstOrDefault();
        if (element is null || element.Name == "Missing")
        {
            return [];
        }

        var control = resolver.ResolveControl(element.Name);
        if (control is null)
        {
            return [];
        }

        var valueMember = element.Properties
            .Where(property =>
                offset >= property.Value.Span.Start &&
                offset <= property.Value.Span.End)
            .Select(property => property.Name)
            .FirstOrDefault() ?? FindValueMember(sourceText, offset);
        if (valueMember is not null)
        {
            if (valueMember == "Class")
            {
                return [];
            }

            var property = resolver.ResolveProperty(control, valueMember);
            var expressionItems = GetExpressionCompletions(
                sourceText,
                offset,
                analysis);
            var nativeItems = property is null || GetExpressionPrefix(sourceText, offset).Contains('.')
                ? []
                : resolver.GetValueCandidates(property)
                    .Select(candidate => new LucentCompletionItem(
                        candidate.Label,
                        LucentCompletionItemKind.Value,
                        candidate.Display,
                        candidate.InsertText,
                        candidate.Documentation));
            return expressionItems
                .Concat(nativeItems)
                .GroupBy(item => item.Label, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.Label, StringComparer.Ordinal)
                .ToArray();
        }

        var usedMembers = element.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var items = resolver.GetProperties(control)
            .Where(property => !usedMembers.Contains(property.Name))
            .Select(property =>
            {
                var symbol = resolver.ToSemanticSymbol(
                    control,
                    property,
                    new SourceSpan(offset, 0));
                return new LucentCompletionItem(
                    property.Name,
                    LucentCompletionItemKind.Property,
                    property.TypeName,
                    property.Name + ": ",
                    symbol.Documentation);
            })
            .Concat(resolver.GetEvents(control)
                .Where(@event => !usedMembers.Contains(@event.Name))
                .Select(@event =>
                {
                    var symbol = resolver.ToSemanticSymbol(
                        control,
                        @event,
                        new SourceSpan(offset, 0));
                    return new LucentCompletionItem(
                        @event.Name,
                        LucentCompletionItemKind.Event,
                        @event.DelegateTypeName,
                        @event.Name + ": (sender, e) => { ${0} };",
                        symbol.Documentation,
                        IsSnippet: true);
                }));

        if (!usedMembers.Contains("Class"))
        {
            items = items.Append(new LucentCompletionItem(
                "Class",
                LucentCompletionItemKind.Property,
                "Lucent CSS classes",
                "Class: \"${0}\";",
                "Adds one or more compiled Lucent CSS classes to the projected Avalonia control.",
                IsSnippet: true));
        }

        return items
            .OrderBy(item => item.Label, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsComponentExpressionPosition(
        ComponentSemanticAnalysis analysis,
        int offset) =>
        analysis.EditorScopes.Any(scope =>
            scope.Role is (CSharpIslandRole.StateInitializer or
                CSharpIslandRole.ComputedInitialValue or
                CSharpIslandRole.ComputedFactory or
                CSharpIslandRole.LoopSource or
                CSharpIslandRole.LoopKey or
                CSharpIslandRole.Condition) &&
            offset >= scope.Span.Start &&
            offset <= scope.Span.End);

    public static LucentSemanticSymbol? GetExpressionSymbol(
        string sourceText,
        int offset,
        ComponentSemanticAnalysis analysis)
    {
        var syntax = analysis.Syntax;
        var resolver = analysis.Resolver;
        var scope = BuildExpressionScope(analysis, offset);
        var (chain, span) = GetExpressionChainAt(sourceText, offset);
        if (chain.Length == 0)
        {
            return null;
        }

        var parts = chain.Split('.');
        var variable = scope.LastOrDefault(candidate => candidate.Name == parts[0]);
        var current = variable is null
            ? resolver.ResolveTypeName(parts[0]) is { } type
                ? new ResolvedExpressionType(type, ExpressionContainer.Type)
                : null
            : new ResolvedExpressionType(variable.Type, variable.Container);
        if (current is null)
        {
            return GetDeclarationSymbol(sourceText, offset, syntax, resolver);
        }

        if (parts.Length == 1)
        {
            if (variable is not null)
            {
                var definitionSpan = FindPersistentMemberNameSpan(
                    sourceText,
                    syntax,
                    variable.Name);
                return new LucentSemanticSymbol(
                    variable.Name,
                    LucentSemanticSymbolKind.Expression,
                    span,
                    variable.Display,
                    null,
                    definitionSpan is null
                        ? null
                        : new LucentDefinition(analysis.SourcePath, definitionSpan.Value));
            }

            return resolver.ToExpressionSemanticSymbol(
                parts[0],
                span,
                $"{TypeKindName(current.Type)} {current.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}",
                current.Type);
        }

        ISymbol? symbol = null;
        string? display = null;
        foreach (var part in parts.Skip(1))
        {
            var resolved = ResolveMember(current, part, resolver);
            if (resolved is null)
            {
                return null;
            }

            current = resolved.Type;
            symbol = resolved.Symbol;
            display = resolved.Display;
        }

        return resolver.ToExpressionSemanticSymbol(
            parts[^1],
            span,
            display ?? $"{current.Type.ToDisplayString()} {parts[^1]}",
            symbol);
    }

    private static IReadOnlyList<LucentCompletionItem> GetExpressionCompletions(
        string sourceText,
        int offset,
        ComponentSemanticAnalysis analysis)
    {
        var resolver = analysis.Resolver;
        var scope = BuildExpressionScope(analysis, offset);
        var prefix = GetExpressionPrefix(sourceText, offset);
        if (!prefix.Contains('.'))
        {
            return scope.Select(variable => new LucentCompletionItem(
                    variable.Name,
                    LucentCompletionItemKind.Variable,
                    variable.Display,
                    variable.Name,
                    null))
                .Concat(resolver.GetExpressionTypes().Select(type =>
                    new LucentCompletionItem(
                        type.Name,
                        LucentCompletionItemKind.Type,
                        $"{TypeKindName(type)} {type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}",
                        type.Name,
                        NativeSymbolResolver.GetExpressionDocumentation(type))))
                .GroupBy(item => item.Label, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.Label, StringComparer.Ordinal)
                .ToArray();
        }

        var parts = prefix.Split('.');
        var variable = scope.LastOrDefault(candidate => candidate.Name == parts[0]);
        var current = variable is null
            ? resolver.ResolveTypeName(parts[0]) is { } type
                ? new ResolvedExpressionType(type, ExpressionContainer.Type)
                : null
            : new ResolvedExpressionType(variable.Type, variable.Container);
        if (current is null)
        {
            return [];
        }

        foreach (var part in parts.Skip(1).SkipLast(1))
        {
            var member = ResolveMember(current, part, resolver);
            if (member is null)
            {
                return [];
            }

            current = member.Type;
        }

        var members = GetMembers(current, resolver)
            .Select(member => new LucentCompletionItem(
                member.Name,
                member.Symbol switch
                {
                    IMethodSymbol => LucentCompletionItemKind.Method,
                    IFieldSymbol => LucentCompletionItemKind.Field,
                    IPropertySymbol => LucentCompletionItemKind.Property,
                    IEventSymbol => LucentCompletionItemKind.Event,
                    _ when member.Name == "Update" => LucentCompletionItemKind.Method,
                    _ => LucentCompletionItemKind.Property,
                },
                member.Display,
                member.Name,
                member.Documentation))
            .ToArray();
        return members;
    }

    private static IReadOnlyList<ExpressionVariable> BuildExpressionScope(
        ComponentSemanticAnalysis analysis,
        int offset)
    {
        var variables = analysis.EditorVariables.Select(variable => new ExpressionVariable(
            variable.Name,
            variable.Type,
            variable.Kind switch
            {
                BoundEditorVariableKind.State => ExpressionContainer.State,
                BoundEditorVariableKind.Computed => ExpressionContainer.Computed,
                _ => ExpressionContainer.Local,
            },
            variable.Display)).ToList();

        foreach (var scope in analysis.EditorScopes.Where(scope =>
                     offset >= scope.Span.Start && offset <= scope.Span.End))
        {
            variables.AddRange(scope.Context.LookupLocals(offset)
                .GroupBy(local => local.Name, StringComparer.Ordinal)
                .Select(group => group.OrderBy(local =>
                    local.Type.TypeKind == TypeKind.Dynamic).First())
                .Select(local => new ExpressionVariable(
                local.Name,
                local.Type,
                ExpressionContainer.Local,
                $"{local.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {local.Name}")));
        }

        return variables;
    }

    private static ExpressionMember? ResolveMember(
        ResolvedExpressionType target,
        string name,
        NativeSymbolResolver resolver) =>
        GetMembers(target, resolver).FirstOrDefault(member => member.Name == name);

    private static IReadOnlyList<ExpressionMember> GetMembers(
        ResolvedExpressionType target,
        NativeSymbolResolver resolver)
    {
        IReadOnlyList<ExpressionMember> special = target.Container switch
        {
            ExpressionContainer.State =>
            [
                new ExpressionMember("Value", new(target.Type, ExpressionContainer.Local), $"{target.Type.ToDisplayString()} Value", null, null),
                new ExpressionMember("Update", new(target.Type, ExpressionContainer.Local), "void Update(...)", null, null),
            ],
            ExpressionContainer.Computed =>
            [
                new ExpressionMember("Value", new(target.Type, ExpressionContainer.Local), $"{target.Type.ToDisplayString()} Value", null, null),
                new ExpressionMember("IsPending", new(resolver.ResolveTypeName("bool")!, ExpressionContainer.Local), "bool IsPending", null, null),
                new ExpressionMember("ErrorMessage", new(resolver.ResolveTypeName("string")!, ExpressionContainer.Local), "string? ErrorMessage", null, null),
            ],
            _ => [],
        };
        if (target.Container is ExpressionContainer.State or ExpressionContainer.Computed)
        {
            return special;
        }

        var memberType = target.Type is IArrayTypeSymbol
            ? resolver.ResolveTypeName("global::System.Array") ?? target.Type
            : target.Type;
        return resolver.GetExpressionMembers(
                memberType,
                staticMembers: target.Container == ExpressionContainer.Type)
            .Select(symbol => new ExpressionMember(
                symbol.Name,
                new(
                    NativeSymbolResolver.GetExpressionMemberType(symbol)!,
                    ExpressionContainer.Local),
                symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                symbol,
                NativeSymbolResolver.GetExpressionDocumentation(symbol)))
            .ToArray();
    }

    private static LucentSemanticSymbol? GetDeclarationSymbol(
        string sourceText,
        int offset,
        LucentCompilationUnitSyntax syntax,
        NativeSymbolResolver resolver)
    {
        var member = syntax.Component.AllStateMembers
            .Where(candidate =>
                offset >= candidate.Span.Start &&
                offset <= candidate.Span.End)
            .Select(candidate => (Kind: ExpressionContainer.State, candidate.TypeName, candidate.Name, candidate.Span))
            .Concat(syntax.Component.AllComputedMembers
                .Where(candidate =>
                    offset >= candidate.Span.Start &&
                    offset <= candidate.Span.End)
                .Select(candidate => (Kind: ExpressionContainer.Computed, candidate.TypeName, candidate.Name, candidate.Span)))
            .FirstOrDefault();
        if (member.Name is null)
        {
            return null;
        }

        var (word, span) = GetWordAt(sourceText, offset);
        if (word == "State" || word == "Computed")
        {
            return resolver.ToExpressionSemanticSymbol(
                word,
                span,
                $"class {word}<T>");
        }

        if (word == "new")
        {
            var display = member.Kind == ExpressionContainer.State
                ? $"{member.TypeName} State<{member.TypeName}>.State({member.TypeName} initialValue)"
                : $"Computed<{member.TypeName}>.Computed(Func<CancellationToken, Task<{member.TypeName}>>, {member.TypeName} initialValue)";
            return resolver.ToExpressionSemanticSymbol(word, span, display);
        }

        var type = resolver.ResolveTypeName(word);
        return type is null
            ? null
            : resolver.ToExpressionSemanticSymbol(
                word,
                span,
                $"{TypeKindName(type)} {type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}",
                type);
    }

    private static SourceSpan? FindPersistentMemberNameSpan(
        string sourceText,
        LucentCompilationUnitSyntax syntax,
        string name)
    {
        var memberSpan = syntax.Component.AllStateMembers
            .Where(member => member.Name == name)
            .Select(member => (SourceSpan?)member.Span)
            .Concat(syntax.Component.AllComputedMembers
                .Where(member => member.Name == name)
                .Select(member => (SourceSpan?)member.Span))
            .FirstOrDefault();
        if (memberSpan is not { } declaration)
        {
            return null;
        }

        var start = sourceText.IndexOf(
            name,
            declaration.Start,
            declaration.Length,
            StringComparison.Ordinal);
        return start < 0 ? null : new SourceSpan(start, name.Length);
    }

    private static string TypeKindName(ITypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Interface => "interface",
        TypeKind.Struct => "struct",
        TypeKind.Enum => "enum",
        TypeKind.Delegate => "delegate",
        _ => "class",
    };

    private static (string Word, SourceSpan Span) GetWordAt(string text, int offset)
    {
        if (text.Length == 0)
        {
            return (string.Empty, new SourceSpan(0, 0));
        }

        offset = Math.Clamp(offset, 0, text.Length - 1);
        var start = offset;
        while (start > 0 &&
               (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length &&
               (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        return (text[start..end], new SourceSpan(start, end - start));
    }

    private static string GetExpressionPrefix(string text, int offset)
    {
        var start = Math.Clamp(offset, 0, text.Length);
        while (start > 0 &&
               (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '.'))
        {
            start--;
        }

        return text[start..Math.Clamp(offset, 0, text.Length)];
    }

    private static (string Chain, SourceSpan Span) GetExpressionChainAt(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, text.Length - 1));
        var wordStart = offset;
        while (wordStart > 0 &&
               (char.IsLetterOrDigit(text[wordStart - 1]) || text[wordStart - 1] == '_'))
        {
            wordStart--;
        }

        var wordEnd = offset;
        while (wordEnd < text.Length &&
               (char.IsLetterOrDigit(text[wordEnd]) || text[wordEnd] == '_'))
        {
            wordEnd++;
        }

        var chainStart = wordStart;
        while (chainStart > 0 &&
               (char.IsLetterOrDigit(text[chainStart - 1]) || text[chainStart - 1] is '_' or '.'))
        {
            chainStart--;
        }

        return (
            text[chainStart..wordEnd],
            new SourceSpan(wordStart, wordEnd - wordStart));
    }

    private static string? FindValueMember(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset;
        while (start > 0 && text[start - 1] is not '\n' and not '\r' and not ';' and not '{' and not '}')
        {
            start--;
        }

        var segment = text[start..offset];
        var colon = segment.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        var left = segment[..colon].TrimEnd();
        var nameStart = left.Length;
        while (nameStart > 0 &&
               (char.IsLetterOrDigit(left[nameStart - 1]) || left[nameStart - 1] == '_'))
        {
            nameStart--;
        }

        var name = left[nameStart..];
        return name.Length == 0 ? null : name;
    }

    private static IEnumerable<UiElementSyntax> EnumerateElements(UiElementSyntax element)
    {
        yield return element;
        foreach (var member in element.Members)
        {
            var child = member switch
            {
                UiChildSyntax nested => nested.Element,
                UiForEachSyntax loop => loop.Body,
                UiIfSyntax conditional => conditional.TrueRoot,
                _ => null,
            };
            if (child is null)
            {
                continue;
            }

            foreach (var descendant in EnumerateElements(child))
            {
                yield return descendant;
            }
            if (member is UiIfSyntax { FalseRoot: { } falseRoot })
            {
                foreach (var descendant in EnumerateElements(falseRoot))
                {
                    yield return descendant;
                }
            }
        }
    }

    private enum ExpressionContainer
    {
        Local,
        State,
        Computed,
        Type,
    }

    private sealed record ExpressionVariable(
        string Name,
        ITypeSymbol Type,
        ExpressionContainer Container,
        string Display);

    private sealed record ResolvedExpressionType(
        ITypeSymbol Type,
        ExpressionContainer Container);

    private sealed record ExpressionMember(
        string Name,
        ResolvedExpressionType Type,
        string Display,
        ISymbol? Symbol,
        string? Documentation);
}
