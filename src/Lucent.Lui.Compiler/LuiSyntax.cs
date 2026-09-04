using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler;

/// <summary>Half-open character range in the parsed <c>.lui</c> source.</summary>
/// <remarks>
/// Source spans identify authored text; generated C# spans use the same coordinate convention but
/// belong to the generated source text. Syntax objects are immutable snapshots.
/// </remarks>
public readonly struct LuiSpan
{
    /// <summary>Creates a range starting at <paramref name="start"/> with <paramref name="length"/> characters.</summary>
    public LuiSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>Zero-based offset of the first character.</summary>
    public int Start { get; }

    /// <summary>Number of characters in the range.</summary>
    public int Length { get; }

    /// <summary>Exclusive offset immediately after the range.</summary>
    public int End
    {
        get { return Start + Length; }
    }

    /// <summary>Creates a range from an inclusive start to an exclusive end, clamping an inverted range to empty.</summary>
    public static LuiSpan From(int start, int end)
    {
        return new LuiSpan(start, Math.Max(0, end - start));
    }
}

/// <summary>Immutable lexical text and its authored source range.</summary>
/// <remarks>A recovery token has <see cref="IsMissing"/> set and an empty anchor span; its <see cref="Text"/> is the expected spelling, not source text.</remarks>
public sealed class LuiToken
{
    /// <summary>Creates a present token or a parser recovery token.</summary>
    public LuiToken(string text, LuiSpan span, bool isMissing)
    {
        Text = text;
        Span = span;
        IsMissing = isMissing;
    }

    /// <summary>Authored token text, or the expected spelling for a missing token.</summary>
    public string Text { get; }

    /// <summary>Source range occupied by authored text or the zero-length recovery anchor.</summary>
    public LuiSpan Span { get; }

    /// <summary>Whether parsing inserted this token to preserve a traversable recovered tree.</summary>
    public bool IsMissing { get; }
}

/// <summary>Parser or lowering failure associated with an authored source range.</summary>
public sealed class LuiDiagnostic
{
    /// <summary>Creates an immutable diagnostic for editor or build-tool reporting.</summary>
    public LuiDiagnostic(
        string id,
        string message,
        LuiSpan span,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        string source = "Lucent.Lui"
    )
    {
        Id = id;
        Message = message;
        Span = span;
        Severity = severity;
        Source = source;
    }

    /// <summary>Stable diagnostic identifier such as <c>LUI1000</c>.</summary>
    public string Id { get; }

    /// <summary>Human-readable explanation suitable for a diagnostic UI.</summary>
    public string Message { get; }

    /// <summary>Authored range to highlight; it never identifies generated C#.</summary>
    public LuiSpan Span { get; }

    /// <summary>Default Roslyn severity before evaluated project configuration.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Diagnostic source/category retained by editor transport.</summary>
    public string Source { get; }
}

/// <summary>Base for immutable parsed nodes, including recovered nodes with missing child tokens.</summary>
public abstract class LuiSyntaxNode
{
    /// <summary>Initializes a node with its complete authored range.</summary>
    protected LuiSyntaxNode(LuiSpan span)
    {
        Span = span;
    }

    /// <summary>Authored range covering this node, including any present delimiters.</summary>
    public LuiSpan Span { get; }
}

/// <summary>Immutable parse result for one <c>.lui</c> document.</summary>
public sealed class LuiDocumentSyntax : LuiSyntaxNode
{
    /// <summary>Creates a document snapshot from parser-owned immutable values.</summary>
    public LuiDocumentSyntax(
        LuiSpan span,
        string source,
        string? ns,
        IReadOnlyList<string> usings,
        LuiComponentSyntax? component,
        IReadOnlyList<LuiStyleSyntax> styles,
        IReadOnlyList<LuiTopLevelCommentSyntax> comments,
        IReadOnlyList<LuiSyntaxNode> topLevel,
        IReadOnlyList<LuiDiagnostic> diagnostics
    )
        : base(span)
    {
        Source = source;
        Namespace = ns;
        Usings = usings;
        Component = component;
        Styles = styles;
        Comments = comments;
        TopLevel = topLevel;
        Diagnostics = diagnostics;
    }

    /// <summary>Exact source text from which all node spans are measured.</summary>
    public string Source { get; }

