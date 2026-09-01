using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler;

public readonly struct LuiSpan
{
    public LuiSpan(int start, int length) { Start = start; Length = length; }
    public int Start { get; }
    public int Length { get; }
    public int End { get { return Start + Length; } }
    public static LuiSpan From(int start, int end) { return new LuiSpan(start, Math.Max(0, end - start)); }
}

public sealed class LuiToken
{
    public LuiToken(string text, LuiSpan span, bool isMissing) { Text = text; Span = span; IsMissing = isMissing; }
    public string Text { get; }
    public LuiSpan Span { get; }
    public bool IsMissing { get; }
}

public sealed class LuiDiagnostic
{
    public LuiDiagnostic(string id, string message, LuiSpan span) { Id = id; Message = message; Span = span; }
    public string Id { get; }
    public string Message { get; }
    public LuiSpan Span { get; }
}

public abstract class LuiSyntaxNode
{
    protected LuiSyntaxNode(LuiSpan span) { Span = span; }
    public LuiSpan Span { get; }
}

public sealed class LuiDocumentSyntax : LuiSyntaxNode
{
    public LuiDocumentSyntax(LuiSpan span, string source, string? ns, IReadOnlyList<string> usings, LuiComponentSyntax? component, IReadOnlyList<LuiStyleSyntax> styles, IReadOnlyList<LuiTopLevelCommentSyntax> comments, IReadOnlyList<LuiSyntaxNode> topLevel, IReadOnlyList<LuiDiagnostic> diagnostics)
        : base(span) { Source = source; Namespace = ns; Usings = usings; Component = component; Styles = styles; Comments = comments; TopLevel = topLevel; Diagnostics = diagnostics; }
    public string Source { get; } public string? Namespace { get; } public IReadOnlyList<string> Usings { get; }
    public LuiComponentSyntax? Component { get; } public IReadOnlyList<LuiStyleSyntax> Styles { get; } public IReadOnlyList<LuiTopLevelCommentSyntax> Comments { get; } public IReadOnlyList<LuiSyntaxNode> TopLevel { get; } public IReadOnlyList<LuiDiagnostic> Diagnostics { get; }
}
public sealed class LuiNamespaceSyntax : LuiSyntaxNode { public LuiNamespaceSyntax(LuiSpan span, LuiToken keyword, string value, LuiToken semicolon) : base(span) { Keyword = keyword; Value = value; Semicolon = semicolon; } public LuiToken Keyword { get; } public string Value { get; } public LuiToken Semicolon { get; } }
public sealed class LuiUsingSyntax : LuiSyntaxNode { public LuiUsingSyntax(LuiSpan span, LuiToken keyword, string value, LuiToken semicolon) : base(span) { Keyword = keyword; Value = value; Semicolon = semicolon; } public LuiToken Keyword { get; } public string Value { get; } public LuiToken Semicolon { get; } }

public sealed class LuiComponentSyntax : LuiSyntaxNode
{
    public LuiComponentSyntax(LuiSpan span, LuiToken accessibility, LuiToken componentKeyword, LuiToken name, IReadOnlyList<LuiParameterSyntax> parameters, LuiToken openParameters, LuiToken closeParameters, LuiToken openBrace, IReadOnlyList<LuiBodySyntax> body, LuiToken closeBrace)
        : base(span) { Accessibility = accessibility; ComponentKeyword = componentKeyword; Name = name; Parameters = parameters; OpenParameters = openParameters; CloseParameters = closeParameters; OpenBrace = openBrace; Body = body; CloseBrace = closeBrace; }
    public LuiToken Accessibility { get; } public LuiToken ComponentKeyword { get; } public LuiToken Name { get; } public IReadOnlyList<LuiParameterSyntax> Parameters { get; } public LuiToken OpenParameters { get; } public LuiToken CloseParameters { get; } public LuiToken OpenBrace { get; } public IReadOnlyList<LuiBodySyntax> Body { get; } public LuiToken CloseBrace { get; }
}

public sealed class LuiParameterSyntax : LuiSyntaxNode
{
    public LuiParameterSyntax(LuiSpan span, string typeText, string declarationText, LuiToken name, LuiToken separator) : base(span) { TypeText = typeText; DeclarationText = declarationText; Name = name; Separator = separator; }
    public string TypeText { get; } public string DeclarationText { get; } public LuiToken Name { get; } public LuiToken Separator { get; }
}

