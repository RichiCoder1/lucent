using System.Text.RegularExpressions;
using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CSharpExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax;
using CSharpLiteralExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax;
using CSharpDefaultExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.DefaultExpressionSyntax;
using CSharpTypeOfExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.TypeOfExpressionSyntax;
using CSharpPrefixUnaryExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.PrefixUnaryExpressionSyntax;
using CSharpParenthesizedExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax;
using CSharpInvocationExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax;
using CSharpIdentifierNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;
using CSharpObjectCreationExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax;
using CSharpAnonymousFunctionExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.AnonymousFunctionExpressionSyntax;
using CSharpAssignmentExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax;
using CSharpAwaitExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.AwaitExpressionSyntax;
using CSharpElementAccessExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ElementAccessExpressionSyntax;

namespace Lucent.Compiler.Parsing;

internal sealed partial class Parser
{
    private readonly SourceDocument _source;
    private readonly DiagnosticBag _diagnostics;
    private readonly IReadOnlyList<SyntaxToken> _tokens;
    private int _position;

    public Parser(string text, string path)
    {
        _source = new SourceDocument(text, path);
        _diagnostics = new DiagnosticBag(_source);
        _tokens = new Lexer(_source, _diagnostics)
            .Lex()
            .Where(token => token.Kind != TokenKind.Bad)
            .ToArray();
    }

    public IReadOnlyList<LucentDiagnostic> Diagnostics => _diagnostics.Items;

    public CompilationUnitSyntax Parse()
    {
        var start = Current.Span.Start;
        var namespaceName = ParseNamespace();
        var usings = new List<UsingDirectiveSyntax>();
        var components = new List<ComponentDeclarationSyntax>();

        while (IsIdentifier("using"))
        {
            usings.Add(ParseUsingDirective());
        }

        while (Current.Kind != TokenKind.EndOfFile)
        {
            if (IsIdentifier("component"))
            {
                components.Add(ParseComponent());
                continue;
            }

            AddUnsupported(
                Current.Span,
                "Expected a Lucent component declaration.");
            SynchronizeTo("component");
        }

        if (components.Count == 0)
        {
            var missing = new ComponentDeclarationSyntax(
                "Missing",
                null,
                CreateMissingRender(Current.Span.Start),
                new SourceSpan(Current.Span.Start, 0));
            components.Add(missing);
        }

        return new CompilationUnitSyntax(
            namespaceName,
            components[0],
            SpanFrom(start, Current.Span.End),
            components,
            usings);
    }

    private string ParseNamespace()
    {
        if (!IsIdentifier("namespace"))
        {
            AddSyntax(Current.Span, "Expected 'namespace' before the component declarations.");
            return string.Empty;
        }

        NextToken();
        var parts = new List<string>();
        while (Current.Kind != TokenKind.Semicolon &&
               Current.Kind != TokenKind.EndOfFile)
        {
            if (Current.Kind == TokenKind.Identifier)
            {
                parts.Add(NextToken().Text);
            }
            else if (Current.Kind == TokenKind.Dot)
            {
                NextToken();
            }
            else
            {
                AddSyntax(Current.Span, "Expected a namespace name.");
                NextToken();
            }
        }

        Expect(TokenKind.Semicolon, "';' after the namespace declaration");
        return string.Join(".", parts);
    }

    private UsingDirectiveSyntax ParseUsingDirective()
    {
        var start = Current.Span.Start;
        while (Current.Kind is not TokenKind.Semicolon and not TokenKind.EndOfFile)
        {
            NextToken();
        }

        var semicolon = Expect(TokenKind.Semicolon, "';' after the using directive");
        var end = semicolon.Span.End;
        return new UsingDirectiveSyntax(
            Slice(start, end).Trim(),
            SpanFrom(start, end));
    }

    private ComponentDeclarationSyntax ParseComponent()
    {
        var start = Current.Span.Start;
        ExpectIdentifier("component");
        var name = Expect(TokenKind.Identifier, "a component name");
        var parameters = ReadDelimited(TokenKind.OpenParen, TokenKind.CloseParen, validate: false);
        var parsedParameters = ParseParameters(parameters);

        if (Current.Kind == TokenKind.Equals && Peek(1).Kind == TokenKind.GreaterThan)
        {
            NextToken();
            NextToken();
            var fragment = ParseRenderedFragment();
            var semicolon = Expect(TokenKind.Semicolon, "';' after the component expression body");
            var root = fragment.Roots.FirstOrDefault() ?? new UiElementSyntax(
                "Missing", [], new SourceSpan(fragment.Span.Start, 0));
            return new ComponentDeclarationSyntax(
                name.Text, null,
                new RenderMethodSyntax(root, fragment.Span,
                    [new ReturnRenderStatementSyntax(root, root.Span)], fragment),
                SpanFrom(start, semicolon.Span.End), Parameters: parsedParameters);
        }

        Expect(TokenKind.OpenBrace, "'{' to open the component body");

        var states = new List<StateMemberSyntax>();
        var computed = new List<ComputedMemberSyntax>();
        RenderMethodSyntax? render = null;
        var members = new List<LucentSyntaxNode>();
        var slots = new List<SlotDeclarationSyntax>();
        var ordinaryMembers = new List<OrdinaryMemberSyntax>();

        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            var before = _position;

            if (IsIdentifier("Fragment") &&
                IsIdentifier(Peek(1), "Render"))
            {
                var candidate = ParseRenderMethod();
                if (render is not null)
                {
                    AddSyntax(
                        candidate.Span,
                        "A component must contain exactly one Fragment Render() method.");
                }
                else
                {
                    render = candidate;
                }

                members.Add(candidate);
            }
            else if (IsIdentifier("slot"))
            {
                var slot = ParseSlotDeclaration();
                slots.Add(slot);
                members.Add(slot);
            }
            else if (IsIdentifier("private") && LooksLikeReactiveField())
            {
                var persistentMember = ParsePersistentMember();
                if (persistentMember is StateMemberSyntax state)
                {
                    states.Add(state);
                    members.Add(state);
                }
                else if (persistentMember is ComputedMemberSyntax computedMember)
                {
                    computed.Add(computedMember);
                    members.Add(computedMember);
                }
            }
            else if (IsIdentifier("private") || IsIdentifier("static") || IsIdentifier("const"))
            {
                var ordinary = ParseOrdinaryMember();
                if (ordinary is not null)
                {
                    ordinaryMembers.Add(ordinary);
                    members.Add(ordinary);
                }
            }
            else
            {
                var member = ParseOpaqueMember();
                if (member is not null)
                {
                    members.Add(member);
                }
            }

            if (_position == before)
            {
                NextToken();
            }
        }