    /// <summary>File-scoped namespace text, when present and valid.</summary>
    public string? Namespace { get; }

    /// <summary>Using-directive text in source order.</summary>
    public IReadOnlyList<string> Usings { get; }

    /// <summary>The sole component declaration, or <see langword="null"/> when recovery could not find one.</summary>
    public LuiComponentSyntax? Component { get; }

    /// <summary>Top-level style declarations in source order.</summary>
    public IReadOnlyList<LuiStyleSyntax> Styles { get; }

    /// <summary>Top-level comments in source order.</summary>
    public IReadOnlyList<LuiTopLevelCommentSyntax> Comments { get; }

    /// <summary>All recognized top-level nodes in source order, including comments.</summary>
    public IReadOnlyList<LuiSyntaxNode> TopLevel { get; }

    /// <summary>Ordered parse diagnostics; consumers may still inspect the recovered tree.</summary>
    public IReadOnlyList<LuiDiagnostic> Diagnostics { get; }
}

/// <summary>File-scoped namespace directive.</summary>
public sealed class LuiNamespaceSyntax : LuiSyntaxNode
{
    /// <summary>Creates a namespace directive with its keyword, name text, and terminator.</summary>
    public LuiNamespaceSyntax(LuiSpan span, LuiToken keyword, string value, LuiToken semicolon)
        : base(span)
    {
        Keyword = keyword;
        Value = value;
        Semicolon = semicolon;
    }

    /// <summary><c>namespace</c> keyword token.</summary>
    public LuiToken Keyword { get; }

    /// <summary>Namespace text between the keyword and semicolon.</summary>
    public string Value { get; }

    /// <summary>Directive terminator, possibly a missing recovery token.</summary>
    public LuiToken Semicolon { get; }
}

/// <summary>Using directive retained for generated C#.</summary>
public sealed class LuiUsingSyntax : LuiSyntaxNode
{
    /// <summary>Creates a using directive with its keyword, target text, and terminator.</summary>
    public LuiUsingSyntax(LuiSpan span, LuiToken keyword, string value, LuiToken semicolon)
        : base(span)
    {
        Keyword = keyword;
        Value = value;
        Semicolon = semicolon;
    }

    /// <summary><c>using</c> keyword token.</summary>
    public LuiToken Keyword { get; }

    /// <summary>Using target text between the keyword and semicolon.</summary>
    public string Value { get; }

    /// <summary>Directive terminator, possibly a missing recovery token.</summary>
    public LuiToken Semicolon { get; }
}

/// <summary>Component declaration and the body lowered to a Lucent component recipe.</summary>
public sealed class LuiComponentSyntax : LuiSyntaxNode
{
    /// <summary>Creates an immutable component declaration from parser tokens and children.</summary>
    public LuiComponentSyntax(
        LuiSpan span,
        LuiToken accessibility,
        LuiToken componentKeyword,
        LuiToken name,
        IReadOnlyList<LuiParameterSyntax> parameters,
        LuiToken openParameters,
        LuiToken closeParameters,
        LuiToken openBrace,
        IReadOnlyList<LuiBodySyntax> body,
        LuiToken closeBrace
    )
        : base(span)
    {
        Accessibility = accessibility;
        ComponentKeyword = componentKeyword;
        Name = name;
        Parameters = parameters;
        OpenParameters = openParameters;
        CloseParameters = closeParameters;
        OpenBrace = openBrace;
        Body = body;
        CloseBrace = closeBrace;
    }

    /// <summary><c>public</c> or <c>internal</c> token; omitted accessibility is recovered as missing <c>internal</c>.</summary>
    public LuiToken Accessibility { get; }

    /// <summary><c>component</c> keyword token.</summary>
    public LuiToken ComponentKeyword { get; }

    /// <summary>Declared component identifier token.</summary>
    public LuiToken Name { get; }

    /// <summary>Declared parameters in source order.</summary>
    public IReadOnlyList<LuiParameterSyntax> Parameters { get; }

    /// <summary>Opening parenthesis before parameters.</summary>
    public LuiToken OpenParameters { get; }

    /// <summary>Closing parenthesis after parameters, possibly recovered.</summary>
    public LuiToken CloseParameters { get; }

    /// <summary>Opening body brace.</summary>
    public LuiToken OpenBrace { get; }