public abstract class LuiBodySyntax : LuiSyntaxNode { protected LuiBodySyntax(LuiSpan span) : base(span) { } }
public sealed class LuiTextSyntax : LuiBodySyntax { public LuiTextSyntax(LuiSpan span, string text) : base(span) { Text = text; } public string Text { get; } }
public sealed class LuiCommentSyntax : LuiBodySyntax { public LuiCommentSyntax(LuiSpan span, string text) : base(span) { Text = text; } public string Text { get; } }
public sealed class LuiTopLevelCommentSyntax : LuiSyntaxNode { public LuiTopLevelCommentSyntax(LuiSpan span, string text) : base(span) { Text = text; } public string Text { get; } }
public sealed class LuiElementSyntax : LuiBodySyntax
{
    public LuiElementSyntax(LuiSpan span, LuiToken openAngle, LuiToken name, IReadOnlyList<LuiAttributeSyntax> attributes, LuiToken openCloseAngle, LuiToken selfClosingSlash, IReadOnlyList<LuiBodySyntax> children, LuiToken closeOpenAngle, LuiToken closeName, LuiToken closeAngle, bool selfClosing) : base(span) { OpenAngle = openAngle; Name = name; Attributes = attributes; OpenCloseAngle = openCloseAngle; SelfClosingSlash = selfClosingSlash; Children = children; CloseOpenAngle = closeOpenAngle; CloseName = closeName; CloseAngle = closeAngle; SelfClosing = selfClosing; }
    public LuiToken OpenAngle { get; } public LuiToken Name { get; } public IReadOnlyList<LuiAttributeSyntax> Attributes { get; } public LuiToken OpenCloseAngle { get; } public LuiToken SelfClosingSlash { get; } public IReadOnlyList<LuiBodySyntax> Children { get; } public LuiToken CloseOpenAngle { get; } public LuiToken CloseName { get; } public LuiToken CloseAngle { get; } public bool SelfClosing { get; }
}
public sealed class LuiAttributeSyntax : LuiSyntaxNode { public LuiAttributeSyntax(LuiSpan span, LuiToken name, LuiToken equalsToken, LuiValueSyntax value) : base(span) { Name = name; EqualsToken = equalsToken; Value = value; } public LuiToken Name { get; } public LuiToken EqualsToken { get; } public LuiValueSyntax Value { get; } }
public abstract class LuiValueSyntax : LuiSyntaxNode { protected LuiValueSyntax(LuiSpan span) : base(span) { } }
public sealed class LuiScalarSyntax : LuiValueSyntax { public LuiScalarSyntax(LuiSpan span, string value, LuiToken openQuote, LuiToken closeQuote) : base(span) { Value = value; OpenQuote = openQuote; CloseQuote = closeQuote; } public string Value { get; } public LuiToken OpenQuote { get; } public LuiToken CloseQuote { get; } }
public sealed class LuiExpressionSyntax : LuiValueSyntax { public LuiExpressionSyntax(LuiSpan span, string text, ExpressionSyntax expression) : this(span, text, expression, new LuiToken("{", new LuiSpan(span.Start, 0), true), new LuiToken("}", new LuiSpan(span.End, 0), true)) { } public LuiExpressionSyntax(LuiSpan span, string text, ExpressionSyntax expression, LuiToken openBrace, LuiToken closeBrace) : base(span) { Text = text; Expression = expression; OpenBrace = openBrace; CloseBrace = closeBrace; } public string Text { get; } public ExpressionSyntax Expression { get; } public LuiToken OpenBrace { get; } public LuiToken CloseBrace { get; } }
public sealed class LuiStyleWithSyntax : LuiValueSyntax { public LuiStyleWithSyntax(LuiSpan span, LuiToken outerOpenBrace, LuiToken name, LuiToken withKeyword, LuiToken openBrace, IReadOnlyList<LuiStyleMemberSyntax> members, LuiToken closeBrace, LuiToken outerCloseBrace, LuiToken? tail = null) : base(span) { OuterOpenBrace = outerOpenBrace; Name = name; WithKeyword = withKeyword; OpenBrace = openBrace; Members = members; Assignments = members.SelectMany(member => member is LuiStyleAssignmentSyntax assignment ? new[] { assignment } : ((LuiVariantGroupSyntax)member).Assignments).ToArray(); CloseBrace = closeBrace; OuterCloseBrace = outerCloseBrace; Tail = tail; } public LuiToken OuterOpenBrace { get; } public LuiToken Name { get; } public LuiToken WithKeyword { get; } public LuiToken OpenBrace { get; } public IReadOnlyList<LuiStyleMemberSyntax> Members { get; } public IReadOnlyList<LuiStyleAssignmentSyntax> Assignments { get; } public LuiToken CloseBrace { get; } public LuiToken OuterCloseBrace { get; } public LuiToken? Tail { get; } }
public sealed class LuiIfSyntax : LuiBodySyntax { public LuiIfSyntax(LuiSpan span, LuiToken ifKeyword, LuiToken openCondition, LuiExpressionSyntax condition, LuiToken closeCondition, LuiToken openBrace, IReadOnlyList<LuiBodySyntax> thenBody, LuiToken closeBrace, LuiToken elseKeyword, LuiToken elseOpenBrace, IReadOnlyList<LuiBodySyntax> elseBody, LuiToken elseCloseBrace) : base(span) { IfKeyword = ifKeyword; OpenCondition = openCondition; Condition = condition; CloseCondition = closeCondition; OpenBrace = openBrace; ThenBody = thenBody; CloseBrace = closeBrace; ElseKeyword = elseKeyword; ElseOpenBrace = elseOpenBrace; ElseBody = elseBody; ElseCloseBrace = elseCloseBrace; } public LuiToken IfKeyword { get; } public LuiToken OpenCondition { get; } public LuiExpressionSyntax Condition { get; } public LuiToken CloseCondition { get; } public LuiToken OpenBrace { get; } public IReadOnlyList<LuiBodySyntax> ThenBody { get; } public LuiToken CloseBrace { get; } public LuiToken ElseKeyword { get; } public LuiToken ElseOpenBrace { get; } public IReadOnlyList<LuiBodySyntax> ElseBody { get; } public LuiToken ElseCloseBrace { get; } }
public sealed class LuiForEachSyntax : LuiBodySyntax { public LuiForEachSyntax(LuiSpan span, LuiToken foreachKeyword, LuiToken openHeader, LuiToken varKeyword, LuiToken variable, LuiToken inKeyword, LuiExpressionSyntax source, LuiToken closeHeader, LuiToken keyedKeyword, LuiToken byKeyword, LuiExpressionSyntax key, LuiToken openBrace, IReadOnlyList<LuiBodySyntax> body, LuiToken closeBrace) : base(span) { ForeachKeyword = foreachKeyword; OpenHeader = openHeader; VarKeyword = varKeyword; Variable = variable; InKeyword = inKeyword; Source = source; CloseHeader = closeHeader; KeyedKeyword = keyedKeyword; ByKeyword = byKeyword; Key = key; OpenBrace = openBrace; Body = body; CloseBrace = closeBrace; } public LuiToken ForeachKeyword { get; } public LuiToken OpenHeader { get; } public LuiToken VarKeyword { get; } public LuiToken Variable { get; } public LuiToken InKeyword { get; } public LuiExpressionSyntax Source { get; } public LuiToken CloseHeader { get; } public LuiToken KeyedKeyword { get; } public LuiToken ByKeyword { get; } public LuiExpressionSyntax Key { get; } public LuiToken OpenBrace { get; } public IReadOnlyList<LuiBodySyntax> Body { get; } public LuiToken CloseBrace { get; } }
public abstract class LuiStyleMemberSyntax : LuiSyntaxNode { protected LuiStyleMemberSyntax(LuiSpan span) : base(span) { } }
public sealed class LuiStyleSyntax : LuiSyntaxNode { public LuiStyleSyntax(LuiSpan span, LuiToken styleKeyword, LuiToken name, LuiToken openBrace, IReadOnlyList<LuiStyleMemberSyntax> members, LuiToken closeBrace) : base(span) { StyleKeyword = styleKeyword; Name = name; OpenBrace = openBrace; Members = members; Assignments = members.SelectMany(member => member is LuiStyleAssignmentSyntax assignment ? new[] { assignment } : ((LuiVariantGroupSyntax)member).Assignments).ToArray(); CloseBrace = closeBrace; } public LuiToken StyleKeyword { get; } public LuiToken Name { get; } public LuiToken OpenBrace { get; } public IReadOnlyList<LuiStyleMemberSyntax> Members { get; } public IReadOnlyList<LuiStyleAssignmentSyntax> Assignments { get; } public LuiToken CloseBrace { get; } }
public sealed class LuiStyleAssignmentSyntax : LuiStyleMemberSyntax { public LuiStyleAssignmentSyntax(LuiSpan span, LuiToken property, LuiToken colon, LuiExpressionSyntax expression, LuiToken terminator) : base(span) { Property = property; Colon = colon; Expression = expression; Terminator = terminator; } public LuiToken Property { get; } public LuiToken Colon { get; } public LuiExpressionSyntax Expression { get; } public LuiToken Terminator { get; } }
public sealed class LuiVariantGroupSyntax : LuiStyleMemberSyntax { public LuiVariantGroupSyntax(LuiSpan span, LuiToken whenKeyword, LuiToken condition, LuiToken openBrace, IReadOnlyList<LuiStyleAssignmentSyntax> assignments, LuiToken closeBrace) : base(span) { WhenKeyword = whenKeyword; Condition = condition; OpenBrace = openBrace; Assignments = assignments; CloseBrace = closeBrace; } public LuiToken WhenKeyword { get; } public LuiToken Condition { get; } public LuiToken OpenBrace { get; } public IReadOnlyList<LuiStyleAssignmentSyntax> Assignments { get; } public LuiToken CloseBrace { get; } }
