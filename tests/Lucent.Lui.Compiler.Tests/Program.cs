using System;
using System.Linq;
using Lucent.Lui.Compiler;

const string Complete = """
namespace Sample.Ui;
using System;
public component Card(System.Collections.Generic.Dictionary<string, (int x, int y)> values, Action save) {
    <Controls.Row Name="card" Style={Panel with { Padding: Insets.All(4) }} P={recordValue with { Value = 2 }}>
        {/* comment } stays a comment */}
        if (values.Any(x => x is { x: > 0 })) { <Text Name="title"> Hello </Text> } else { }
        foreach (var item in Items.Where(x => x != "}")) keyed by item.Id { <Row Name="item" /> }
    </Controls.Row>
}
style Panel { Padding: Insets.All(2); when Hover { Opacity: .5; } }
""";

var document = LuiParser.Parse(Complete);
Assert(document.Diagnostics.Count == 0, "complete document diagnostics: " + Diagnostics(document));
var root = (LuiElementSyntax)document.Component!.Body.Single();
Assert(root.Name.Text == "Controls.Row" && root.Name.Span.Start == Complete.IndexOf("Controls.Row", StringComparison.Ordinal), "qualified element name/span changed.");
Assert(root.Attributes.Single(attribute => attribute.Name.Text == "Style").Value is LuiStyleWithSyntax, "colon inline style was not retained.");
Assert(root.Attributes.Single(attribute => attribute.Name.Text == "P").Value is LuiExpressionSyntax, "ordinary C# with expression was not retained.");
var conditional = root.Children.OfType<LuiIfSyntax>().Single();
Assert(!conditional.ElseKeyword.IsMissing && conditional.ElseBody.Count == 0, "explicit empty else was lost.");
Assert(conditional.Condition.Span.Start == Complete.IndexOf("values.Any", StringComparison.Ordinal), "expression span is not absolute.");

foreach (var source in new[]
{
    "internal component X() { <A / > }",
    "internal component X() { <Controls.Row /> }",
    "internal component X() { <A>{/* } */}</A> }",
    "internal component X() { <A P={\"escape\\} /> }",
    "internal component X() { <A P={new[] { @\"}\", \"\"\"raw } text\"\"\", $\"value {x}\", '}' }.Length} /> }",
    "internal component X() { if (M(/* } */ x => x is { Value: > 0 })) { <A /> } }"
})
{
    for (var i = 0; i != 8; i++) _ = LuiParser.Parse(source);
}