    /// <summary>Body constructs in source order.</summary>
    public IReadOnlyList<LuiBodySyntax> Body { get; }

    /// <summary>Closing body brace, possibly recovered.</summary>
    public LuiToken CloseBrace { get; }
}

/// <summary>Component parameter carried through to the generated C# declaration.</summary>
public sealed class LuiParameterSyntax : LuiSyntaxNode
{
    /// <summary>Creates a parameter with its C# declaration text, name token, and following separator.</summary>
    public LuiParameterSyntax(
        LuiSpan span,
        string typeText,
        string declarationText,
        LuiToken name,
        LuiToken separator
    )
        : base(span)
    {
        TypeText = typeText;
        DeclarationText = declarationText;
        Name = name;
        Separator = separator;
    }

    /// <summary>Exact C# type text.</summary>
    public string TypeText { get; }

    /// <summary>Exact C# parameter declaration text.</summary>
    public string DeclarationText { get; }

    /// <summary>Parameter identifier token.</summary>
    public LuiToken Name { get; }

    /// <summary>Following comma, or a missing token after the final parameter.</summary>
    public LuiToken Separator { get; }
}

/// <summary>Base for constructs allowed inside a component or element body.</summary>
public abstract class LuiBodySyntax : LuiSyntaxNode
{
    /// <summary>Initializes a body construct with its authored range.</summary>
    protected LuiBodySyntax(LuiSpan span)
        : base(span) { }
}

/// <summary>Literal text child preserved verbatim from the authored body.</summary>
public sealed class LuiTextSyntax : LuiBodySyntax
{
    /// <summary>Creates a text child from its source range and exact text.</summary>
    public LuiTextSyntax(LuiSpan span, string text)
        : base(span)
    {
        Text = text;
    }

    /// <summary>Exact literal text.</summary>
    public string Text { get; }
}

/// <summary>Comment found inside a component or element body.</summary>
public sealed class LuiCommentSyntax : LuiBodySyntax
{
    /// <summary>Creates a body comment from its source range and exact text.</summary>
    public LuiCommentSyntax(LuiSpan span, string text)
        : base(span)
    {
        Text = text;
    }

    /// <summary>Exact comment text, including comment delimiters.</summary>
    public string Text { get; }
}

/// <summary>Roslyn-parsed expression island used as a scalar body child.</summary>
public sealed class LuiExpressionBodySyntax : LuiBodySyntax
{
    /// <summary>Creates a body expression with its exact text and brace tokens.</summary>
    public LuiExpressionBodySyntax(
        LuiSpan span,
        string text,
        ExpressionSyntax expression,
        LuiToken openBrace,
        LuiToken closeBrace
    )
        : base(span)
    {
        Text = text;
        Expression = expression;
        OpenBrace = openBrace;
        CloseBrace = closeBrace;
    }

    /// <summary>Exact C# expression text inside the braces.</summary>
    public string Text { get; }

    /// <summary>Roslyn expression tree for semantic binding; its positions are relative to <see cref="Text"/>.</summary>
    public ExpressionSyntax Expression { get; }

    /// <summary>Opening expression-island brace.</summary>
    public LuiToken OpenBrace { get; }

    /// <summary>Closing expression-island brace, possibly a missing recovery token.</summary>
    public LuiToken CloseBrace { get; }
}

/// <summary>Comment found between top-level declarations.</summary>
public sealed class LuiTopLevelCommentSyntax : LuiSyntaxNode
{
    /// <summary>Creates a top-level comment from its source range and exact text.</summary>
    public LuiTopLevelCommentSyntax(LuiSpan span, string text)
        : base(span)
    {
        Text = text;
    }

    /// <summary>Exact comment text, including comment delimiters.</summary>
    public string Text { get; }
}