        var closeBrace = Expect(TokenKind.CloseBrace, "'}' to close the component");
        render ??= CreateMissingRender(name.Span.End);

        return new ComponentDeclarationSyntax(
            name.Text,
            states.FirstOrDefault(),
            render,
            SpanFrom(start, closeBrace.Span.End),
            states,
            members,
            computed,
            parsedParameters,
            slots,
            ordinaryMembers);
    }

    private IReadOnlyList<ComponentParameterSyntax> ParseParameters(CSharpIslandSyntax clause)
    {
        var list = SyntaxFactory.ParseParameterList("(" + clause.Text + ")");
        var result = new List<ComponentParameterSyntax>();
        foreach (var diagnostic in list.GetDiagnostics())
        {
            var offset = clause.Span.Start - 1 + diagnostic.Location.SourceSpan.Start;
            AddSyntax(new SourceSpan(offset, Math.Max(1, diagnostic.Location.SourceSpan.Length)),
                diagnostic.GetMessage());
        }
        foreach (var parameter in list.Parameters)
        {
            var offset = clause.Span.Start - 1;
            if (parameter.Modifiers.Count > 0 || parameter.AttributeLists.Count > 0)
            {
                AddUnsupported(new SourceSpan(offset + parameter.SpanStart,
                    Math.Max(1, parameter.Span.Length)),
                    "Component parameters do not support attributes, ref, out, in, or params.");
            }

            if (parameter.Type is null)
            {
                AddSyntax(new SourceSpan(offset + parameter.SpanStart,
                    Math.Max(1, parameter.Span.Length)),
                    "Component parameters require an explicit type.");
            }
            var type = parameter.Type?.ToFullString().Trim() ?? "object";
            var name = parameter.Identifier.ValueText;
            var nameSpan = new SourceSpan(offset + parameter.Identifier.SpanStart,
                parameter.Identifier.Span.Length);
            var defaultValue = parameter.Default?.Value.ToFullString().Trim();
            SourceSpan? defaultSpan = parameter.Default is null ? null : new SourceSpan(
                offset + parameter.Default.Value.SpanStart,
                parameter.Default.Value.Span.Length);
            if (parameter.Default is { Value: { } defaultValueSyntax } &&
                !IsCompileTimeDefault(defaultValueSyntax))
            {
                AddUnsupported(defaultSpan!.Value,
                    "Component parameter defaults must be compile-time constant expressions.");
            }
            result.Add(new ComponentParameterSyntax(type, name, defaultValue,
                new SourceSpan(offset + parameter.SpanStart, parameter.Span.Length),
                nameSpan, defaultSpan));
        }

        var sawDefault = false;
        foreach (var parameter in result)
        {
            if (parameter.DefaultValueText is not null)
            {
                sawDefault = true;
            }
            else if (sawDefault)
            {
                AddUnsupported(parameter.NameSpan,
                    "A required component parameter cannot follow an optional parameter.");
            }
        }

        foreach (var duplicate in result.GroupBy(parameter => parameter.Name, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1).SelectMany(group => group))
        {
            AddUnsupported(duplicate.NameSpan,
                $"Component parameter '{duplicate.Name}' is declared more than once.");
        }

        return result;

        static bool IsCompileTimeDefault(CSharpExpressionSyntax expression) =>
            !expression.DescendantNodesAndSelf().Any(node => node switch
            {
                CSharpInvocationExpressionSyntax invocation =>
                    invocation.Expression is not CSharpIdentifierNameSyntax
                    { Identifier.ValueText: "nameof" },
                CSharpObjectCreationExpressionSyntax or
                CSharpAnonymousFunctionExpressionSyntax or
                CSharpAssignmentExpressionSyntax or
                CSharpAwaitExpressionSyntax or
                CSharpElementAccessExpressionSyntax => true,
                _ => false,
            });
    }

    private SlotDeclarationSyntax ParseSlotDeclaration()
    {
        var start = ExpectIdentifier("slot").Span.Start;
        var name = Expect(TokenKind.Identifier, "a slot name");
        var end = Expect(TokenKind.Semicolon, "';' after the slot declaration").Span.End;
        return new SlotDeclarationSyntax(name.Text, SpanFrom(start, end), name.Span);
    }

    private bool LooksLikeReactiveField()
    {
        var type = Peek(1);
        if (type.Text == "readonly")
        {
            type = Peek(2);
        }

        return type.Kind == TokenKind.Identifier &&
               (type.Text == "State" || type.Text == "Computed");
    }

    private OrdinaryMemberSyntax? ParseOrdinaryMember()
    {
        var start = Current.Span.Start;
        var end = FindOrdinaryMemberEnd(start);
        if (end <= start)
        {
            NextToken();
            return null;
        }

        var text = Slice(start, end).Trim();
        AdvanceTo(end);
        if (Current.Kind == TokenKind.Semicolon)
        {
            end = NextToken().Span.End;
            text = Slice(start, end).Trim();
        }

        var member = SyntaxFactory.ParseMemberDeclaration(text);
        var nameToken = member switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method => method.Identifier,
            Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax field when field.Declaration.Variables.Count == 1 =>
                field.Declaration.Variables[0].Identifier,
            _ => default,
        };
        if (member is null || nameToken == default)
        {
            AddUnsupported(SpanFrom(start, end),
                "Only one-variable fields and private methods are supported component members.");
            return null;
        }

        return new OrdinaryMemberSyntax(text, nameToken.ValueText,
            SpanFrom(start, end), new SourceSpan(start + nameToken.SpanStart, nameToken.Span.Length));
    }

    private int FindOrdinaryMemberEnd(int start)
    {
        var source = _source.Text;
        var brace = 0;
        var sawBrace = false;
        for (var index = start; index < source.Length; index++)
        {
            if (source[index] == '{') { brace++; sawBrace = true; }
            else if (source[index] == '}')
            {
                if (brace == 0) return index;
                brace--;
                if (sawBrace && brace == 0) return index + 1;
            }
            else if (source[index] == ';' && brace == 0) return index;
        }
        return source.Length;
    }

    private LucentSyntaxNode? ParsePersistentMember()
    {
        var start = Current.Span.Start;
        var end = ScanToTerminator(start, stopAtCloseBrace: true);
        var raw = Slice(start, end).Trim();
        AdvanceTo(end);

        if (Current.Kind == TokenKind.Semicolon)
        {
            var semicolon = NextToken();
            end = semicolon.Span.End;
        }
        else
        {
            AddSyntax(
                new SourceSpan(end, 0),
                "Expected ';' after the state member.");
        }

        var match = StatePattern().Match(raw);
        if (match.Success)
        {
            var typeName = match.Groups["type"].Value.Trim();
            var memberName = match.Groups["name"].Value;
            var initializerText = match.Groups["initializer"].Value.Trim();
            var initializerOffset = raw.IndexOf(
                match.Groups["initializer"].Value,
                StringComparison.Ordinal);
            var initializerSpan = new SourceSpan(
                start + Math.Max(0, initializerOffset),
                initializerText.Length);
            _ = int.TryParse(initializerText, out var initialValue);

            return new StateMemberSyntax(
                typeName,
                memberName,
                initialValue,
                new SourceSpan(start, Math.Max(1, end - start)),
                initializerText,
                initializerSpan);
        }

        match = ComputedPattern().Match(raw);
        if (match.Success)
        {
            var initializerText = match.Groups["initializer"].Value.Trim();
            var initializerOffset = raw.IndexOf(
                match.Groups["initializer"].Value,
                StringComparison.Ordinal);
            return new ComputedMemberSyntax(
                match.Groups["type"].Value.Trim(),
                match.Groups["name"].Value,
                initializerText,
                new SourceSpan(start, Math.Max(1, end - start)),
                new SourceSpan(
                    start + Math.Max(0, initializerOffset),
                    initializerText.Length));
        }

        AddUnsupported(
            new SourceSpan(start, Math.Max(1, end - start)),
            "Persistent members must use State<T> name = new(...) or Computed<T> name = new(factory, initialValue).");
        return null;
    }

    private RenderMethodSyntax ParseRenderMethod()
    {
        var start = Current.Span.Start;
        ExpectIdentifier("Fragment");
        ExpectIdentifier("Render");
        ReadDelimited(TokenKind.OpenParen, TokenKind.CloseParen);
        if (Current.Kind == TokenKind.Equals && Peek(1).Kind == TokenKind.GreaterThan)
        {
            NextToken();
            NextToken();
            var expressionFragment = ParseRenderedFragment();
            var semicolon = Expect(TokenKind.Semicolon, "';' after Render expression body");
            var expressionRoot = expressionFragment.Roots.FirstOrDefault() ?? new UiElementSyntax(
                "Missing", [], new SourceSpan(expressionFragment.Span.Start, 0));
            return new RenderMethodSyntax(expressionRoot, SpanFrom(start, semicolon.Span.End),
                [new ReturnRenderStatementSyntax(expressionRoot, expressionRoot.Span)], expressionFragment);
        }
        Expect(TokenKind.OpenBrace, "'{' to open Render");

        while (!IsIdentifier("return") &&
               Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            AddUnsupported(
                Current.Span,
                "Only a return statement is supported in the initial Render method.");
            SkipUntil(TokenKind.CloseBrace);
        }

        UiElementSyntax root;
        if (IsIdentifier("return"))
        {
            NextToken();
            var fragment = ParseRenderedFragment();
            root = fragment.Roots.FirstOrDefault() ?? new UiElementSyntax(
                "Missing", [], new SourceSpan(fragment.Span.Start, 0));
            Expect(TokenKind.Semicolon, "';' after the rendered root");
            var close = Expect(TokenKind.CloseBrace, "'}' to close Render");
            return new RenderMethodSyntax(
                root,
                SpanFrom(start, close.Span.End),
                [new ReturnRenderStatementSyntax(root, root.Span)],
                fragment);
        }
        else
        {
            root = new UiElementSyntax(
                "Missing",
                [],
                new SourceSpan(Current.Span.Start, 0));
        }

        var closeBrace = Expect(TokenKind.CloseBrace, "'}' to close Render");
        return new RenderMethodSyntax(
            root,
            SpanFrom(start, closeBrace.Span.End),
            [new ReturnRenderStatementSyntax(root, root.Span)]);
    }

    private UiFragmentSyntax ParseRenderedFragment()
    {
        if (IsIdentifier("Fragment") && Peek(1).Kind == TokenKind.OpenBrace)
        {
            var start = NextToken().Span.Start;
            Expect(TokenKind.OpenBrace, "'{' to open Fragment");
            var roots = new List<UiElementSyntax>();
            while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
            {
                roots.Add(ParseElement());
            }
            var end = Expect(TokenKind.CloseBrace, "'}' to close Fragment").Span.End;
            return new UiFragmentSyntax(roots, SpanFrom(start, end));
        }

        var root = ParseElement();
        return new UiFragmentSyntax([root], root.Span);
    }

    private UiElementSyntax ParseElement()
    {
        var name = Expect(TokenKind.Identifier, "a UI element name");
        var start = name.Span.Start;
        var nameText = name.Text;
        while (Current.Kind == TokenKind.Dot && Peek(1).Kind == TokenKind.Identifier)
        {
            NextToken();
            nameText += "." + NextToken().Text;
        }
        IReadOnlyList<UiArgumentSyntax> arguments = [];
        if (Current.Kind == TokenKind.OpenParen)
        {
            arguments = ParseArguments(ReadDelimited(TokenKind.OpenParen, TokenKind.CloseParen, validate: false));
        }
        Expect(TokenKind.OpenBrace, "'{' to open the UI element");
        var members = new List<UiMemberSyntax>();

        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            var before = _position;

            if (IsIdentifier("if"))
            {
                members.Add(ParseIf());
            }
            else if (IsIdentifier("foreach"))
            {
                members.Add(ParseForEach());
            }
            else if (IsIdentifier("yield"))
            {
                members.Add(ParseYield());
            }
            else if (IsIdentifier("slot"))
            {
                members.Add(ParseSlotSupply());
            }
            else if (Current.Kind is TokenKind.String or TokenKind.InterpolatedString)
            {
                members.Add(ParseImplicitContent());
            }
            else if (IsPropertyHeader())
            {
                members.Add(ParseProperty());
            }
            else if (Current.Kind == TokenKind.Identifier &&
                     Peek(1).Kind is TokenKind.OpenBrace or TokenKind.OpenParen)
            {
                var child = ParseElement();
                members.Add(new UiChildSyntax(child, child.Span));
            }
            else
            {
                AddUnsupported(
                    Current.Span,
                    "Expected a property assignment or nested UI element.");
                SynchronizeUiMember();
            }

            if (_position == before)
            {
                NextToken();
            }
        }

        var closeBrace = Expect(TokenKind.CloseBrace, "'}' to close the UI element");
        return new UiElementSyntax(
            nameText,
            members,
            SpanFrom(start, closeBrace.Span.End),
            arguments);
    }

    private IReadOnlyList<UiArgumentSyntax> ParseArguments(CSharpIslandSyntax clause)
    {
        var list = SyntaxFactory.ParseArgumentList("(" + clause.Text + ")");
        var offset = clause.Span.Start - 1;
        foreach (var diagnostic in list.GetDiagnostics())
        {
            AddSyntax(new SourceSpan(offset + diagnostic.Location.SourceSpan.Start,
                Math.Max(1, diagnostic.Location.SourceSpan.Length)),
                diagnostic.GetMessage());
        }
        return list.Arguments.Select(argument => new UiArgumentSyntax(
            argument.NameColon?.Name.Identifier.ValueText,
            argument.Expression.ToFullString().Trim(),
            new SourceSpan(offset + argument.SpanStart, argument.Span.Length),
            new SourceSpan(offset + argument.Expression.SpanStart, argument.Expression.Span.Length)))
            .ToArray();
    }

    private UiYieldSyntax ParseYield()
    {
        var start = ExpectIdentifier("yield").Span.Start;
        var name = Expect(TokenKind.Identifier, "a slot name after yield");
        var end = Expect(TokenKind.Semicolon, "';' after yield").Span.End;
        return new UiYieldSyntax(name.Text, SpanFrom(start, end), name.Span);
    }

    private UiSlotSupplySyntax ParseSlotSupply()
    {
        var start = ExpectIdentifier("slot").Span.Start;
        var name = Expect(TokenKind.Identifier, "a supplied slot name");
        var open = Expect(TokenKind.OpenBrace, "'{' to open slot supply");
        var roots = new List<UiElementSyntax>();
        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            roots.Add(ParseElement());
        }
        var close = Expect(TokenKind.CloseBrace, "'}' to close slot supply");
        return new UiSlotSupplySyntax(name.Text,
            new UiFragmentSyntax(roots, SpanFrom(open.Span.Start, close.Span.End)),
            SpanFrom(start, close.Span.End), name.Span);
    }

    private UiForEachSyntax ParseForEach()
    {
        var start = ExpectIdentifier("foreach").Span.Start;
        var openParen = Expect(TokenKind.OpenParen, "'(' after foreach");
        var headerStart = openParen.Span.End;
        var headerEnd = ScanMatchingDelimiter(
            headerStart,
            TokenKind.OpenParen,
            TokenKind.CloseParen);
        var header = Slice(headerStart, headerEnd);
        AdvanceTo(headerEnd);
        Expect(TokenKind.CloseParen, "')' after the foreach header");

        var match = ForEachHeaderPattern().Match(header);
        var itemName = match.Success ? match.Groups["item"].Value : "item";
        var sourceText = match.Success ? match.Groups["source"].Value.Trim() : header.Trim();
        var sourceOffset = match.Success
            ? header.IndexOf(match.Groups["source"].Value, StringComparison.Ordinal) +
              (match.Groups["source"].Value.Length - match.Groups["source"].Value.TrimStart().Length)
            : header.Length - header.TrimStart().Length;
        var sourceSpan = new SourceSpan(
            headerStart + Math.Max(0, sourceOffset),
            sourceText.Length);

        if (!match.Success)
        {
            AddSyntax(
                SpanFrom(headerStart, headerEnd),
                "A loop must use 'foreach (var item in expression) keyed by keyExpression'.");
        }
        else
        {
            ValidateCSharpIsland(
                sourceText,
                sourceSpan.Start,
                CSharpIslandKind.Expression);
        }

        ExpectIdentifier("keyed");
        ExpectIdentifier("by");
        var keyStart = Current.Span.Start;
        while (Current.Kind is not TokenKind.OpenBrace and not TokenKind.EndOfFile)
        {
            NextToken();
        }

        var rawKey = Slice(keyStart, Current.Span.Start);
        var keyText = rawKey.Trim();
        var keyTrim = rawKey.Length - rawKey.TrimStart().Length;
        var keySpan = new SourceSpan(keyStart + keyTrim, keyText.Length);
        ValidateCSharpIsland(keyText, keySpan.Start, CSharpIslandKind.Expression);

        Expect(TokenKind.OpenBrace, "'{' to open the foreach body");
        UiElementSyntax body;
        if (Current.Kind == TokenKind.Identifier &&
            Peek(1).Kind is TokenKind.OpenBrace or TokenKind.OpenParen)
        {
            body = ParseElement();
        }
        else
        {
            AddSyntax(Current.Span, "A keyed foreach body must contain one UI element.");
            body = new UiElementSyntax(
                "Missing",
                [],
                new SourceSpan(Current.Span.Start, 0));
        }

        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            AddUnsupported(
                Current.Span,
                "The initial keyed foreach supports one root UI element.");
            NextToken();
        }

        var closeBrace = Expect(TokenKind.CloseBrace, "'}' to close the foreach body");
        return new UiForEachSyntax(
            itemName,
            sourceText,
            sourceSpan,
            keyText,
            keySpan,
            body,
            SpanFrom(start, closeBrace.Span.End));
    }

    private UiIfSyntax ParseIf()
    {
        var start = ExpectIdentifier("if").Span.Start;
        var openParen = Expect(TokenKind.OpenParen, "'(' after if");
        var conditionStart = openParen.Span.End;
        var conditionEnd = ScanMatchingDelimiter(
            conditionStart,
            TokenKind.OpenParen,
            TokenKind.CloseParen);
        var rawCondition = Slice(conditionStart, conditionEnd);
        var condition = rawCondition.Trim();
        var trim = rawCondition.Length - rawCondition.TrimStart().Length;
        var conditionSpan = new SourceSpan(conditionStart + trim, condition.Length);
        ValidateCSharpIsland(condition, conditionSpan.Start, CSharpIslandKind.Expression);
        AdvanceTo(conditionEnd);
        Expect(TokenKind.CloseParen, "')' after the condition");
        var trueBranch = ParseConditionalBranch();
        UiConditionalBranchSyntax? falseBranch = null;
        if (IsIdentifier("else"))
        {
            NextToken();
            falseBranch = ParseConditionalBranch();
        }

        var end = falseBranch?.Span.End ?? trueBranch.Span.End;
        return new UiIfSyntax(condition, conditionSpan, trueBranch, falseBranch, SpanFrom(start, end));
    }

    private UiConditionalBranchSyntax ParseConditionalBranch()
    {
        var openBrace = Expect(TokenKind.OpenBrace, "'{' to open the conditional branch");
        var roots = new List<UiElementSyntax>();

        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            if (Current.Kind == TokenKind.Identifier &&
                Peek(1).Kind is TokenKind.OpenBrace or TokenKind.OpenParen)
            {
                roots.Add(ParseElement());
                continue;
            }

            AddSyntax(Current.Span, "Expected a native control root in the conditional branch.");
            SynchronizeUiMember();
        }
        var closeBrace = Expect(TokenKind.CloseBrace, "'}' to close the conditional branch");
        return new UiConditionalBranchSyntax(
            roots,
            SpanFrom(openBrace.Span.Start, closeBrace.Span.End));
    }

    private UiContentSyntax ParseImplicitContent()
    {
        var token = NextToken();
        var value = new StringValueSyntax(
            token.Text,
            token.Kind == TokenKind.InterpolatedString,
            token.Span);
        ValidateCSharpIsland(value.Text, value.Span.Start, CSharpIslandKind.Expression);

        var end = token.Span.End;
        if (Current.Kind == TokenKind.Semicolon)
        {
            end = NextToken().Span.End;
        }

        return new UiContentSyntax(
            value,
            SpanFrom(token.Span.Start, end));
    }

    private UiPropertySyntax ParseProperty()
    {
        var name = NextToken();
        var start = name.Span.Start;
        var segments = new List<SourceSpan> { name.Span };
        var nameText = name.Text;
        while (Current.Kind == TokenKind.Dot && Peek(1).Kind == TokenKind.Identifier)
        {
            NextToken();
            var segment = NextToken();
            segments.Add(segment.Span);
            nameText += "." + segment.Text;
        }
        Expect(TokenKind.Colon, "':' after the property name");

        if (Current.Kind == TokenKind.OpenBrace)
        {
            var eventBlock = ParseEventBlock();
            if (Current.Kind == TokenKind.Semicolon)
            {
                NextToken();
            }
            return new UiPropertySyntax(
                nameText,
                eventBlock,
                SpanFrom(start, Current.Span.Start), segments);
        }

        var island = ReadExpressionIsland();
        UiValueSyntax value;
        if (LooksLikeStringLiteral(island.Text))
        {
            value = new StringValueSyntax(
                island.Text,
                IsInterpolatedString(island.Text),
                island.Span);
        }
        else
        {
            value = new CSharpExpressionValueSyntax(island.Text, island.Span);
        }

        if (Current.Kind == TokenKind.Semicolon)
        {
            NextToken();
        }
        else
        {
            AddSyntax(
                new SourceSpan(island.Span.End, 0),
                "Expected ';' after the property value.");
        }

        return new UiPropertySyntax(
            nameText,
            value,
            SpanFrom(start, Math.Max(island.Span.End, Current.Span.Start)), segments);
    }

    private bool IsPropertyHeader()
    {
        if (Current.Kind != TokenKind.Identifier)
        {
            return false;
        }

        var lookahead = 1;
        while (Peek(lookahead).Kind == TokenKind.Dot &&
               Peek(lookahead + 1).Kind == TokenKind.Identifier)
        {
            lookahead += 2;
        }

        return Peek(lookahead).Kind == TokenKind.Colon;
    }

    private EventBlockValueSyntax ParseEventBlock()
    {
        var openBrace = Expect(TokenKind.OpenBrace, "'{' to open the event block");
        var contentStart = openBrace.Span.End;
        var closeStart = ScanMatchingBrace(contentStart - 1);

        AdvanceTo(closeStart);
        var closeBrace = Expect(TokenKind.CloseBrace, "'}' to close the event block");
        var contentLength = Math.Max(0, closeBrace.Span.Start - contentStart);
        var content = Slice(contentStart, closeBrace.Span.Start).Trim();

        ValidateCSharpIsland(
            content,
            contentStart + Math.Max(0, Slice(contentStart, closeBrace.Span.Start)
                .IndexOf(content, StringComparison.Ordinal)),
            CSharpIslandKind.StatementBlock);

        return new EventBlockValueSyntax(
            content,
            SpanFrom(openBrace.Span.Start, closeBrace.Span.End));
    }

    private CSharpIslandSyntax ReadExpressionIsland()
    {
        var start = Current.Span.Start;
        var end = ScanToTerminator(start, stopAtCloseBrace: true);
        var raw = Slice(start, end);
        var trimStart = raw.Length - raw.TrimStart().Length;
        var trimEnd = raw.TrimEnd().Length;
        var text = raw.Trim();
        var span = new SourceSpan(start + trimStart, Math.Max(0, trimEnd - trimStart));
        ValidateCSharpIsland(text, span.Start, CSharpIslandKind.Expression);
        AdvanceTo(end);
        return new CSharpIslandSyntax(
            CSharpIslandKind.Expression,
            text,
            span,
            new SourceSpan(end, Current.Span.Start == end ? 0 : 1));
    }

    private LucentSyntaxNode? ParseOpaqueMember()
    {
        var start = Current.Span.Start;
        var end = ScanToTerminator(start, stopAtCloseBrace: true);
        if (end <= start)
        {
            NextToken();
            return null;
        }

        AddUnsupported(
            new SourceSpan(start, end - start),
            "Only State<T>, Computed<T>, and Fragment Render() are supported in the initial compiler.");
        AdvanceTo(end);
        if (Current.Kind == TokenKind.Semicolon)
        {
            NextToken();
        }

        return null;
    }

    private CSharpIslandSyntax ReadDelimited(TokenKind open, TokenKind close, bool validate = true)
    {
        var openToken = Expect(open, $"'{TokenText(open)}'");
        var start = openToken.Span.End;
        var end = ScanMatchingDelimiter(start, open, close);
        AdvanceTo(end);
        var closeToken = Expect(close, $"'{TokenText(close)}'");
        var text = Slice(start, closeToken.Span.Start);
        if (validate)
        {
            ValidateCSharpIsland(text, start, CSharpIslandKind.Expression);
        }
        return new CSharpIslandSyntax(
            CSharpIslandKind.Expression,
            text.Trim(),
            new SourceSpan(start, Math.Max(0, closeToken.Span.Start - start)),
            new SourceSpan(closeToken.Span.Start, closeToken.Span.Length));
    }

    private int ScanToTerminator(int start, bool stopAtCloseBrace)
    {
        var source = _source.Text;
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;
        var index = start;
        var inString = '\0';
        var verbatim = false;
        var inLineComment = false;
        var inBlockComment = false;

        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                }

                index++;
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index += 2;
                }
                else
                {
                    index++;
                }

                continue;
            }

            if (inString != '\0')
            {
                if (!verbatim && current == '\\')
                {
                    index += Math.Min(2, source.Length - index);
                    continue;
                }

                if (current == inString)
                {
                    if (verbatim && next == '"')
                    {
                        index += 2;
                        continue;
                    }

                    inString = '\0';
                }

                index++;
                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                index += 2;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index += 2;
                continue;
            }

            var rawStringEnd = ScanRawString(index);
            if (rawStringEnd >= 0)
            {
                index = rawStringEnd;
                continue;
            }

            if (current is '"' or '\'')
            {
                inString = current;
                verbatim = false;
                index++;
                continue;
            }

            if (current == '@' &&
                next == '"')
            {
                inString = '"';
                verbatim = true;
                index += 2;
                continue;
            }

            switch (current)
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses = Math.Max(0, parentheses - 1);
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets = Math.Max(0, brackets - 1);
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    if (braces == 0 &&
                        parentheses == 0 &&
                        brackets == 0 &&
                        stopAtCloseBrace)
                    {
                        return index;
                    }

                    braces = Math.Max(0, braces - 1);
                    break;
                case ';':
                    if (parentheses == 0 && brackets == 0 && braces == 0)
                    {
                        return index;
                    }

                    break;
            }

            index++;
        }

        return index;
    }

    private int ScanMatchingBrace(int openingBrace)
    {
        var depth = 0;
        var inString = '\0';
        var verbatim = false;
        var lineComment = false;
        var blockComment = false;
        for (var index = openingBrace; index < _source.Text.Length; index++)
        {
            var current = _source.Text[index];
            var next = index + 1 < _source.Text.Length
                ? _source.Text[index + 1]
                : '\0';

            if (lineComment)
            {
                if (current is '\r' or '\n')
                {
                    lineComment = false;
                }

                continue;
            }

            if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }

                continue;
            }

            if (inString != '\0')
            {
                if (!verbatim && current == '\\')
                {
                    index++;
                    continue;
                }

                if (current == inString)
                {
                    if (verbatim && next == '"')
                    {
                        index++;
                        continue;
                    }

                    inString = '\0';
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                lineComment = true;
                index++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                blockComment = true;
                index++;
                continue;
            }

            var rawStringEnd = ScanRawString(index);
            if (rawStringEnd >= 0)
            {
                index = rawStringEnd;
                continue;
            }

            if (current is '"' or '\'')
            {
                inString = current;
                verbatim = false;
                continue;
            }

            if (current == '@' && next == '"')
            {
                inString = '"';
                verbatim = true;
                index++;
                continue;
            }

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}' &&
                     --depth == 0)
            {
                return index;
            }
        }

        return _source.Text.Length;
    }

    private int ScanRawString(int start)
    {
        var text = _source.Text;
        var quoteStart = start;
        if (start < text.Length && text[start] is ('$' or '@'))
        {
            quoteStart++;
            if (quoteStart < text.Length &&
                text[start] != text[quoteStart] &&
                text[quoteStart] is ('$' or '@'))
            {
                quoteStart++;
            }
        }

        if (quoteStart >= text.Length ||
            text[quoteStart] != '"')
        {
            return -1;
        }

        var quoteCount = 0;
        while (quoteStart + quoteCount < text.Length &&
               text[quoteStart + quoteCount] == '"')
        {
            quoteCount++;
        }

        if (quoteCount < 3)
        {
            return -1;
        }

        var contentStart = quoteStart + quoteCount;
        for (var index = contentStart; index < text.Length; index++)
        {
            if (text[index] != '"')
            {
                continue;
            }

            var closingCount = 0;
            while (index + closingCount < text.Length &&
                   text[index + closingCount] == '"')
            {
                closingCount++;
            }

            if (closingCount >= quoteCount)
            {
                return index + quoteCount;
            }

            index += closingCount - 1;
        }

        return text.Length;
    }

    private int ScanMatchingDelimiter(int start, TokenKind open, TokenKind close)
    {
        var openChar = open == TokenKind.OpenParen ? '(' : '{';
        var closeChar = close == TokenKind.CloseParen ? ')' : '}';
        var depth = 1;
        var inString = '\0';
        var verbatim = false;
        var lineComment = false;
        var blockComment = false;
        for (var index = start; index < _source.Text.Length; index++)
        {
            var current = _source.Text[index];
            var next = index + 1 < _source.Text.Length
                ? _source.Text[index + 1]
                : '\0';

            if (lineComment)
            {
                if (current is '\r' or '\n')
                {
                    lineComment = false;
                }

                continue;
            }

            if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }

                continue;
            }

            if (inString != '\0')
            {
                if (!verbatim && current == '\\')
                {
                    index++;
                    continue;
                }

                if (current == inString)
                {
                    if (verbatim && next == '"')
                    {
                        index++;
                        continue;
                    }

                    inString = '\0';
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                lineComment = true;
                index++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                blockComment = true;
                index++;
                continue;
            }

            var rawStringEnd = ScanRawString(index);
            if (rawStringEnd >= 0)
            {
                index = rawStringEnd;
                continue;
            }

            if (current is '"' or '\'')
            {
                inString = current;
                verbatim = false;
                continue;
            }

            if (current == '@' && next == '"')
            {
                inString = '"';
                verbatim = true;
                index++;
                continue;
            }

            if (current == openChar)
            {
                depth++;
            }
            else if (current == closeChar &&
                     --depth == 0)
            {
                return index;
            }
        }

        return _source.Text.Length;
    }

    private void ValidateCSharpIsland(string text, int absoluteStart, CSharpIslandKind kind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        SyntaxNode node = kind switch
        {
            CSharpIslandKind.StatementBlock =>
                SyntaxFactory.ParseStatement("{" + text + "}"),
            CSharpIslandKind.Member =>
                (SyntaxNode?)SyntaxFactory.ParseMemberDeclaration(text)
                ?? SyntaxFactory.ParseStatement(text),
            _ => SyntaxFactory.ParseExpression(text),
        };

        foreach (var diagnostic in node.GetDiagnostics())
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error ||
                !diagnostic.Location.IsInSource)
            {
                continue;
            }

            var span = diagnostic.Location.SourceSpan;
            var syntheticPrefixLength = kind == CSharpIslandKind.StatementBlock
                ? 1
                : 0;
            _diagnostics.Add(
                "LUC3001",
                $"Embedded C# is invalid: {diagnostic.GetMessage()}",
                new SourceSpan(
                    Math.Max(
                        absoluteStart,
                        absoluteStart + span.Start - syntheticPrefixLength),
                    Math.Max(1, span.Length)));
        }
    }

    private void SynchronizeUiMember()
    {
        while (Current.Kind is not TokenKind.EndOfFile and not TokenKind.CloseBrace)
        {
            if (Current.Kind == TokenKind.Identifier &&
                (Peek(1).Kind is TokenKind.Colon or TokenKind.OpenBrace))
            {
                return;
            }

            NextToken();
        }
    }

    private void SynchronizeTo(string keyword)
    {
        while (Current.Kind != TokenKind.EndOfFile &&
               !IsIdentifier(keyword))
        {
            NextToken();
        }
    }

    private void SkipUntil(TokenKind kind)
    {
        while (Current.Kind != kind &&
               Current.Kind != TokenKind.EndOfFile)
        {
            NextToken();
        }
    }

    private void AdvanceTo(int sourcePosition)
    {
        while (Current.Kind != TokenKind.EndOfFile &&
               Current.Span.Start < sourcePosition)
        {
            NextToken();
        }
    }

    private SyntaxToken ExpectIdentifier(string text)
    {
        if (IsIdentifier(text))
        {
            return NextToken();
        }

        AddSyntax(Current.Span, $"Expected '{text}'.");
        return new SyntaxToken(
            TokenKind.Identifier,
            text,
            new SourceSpan(Current.Span.Start, 0));
    }

    private SyntaxToken Expect(TokenKind kind, string expected)
    {
        if (Current.Kind == kind)
        {
            return NextToken();
        }

        AddSyntax(Current.Span, $"Expected {expected}.");
        return new SyntaxToken(
            kind,
            string.Empty,
            new SourceSpan(Current.Span.Start, 0));
    }

    private void AddSyntax(SourceSpan span, string message) =>
        _diagnostics.Add("LUC1001", message, span);

    private void AddUnsupported(SourceSpan span, string message) =>
        _diagnostics.Add("LUC1006", message, span);

    private bool IsIdentifier(string text) => IsIdentifier(Current, text);

    private static bool IsIdentifier(SyntaxToken token, string text) =>
        token.Kind == TokenKind.Identifier &&
        string.Equals(token.Text, text, StringComparison.Ordinal);

    private SyntaxToken Current => Peek(0);

    private SyntaxToken Peek(int offset)
    {
        var index = Math.Min(_position + offset, _tokens.Count - 1);
        return _tokens[index];
    }

    private SyntaxToken NextToken()
    {
        var current = Current;
        if (_position < _tokens.Count - 1)
        {
            _position++;
        }

        return current;
    }

    private string Slice(int start, int end) =>
        _source.Text.Substring(
            Math.Clamp(start, 0, _source.Text.Length),
            Math.Clamp(end - start, 0, _source.Text.Length - Math.Clamp(start, 0, _source.Text.Length)));

    private static bool LooksLikeStringLiteral(string text) =>
        text.TrimStart().StartsWith("\"", StringComparison.Ordinal) ||
        text.TrimStart().StartsWith("$\"", StringComparison.Ordinal) ||
        text.TrimStart().StartsWith("@\"", StringComparison.Ordinal) ||
        text.TrimStart().StartsWith("$@\"", StringComparison.Ordinal) ||
        text.TrimStart().StartsWith("@$\"", StringComparison.Ordinal);

    private static bool IsInterpolatedString(string text) =>
        text.TrimStart().StartsWith("$", StringComparison.Ordinal);

    private RenderMethodSyntax CreateMissingRender(int position) =>
        new(
            new UiElementSyntax("Missing", [], new SourceSpan(position, 0)),
            new SourceSpan(position, 0),
            []);

    private static string TokenText(TokenKind kind) =>
        kind switch
        {
            TokenKind.OpenParen => "'('",
            TokenKind.CloseParen => "')'",
            TokenKind.OpenBrace => "'{'",
            TokenKind.CloseBrace => "'}'",
            _ => "the expected token",
        };

    private static SourceSpan SpanFrom(int start, int end) =>
        new(start, Math.Max(0, end - start));

    [GeneratedRegex(
        @"\bState\s*<\s*(?<type>.+)\s*>\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s*\(\s*(?<initializer>.*)\s*\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex StatePattern();

    [GeneratedRegex(
        @"\bComputed\s*<\s*(?<type>.+)\s*>\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s*\(\s*(?<initializer>.*)\s*\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ComputedPattern();

    [GeneratedRegex(
        @"^\s*var\s+(?<item>[A-Za-z_][A-Za-z0-9_]*)\s+in\s+(?<source>.+)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ForEachHeaderPattern();
}