foreach (var source in new[]
{
    "internal component X() { <A P={await x} /> }",
    "internal component X() { <A P={x = 1} /> }",
    "internal component X() { <A P={var x = 1} /> }",
    "internal component X() { <A P={x => { return x; }} /> }"
}) Assert(LuiParser.Parse(source).Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1012"), "disallowed island was accepted: " + source);

var malformedHeader = LuiParser.Parse("namespace ???; using =; internal component @() { <A /> }");
Assert(malformedHeader.Diagnostics.Take(3).All(diagnostic => diagnostic.Id == "LUI1000") && malformedHeader.Diagnostics[0].Span.Start == 0, "invalid headers did not diagnose in source order: " + Diagnostics(malformedHeader));
var missing = LuiParser.Parse("internal component X() { <A> <B /> }");
var missingElement = missing.Component!.Body.OfType<LuiElementSyntax>().Single();
Assert(missingElement.CloseName.IsMissing && missingElement.CloseName.Span.Start == missingElement.Span.End && missingElement.OpenAngle.Text == "<" && missingElement.OpenCloseAngle.Text == ">" && missingElement.CloseOpenAngle.IsMissing && missingElement.CloseAngle.IsMissing && missing.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1008"), "missing closing tag token was not represented.");

var tokenDocument = LuiParser.Parse("internal component X() { <Root><A P=\"x\" /> if (ok) { <B /> } else { } foreach (var x in xs) keyed by x.Id { <C /> }</Root> } style S { P: X; }");
Assert(tokenDocument.Diagnostics.Count == 0, "token document diagnostics: " + Diagnostics(tokenDocument));
var tokenComponent = tokenDocument.Component!; var tokenRoot = tokenComponent.Body.OfType<LuiElementSyntax>().Single(); var tokenElement = tokenRoot.Children.OfType<LuiElementSyntax>().Single(); var tokenIf = tokenRoot.Children.OfType<LuiIfSyntax>().Single(); var tokenForEach = tokenRoot.Children.OfType<LuiForEachSyntax>().Single(); var tokenStyle = tokenDocument.Styles.Single();
Assert(!tokenComponent.ComponentKeyword.IsMissing && !tokenComponent.OpenParameters.IsMissing && !tokenComponent.CloseParameters.IsMissing && !tokenComponent.OpenBrace.IsMissing && !tokenComponent.CloseBrace.IsMissing && !tokenElement.SelfClosingSlash.IsMissing && !tokenElement.OpenCloseAngle.IsMissing && !tokenElement.Attributes.Single().EqualsToken.IsMissing && !tokenIf.IfKeyword.IsMissing && !tokenIf.OpenCondition.IsMissing && !tokenIf.CloseCondition.IsMissing && !tokenIf.OpenBrace.IsMissing && !tokenIf.CloseBrace.IsMissing && !tokenIf.ElseKeyword.IsMissing && !tokenIf.ElseOpenBrace.IsMissing && !tokenIf.ElseCloseBrace.IsMissing && !tokenForEach.ForeachKeyword.IsMissing && !tokenForEach.KeyedKeyword.IsMissing && !tokenForEach.ByKeyword.IsMissing && !tokenForEach.OpenBrace.IsMissing && !tokenForEach.CloseBrace.IsMissing && !tokenStyle.OpenBrace.IsMissing && !tokenStyle.CloseBrace.IsMissing && !tokenStyle.Assignments.Single().Colon.IsMissing && !tokenStyle.Assignments.Single().Terminator.IsMissing, "structural token ownership is incomplete.");

var missingOpen = LuiParser.Parse("internal component X() { if (ok) <A /> <B /> }");
Assert(missingOpen.Component!.Body.OfType<LuiIfSyntax>().Single().OpenBrace.IsMissing && missingOpen.Component.Body.OfType<LuiElementSyntax>().Count() == 2, "missing if opener consumed later siblings.");
var missingElementClose = LuiParser.Parse("internal component X() { <A><B /> } style S { P: X; }");
Assert(missingElementClose.Component!.Body.OfType<LuiElementSyntax>().Single().CloseName.IsMissing && missingElementClose.Styles.Single().Name.Text == "S", "missing element close consumed later top-level style.");
var missingStyleClose = LuiParser.Parse("style S { P: X; internal component X() { <A /> }");
Assert(missingStyleClose.Styles.Single().CloseBrace.IsMissing && missingStyleClose.Component!.Name.Text == "X", "missing style close consumed later component.");
Assert(missingStyleClose.Diagnostics.Select(diagnostic => diagnostic.Span.Start).SequenceEqual(missingStyleClose.Diagnostics.Select(diagnostic => diagnostic.Span.Start).OrderBy(start => start)), "recovery diagnostics are not in source order.");

var comments = LuiParser.Parse("// header\nnamespace Sample;\n/* between */\nusing System;\n// component\ninternal component X() { <A>  Hello   world  </A> }\n// style\nstyle S { P: X; }");
Assert(comments.Diagnostics.Count == 0 && comments.Comments.Select(comment => comment.Text).SequenceEqual(new[] { "// header", "/* between */", "// component", "// style" }) && ((LuiElementSyntax)comments.Component!.Body.Single()).Children.OfType<LuiTextSyntax>().Single().Text == "Hello   world", "top-level comments or trimmed text were not retained.");
var commentsFormatted = LuiFormatter.Format(comments.Source, LuiLineEnding.Lf);
Assert(commentsFormatted.IndexOf("// header", StringComparison.Ordinal) < commentsFormatted.IndexOf("/* between */", StringComparison.Ordinal) && commentsFormatted.IndexOf("/* between */", StringComparison.Ordinal) < commentsFormatted.IndexOf("// component", StringComparison.Ordinal) && commentsFormatted.IndexOf("// component", StringComparison.Ordinal) < commentsFormatted.IndexOf("// style", StringComparison.Ordinal), "formatter changed top-level comment order.");

var formatted = LuiFormatter.Format(Complete, LuiLineEnding.Lf);
Assert(formatted == LuiFormatter.Format(formatted, LuiLineEnding.Lf) && formatted.Contains("else {\n", StringComparison.Ordinal) && !formatted.Contains("}\n    }\n}", StringComparison.Ordinal), "formatter is not idempotent or emitted an else brace.");
Assert(LuiFormatter.Format("internal component X() { <A>", LuiLineEnding.Lf) == "internal component X() { <A>", "malformed formatting was destructive.");
var element = conditional.ThenBody.OfType<LuiElementSyntax>().Single(); var ranged = LuiFormatter.FormatRange(Complete, element.Span, LuiLineEnding.CrLf);
Assert(ranged.Substring(0, element.Span.Start) == Complete.Substring(0, element.Span.Start) && ranged.Substring(ranged.Length - (Complete.Length - element.Span.End)) == Complete.Substring(element.Span.End), "range formatting changed text outside its selected node.");
Assert(LuiFormatter.FormatRange(Complete, new LuiSpan(0, 1)) == Complete, "range formatting changed an incomplete selection.");
var commentRangeSource = "// comment\ninternal component X() { <A /> }";
var commentRange = LuiParser.Parse(commentRangeSource).Comments.Single().Span;
var commentRanged = LuiFormatter.FormatRange(commentRangeSource, commentRange, LuiLineEnding.Lf);
Assert(commentRanged == LuiFormatter.FormatRange(commentRanged, commentRange, LuiLineEnding.Lf), "comment range formatting threw or was not idempotent.");
Assert(LuiFormatter.Format("internal component X() { if (x) { <A /> } }", LuiLineEnding.Lf).Contains("if (x) {\n", StringComparison.Ordinal) && !LuiFormatter.Format("internal component X() { if (x) { <A /> } }", LuiLineEnding.Lf).Contains(" else {", StringComparison.Ordinal), "formatter invented an else.");
var styleNewlines = LuiParser.Parse("internal component X() { <A /> } style S { P: X\nQ: Y; }");
Assert(styleNewlines.Diagnostics.Count == 0 && styleNewlines.Styles.Single().Assignments.Count == 2, "newline-delimited style assignments were not retained.");
var adjacentStyles = LuiParser.Parse("internal component X() { <A /> } style S { P: X Q: Y; }");
Assert(adjacentStyles.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1015") && adjacentStyles.Styles.Single().Assignments.Count == 2 && adjacentStyles.Styles.Single().Assignments[1].Property.Text == "Q", "adjacent malformed style assignment swallowed Q.");
var textRegions = LuiParser.Parse("internal component X() { <Text>if only</Text> <Text>foreach item</Text> }");
Assert(textRegions.Component!.Body.OfType<LuiElementSyntax>().SelectMany(element => element.Children).OfType<LuiTextSyntax>().Select(value => value.Text).SequenceEqual(new[] { "if only", "foreach item" }), "plain if/foreach text was parsed as a region.");
var badRegion = LuiParser.Parse("internal component X() { if (ok { <A /> } <B /> }");
Assert(badRegion.Component!.Body.OfType<LuiElementSyntax>().Any(element => element.Name.Text == "B"), "missing region ')' consumed a later sibling.");
var recordWith = LuiParser.Parse("internal component X() { <A Style={recordValue /* ordinary */ with { Value = 2 }} /> }");
Assert(recordWith.Diagnostics.Count == 0 && ((LuiElementSyntax)recordWith.Component!.Body.Single()).Attributes.Single().Value is LuiExpressionSyntax, "ordinary record with was speculatively classified as style.");
foreach (var expression in new[] { "from x in xs select x", "x switch { _ => x }" }) Assert(LuiParser.Parse("internal component X() { <A P={" + expression + "} /> }").Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1012"), "query/switch expression was accepted.");
Assert(LuiParser.Parse("internal component X() { <A..B /> }").Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1014"), "empty qualified-name segment was accepted.");
Assert(LuiParser.Parse("internal component X() { <A\u0301 /> }").Diagnostics.Count == 0, "combining-mark identifier was rejected.");
var ordered = LuiParser.Parse("internal component X( { <A P={await x} /> }");
Assert(ordered.Diagnostics.Select(diagnostic => diagnostic.Span.Start).SequenceEqual(ordered.Diagnostics.Select(diagnostic => diagnostic.Span.Start).OrderBy(start => start)), "diagnostics were not stable-sorted.");
var deep = "internal component X() { " + string.Concat(Enumerable.Repeat("<A>", 5000)) + string.Concat(Enumerable.Repeat("</A>", 5000)) + " <B /> }";
var deepDocument = LuiParser.Parse(deep);
Assert(deepDocument.Diagnostics.Count(diagnostic => diagnostic.Id == "LUI1018") == 1 && deepDocument.Component!.Body.OfType<LuiElementSyntax>().Any(element => element.Name.Text == "B"), "deep nesting did not recover to the later sibling.");
var owned = LuiParser.Parse("namespace Sample; using System; internal component X(int x, string y) { <A P=\"x\" Q={x} Style={Panel with { P: X; }} /> } style S { when Hover { P: X; } }");
var ownedStyle = owned.Styles.Single().Assignments.Single(); var ownedInline = (LuiStyleWithSyntax)((LuiElementSyntax)owned.Component!.Body.Single()).Attributes.Single(attribute => attribute.Name.Text == "Style").Value;
var ownedGroup = owned.Styles.Single().Members.OfType<LuiVariantGroupSyntax>().Single(); var ownedRoot = (LuiElementSyntax)owned.Component.Body.Single();
var ownedScalar = (LuiScalarSyntax)ownedRoot.Attributes.Single(attribute => attribute.Name.Text == "P").Value; var ownedExpression = (LuiExpressionSyntax)ownedRoot.Attributes.Single(attribute => attribute.Name.Text == "Q").Value;
Assert(!((LuiNamespaceSyntax)owned.TopLevel.OfType<LuiNamespaceSyntax>().Single()).Keyword.IsMissing && !((LuiNamespaceSyntax)owned.TopLevel.OfType<LuiNamespaceSyntax>().Single()).Semicolon.IsMissing && !((LuiUsingSyntax)owned.TopLevel.OfType<LuiUsingSyntax>().Single()).Semicolon.IsMissing && !owned.Component.Parameters[0].Separator.IsMissing && !ownedScalar.OpenQuote.IsMissing && !ownedExpression.CloseBrace.IsMissing && !ownedInline.OuterOpenBrace.IsMissing && !ownedInline.WithKeyword.IsMissing && !ownedInline.OpenBrace.IsMissing && !ownedInline.CloseBrace.IsMissing && !ownedInline.OuterCloseBrace.IsMissing && !ownedGroup.WhenKeyword.IsMissing && !ownedGroup.OpenBrace.IsMissing && !ownedGroup.CloseBrace.IsMissing, "remaining structural tokens were not owned.");
var missingTokens = LuiParser.Parse("namespace Sample\nusing System\ninternal component X() { <A P=\"x }");
var missingScalar = (LuiScalarSyntax)((LuiElementSyntax)missingTokens.Component!.Body.Single()).Attributes.Single().Value;
var missingExpression = (LuiExpressionSyntax)((LuiElementSyntax)LuiParser.Parse("internal component X() { <A P={x").Component!.Body.Single()).Attributes.Single().Value;
var missingGroup = LuiParser.Parse("internal component X() { <A /> } style S { when Hover { P: X;").Styles.Single().Members.OfType<LuiVariantGroupSyntax>().Single();
var missingOuterStyle = (LuiStyleWithSyntax)((LuiElementSyntax)LuiParser.Parse("internal component X() { <A Style={Panel with { P: X; }").Component!.Body.Single()).Attributes.Single().Value;
Assert(((LuiNamespaceSyntax)missingTokens.TopLevel.OfType<LuiNamespaceSyntax>().Single()).Semicolon.IsMissing && ((LuiUsingSyntax)missingTokens.TopLevel.OfType<LuiUsingSyntax>().Single()).Semicolon.IsMissing && missingScalar.CloseQuote.IsMissing && missingExpression.CloseBrace.IsMissing && missingGroup.CloseBrace.IsMissing && missingOuterStyle.OuterCloseBrace.IsMissing, "missing directive, attribute, or variant token was not represented.");
foreach (var expression in new[] { "$\"{x = 1}\"", "$\"{await x}\"", "$\"{from x in xs select x}\"", "$\"{x switch { _ => x }}\"", "$\"{x, x = 1}\"" }) Assert(LuiParser.Parse("internal component X() { <A P={" + expression + "} /> }").Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1012"), "disallowed interpolation was accepted: " + expression);
Assert(LuiParser.Parse("internal component X() { <A P={$\"{x, x + 1}\"} /> }").Diagnostics.Count == 0, "simple interpolation was rejected.");
var conditionalStyle = LuiParser.Parse("internal component X() { <A /> } style S { P: ok ? X : Y }");
Assert(conditionalStyle.Diagnostics.Count == 0 && conditionalStyle.Styles.Single().Assignments.Single().Expression.Text == "ok ? X : Y", "conditional style expression was split as an assignment.");
var roslynNames = LuiParser.Parse("internal component X() { <global::Sample.A Alias::Sample.A=\"x\" Sample.\\u00C5ngström=\"y\" @verbatim=\"z\" Á=\"q\" /> }");
Assert(roslynNames.Diagnostics.Count == 0 && ((LuiElementSyntax)roslynNames.Component!.Body.Single()).Name.Text == "global::Sample.A" && ((LuiElementSyntax)roslynNames.Component.Body.Single()).Attributes.Any(attribute => attribute.Name.Text == "Sample.\\u00C5ngström"), "Roslyn name boundaries or source text changed.");
for (var i = 0; i < 64; i++)
{
    var source = "internal component X() { <A P={new[] { \"}\", " + i + " }.Length}> text " + i + "</A> }";
    var parsed = LuiParser.Parse(source); Assert(parsed.Diagnostics.Count == 0 && LuiFormatter.Format(source) == LuiFormatter.Format(LuiFormatter.Format(source)), "adversarial corpus failed at " + i);
}
return 0;

static string Diagnostics(LuiDocumentSyntax document) => string.Join(" | ", document.Diagnostics.Select(diagnostic => diagnostic.Id + "@" + diagnostic.Span.Start));
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