/// <summary>Markup element with attributes, children, and optional closing-element tokens.</summary>
public sealed class LuiElementSyntax : LuiBodySyntax
{
    /// <summary>Creates an immutable element, preserving all delimiters for formatting and recovery.</summary>
    public LuiElementSyntax(
        LuiSpan span,
        LuiToken openAngle,
        LuiToken name,
        IReadOnlyList<LuiAttributeSyntax> attributes,
        LuiToken openCloseAngle,
        LuiToken selfClosingSlash,
        IReadOnlyList<LuiBodySyntax> children,
        LuiToken closeOpenAngle,
        LuiToken closeName,
        LuiToken closeAngle,
        bool selfClosing
    )
        : base(span)
    {
        OpenAngle = openAngle;
        Name = name;
        Attributes = attributes;
        OpenCloseAngle = openCloseAngle;
        SelfClosingSlash = selfClosingSlash;
        Children = children;
        CloseOpenAngle = closeOpenAngle;
        CloseName = closeName;
        CloseAngle = closeAngle;
        SelfClosing = selfClosing;
    }

    /// <summary>Opening <c>&lt;</c> token.</summary>
    public LuiToken OpenAngle { get; }

    /// <summary>Opening element-name token.</summary>
    public LuiToken Name { get; }

    /// <summary>Attributes in source order.</summary>
    public IReadOnlyList<LuiAttributeSyntax> Attributes { get; }

    /// <summary>Closing <c>&gt;</c> token of the opening tag.</summary>
    public LuiToken OpenCloseAngle { get; }

    /// <summary>Self-closing <c>/</c> token, or a missing token for paired elements.</summary>
    public LuiToken SelfClosingSlash { get; }

    /// <summary>Nested body constructs; empty for self-closing elements.</summary>
    public IReadOnlyList<LuiBodySyntax> Children { get; }

    /// <summary>Opening <c>&lt;/</c> token of a paired closing tag, or a missing recovery token.</summary>
    public LuiToken CloseOpenAngle { get; }

    /// <summary>Closing element-name token, or a missing recovery token.</summary>
    public LuiToken CloseName { get; }

    /// <summary>Closing <c>&gt;</c> token of a paired closing tag, or a missing recovery token.</summary>
    public LuiToken CloseAngle { get; }

    /// <summary>Whether this element ends with <c>/&gt;</c> instead of a child body and closing tag.</summary>
    public bool SelfClosing { get; }
}

/// <summary>Named element attribute and its scalar, expression, or style value.</summary>
public sealed class LuiAttributeSyntax : LuiSyntaxNode
{
    /// <summary>Creates an attribute with its name, assignment delimiter, and value node.</summary>
    public LuiAttributeSyntax(
        LuiSpan span,
        LuiToken name,
        LuiToken equalsToken,
        LuiValueSyntax value
    )
        : base(span)
    {
        Name = name;
        EqualsToken = equalsToken;
        Value = value;
    }

    /// <summary>Attribute-name token.</summary>
    public LuiToken Name { get; }

    /// <summary>Assignment <c>=</c> token, possibly recovered.</summary>
    public LuiToken EqualsToken { get; }

    /// <summary>Parsed attribute value.</summary>
    public LuiValueSyntax Value { get; }
}

/// <summary>Base for values assigned to element attributes.</summary>
public abstract class LuiValueSyntax : LuiSyntaxNode
{
    /// <summary>Initializes an attribute value with its authored range.</summary>
    protected LuiValueSyntax(LuiSpan span)
        : base(span) { }
}

/// <summary>Quoted scalar attribute value.</summary>
public sealed class LuiScalarSyntax : LuiValueSyntax
{
    /// <summary>Creates a scalar with unquoted value text and quote tokens.</summary>
    public LuiScalarSyntax(LuiSpan span, string value, LuiToken openQuote, LuiToken closeQuote)
        : base(span)
    {
        Value = value;
        OpenQuote = openQuote;
        CloseQuote = closeQuote;
    }

    /// <summary>Text between the quotes.</summary>
    public string Value { get; }

    /// <summary>Opening quote token.</summary>
    public LuiToken OpenQuote { get; }

    /// <summary>Closing quote token, possibly recovered.</summary>
    public LuiToken CloseQuote { get; }
}

/// <summary>Roslyn-parsed C# expression island delimited by braces.</summary>
public sealed class LuiExpressionSyntax : LuiValueSyntax
{
    /// <summary>Creates an expression island with missing brace recovery tokens for callers that supply only expression text.</summary>
    public LuiExpressionSyntax(LuiSpan span, string text, ExpressionSyntax expression)
        : this(
            span,
            text,
            expression,
            new LuiToken("{", new LuiSpan(span.Start, 0), true),
            new LuiToken("}", new LuiSpan(span.End, 0), true)
        ) { }

