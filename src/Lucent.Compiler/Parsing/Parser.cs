using Lucent.Compiler.Syntax;

namespace Lucent.Compiler.Parsing;

internal sealed class Parser
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
        ExpectIdentifier("namespace");
        var namespaceName = ParseQualifiedName();
        Expect(TokenKind.Semicolon, "';' after the namespace declaration");

        var component = ParseComponent();

        if (Current.Kind != TokenKind.EndOfFile)
        {
            AddUnsupported(
                Current.Span,
                "The initial compiler supports exactly one component per file.");
        }

        return new CompilationUnitSyntax(
            namespaceName,
            component,
            new SourceSpan(start, Math.Max(0, Current.Span.Start - start)));
    }

    private ComponentDeclarationSyntax ParseComponent()
    {
        var start = Current.Span.Start;
        ExpectIdentifier("component");
        var name = Expect(TokenKind.Identifier, "a component name");

        Expect(TokenKind.OpenParen, "'(' after the component name");
        if (Current.Kind != TokenKind.CloseParen)
        {
            AddUnsupported(
                Current.Span,
                "Component parameters are not supported in the initial compiler.");
            SkipUntil(TokenKind.CloseParen);
        }

        Expect(TokenKind.CloseParen, "')' after the component parameters");
        Expect(TokenKind.OpenBrace, "'{' to open the component body");

        StateMemberSyntax? state = null;
        RenderMethodSyntax? render = null;

        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            var before = _position;

            if (IsIdentifier("private"))
            {
                if (state is not null)
                {
                    AddUnsupported(
                        Current.Span,
                        "The initial compiler supports exactly one state member.");
                    SkipMember();
                }
                else
                {
                    state = ParseStateMember();
                }
            }
            else if (IsIdentifier("Fragment"))
            {
                var parsedRender = ParseRenderMethod();
                if (render is not null)
                {
                    _diagnostics.Add(
                        "LUC1002",
                        "A component must contain exactly one Fragment Render() method.",
                        parsedRender.Span);
                }
                else
                {
                    render = parsedRender;
                }
            }
            else
            {
                AddUnsupported(
                    Current.Span,
                    "Only a State<T> member and Fragment Render() are supported in the initial compiler.");
                SkipMember();
            }

            if (_position == before)
            {
                NextToken();
            }
        }

        var closeBrace = Expect(TokenKind.CloseBrace, "'}' to close the component");

        if (state is null)
        {
            _diagnostics.Add(
                "LUC1003",
                "The initial compiler requires one private readonly State<int> member.",
                name.Span);
            state = new StateMemberSyntax(
                "int",
                "missingState",
                0,
                new SourceSpan(name.Span.End, 0));
        }

        if (render is null)
        {
            _diagnostics.Add(
                "LUC1002",
                "A component must contain exactly one Fragment Render() method.",
                name.Span);
            render = new RenderMethodSyntax(
                new UiElementSyntax(
                    "Missing",
                    [],
                    new SourceSpan(name.Span.End, 0)),
                new SourceSpan(name.Span.End, 0));
        }

        return new ComponentDeclarationSyntax(
            name.Text,
            state,
            render,
            SpanFrom(start, closeBrace.Span.End));
    }

    private StateMemberSyntax ParseStateMember()
    {
        var start = Current.Span.Start;
        ExpectIdentifier("private");
        ExpectIdentifier("readonly");
        ExpectIdentifier("State");
        Expect(TokenKind.LessThan, "'<' in the state type");
        var type = Expect(TokenKind.Identifier, "a state value type");
        Expect(TokenKind.GreaterThan, "'>' in the state type");
        var name = Expect(TokenKind.Identifier, "a state member name");
        Expect(TokenKind.Equals, "'=' in the state initializer");
        ExpectIdentifier("new");
        Expect(TokenKind.OpenParen, "'(' in the state initializer");
        var initialValue = Expect(TokenKind.Number, "an integer state initializer");
        Expect(TokenKind.CloseParen, "')' in the state initializer");
        var semicolon = Expect(TokenKind.Semicolon, "';' after the state member");

        _ = int.TryParse(initialValue.Text, out var parsedInitialValue);
        return new StateMemberSyntax(
            type.Text,
            name.Text,
            parsedInitialValue,
            SpanFrom(start, semicolon.Span.End));
    }

    private RenderMethodSyntax ParseRenderMethod()
    {
        var start = Current.Span.Start;
        ExpectIdentifier("Fragment");
        ExpectIdentifier("Render");
        Expect(TokenKind.OpenParen, "'(' after Render");
        Expect(TokenKind.CloseParen, "')' after Render");
        Expect(TokenKind.OpenBrace, "'{' to open Render");
        ExpectIdentifier("return");
        var root = ParseElement();
        Expect(TokenKind.Semicolon, "';' after the rendered root");
        var closeBrace = Expect(TokenKind.CloseBrace, "'}' to close Render");

        return new RenderMethodSyntax(
            root,
            SpanFrom(start, closeBrace.Span.End));
    }

    private UiElementSyntax ParseElement()
    {
        var name = Expect(TokenKind.Identifier, "a UI element name");
        var start = name.Span.Start;
        Expect(TokenKind.OpenBrace, "'{' to open the UI element");
        var members = new List<UiMemberSyntax>();

        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            var before = _position;

            if (Current.Kind == TokenKind.Identifier &&
                Peek(1).Kind == TokenKind.Colon)
            {
                members.Add(ParseProperty());
            }
            else if (Current.Kind == TokenKind.Identifier &&
                     Peek(1).Kind == TokenKind.OpenBrace)
            {
                var child = ParseElement();
                members.Add(new UiChildSyntax(child, child.Span));
            }
            else
            {
                AddUnsupported(
                    Current.Span,
                    "Expected a property assignment or nested UI element.");
                NextToken();
            }

            if (_position == before)
            {
                NextToken();
            }
        }

        var closeBrace = Expect(TokenKind.CloseBrace, "'}' to close the UI element");
        return new UiElementSyntax(
            name.Text,
            members,
            SpanFrom(start, closeBrace.Span.End));
    }

    private UiPropertySyntax ParseProperty()
    {
        var name = NextToken();
        var start = name.Span.Start;
        Expect(TokenKind.Colon, "':' after the property name");

        UiValueSyntax value;
        if (Current.Kind == TokenKind.OpenBrace)
        {
            value = ParseEventBlock();
            if (Current.Kind == TokenKind.Semicolon)
            {
                NextToken();
            }
        }
        else if (Current.Kind is TokenKind.String or TokenKind.InterpolatedString)
        {
            var token = NextToken();
            value = new StringValueSyntax(
                token.Text,
                token.Kind == TokenKind.InterpolatedString,
                token.Span);
            Expect(TokenKind.Semicolon, "';' after the property value");
        }
        else
        {
            AddUnsupported(
                Current.Span,
                "The initial compiler supports string values, interpolated strings, and event blocks.");
            var token = NextToken();
            value = new StringValueSyntax(token.Text, false, token.Span);
            Expect(TokenKind.Semicolon, "';' after the property value");
        }

        return new UiPropertySyntax(
            name.Text,
            value,
            SpanFrom(start, value.Span.End));
    }

    private EventBlockValueSyntax ParseEventBlock()
    {
        var openBrace = Expect(TokenKind.OpenBrace, "'{' to open the event block");
        var contentStart = openBrace.Span.End;
        var depth = 1;
        SyntaxToken closeBrace = default;

        while (Current.Kind != TokenKind.EndOfFile)
        {
            var token = NextToken();
            if (token.Kind == TokenKind.OpenBrace)
            {
                depth++;
            }
            else if (token.Kind == TokenKind.CloseBrace)
            {
                depth--;
                if (depth == 0)
                {
                    closeBrace = token;
                    break;
                }
            }
        }

        if (depth != 0)
        {
            _diagnostics.Add(
                "LUC1001",
                "Expected '}' to close the event block.",
                new SourceSpan(Current.Span.Start, 0));
            closeBrace = new SyntaxToken(
                TokenKind.CloseBrace,
                string.Empty,
                new SourceSpan(Current.Span.Start, 0));
        }

        var contentLength = Math.Max(0, closeBrace.Span.Start - contentStart);
        return new EventBlockValueSyntax(
            _source.Text.Substring(contentStart, contentLength).Trim(),
            SpanFrom(openBrace.Span.Start, closeBrace.Span.End));
    }

    private string ParseQualifiedName()
    {
        var parts = new List<string>
        {
            Expect(TokenKind.Identifier, "a namespace name").Text,
        };

        while (Current.Kind == TokenKind.Dot)
        {
            NextToken();
            parts.Add(Expect(TokenKind.Identifier, "a namespace name").Text);
        }

        return string.Join(".", parts);
    }

    private void SkipMember()
    {
        var braceDepth = 0;
        do
        {
            var token = NextToken();
            if (token.Kind == TokenKind.OpenBrace)
            {
                braceDepth++;
            }
            else if (token.Kind == TokenKind.CloseBrace)
            {
                if (braceDepth == 0)
                {
                    _position--;
                    return;
                }

                braceDepth--;
            }

            if (braceDepth == 0 && token.Kind == TokenKind.Semicolon)
            {
                return;
            }
        }
        while (Current.Kind != TokenKind.EndOfFile);
    }

    private void SkipUntil(TokenKind kind)
    {
        while (Current.Kind != kind &&
               Current.Kind != TokenKind.EndOfFile)
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

        _diagnostics.Add(
            "LUC1001",
            $"Expected '{text}'.",
            new SourceSpan(Current.Span.Start, 0));
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

        _diagnostics.Add(
            "LUC1001",
            $"Expected {expected}.",
            new SourceSpan(Current.Span.Start, 0));
        return new SyntaxToken(
            kind,
            string.Empty,
            new SourceSpan(Current.Span.Start, 0));
    }

    private void AddUnsupported(SourceSpan span, string message) =>
        _diagnostics.Add("LUC1006", message, span);

    private bool IsIdentifier(string text) =>
        Current.Kind == TokenKind.Identifier &&
        string.Equals(Current.Text, text, StringComparison.Ordinal);

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

    private static SourceSpan SpanFrom(int start, int end) =>
        new(start, Math.Max(0, end - start));
}
