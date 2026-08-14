using System.Text.RegularExpressions;
using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
        var components = new List<ComponentDeclarationSyntax>();

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
            components);
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

    private ComponentDeclarationSyntax ParseComponent()
    {
        var start = Current.Span.Start;
        ExpectIdentifier("component");
        var name = Expect(TokenKind.Identifier, "a component name");
        var parameters = ReadDelimited(TokenKind.OpenParen, TokenKind.CloseParen);

        Expect(TokenKind.OpenBrace, "'{' to open the component body");

        var states = new List<StateMemberSyntax>();
        RenderMethodSyntax? render = null;
        var members = new List<LucentSyntaxNode>();

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
            else if (IsIdentifier("private"))
            {
                var state = ParseStateMember();
                if (state is not null)
                {
                    states.Add(state);
                    members.Add(state);
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
            members);
    }

    private StateMemberSyntax? ParseStateMember()
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
        if (!match.Success)
        {
            AddUnsupported(
                new SourceSpan(start, Math.Max(1, end - start)),
                "State members must use the form State<T> name = new(...).");
            return null;
        }

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

    private RenderMethodSyntax ParseRenderMethod()
    {
        var start = Current.Span.Start;
        ExpectIdentifier("Fragment");
        ExpectIdentifier("Render");
        ReadDelimited(TokenKind.OpenParen, TokenKind.CloseParen);
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
            root = ParseElement();
            Expect(TokenKind.Semicolon, "';' after the rendered root");
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
                SynchronizeUiMember();
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

        if (Current.Kind == TokenKind.OpenBrace)
        {
            var eventBlock = ParseEventBlock();
            if (Current.Kind == TokenKind.Semicolon)
            {
                NextToken();
            }
            return new UiPropertySyntax(
                name.Text,
                eventBlock,
                SpanFrom(start, Current.Span.Start));
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
            name.Text,
            value,
            SpanFrom(start, Math.Max(island.Span.End, Current.Span.Start)));
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
            "Only State<T> members and Fragment Render() are supported in the initial compiler.");
        AdvanceTo(end);
        if (Current.Kind == TokenKind.Semicolon)
        {
            NextToken();
        }

        return null;
    }

    private CSharpIslandSyntax ReadDelimited(TokenKind open, TokenKind close)
    {
        var openToken = Expect(open, $"'{TokenText(open)}'");
        var start = openToken.Span.End;
        var end = ScanMatchingDelimiter(start, open, close);
        AdvanceTo(end);
        var closeToken = Expect(close, $"'{TokenText(close)}'");
        var text = Slice(start, closeToken.Span.Start);
        ValidateCSharpIsland(text, start, CSharpIslandKind.Expression);
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
        for (var index = start; index < _source.Text.Length; index++)
        {
            if (_source.Text[index] == openChar)
            {
                depth++;
            }
            else if (_source.Text[index] == closeChar &&
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
        @"\bState\s*<\s*(?<type>[^>]+)\s*>\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s*\(\s*(?<initializer>[^)]*)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex StatePattern();
}