    /// <summary>Creates an expression island with its exact text, Roslyn expression, and brace tokens.</summary>
    public LuiExpressionSyntax(
        LuiSpan span,
        string text,
        ExpressionSyntax expression,
        LuiToken openBrace,
        LuiToken closeBrace
    )
        : base(span)
    {
        Text = text;
        Expression = expression;
        OpenBrace = openBrace;
        CloseBrace = closeBrace;
    }

    /// <summary>Exact C# expression text inside the braces.</summary>
    public string Text { get; }

    /// <summary>Roslyn expression tree for semantic binding; its positions are relative to <see cref="Text"/>.</summary>
    public ExpressionSyntax Expression { get; }

    /// <summary>Opening expression-island brace.</summary>
    public LuiToken OpenBrace { get; }

    /// <summary>Closing expression-island brace, possibly recovered.</summary>
    public LuiToken CloseBrace { get; }
}

/// <summary>Style value that applies assignments or an expression tail to a named style.</summary>
public sealed class LuiStyleWithSyntax : LuiValueSyntax
{
    /// <summary>Creates a style-with value and its flattened assignment view.</summary>
    public LuiStyleWithSyntax(
        LuiSpan span,
        LuiToken outerOpenBrace,
        LuiToken name,
        LuiToken withKeyword,
        LuiToken openBrace,
        IReadOnlyList<LuiStyleMemberSyntax> members,
        LuiToken closeBrace,
        LuiToken outerCloseBrace,
        LuiToken? tail = null
    )
        : base(span)
    {
        OuterOpenBrace = outerOpenBrace;
        Name = name;
        WithKeyword = withKeyword;
        OpenBrace = openBrace;
        Members = members;
        Assignments = members
            .SelectMany(member =>
                member is LuiStyleAssignmentSyntax assignment
                    ? new[] { assignment }
                    : ((LuiVariantGroupSyntax)member).Assignments
            )
            .ToArray();
        CloseBrace = closeBrace;
        OuterCloseBrace = outerCloseBrace;
        Tail = tail;
    }

    /// <summary>Opening brace for the enclosing attribute value.</summary>
    public LuiToken OuterOpenBrace { get; }

    /// <summary>Referenced style-name token.</summary>
    public LuiToken Name { get; }

    /// <summary><c>with</c> keyword token.</summary>
    public LuiToken WithKeyword { get; }

    /// <summary>Opening brace for the assignment block, possibly missing when <see cref="Tail"/> is used.</summary>
    public LuiToken OpenBrace { get; }

    /// <summary>Style assignments and variant groups in source order.</summary>
    public IReadOnlyList<LuiStyleMemberSyntax> Members { get; }

    /// <summary>Immutable flattened assignments from <see cref="Members"/>, including variant-group assignments.</summary>
    public IReadOnlyList<LuiStyleAssignmentSyntax> Assignments { get; }

    /// <summary>Closing brace for the assignment block, possibly recovered.</summary>
    public LuiToken CloseBrace { get; }

    /// <summary>Closing brace for the enclosing attribute value, possibly recovered.</summary>
    public LuiToken OuterCloseBrace { get; }

    /// <summary>Expression tail after <c>with</c>, when the value is not an assignment block.</summary>
    public LuiToken? Tail { get; }
}

/// <summary>Conditional body with a C# condition and optional <c>else</c> body.</summary>
public sealed class LuiIfSyntax : LuiBodySyntax
{
    /// <summary>Creates a conditional while retaining every header and body delimiter for recovery-aware tooling.</summary>
    public LuiIfSyntax(
        LuiSpan span,
        LuiToken ifKeyword,
        LuiToken openCondition,
        LuiExpressionSyntax condition,
        LuiToken closeCondition,
        LuiToken openBrace,
        IReadOnlyList<LuiBodySyntax> thenBody,
        LuiToken closeBrace,
        LuiToken elseKeyword,
        LuiToken elseOpenBrace,
        IReadOnlyList<LuiBodySyntax> elseBody,
        LuiToken elseCloseBrace
    )
        : base(span)
    {
        IfKeyword = ifKeyword;
        OpenCondition = openCondition;
        Condition = condition;
        CloseCondition = closeCondition;
        OpenBrace = openBrace;
        ThenBody = thenBody;
        CloseBrace = closeBrace;
        ElseKeyword = elseKeyword;
        ElseOpenBrace = elseOpenBrace;
        ElseBody = elseBody;
        ElseCloseBrace = elseCloseBrace;
    }

