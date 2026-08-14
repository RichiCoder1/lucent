namespace Lucent.Compiler.Parsing;

internal enum TokenKind
{
    Bad,
    EndOfFile,
    Identifier,
    Number,
    String,
    InterpolatedString,
    Dot,
    Colon,
    Semicolon,
    Comma,
    OpenParen,
    CloseParen,
    OpenBrace,
    CloseBrace,
    LessThan,
    GreaterThan,
    Equals,
    Plus,
}

internal readonly record struct SyntaxToken(
    TokenKind Kind,
    string Text,
    SourceSpan Span)
{
    public int End => Span.End;
}
