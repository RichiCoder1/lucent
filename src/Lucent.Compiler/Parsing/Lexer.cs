namespace Lucent.Compiler.Parsing;

internal sealed class Lexer(SourceDocument source, DiagnosticBag diagnostics)
{
    private int _position;

    public IReadOnlyList<SyntaxToken> Lex()
    {
        var tokens = new List<SyntaxToken>();

        while (true)
        {
            SkipTrivia();

            var token = LexToken();
            tokens.Add(token);

            if (token.Kind == TokenKind.EndOfFile)
            {
                return tokens;
            }
        }
    }

    private SyntaxToken LexToken()
    {
        if (_position >= source.Text.Length)
        {
            return new SyntaxToken(
                TokenKind.EndOfFile,
                string.Empty,
                new SourceSpan(_position, 0));
        }

        var start = _position;
        var current = source.Text[_position];

        if (char.IsLetter(current) || current == '_')
        {
            _position++;
            while (_position < source.Text.Length)
            {
                var next = source.Text[_position];
                if (!char.IsLetterOrDigit(next) && next != '_')
                {
                    break;
                }

                _position++;
            }

            return CreateToken(TokenKind.Identifier, start);
        }

        if (char.IsDigit(current))
        {
            _position++;
            while (_position < source.Text.Length &&
                   char.IsDigit(source.Text[_position]))
            {
                _position++;
            }

            return CreateToken(TokenKind.Number, start);
        }

        if (current == '$' &&
            Peek(1) == '"')
        {
            return LexString(isInterpolated: true);
        }

        if (current == '"')
        {
            return LexString(isInterpolated: false);
        }

        _position++;
        return current switch
        {
            '.' => CreateToken(TokenKind.Dot, start),
            ':' => CreateToken(TokenKind.Colon, start),
            ';' => CreateToken(TokenKind.Semicolon, start),
            ',' => CreateToken(TokenKind.Comma, start),
            '(' => CreateToken(TokenKind.OpenParen, start),
            ')' => CreateToken(TokenKind.CloseParen, start),
            '{' => CreateToken(TokenKind.OpenBrace, start),
            '}' => CreateToken(TokenKind.CloseBrace, start),
            '<' => CreateToken(TokenKind.LessThan, start),
            '>' => CreateToken(TokenKind.GreaterThan, start),
            '=' => CreateToken(TokenKind.Equals, start),
            '+' => CreateToken(TokenKind.Plus, start),
            _ => CreateToken(TokenKind.Unknown, start),
        };
    }

    private SyntaxToken LexString(bool isInterpolated)
    {
        var start = _position;
        _position += isInterpolated ? 2 : 1;
        var terminated = false;

        while (_position < source.Text.Length)
        {
            var current = source.Text[_position++];
            if (current == '\\' && _position < source.Text.Length)
            {
                _position++;
                continue;
            }

            if (current == '"')
            {
                terminated = true;
                break;
            }

            if (!isInterpolated && current is ('\r' or '\n'))
            {
                break;
            }
        }

        if (!terminated)
        {
            diagnostics.Add(
                "LUC0002",
                "Unterminated string literal.",
                new SourceSpan(start, Math.Max(1, _position - start)));
        }

        return CreateToken(
            isInterpolated
                ? TokenKind.InterpolatedString
                : TokenKind.String,
            start);
    }

    private SyntaxToken CreateToken(TokenKind kind, int start) =>
        new(
            kind,
            source.Text[start.._position],
            new SourceSpan(start, _position - start));

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < source.Text.Length
            ? source.Text[index]
            : '\0';
    }

    private void SkipTrivia()
    {
        while (_position < source.Text.Length)
        {
            if (char.IsWhiteSpace(source.Text[_position]))
            {
                _position++;
                continue;
            }

            if (source.Text[_position] == '/' && Peek(1) == '/')
            {
                _position += 2;
                while (_position < source.Text.Length &&
                       source.Text[_position] != '\n')
                {
                    _position++;
                }

                continue;
            }

            if (source.Text[_position] == '/' && Peek(1) == '*')
            {
                var start = _position;
                _position += 2;

                while (_position < source.Text.Length &&
                       !(source.Text[_position] == '*' && Peek(1) == '/'))
                {
                    _position++;
                }

                if (_position >= source.Text.Length)
                {
                    diagnostics.Add(
                        "LUC0003",
                        "Unterminated block comment.",
                        new SourceSpan(start, source.Text.Length - start));
                    return;
                }

                _position += 2;
                continue;
            }

            return;
        }
    }
}