    /// <summary><c>if</c> keyword token.</summary>
    public LuiToken IfKeyword { get; }

    /// <summary>Opening parenthesis before <see cref="Condition"/>.</summary>
    public LuiToken OpenCondition { get; }

    /// <summary>C# condition expression.</summary>
    public LuiExpressionSyntax Condition { get; }

    /// <summary>Closing condition parenthesis, possibly recovered.</summary>
    public LuiToken CloseCondition { get; }

    /// <summary>Opening brace for <see cref="ThenBody"/>.</summary>
    public LuiToken OpenBrace { get; }

    /// <summary>Body selected when <see cref="Condition"/> is true.</summary>
    public IReadOnlyList<LuiBodySyntax> ThenBody { get; }

    /// <summary>Closing brace for <see cref="ThenBody"/>, possibly recovered.</summary>
    public LuiToken CloseBrace { get; }

    /// <summary><c>else</c> keyword, or a missing token when no else body was authored.</summary>
    public LuiToken ElseKeyword { get; }

    /// <summary>Opening brace for <see cref="ElseBody"/>, or a missing token.</summary>
    public LuiToken ElseOpenBrace { get; }

    /// <summary>Body selected when <see cref="Condition"/> is false.</summary>
    public IReadOnlyList<LuiBodySyntax> ElseBody { get; }

    /// <summary>Closing brace for <see cref="ElseBody"/>, or a missing token.</summary>
    public LuiToken ElseCloseBrace { get; }
}

/// <summary>Keyed iteration body with C# source and key expressions.</summary>
public sealed class LuiForEachSyntax : LuiBodySyntax
{
    /// <summary>Creates a keyed iteration node while retaining its header and body tokens.</summary>
    public LuiForEachSyntax(
        LuiSpan span,
        LuiToken foreachKeyword,
        LuiToken openHeader,
        LuiToken varKeyword,
        LuiToken variable,
        LuiToken inKeyword,
        LuiExpressionSyntax source,
        LuiToken closeHeader,
        LuiToken keyedKeyword,
        LuiToken byKeyword,
        LuiExpressionSyntax key,
        LuiToken openBrace,
        IReadOnlyList<LuiBodySyntax> body,
        LuiToken closeBrace
    )
        : base(span)
    {
        ForeachKeyword = foreachKeyword;
        OpenHeader = openHeader;
        VarKeyword = varKeyword;
        Variable = variable;
        InKeyword = inKeyword;
        Source = source;
        CloseHeader = closeHeader;
        KeyedKeyword = keyedKeyword;
        ByKeyword = byKeyword;
        Key = key;
        OpenBrace = openBrace;
        Body = body;
        CloseBrace = closeBrace;
    }

    /// <summary><c>foreach</c> keyword token.</summary>
    public LuiToken ForeachKeyword { get; }

    /// <summary>Opening iteration-header parenthesis.</summary>
    public LuiToken OpenHeader { get; }

    /// <summary><c>var</c> keyword token.</summary>
    public LuiToken VarKeyword { get; }

    /// <summary>Iteration-variable identifier token.</summary>
    public LuiToken Variable { get; }

    /// <summary><c>in</c> keyword token.</summary>
    public LuiToken InKeyword { get; }

    /// <summary>C# expression yielding the iterated sequence.</summary>
    public LuiExpressionSyntax Source { get; }

    /// <summary>Closing iteration-header parenthesis, possibly recovered.</summary>
    public LuiToken CloseHeader { get; }

    /// <summary><c>keyed</c> keyword token.</summary>
    public LuiToken KeyedKeyword { get; }

    /// <summary><c>by</c> keyword token.</summary>
    public LuiToken ByKeyword { get; }

    /// <summary>C# expression producing the stable key for each item.</summary>
    public LuiExpressionSyntax Key { get; }

    /// <summary>Opening body brace.</summary>
    public LuiToken OpenBrace { get; }

    /// <summary>Iteration body constructs in source order.</summary>
    public IReadOnlyList<LuiBodySyntax> Body { get; }

