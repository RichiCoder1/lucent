using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler;

/// <summary>Bounded parser for the preview <c>.lui</c> authoring surface.</summary>
/// <remarks>C# islands are lexed by Roslyn. Parsing returns an immutable recovered tree and diagnostics rather than throwing for malformed syntax.</remarks>
public static class LuiParser
{
    /// <summary>Parses source text into a span-preserving recovered syntax tree.</summary>
    /// <param name="source">Exact <c>.lui</c> text to parse.</param>
    /// <returns>An immutable document whose spans are measured against <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static LuiDocumentSyntax Parse(string source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        return new Reader(source).Document();
    }

    private sealed class Reader
    {
        private readonly string text;
        private readonly List<(LuiDiagnostic Diagnostic, int Ordinal)> diagnostics =
            new List<(LuiDiagnostic, int)>();
        private int position;
        private int nesting;
        private bool nestingReported;
        private const int NestingBudget = 256;

        internal Reader(string text)
        {
            this.text = text;
        }

        internal LuiDocumentSyntax Document()
        {
            string? ns = null;
            var usings = new List<string>();
            LuiComponentSyntax? component = null;
            var styles = new List<LuiStyleSyntax>();
            var comments = new List<LuiTopLevelCommentSyntax>();
            var topLevel = new List<LuiSyntaxNode>();
            while (!End)
            {
                White();
                if (End)
                    break;
                var start = position;
                if (Starts("//") || Starts("/*"))
                {
                    var comment = TopLevelComment();
                    comments.Add(comment);
                    topLevel.Add(comment);
                    continue;
                }
                if (Word("namespace"))
                {
                    var keyword = Token("namespace", start, 9);
                    var statement = Statement("namespace");
                    if (
                        ValidCompilationUnit(
                            "namespace " + statement.Value + ";",
                            unit =>
                                unit.Members.Count == 1
                                && (
                                    unit.Members[0] is NamespaceDeclarationSyntax
                                    || unit.Members[0] is FileScopedNamespaceDeclarationSyntax
                                )
                        )
                    )
                    {
                        ns = statement.Value;
                        topLevel.Add(
                            new LuiNamespaceSyntax(
                                LuiSpan.From(start, position),
                                keyword,
                                statement.Value,
                                statement.Semicolon
                            )
                        );
                    }
                    else
                        Error(
                            "LUI1000",
                            "Invalid C# namespace.",
                            new LuiSpan(start, Math.Max(1, position - start))
                        );
                    continue;
                }
                if (Word("using"))
                {
                    var keyword = Token("using", start, 5);
                    var statement = Statement("using");
                    if (
                        ValidCompilationUnit(
                            "using " + statement.Value + ";",
                            unit => unit.Usings.Count == 1
                        )
                    )
                    {
                        usings.Add(statement.Value);
                        topLevel.Add(
                            new LuiUsingSyntax(
                                LuiSpan.From(start, position),
                                keyword,
                                statement.Value,
                                statement.Semicolon
                            )
                        );
                    }
                    else
                        Error(
                            "LUI1000",
                            "Invalid C# using directive.",
                            new LuiSpan(start, Math.Max(1, position - start))
                        );
                    continue;
                }
                if (PeekWord("style"))
                {
                    var style = Style();
                    styles.Add(style);
                    topLevel.Add(style);
                    continue;
                }
                if (PeekWord("public") || PeekWord("internal") || PeekWord("component"))
                {
                    var next = Component();
                    topLevel.Add(next);
                    if (component == null)
                        component = next;
                    else
                        Error("LUI1001", "A document declares exactly one component.", next.Span);
                    continue;
                }
                Error(
                    "LUI1002",
                    "Expected namespace, using, style, or component.",
                    new LuiSpan(position, 1)
                );
                position++;
            }
            if (component == null)
                Error("LUI1003", "A component declaration is required.", new LuiSpan(position, 0));
            return new LuiDocumentSyntax(
                new LuiSpan(0, text.Length),
                text,
                ns,
                usings.ToArray(),
                component,
                styles.ToArray(),
                comments.ToArray(),
                topLevel.ToArray(),
                diagnostics
                    .OrderBy(item => item.Diagnostic.Span.Start)
                    .ThenBy(item => item.Ordinal)
                    .Select(item => item.Diagnostic)
                    .ToArray()
            );
        }

        private LuiComponentSyntax Component()
        {
            var start = position;
            LuiToken accessibility;
            if (Word("public"))
                accessibility = Token("public", start, 6);
            else if (Word("internal"))
                accessibility = Token("internal", start, 8);
            else
                accessibility = Missing("internal");
            var componentKeyword = ExpectWord("component");
            var name = Name("component name");
            var openParameters = Expect('(');
            var parameterStart = position;
            var parameterEnd = IslandScanner.End(text, position, ')');
            position = parameterEnd;
            var parameters = Parameters(parameterStart, parameterEnd);
            var closeParameters = Expect(')');
            var open = Expect('{');
            var body = open.IsMissing ? Array.Empty<LuiBodySyntax>() : Body('}', true);
            var close = open.IsMissing ? Missing("}") : Expect('}');
            if (
                !name.IsMissing
                && !ValidCompilationUnit(
                    "class " + name.Text + " {}",
                    unit => unit.Members.Count == 1 && unit.Members[0] is ClassDeclarationSyntax
                )
            )
                Error("LUI1000", "Invalid C# component identifier.", name.Span);
            if (body.Count(node => !(node is LuiCommentSyntax)) != 1)
                Error(
                    "LUI1004",
                    "A component body requires exactly one root construct.",
                    LuiSpan.From(open.Span.End, close.Span.Start)
                );
            return new LuiComponentSyntax(
                LuiSpan.From(start, position),
                accessibility,
                componentKeyword,
                name,
                parameters,
                openParameters,
                closeParameters,
                open,
                body,
                close
            );
        }

        private IReadOnlyList<LuiParameterSyntax> Parameters(int start, int end)
        {
            var raw = text.Substring(start, Math.Max(0, end - start));
            var list = SyntaxFactory.ParseParameterList("(" + raw + ")");
            if (
                list.ContainsDiagnostics
                || list.Parameters.Any(parameter =>
                    parameter.Type == null || parameter.Identifier.IsMissing
                )
            )
            {
                Error(
                    "LUI1005",
                    "Parameters require valid C# type and name syntax.",
                    new LuiSpan(start, raw.Length)
                );
                return Array.Empty<LuiParameterSyntax>();
            }
            var result = new List<LuiParameterSyntax>();
            for (var i = 0; i < list.Parameters.Count; i++)
            {
                var parameter = list.Parameters[i];
                var parameterStart = start + Math.Max(0, parameter.SpanStart - 1);
                if (
                    parameter.AttributeLists.Count != 0
                    || parameter.Modifiers.Any(modifier =>
                        modifier.IsKind(SyntaxKind.RefKeyword)
                        || modifier.IsKind(SyntaxKind.OutKeyword)
                        || modifier.IsKind(SyntaxKind.InKeyword)
                        || modifier.IsKind(SyntaxKind.ParamsKeyword)
                    )
                )
                    Error(
                        "LUI3004",
                        "Component parameters cannot have attributes, ref, out, in, or params modifiers.",
                        new LuiSpan(parameterStart, parameter.Span.Length)
                    );
                var type = parameter.Type!;
                var typeStart = start + Math.Max(0, type.SpanStart - 1);
                var nameStart = start + Math.Max(0, parameter.Identifier.SpanStart - 1);
                var separator =
                    i < list.Parameters.SeparatorCount ? list.Parameters.GetSeparator(i) : default;
                var comma =
                    separator.RawKind == 0
                        ? Missing(",")
                        : new LuiToken(
                            ",",
                            new LuiSpan(start + separator.SpanStart - 1, separator.Span.Length),
                            false
                        );
                result.Add(
                    new LuiParameterSyntax(
                        new LuiSpan(parameterStart, parameter.Span.Length),
                        text.Substring(typeStart, type.Span.Length),
                        text.Substring(parameterStart, parameter.Span.Length),
                        new LuiToken(
                            parameter.Identifier.Text,
                            new LuiSpan(nameStart, parameter.Identifier.Span.Length),
                            false
                        ),
                        comma
                    )
                );
            }
            return result;
        }

        private LuiStyleSyntax Style()
        {
            var start = position;
            var styleKeyword = ExpectWord("style");
            var name = Name("style name");
            var open = Expect('{');
            var members = open.IsMissing ? Array.Empty<LuiStyleMemberSyntax>() : StyleMembers('}');
            var close = open.IsMissing ? Missing("}") : Expect('}');
            return new LuiStyleSyntax(
                LuiSpan.From(start, position),
                styleKeyword,
                name,
                open,
                members,
                close
            );
        }

        private IReadOnlyList<LuiStyleMemberSyntax> StyleMembers(char end)
        {
            var result = new List<LuiStyleMemberSyntax>();
            while (!End && Current != end && !TopLevelStart())
            {
                White();
                if (Current == end || End || TopLevelStart())
                    break;
                var start = position;
                if (Word("when"))
                {
                    var when = Token("when", start, 4);
                    var variant = VariantCondition();
                    var whenOpen = Expect('{');
                    if (!whenOpen.IsMissing && EnterNesting())
                    {
                        var inner = StyleMembers('}').OfType<LuiStyleAssignmentSyntax>().ToArray();
                        var whenClose = Expect('}');
                        ExitNesting();
                        result.Add(
                            new LuiVariantGroupSyntax(
                                LuiSpan.From(start, position),
                                when,
                                variant,
                                whenOpen,
                                inner,
                                whenClose
                            )
                        );
                    }
                    else if (!whenOpen.IsMissing)
                        SkipBalancedBrace();
                    continue;
                }
                var property = Name("style property");
                var colon = Expect(':');
                if (property.IsMissing)
                {
                    RecoverTo(end, ';');
                    continue;
                }
                var valueStart = position;
                var valueEnd = IslandScanner.StyleEnd(text, position);
                position = valueEnd;
                var expression = Expression(
                    new LuiSpan(valueStart, valueEnd - valueStart),
                    text.Substring(valueStart, valueEnd - valueStart)
                );
                var terminator = Current == ';' ? Token(";", position++, 1) : Missing(";");
                result.Add(
                    new LuiStyleAssignmentSyntax(
                        LuiSpan.From(start, position),
                        property,
                        colon,
                        expression,
                        terminator
                    )
                );
                if (terminator.IsMissing && !colon.IsMissing)
                    Error("LUI1015", "Expected ';' after style assignment.", terminator.Span);
            }
            return result;
        }

        private LuiToken VariantCondition()
        {
            White();
            var start = position;
            while (!End && Current != '{')
                position++;
            var value = text.Substring(start, position - start).Trim();
            var leading = text.Substring(start, position - start)
                .IndexOf(value, StringComparison.Ordinal);
            if (value.Length == 0)
            {
                Error("LUI1014", "Expected variant condition.", new LuiSpan(start, 0));
                return Missing("");
            }
            return new LuiToken(value, new LuiSpan(start + leading, value.Length), false);
        }

        private IReadOnlyList<LuiBodySyntax> Body(char end, bool stopTopLevel = false)
        {
            var result = new List<LuiBodySyntax>();
            while (
                !End
                && Current != end
                && Current != '}'
                && (end != '\0' || !Starts("</"))
                && !(stopTopLevel && TopLevelStart())
            )
            {
                var white = position;
                White();
                if (
                    End
                    || Current == end
                    || Current == '}'
                    || (stopTopLevel && TopLevelStart())
                    || (end == '\0' && Starts("</"))
                )
                    break;
                if (
                    !(
                        Current == '<'
                        || Current == '{'
                        || Starts("{/*")
                        || PeekRegion("if")
                        || PeekRegion("foreach")
                    )
                )
                    position = white;
                var before = position;
                if (Starts("{/*"))
                {
                    result.Add(Comment());
                }
                else if (Current == '<')
                    result.Add(Element());
                else if (Current == '{')
                    result.Add(ExpressionBody());
                else if (PeekRegion("if"))
                    result.Add(If());
                else if (PeekRegion("foreach"))
                    result.Add(ForEach());
                else
                {
                    var start = position;
                    while (
                        !End
                        && Current != end
                        && Current != '}'
                        && Current != '<'
                        && Current != '{'
                        && !Starts("{/*")
                        && !PeekRegion("if")
                        && !PeekRegion("foreach")
                        && !(stopTopLevel && TopLevelStart())
                        && !(end == '\0' && Starts("</"))
                    )
                        position++;
                    AddText(result, start, position);
                }
                if (position == before)
                {
                    Error("LUI1017", "Parser recovery made no progress.", new LuiSpan(position, 0));
                    position++;
                }
            }
            return result;
        }

        private LuiExpressionBodySyntax ExpressionBody()
        {
            var start = position;
            var open = Token("{", position++, 1);
            var contentStart = position;
            var end = IslandScanner.End(text, position, '}');
            position = end;
            var content = text.Substring(contentStart, end - contentStart);
            var close = Missing("}");
            if (Current == '}')
            {
                close = Token("}", position++, 1);
            }
            else
                Error(
                    "LUI1013",
                    "Unterminated expression island.",
                    new LuiSpan(start, text.Length - start)
                );
            var expression = Expression(
                new LuiSpan(contentStart, content.Length),
                content,
                open,
                close
            );
            return new LuiExpressionBodySyntax(
                expression.Span,
                expression.Text,
                expression.Expression,
                expression.OpenBrace,
                expression.CloseBrace
            );
        }

        private LuiCommentSyntax Comment()
        {
            var start = position;
            position += 3;
            var close = text.IndexOf("*/}", position, StringComparison.Ordinal);
            if (close < 0)
            {
                Error("LUI1006", "Unterminated comment.", new LuiSpan(start, text.Length - start));
                position = text.Length;
            }
            else
                position = close + 3;
            return new LuiCommentSyntax(
                LuiSpan.From(start, position),
                text.Substring(start, position - start)
            );
        }

        private LuiTopLevelCommentSyntax TopLevelComment()
        {
            var start = position;
            if (Starts("//"))
            {
                position += 2;
                while (!End && Current != '\r' && Current != '\n')
                    position++;
            }
            else
            {
                position += 2;
                var close = text.IndexOf("*/", position, StringComparison.Ordinal);
                if (close < 0)
                {
                    Error(
                        "LUI1006",
                        "Unterminated comment.",
                        new LuiSpan(start, text.Length - start)
                    );
                    position = text.Length;
                }
                else
                    position = close + 2;
            }
            return new LuiTopLevelCommentSyntax(
                LuiSpan.From(start, position),
                text.Substring(start, position - start)
            );
        }

        private LuiIfSyntax If()
        {
            var start = position;
            var ifKeyword = ExpectWord("if");
            var openCondition = Expect('(');
            var condition = IslandExpression(')', '{', '<', '}');
            var closeCondition = Expect(')');
            var open = Expect('{');
            var entered = !open.IsMissing && EnterNesting();
            var thenBody = entered ? Body('}') : Array.Empty<LuiBodySyntax>();
            var close =
                open.IsMissing ? Missing("}")
                : entered ? Expect('}')
                : SkipBalancedBrace();
            if (entered)
                ExitNesting();
            White();
            LuiToken elseKeyword = Missing("else");
            var elseBody = new List<LuiBodySyntax>();
            LuiToken elseOpen = Missing("{");
            LuiToken elseClose = Missing("}");
            if (Word("else"))
            {
                elseKeyword = Token("else", position - 4, 4);
                elseOpen = Expect('{');
                if (!elseOpen.IsMissing && EnterNesting())
                {
                    elseBody.AddRange(Body('}'));
                    elseClose = Expect('}');
                    ExitNesting();
                }
                else if (!elseOpen.IsMissing)
                    elseClose = SkipBalancedBrace();
            }
            return new LuiIfSyntax(
                LuiSpan.From(start, position),
                ifKeyword,
                openCondition,
                condition,
                closeCondition,
                open,
                thenBody,
                close,
                elseKeyword,
                elseOpen,
                elseBody,
                elseClose
            );
        }

        private LuiForEachSyntax ForEach()
        {
            var start = position;
            var foreachKeyword = ExpectWord("foreach");
            var openHeader = Expect('(');
            var varKeyword = ExpectWord("var");
            var variable = Name("foreach variable");
            var inKeyword = ExpectWord("in");
            var source = ForEachSourceExpression();
            var closeHeader = Expect(')');
            var keyedKeyword = ExpectWord("keyed");
            var byKeyword = ExpectWord("by");
            var key = IslandExpression('{', '<', '}');
            var open = Expect('{');
            var entered = !open.IsMissing && EnterNesting();
            var body = entered ? Body('}') : Array.Empty<LuiBodySyntax>();
            var close =
                open.IsMissing ? Missing("}")
                : entered ? Expect('}')
                : SkipBalancedBrace();
            if (entered)
                ExitNesting();
            return new LuiForEachSyntax(
                LuiSpan.From(start, position),
                foreachKeyword,
                openHeader,
                varKeyword,
                variable,
                inKeyword,
                source,
                closeHeader,
                keyedKeyword,
                byKeyword,
                key,
                open,
                body,
                close
            );
        }

        private LuiElementSyntax Element()
        {
            var start = position;
            var openAngle = Expect('<');
            var name = Name("element name");
            var attributes = new List<LuiAttributeSyntax>();
            var selfClosing = false;
            var openClose = Missing(">");
            var slash = Missing("/");
            while (!End)
            {
                White();
                if (Starts("/>"))
                {
                    slash = Token("/", position++, 1);
                    openClose = Token(">", position++, 1);
                    selfClosing = true;
                    break;
                }
                if (Current == '>')
                {
                    openClose = Token(">", position++, 1);
                    break;
                }
                if (Current == '<' || Starts("</"))
                {
                    Error("LUI1007", "Expected '>' or '/>' in element.", new LuiSpan(position, 1));
                    break;
                }
                var before = position;
                attributes.Add(Attribute());
                if (position == before)
                {
                    Error("LUI1017", "Parser recovery made no progress.", new LuiSpan(position, 0));
                    position++;
                }
            }
            if (selfClosing)
                return new LuiElementSyntax(
                    LuiSpan.From(start, position),
                    openAngle,
                    name,
                    attributes,
                    openClose,
                    slash,
                    Array.Empty<LuiBodySyntax>(),
                    Missing("</"),
                    Missing(""),
                    Missing(">"),
                    true
                );
            if (openClose.IsMissing)
                return new LuiElementSyntax(
                    LuiSpan.From(start, position),
                    openAngle,
                    name,
                    attributes,
                    openClose,
                    slash,
                    Array.Empty<LuiBodySyntax>(),
                    Missing("</"),
                    Missing(name.Text),
                    Missing(">"),
                    false
                );
            if (!EnterNesting())
            {
                SkipElementContent();
                return new LuiElementSyntax(
                    LuiSpan.From(start, position),
                    openAngle,
                    name,
                    attributes,
                    openClose,
                    slash,
                    Array.Empty<LuiBodySyntax>(),
                    Missing("</"),
                    Missing(name.Text),
                    Missing(">"),
                    false
                );
            }
            var children = Body('\0');
            ExitNesting();
            if (!Starts("</"))
            {
                Error("LUI1008", "Missing closing element tag.", new LuiSpan(position, 0));
                return new LuiElementSyntax(
                    LuiSpan.From(start, position),
                    openAngle,
                    name,
                    attributes,
                    openClose,
                    slash,
                    children,
                    Missing("</"),
                    Missing(name.Text),
                    Missing(">"),
                    false
                );
            }
            var closeOpen = Token("</", position, 2);
            position += 2;
            var close = Name("closing element name");
            var closeAngle = Expect('>');
            if (!name.IsMissing && !close.IsMissing && name.Text != close.Text)
                Error("LUI1009", "Closing element tag does not match opening tag.", close.Span);
            return new LuiElementSyntax(
                LuiSpan.From(start, position),
                openAngle,
                name,
                attributes,
                openClose,
                slash,
                children,
                closeOpen,
                close,
                closeAngle,
                false
            );
        }

        private LuiAttributeSyntax Attribute()
        {
            var start = position;
            var name = Name("attribute name");
            var equals = Expect('=');
            White();
            LuiValueSyntax value;
            if (Current == '"')
            {
                var quote = position++;
                var end = text.IndexOf('"', position);
                if (end < 0)
                {
                    Error(
                        "LUI1010",
                        "Unterminated attribute string.",
                        new LuiSpan(quote, text.Length - quote)
                    );
                    end = text.Length;
                    position = end;
                }
                else
                    position = end + 1;
                value = new LuiScalarSyntax(
                    new LuiSpan(quote, position - quote),
                    text.Substring(
                        quote + 1,
                        Math.Max(0, position - quote - (end == text.Length ? 1 : 2))
                    ),
                    Token("\"", quote, 1),
                    end == text.Length ? Missing("\"") : Token("\"", end, 1)
                );
            }
            else if (Current == '{')
                value = BracedValue(name.Text == "style" || name.Text == "Style");
            else
            {
                Error(
                    "LUI1011",
                    "Expected quoted scalar or expression attribute value.",
                    new LuiSpan(position, 0)
                );
                value = new LuiScalarSyntax(
                    new LuiSpan(position, 0),
                    "",
                    Missing("\""),
                    Missing("\"")
                );
            }
            return new LuiAttributeSyntax(LuiSpan.From(start, position), name, equals, value);
        }

        private LuiValueSyntax BracedValue(bool styleContext)
        {
            var start = position;
            var open = Token("{", position++, 1);
            var contentStart = position;
            var end = IslandScanner.End(text, position, '}');
            position = end;
            var content = text.Substring(contentStart, end - contentStart);
            var span = LuiSpan.From(start, position);
            var close = Missing("}");
            if (Current == '}')
            {
                close = Token("}", position++, 1);
                span = LuiSpan.From(start, position);
            }
            else
                Error(
                    "LUI1013",
                    "Unterminated expression island.",
                    new LuiSpan(start, text.Length - start)
                );
            LuiStyleWithSyntax? style;
            if (styleContext && TryInlineStyle(span, open, close, contentStart, content, out style))
                return style!;
            return Expression(new LuiSpan(contentStart, content.Length), content, open, close);
        }

        private bool TryInlineStyle(
            LuiSpan span,
            LuiToken outerOpen,
            LuiToken outerClose,
            int contentStart,
            string content,
            out LuiStyleWithSyntax? style
        )
        {
            style = null;
            var tokens = SyntaxFactory
                .ParseTokens(content)
                .Where(token => !token.IsKind(SyntaxKind.EndOfFileToken))
                .ToArray();
            var with = Array.FindIndex(tokens, token => token.ValueText == "with");
            if (with <= 0 || with + 1 >= tokens.Length)
                return false;
            var baseText = content.Substring(0, tokens[with].SpanStart).Trim();
            if (!ValidName(baseText))
                return false;
            if (!tokens[with + 1].IsKind(SyntaxKind.OpenBraceToken))
            {
                var tailText = content.Substring(tokens[with + 1].SpanStart).Trim();
                if (with + 2 != tokens.Length || !ValidName(tailText))
                    return false;
                var tailStart = content.IndexOf(
                    tailText,
                    tokens[with + 1].SpanStart,
                    StringComparison.Ordinal
                );
                var tailBaseStart = content.IndexOf(baseText, StringComparison.Ordinal);
                style = new LuiStyleWithSyntax(
                    span,
                    outerOpen,
                    new LuiToken(
                        baseText,
                        new LuiSpan(contentStart + tailBaseStart, baseText.Length),
                        false
                    ),
                    new LuiToken(
                        "with",
                        new LuiSpan(
                            contentStart + tokens[with].SpanStart,
                            tokens[with].Span.Length
                        ),
                        false
                    ),
                    new LuiToken("{", new LuiSpan(contentStart + tokens[with].Span.End, 0), true),
                    [],
                    new LuiToken(
                        "}",
                        new LuiSpan(contentStart + tailStart + tailText.Length, 0),
                        true
                    ),
                    outerClose,
                    new LuiToken(
                        tailText,
                        new LuiSpan(contentStart + tailStart, tailText.Length),
                        false
                    )
                );
                return true;
            }
            if (!tokens[tokens.Length - 1].IsKind(SyntaxKind.CloseBraceToken))
                return false;
            var members = new List<LuiStyleMemberSyntax>();
            var p = tokens[with + 1].Span.End;
            var close = tokens[tokens.Length - 1].SpanStart;
            while (p < close)
            {
                Skip(content, ref p);
                if (p >= close)
                    break;
                if (content.AsSpan(p).StartsWith("when ".AsSpan(), StringComparison.Ordinal))
                {
                    var groupStart = p;
                    p += 4;
                    Skip(content, ref p);
                    var variantStart = p;
                    while (p < close && content[p] != '{')
                        p++;
                    var variantText = content.Substring(variantStart, p - variantStart).Trim();
                    var variantLeading = content
                        .Substring(variantStart, p - variantStart)
                        .IndexOf(variantText, StringComparison.Ordinal);
                    if (variantText.Length == 0 || p >= close || content[p++] != '{')
                        return false;
                    var variant = new LuiToken(
                        variantText,
                        new LuiSpan(
                            contentStart + variantStart + variantLeading,
                            variantText.Length
                        ),
                        false
                    );
                    var groupOpen = new LuiToken("{", new LuiSpan(contentStart + p - 1, 1), false);
                    var assignments = new List<LuiStyleAssignmentSyntax>();
                    while (p < close)
                    {
                        Skip(content, ref p);
                        if (p < close && content[p] == '}')
                            break;
                        if (
                            !InlineAssignment(
                                content,
                                contentStart,
                                close,
                                ref p,
                                out var assignment
                            )
                        )
                            return false;
                        assignments.Add(assignment);
                    }
                    if (p >= close || content[p++] != '}')
                        return false;
                    members.Add(
                        new LuiVariantGroupSyntax(
                            new LuiSpan(contentStart + groupStart, p - groupStart),
                            new LuiToken("when", new LuiSpan(contentStart + groupStart, 4), false),
                            variant,
                            groupOpen,
                            assignments,
                            new LuiToken("}", new LuiSpan(contentStart + p - 1, 1), false)
                        )
                    );
                    continue;
                }
                if (!InlineAssignment(content, contentStart, close, ref p, out var normal))
                    return false;
                members.Add(normal);
            }
            if (members.Count == 0)
                return false;
            var baseStart = content.IndexOf(baseText, StringComparison.Ordinal);
            style = new LuiStyleWithSyntax(
                span,
                outerOpen,
                new LuiToken(
                    baseText,
                    new LuiSpan(contentStart + baseStart, baseText.Length),
                    false
                ),
                new LuiToken(
                    "with",
                    new LuiSpan(contentStart + tokens[with].SpanStart, tokens[with].Span.Length),
                    false
                ),
                new LuiToken("{", new LuiSpan(contentStart + tokens[with + 1].SpanStart, 1), false),
                members,
                new LuiToken(
                    "}",
                    new LuiSpan(contentStart + tokens[tokens.Length - 1].SpanStart, 1),
                    false
                ),
                outerClose
            );
            return true;
        }

        private bool InlineAssignment(
            string content,
            int contentStart,
            int close,
            ref int p,
            out LuiStyleAssignmentSyntax result
        )
        {
            var assignmentStart = p;
            if (!TryLocalName(content, ref p, contentStart, out var property))
            {
                result = null!;
                return false;
            }
            Skip(content, ref p);
            if (p >= close || content[p] != ':')
            {
                result = null!;
                return false;
            }
            p++;
            var expressionStart = p;
            var expressionEnd = IslandScanner.StyleEnd(content, p);
            if (expressionEnd > close)
                expressionEnd = close;
            p = expressionEnd;
            var terminator =
                p < close && content[p] == ';'
                    ? new LuiToken(";", new LuiSpan(contentStart + p++, 1), false)
                    : new LuiToken(";", new LuiSpan(contentStart + p, 0), true);
            if (expressionEnd == expressionStart)
            {
                result = null!;
                return false;
            }
            result = new LuiStyleAssignmentSyntax(
                new LuiSpan(
                    contentStart + assignmentStart,
                    terminator.Span.End - contentStart - assignmentStart
                ),
                property,
                new LuiToken(":", new LuiSpan(contentStart + expressionStart - 1, 1), false),
                Expression(
                    new LuiSpan(contentStart + expressionStart, expressionEnd - expressionStart),
                    content.Substring(expressionStart, expressionEnd - expressionStart)
                ),
                terminator
            );
            if (terminator.IsMissing)
                Error("LUI1015", "Expected ';' after style assignment.", terminator.Span);
            return true;
        }

        private LuiExpressionSyntax IslandExpression(params char[] stops)
        {
            var start = position;
            var end = IslandScanner.End(text, position, stops);
            position = end;
            return Expression(new LuiSpan(start, end - start), text.Substring(start, end - start));
        }

        private LuiExpressionSyntax ForEachSourceExpression()
        {
            var start = position;
            var end = IslandScanner.End(text, position, true, ')');
            position = end;
            return Expression(new LuiSpan(start, end - start), text.Substring(start, end - start));
        }

        private LuiExpressionSyntax Expression(
            LuiSpan span,
            string value,
            LuiToken? openBrace = null,
            LuiToken? closeBrace = null
        )
        {
            var leading = value.Length - value.TrimStart().Length;
            var trimmed = value.Trim();
            span = new LuiSpan(span.Start + leading, trimmed.Length);
            var expression = SyntaxFactory.ParseExpression(trimmed);
            if (
                trimmed.Length == 0
                || expression.ContainsDiagnostics
                || expression.IsMissing
                || !Allowed(expression)
            )
                Error("LUI1012", "Expression island is not an allowed C# expression.", span);
            return openBrace == null
                ? new LuiExpressionSyntax(span, trimmed, expression)
                : new LuiExpressionSyntax(span, trimmed, expression, openBrace, closeBrace!);
        }

        private static bool Allowed(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax _:
                case IdentifierNameSyntax _:
                case ThisExpressionSyntax _:
                case BaseExpressionSyntax _:
                case TypeOfExpressionSyntax _:
                case DefaultExpressionSyntax _:
                    return true;
                case ParenthesizedExpressionSyntax value:
                    return Allowed(value.Expression);
                case MemberAccessExpressionSyntax value:
                    return Allowed(value.Expression);
                case MemberBindingExpressionSyntax _:
                    return true;
                case ElementAccessExpressionSyntax value:
                    return Allowed(value.Expression)
                        && value.ArgumentList.Arguments.All(argument =>
                            Allowed(argument.Expression)
                        );
                case InvocationExpressionSyntax value:
                    return Allowed(value.Expression)
                        && value.ArgumentList.Arguments.All(argument =>
                            Allowed(argument.Expression)
                        );
                case PrefixUnaryExpressionSyntax value:
                    return Allowed(value.Operand);
                case PostfixUnaryExpressionSyntax value:
                    return Allowed(value.Operand);
                case BinaryExpressionSyntax value:
                    return Allowed(value.Left) && Allowed(value.Right);
                case ConditionalExpressionSyntax value:
                    return Allowed(value.Condition)
                        && Allowed(value.WhenTrue)
                        && Allowed(value.WhenFalse);
                case CastExpressionSyntax value:
                    return Allowed(value.Expression);
                case InterpolatedStringExpressionSyntax value:
                    return value.Contents.All(content =>
                        content is InterpolatedStringTextSyntax
                        || content is InterpolationSyntax interpolation
                            && Allowed(interpolation.Expression)
                            && (
                                interpolation.AlignmentClause == null
                                || Allowed(interpolation.AlignmentClause.Value)
                            )
                    );
                case TupleExpressionSyntax value:
                    return value.Arguments.All(argument => Allowed(argument.Expression));
                case ObjectCreationExpressionSyntax value:
                    return (
                            value.ArgumentList == null
                            || value.ArgumentList.Arguments.All(argument =>
                                Allowed(argument.Expression)
                            )
                        ) && AllowedInitializer(value.Initializer);
                case ImplicitObjectCreationExpressionSyntax value:
                    return value.ArgumentList.Arguments.All(argument =>
                            Allowed(argument.Expression)
                        ) && AllowedInitializer(value.Initializer);
                case ArrayCreationExpressionSyntax value:
                    return value.Initializer == null || AllowedInitializer(value.Initializer);
                case ImplicitArrayCreationExpressionSyntax value:
                    return AllowedInitializer(value.Initializer);
                case CollectionExpressionSyntax value:
                    return value.Elements.All(element =>
                        element is ExpressionElementSyntax expressionElement
                        && Allowed(expressionElement.Expression)
                    );
                case SimpleLambdaExpressionSyntax value:
                    return value.Body is ExpressionSyntax simpleBody && Allowed(simpleBody);
                case ParenthesizedLambdaExpressionSyntax value:
                    return value.Body is ExpressionSyntax parenthesizedBody
                        && Allowed(parenthesizedBody);
                case IsPatternExpressionSyntax value:
                    return Allowed(value.Expression) && AllowedPattern(value.Pattern);
                case WithExpressionSyntax value:
                    return Allowed(value.Expression) && AllowedInitializer(value.Initializer);
                default:
                    return false;
            }
        }

        private static bool AllowedInitializer(InitializerExpressionSyntax? initializer) =>
            initializer == null
            || initializer.Expressions.All(value =>
                value is AssignmentExpressionSyntax assignment
                    ? assignment.Left is IdentifierNameSyntax && Allowed(assignment.Right)
                    : Allowed(value)
            );

        private static bool AllowedPattern(PatternSyntax pattern)
        {
            switch (pattern)
            {
                case ConstantPatternSyntax value:
                    return Allowed(value.Expression);
                case RelationalPatternSyntax value:
                    return Allowed(value.Expression);
                case ParenthesizedPatternSyntax value:
                    return AllowedPattern(value.Pattern);
                case UnaryPatternSyntax value:
                    return AllowedPattern(value.Pattern);
                case BinaryPatternSyntax value:
                    return AllowedPattern(value.Left) && AllowedPattern(value.Right);
                case DeclarationPatternSyntax _:
                case VarPatternSyntax _:
                    return true;
                case RecursivePatternSyntax value:
                    return (
                            value.PositionalPatternClause == null
                            || value.PositionalPatternClause.Subpatterns.All(item =>
                                AllowedPattern(item.Pattern)
                            )
                        )
                        && (
                            value.PropertyPatternClause == null
                            || value.PropertyPatternClause.Subpatterns.All(item =>
                                AllowedPattern(item.Pattern)
                            )
                        );
                default:
                    return false;
            }
        }

        private (string Value, LuiToken Semicolon) Statement(string kind)
        {
            White();
            var start = position;
            while (!End && Current != ';' && Current != '\r' && Current != '\n')
                position++;
            var value = text.Substring(start, position - start).Trim();
            var semicolon = Current == ';' ? Token(";", position++, 1) : Missing(";");
            if (semicolon.IsMissing)
                Error("LUI1015", "Expected ';' after " + kind + ".", semicolon.Span);
            return (value, semicolon);
        }

        private void RecoverTo(params char[] stops)
        {
            while (!End && !stops.Contains(Current))
                position++;
            if (!End && Current == ';')
                position++;
        }

        private void AddText(List<LuiBodySyntax> result, int start, int end)
        {
            var raw = text.Substring(start, Math.Max(0, end - start));
            var leading = raw.Length - raw.TrimStart().Length;
            var value = raw.Trim();
            if (value.Length != 0)
                result.Add(new LuiTextSyntax(new LuiSpan(start + leading, value.Length), value));
        }

        private static bool ValidCompilationUnit(
            string source,
            Func<CompilationUnitSyntax, bool> shape
        )
        {
            var unit = SyntaxFactory.ParseCompilationUnit(source);
            return !unit.ContainsDiagnostics && shape(unit);
        }

        private void White()
        {
            while (!End && char.IsWhiteSpace(Current))
                position++;
        }

        private void Skip(string source, ref int p)
        {
            while (p < source.Length && char.IsWhiteSpace(source[p]))
                p++;
        }

        private bool TryLocalName(string source, ref int p, int offset, out LuiToken name)
        {
            var start = p;
            if (!TryReadName(source, p, out var length))
            {
                name = Missing("");
                return false;
            }
            p += length;
            name = new LuiToken(
                source.Substring(start, length),
                new LuiSpan(offset + start, length),
                false
            );
            return true;
        }

        private LuiToken LocalName(string source, ref int p, int offset, string description)
        {
            var start = p;
            if (TryLocalName(source, ref p, offset, out var name))
                return name;
            Error("LUI1014", "Expected " + description + ".", new LuiSpan(offset + start, 0));
            return new LuiToken("", new LuiSpan(offset + start, 0), true);
        }

        private bool End
        {
            get { return position >= text.Length; }
        }
        private char Current
        {
            get { return End ? '\0' : text[position]; }
        }

        private bool Starts(string value)
        {
            return position + value.Length <= text.Length
                && string.CompareOrdinal(text, position, value, 0, value.Length) == 0;
        }

        private bool PeekWord(string word)
        {
            var save = position;
            White();
            var result =
                Starts(word)
                && (
                    position + word.Length == text.Length
                    || !WordCharacter(text[position + word.Length])
                );
            position = save;
            return result;
        }

        private bool PeekRegion(string word)
        {
            var save = position;
            White();
            var result =
                Starts(word)
                && (
                    position + word.Length == text.Length
                    || !WordCharacter(text[position + word.Length])
                );
            if (result)
            {
                position += word.Length;
                White();
                result = Current == '(';
            }
            position = save;
            return result;
        }

        private bool Word(string word)
        {
            White();
            if (
                !Starts(word)
                || (
                    position + word.Length < text.Length
                    && WordCharacter(text[position + word.Length])
                )
            )
                return false;
            position += word.Length;
            return true;
        }

        private static bool WordCharacter(char value)
        {
            return SyntaxFacts.IsIdentifierPartCharacter(value);
        }

        private LuiToken Name(string description)
        {
            White();
            var start = position;
            if (TryReadName(text, position, out var length))
            {
                position += length;
                return new LuiToken(
                    text.Substring(start, length),
                    new LuiSpan(start, length),
                    false
                );
            }
            if (Current == '@' && description == "component name")
            {
                position++;
                return Token("@", start, 1);
            }
            Error("LUI1014", "Expected " + description + ".", new LuiSpan(start, 0));
            return Missing("");
        }

        private LuiToken Expect(char value)
        {
            White();
            var start = position;
            if (Current == value)
            {
                position++;
                return Token(value.ToString(), start, 1);
            }
            Error("LUI1015", "Expected '" + value + "'.", new LuiSpan(start, 0));
            return Missing(value.ToString());
        }

        private LuiToken ExpectWord(string value)
        {
            var start = position;
            if (Word(value))
                return Token(value, position - value.Length, value.Length);
            Error("LUI1016", "Expected '" + value + "'.", new LuiSpan(start, 0));
            return Missing(value);
        }

        private LuiToken Token(string value, int start, int length)
        {
            return new LuiToken(value, new LuiSpan(start, length), false);
        }

        private LuiToken Missing(string value)
        {
            return new LuiToken(value, new LuiSpan(position, 0), true);
        }

        private static bool ValidName(string value)
        {
            var name = SyntaxFactory.ParseName(value);
            return value.Length != 0
                && !name.ContainsDiagnostics
                && name.Span.Length == value.Length;
        }

        private bool EnterNesting()
        {
            if (nesting++ < NestingBudget)
                return true;
            nesting--;
            if (!nestingReported)
            {
                nestingReported = true;
                Error("LUI1018", "Nesting limit exceeded.", new LuiSpan(position, 0));
            }
            return false;
        }

        private void ExitNesting()
        {
            nesting--;
        }

        private LuiToken SkipBalancedBrace()
        {
            var start = position;
            var depth = 1;
            foreach (var token in SyntaxFactory.ParseTokens(text.Substring(position)))
            {
                if (token.IsKind(SyntaxKind.OpenBraceToken))
                    depth++;
                else if (token.IsKind(SyntaxKind.CloseBraceToken) && --depth == 0)
                {
                    position += token.Span.End;
                    return Token("}", position - 1, 1);
                }
            }
            position = text.Length;
            return new LuiToken("}", new LuiSpan(start, 0), true);
        }

        private void SkipElementContent()
        {
            var depth = 1;
            while (!End && depth != 0)
            {
                var next = text.IndexOf('<', position);
                if (next < 0)
                {
                    position = text.Length;
                    return;
                }
                position = next;
                var close = Starts("</");
                var end = text.IndexOf('>', position);
                if (end < 0)
                {
                    position = text.Length;
                    return;
                }
                if (close)
                    depth--;
                else if (end == position || text[end - 1] != '/')
                    depth++;
                position = end + 1;
            }
        }

        private static bool TryReadName(string source, int start, out int length)
        {
            var end = 0;
            var identifier = true;
            foreach (var token in SyntaxFactory.ParseTokens(source.Substring(start)))
            {
                if (token.IsKind(SyntaxKind.EndOfFileToken))
                    break;
                if (identifier)
                {
                    if (!token.IsKind(SyntaxKind.IdentifierToken))
                    {
                        length = 0;
                        return false;
                    }
                    end = token.Span.End;
                    identifier = false;
                }
                else if (
                    token.IsKind(SyntaxKind.DotToken) || token.IsKind(SyntaxKind.ColonColonToken)
                )
                {
                    end = token.Span.End;
                    identifier = true;
                }
                else
                    break;
            }
            var value = source.Substring(start, end);
            length = !identifier && ValidName(value) ? end : 0;
            return length != 0;
        }

        private void Error(string id, string message, LuiSpan span)
        {
            diagnostics.Add((new LuiDiagnostic(id, message, span), diagnostics.Count));
        }

        private bool TopLevelStart() =>
            PeekWord("style")
            || PeekWord("public")
            || PeekWord("internal")
            || PeekWord("component");
    }

    private static class IslandScanner
    {
        private static readonly char[] NewlineCharacters = new[] { '\r', '\n' };

        internal static int StyleEnd(string source, int start)
        {
            var tokens = SyntaxFactory
                .ParseTokens(source.Substring(start))
                .Where(token => !token.IsKind(SyntaxKind.EndOfFileToken))
                .ToArray();
            var parens = 0;
            var brackets = 0;
            var braces = 0;
            var conditionals = 0;
            var previousEnd = 0;
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                var kind = token.Kind();
                var top = parens == 0 && brackets == 0 && braces == 0;
                if (
                    top && (kind == SyntaxKind.SemicolonToken || kind == SyntaxKind.CloseBraceToken)
                )
                    return start + token.SpanStart;
                var newline =
                    i != 0
                    && source
                        .Substring(start + previousEnd, token.SpanStart - previousEnd)
                        .IndexOfAny(NewlineCharacters) >= 0;
                if (top && kind == SyntaxKind.QuestionToken)
                    conditionals++;
                if (top && kind == SyntaxKind.ColonToken && conditionals != 0)
                    conditionals--;
                var nextIsColon =
                    i + 1 < tokens.Length && tokens[i + 1].IsKind(SyntaxKind.ColonToken);
                if (
                    top
                    && conditionals == 0
                    && nextIsColon
                    && token.IsKind(SyntaxKind.IdentifierToken)
                    && (newline || token.SpanStart > previousEnd)
                )
                    return start + token.SpanStart;
                if (top && newline && token.ValueText == "when")
                    return start + token.SpanStart;
                if (kind == SyntaxKind.OpenParenToken)
                    parens++;
                else if (kind == SyntaxKind.CloseParenToken && parens > 0)
                    parens--;
                else if (kind == SyntaxKind.OpenBracketToken)
                    brackets++;
                else if (kind == SyntaxKind.CloseBracketToken && brackets > 0)
                    brackets--;
                else if (kind == SyntaxKind.OpenBraceToken)
                    braces++;
                else if (kind == SyntaxKind.CloseBraceToken && braces > 0)
                    braces--;
                previousEnd = token.Span.End;
            }
            return source.Length;
        }

        internal static int End(string source, int start, params char[] stops) =>
            End(source, start, false, stops);

        internal static int End(string source, int start, bool stopAtKeyed, params char[] stops)
        {
            var parens = 0;
            var brackets = 0;
            var braces = 0;
            var tokens = SyntaxFactory.ParseTokens(source.Substring(start)).ToArray();
            for (var index = 0; index < tokens.Length; index++)
            {
                var token = tokens[index];
                if (token.IsKind(SyntaxKind.EndOfFileToken))
                    return source.Length;
                var kind = token.Kind();
                var atTop = parens == 0 && brackets == 0 && braces == 0;
                if (
                    stopAtKeyed
                    && atTop
                    && token.ValueText == "keyed"
                    && index + 1 < tokens.Length
                    && tokens[index + 1].ValueText == "by"
                )
                    return start + token.SpanStart;
                if (
                    atTop
                    && (
                        (kind == SyntaxKind.CloseParenToken && stops.Contains(')'))
                        || (kind == SyntaxKind.CloseBraceToken && stops.Contains('}'))
                        || (kind == SyntaxKind.OpenBraceToken && stops.Contains('{'))
                        || (kind == SyntaxKind.LessThanToken && stops.Contains('<'))
                        || (kind == SyntaxKind.SemicolonToken && stops.Contains(';'))
                    )
                )
                    return start + token.SpanStart;
                if (kind == SyntaxKind.OpenParenToken)
                    parens++;
                else if (kind == SyntaxKind.CloseParenToken && parens > 0)
                    parens--;
                else if (kind == SyntaxKind.OpenBracketToken)
                    brackets++;
                else if (kind == SyntaxKind.CloseBracketToken && brackets > 0)
                    brackets--;
                else if (kind == SyntaxKind.OpenBraceToken)
                    braces++;
                else if (kind == SyntaxKind.CloseBraceToken && braces > 0)
                    braces--;
            }
            return source.Length;
        }
    }
}