    /// <summary>Closing body brace, possibly recovered.</summary>
    public LuiToken CloseBrace { get; }
}

/// <summary>Base for direct style assignments and conditional style groups.</summary>
public abstract class LuiStyleMemberSyntax : LuiSyntaxNode
{
    /// <summary>Initializes a style member with its authored range.</summary>
    protected LuiStyleMemberSyntax(LuiSpan span)
        : base(span) { }
}

/// <summary>Named top-level style declaration.</summary>
public sealed class LuiStyleSyntax : LuiSyntaxNode
{
    /// <summary>Creates a style declaration and its flattened assignment view.</summary>
    public LuiStyleSyntax(
        LuiSpan span,
        LuiToken styleKeyword,
        LuiToken name,
        LuiToken openBrace,
        IReadOnlyList<LuiStyleMemberSyntax> members,
        LuiToken closeBrace
    )
        : base(span)
    {
        StyleKeyword = styleKeyword;
        Name = name;
        OpenBrace = openBrace;
        Members = members;
        Assignments = members
            .SelectMany(member =>
                member is LuiStyleAssignmentSyntax assignment
                    ? new[] { assignment }
                    : ((LuiVariantGroupSyntax)member).Assignments
            )
            .ToArray();
        CloseBrace = closeBrace;
    }

    /// <summary><c>style</c> keyword token.</summary>
    public LuiToken StyleKeyword { get; }

    /// <summary>Declared style-name token.</summary>
    public LuiToken Name { get; }

    /// <summary>Opening declaration brace.</summary>
    public LuiToken OpenBrace { get; }

    /// <summary>Assignments and variant groups in source order.</summary>
    public IReadOnlyList<LuiStyleMemberSyntax> Members { get; }

    /// <summary>Immutable flattened assignments from <see cref="Members"/>, including variant groups.</summary>
    public IReadOnlyList<LuiStyleAssignmentSyntax> Assignments { get; }

    /// <summary>Closing declaration brace, possibly recovered.</summary>
    public LuiToken CloseBrace { get; }
}

/// <summary>Single style property assignment with a C# expression value.</summary>
public sealed class LuiStyleAssignmentSyntax : LuiStyleMemberSyntax
{
    /// <summary>Creates a property assignment from its property, delimiter, expression, and terminator tokens.</summary>
    public LuiStyleAssignmentSyntax(
        LuiSpan span,
        LuiToken property,
        LuiToken colon,
        LuiExpressionSyntax expression,
        LuiToken terminator
    )
        : base(span)
    {
        Property = property;
        Colon = colon;
        Expression = expression;
        Terminator = terminator;
    }

    /// <summary>Style property-name token.</summary>
    public LuiToken Property { get; }

    /// <summary>Property/value separator token.</summary>
    public LuiToken Colon { get; }

    /// <summary>C# expression supplying the property value.</summary>
    public LuiExpressionSyntax Expression { get; }

    /// <summary>Assignment semicolon, possibly recovered.</summary>
    public LuiToken Terminator { get; }
}

/// <summary>Conditional group of style assignments.</summary>
public sealed class LuiVariantGroupSyntax : LuiStyleMemberSyntax
{
    /// <summary>Creates a conditional style group and its immutable assignments.</summary>
    public LuiVariantGroupSyntax(
        LuiSpan span,
        LuiToken whenKeyword,
        LuiToken condition,
        LuiToken openBrace,
        IReadOnlyList<LuiStyleAssignmentSyntax> assignments,
        LuiToken closeBrace
    )
        : base(span)
    {
        WhenKeyword = whenKeyword;
        Condition = condition;
        OpenBrace = openBrace;
        Assignments = assignments;
        CloseBrace = closeBrace;
    }

    /// <summary><c>when</c> keyword token.</summary>
    public LuiToken WhenKeyword { get; }

    /// <summary>Condition text token evaluated by lowering.</summary>
    public LuiToken Condition { get; }

    /// <summary>Opening assignment-group brace.</summary>
    public LuiToken OpenBrace { get; }

    /// <summary>Conditional assignments in source order.</summary>
    public IReadOnlyList<LuiStyleAssignmentSyntax> Assignments { get; }

    /// <summary>Closing assignment-group brace, possibly recovered.</summary>
    public LuiToken CloseBrace { get; }
}
