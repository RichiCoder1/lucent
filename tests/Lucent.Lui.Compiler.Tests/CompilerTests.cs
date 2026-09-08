using System;
using System.Linq;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class CompilerTests
{
    private static readonly string[] ExpectedComments =
    [
        "// header",
        "/* between */",
        "// component",
        "// style",
    ];
    private static readonly string[] ExpectedTextRegions = ["if only", "foreach item"];

    const string Complete = """
namespace Sample.Ui;
using System;
public component Card(System.Collections.Generic.Dictionary<string, (int x, int y)> values, Action save) {
    <Components.Row Name="card" Style={Panel with { Padding: Insets.All(4); }} P={recordValue with { Value = 2 }}>
        {/* comment } stays a comment */}
        if (values.Any(x => x is { x: > 0 })) { <Text Name="title"> Hello </Text> } else { }
        foreach (var item in Items.Where(x => x != "}")) keyed by item.Id { <Row Name="item" /> }
    </Components.Row>
}
style Panel { Padding: Insets.All(2); when Hover { Opacity: .5; } }
""";

    [TestMethod]
    public void ScrollbarStyleHooksBindWithoutStaticImports()
    {
        var source = """
namespace Sample;
using Lucent.Core;
style Scrolling {
    Visibility: ScrollBarVisibility.Auto;
    Thickness: 10;
    MinimumThumbLength: 28;
    ThumbCornerRadius: 5;
    TrackBrush: Brush.Solid(Color.Parse("#eeeeee"));
    ThumbBrush: Brush.Solid(Color.Parse("#666666"));
    HoverThumbBrush: Brush.Solid(Color.Parse("#444444"));
    PressedThumbBrush: Brush.Solid(Color.Parse("#222222"));
    CornerRadius: 6;
}
internal component Example() { <Text style={Scrolling}>Scrollbar theme</Text> }
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create("scrollbar-styles", references: References()),
            new LuiFreshnessIdentity(
                "1",
                "scrollbar",
                new LuiDocumentIdentity("Scrollbar.lui"),
                "v1",
                "preview"
            )
        );
        Assert(result.Success, string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        Assert(
            result.Source!.Contains("global::Lucent.Core.ScrollBarProperties.ThumbCornerRadius")
                && result.Source.Contains("global::Lucent.Core.VisualProperties.CornerRadius")
                && result.Source.Contains(
                    "global::Lucent.Core.ScrollBarProperties.PressedThumbBrush"
                ),
            "Scrollbar hooks and the control corner radius did not retain distinct typed owners."
        );
    }

    [TestMethod]
    public void ParserAndFormatting()
    {
        var document = LuiParser.Parse(Complete);
        Assert(
            document.Diagnostics.Count == 0,
            "complete document diagnostics: " + Diagnostics(document)
        );
        var root = (LuiElementSyntax)document.Component!.Body.Single();
        Assert(
            root.Name.Text == "Components.Row"
                && root.Name.Span.Start
                    == Complete.IndexOf("Components.Row", StringComparison.Ordinal),
            "qualified element name/span changed."
        );
        Assert(
            root.Attributes.Single(attribute => attribute.Name.Text == "Style").Value
                is LuiStyleWithSyntax,
            "colon inline style was not retained."
        );
        Assert(
            root.Attributes.Single(attribute => attribute.Name.Text == "P").Value
                is LuiExpressionSyntax,
            "ordinary C# with expression was not retained."
        );
        var conditional = root.Children.OfType<LuiIfSyntax>().Single();
        Assert(
            !conditional.ElseKeyword.IsMissing && conditional.ElseBody.Count == 0,
            "explicit empty else was lost."
        );
        Assert(
            conditional.Condition.Span.Start
                == Complete.IndexOf("values.Any", StringComparison.Ordinal),
            "expression span is not absolute."
        );

        foreach (
            var source in new[]
            {
                "internal component X() { <A / > }",
                "internal component X() { <Components.Row /> }",
                "internal component X() { <A>{/* } */}</A> }",
                "internal component X() { <A P={\"escape\\} /> }",
                "internal component X() { <A P={new[] { @\"}\", \"\"\"raw } text\"\"\", $\"value {x}\", '}' }.Length} /> }",
                "internal component X() { if (M(/* } */ x => x is { Value: > 0 })) { <A /> } }",
            }
        )
        {
            for (var i = 0; i != 8; i++)
                _ = LuiParser.Parse(source);
        }

        foreach (
            var source in new[]
            {
                "internal component X() { <A P={await x} /> }",
                "internal component X() { <A P={x = 1} /> }",
                "internal component X() { <A P={var x = 1} /> }",
                "internal component X() { <A P={x => { return x; }} /> }",
            }
        )
            Assert(
                LuiParser.Parse(source).Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1012"),
                "disallowed island was accepted: " + source
            );

        var malformedHeader = LuiParser.Parse(
            "namespace ???; using =; internal component @() { <A /> }"
        );
        Assert(
            malformedHeader.Diagnostics.Take(3).All(diagnostic => diagnostic.Id == "LUI1000")
                && malformedHeader.Diagnostics[0].Span.Start == 0,
            "invalid headers did not diagnose in source order: " + Diagnostics(malformedHeader)
        );
        var missing = LuiParser.Parse("internal component X() { <A> <B /> }");
        var missingElement = missing.Component!.Body.OfType<LuiElementSyntax>().Single();
        Assert(
            missingElement.CloseName.IsMissing
                && missingElement.CloseName.Span.Start == missingElement.Span.End
                && missingElement.OpenAngle.Text == "<"
                && missingElement.OpenCloseAngle.Text == ">"
                && missingElement.CloseOpenAngle.IsMissing
                && missingElement.CloseAngle.IsMissing
                && missing.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1008"),
            "missing closing tag token was not represented."
        );

        var tokenDocument = LuiParser.Parse(
            "internal component X() { <Root><A P=\"x\" /> if (ok) { <B /> } else { } foreach (var x in xs) keyed by x.Id { <C /> }</Root> } style S { P: X; }"
        );
        Assert(
            tokenDocument.Diagnostics.Count == 0,
            "token document diagnostics: " + Diagnostics(tokenDocument)
        );
        var tokenComponent = tokenDocument.Component!;
        var tokenRoot = tokenComponent.Body.OfType<LuiElementSyntax>().Single();
        var tokenElement = tokenRoot.Children.OfType<LuiElementSyntax>().Single();
        var tokenIf = tokenRoot.Children.OfType<LuiIfSyntax>().Single();
        var tokenForEach = tokenRoot.Children.OfType<LuiForEachSyntax>().Single();
        var tokenStyle = tokenDocument.Styles.Single();
        Assert(
            !tokenComponent.ComponentKeyword.IsMissing
                && !tokenComponent.OpenParameters.IsMissing
                && !tokenComponent.CloseParameters.IsMissing
                && !tokenComponent.OpenBrace.IsMissing
                && !tokenComponent.CloseBrace.IsMissing
                && !tokenElement.SelfClosingSlash.IsMissing
                && !tokenElement.OpenCloseAngle.IsMissing
                && !tokenElement.Attributes.Single().EqualsToken.IsMissing
                && !tokenIf.IfKeyword.IsMissing
                && !tokenIf.OpenCondition.IsMissing
                && !tokenIf.CloseCondition.IsMissing
                && !tokenIf.OpenBrace.IsMissing
                && !tokenIf.CloseBrace.IsMissing
                && !tokenIf.ElseKeyword.IsMissing
                && !tokenIf.ElseOpenBrace.IsMissing
                && !tokenIf.ElseCloseBrace.IsMissing
                && !tokenForEach.ForeachKeyword.IsMissing
                && !tokenForEach.KeyedKeyword.IsMissing
                && !tokenForEach.ByKeyword.IsMissing
                && !tokenForEach.OpenBrace.IsMissing
                && !tokenForEach.CloseBrace.IsMissing
                && !tokenStyle.OpenBrace.IsMissing
                && !tokenStyle.CloseBrace.IsMissing
                && !tokenStyle.Assignments.Single().Colon.IsMissing
                && !tokenStyle.Assignments.Single().Terminator.IsMissing,
            "structural token ownership is incomplete."
        );
        var recursivePatternLoop = LuiParser.Parse(
            "internal component X() { <Root>foreach (var x in selected is { } value ? [value] : Array.Empty<Item>()) keyed by x.Id { <A /> }</Root> }"
        );
        Assert(
            recursivePatternLoop.Diagnostics.Count == 0
                && ((LuiElementSyntax)recursivePatternLoop.Component!.Body.Single())
                    .Children.OfType<LuiForEachSyntax>()
                    .Single()
                    .Source.Text == "selected is { } value ? [value] : Array.Empty<Item>()",
            "keyed foreach source stopped at a recursive pattern or generic type."
        );
        foreach (
            var directCondition in new[]
            {
                "count < 2",
                "count <= limit",
                "count > 0 && limit < count",
                "Check<int>()",
                "selected.Value is { } detail",
                "Helpers.Format(count) is { } detail",
                "candidate is Pair { Left: Row left, Right: Row right }",
            }
        )
        {
            var directConditionSource =
                "internal component X(int count, int limit) { <Root>if ("
                + directCondition
                + ") { <A /> } <B /> </Root> }";
            var directConditionDocument = LuiParser.Parse(directConditionSource);
            var directConditionRoot = (LuiElementSyntax)
                directConditionDocument.Component!.Body.Single();
            var directConditionFormatted = LuiFormatter.Format(
                directConditionSource,
                LuiLineEnding.Lf
            );
            Assert(
                directConditionDocument.Diagnostics.Count == 0
                    && directConditionRoot.Children.OfType<LuiIfSyntax>().Single().Condition.Text
                        == directCondition
                    && directConditionRoot.Children.OfType<LuiElementSyntax>().Single().Name.Text
                        == "B"
                    && directConditionFormatted.Contains(
                        "if (" + directCondition + ")",
                        StringComparison.Ordinal
                    )
                    && directConditionFormatted
                        == LuiFormatter.Format(directConditionFormatted, LuiLineEnding.Lf),
                "a direct structural condition stopped at a valid comparison or generic token: "
                    + directCondition
                    + " "
                    + Diagnostics(directConditionDocument)
            );
        }
        const string structuralOperatorSource =
            "internal component X(int count, int limit) { <Root>"
            + "if ((count < 2 && Check<int>()) || (count <= limit && (limit > 0))) { <A /> } "
            + "foreach (var item in Items.Where(item => item < limit)) keyed by Key<int>(item <= count ? item : count) { <B /> }"
            + "</Root> }";
        var structuralOperators = LuiParser.Parse(structuralOperatorSource);
        var structuralOperatorRoot = (LuiElementSyntax)structuralOperators.Component!.Body.Single();
        var structuralOperatorIf = structuralOperatorRoot.Children.OfType<LuiIfSyntax>().Single();
        var structuralOperatorLoop = structuralOperatorRoot
            .Children.OfType<LuiForEachSyntax>()
            .Single();
        Assert(
            structuralOperators.Diagnostics.Count == 0
                && structuralOperatorIf.Condition.Text
                    == "(count < 2 && Check<int>()) || (count <= limit && (limit > 0))"
                && structuralOperatorLoop.Source.Text == "Items.Where(item => item < limit)"
                && structuralOperatorLoop.Key.Text == "Key<int>(item <= count ? item : count)",
            "comparison, generic, nested, or keyed structural expressions stopped at '<': "
                + Diagnostics(structuralOperators)
        );
        var structuralOperatorFormatted = LuiFormatter.Format(
            structuralOperatorSource,
            LuiLineEnding.Lf
        );
        Assert(
            structuralOperatorFormatted
                == LuiFormatter.Format(structuralOperatorFormatted, LuiLineEnding.Lf)
                && structuralOperatorFormatted.Contains(
                    "if ((count < 2 && Check<int>()) || (count <= limit && (limit > 0)))",
                    StringComparison.Ordinal
                )
                && structuralOperatorFormatted.Contains(
                    "keyed by Key<int>(item <= count ? item : count)",
                    StringComparison.Ordinal
                ),
            "structural expression formatting changed comparison or generic token text."
        );
        var missingLoopClose = LuiParser.Parse(
            "internal component X() { <Root>foreach (var x in xs keyed by x.Id { <A /> } <B /> </Root> }"
        );
        Assert(
            ((LuiElementSyntax)missingLoopClose.Component!.Body.Single())
                .Children.OfType<LuiElementSyntax>()
                .Any(element => element.Name.Text == "B"),
            "a missing keyed foreach ')' consumed a later sibling."
        );
        var missingComparisonClose = LuiParser.Parse(
            "internal component X() { <Root>if (count < 2 { <A /> } <B /> </Root> }"
        );
        var missingComparisonRoot = (LuiElementSyntax)
            missingComparisonClose.Component!.Body.Single();
        Assert(
            missingComparisonRoot.Children.OfType<LuiIfSyntax>().Single().CloseCondition.IsMissing
                && missingComparisonRoot
                    .Children.OfType<LuiElementSyntax>()
                    .Any(element => element.Name.Text == "B"),
            "a missing comparison condition ')' consumed a later sibling."
        );
        var missingKeyOpen = LuiParser.Parse(
            "internal component X() { <Root>foreach (var item in Items) keyed by Key<int>(item < limit) <A /> <B /> </Root> }"
        );
        var missingKeyRoot = (LuiElementSyntax)missingKeyOpen.Component!.Body.Single();
        Assert(
            missingKeyRoot.Children.OfType<LuiForEachSyntax>().Single().OpenBrace.IsMissing
                && missingKeyRoot
                    .Children.OfType<LuiElementSyntax>()
                    .Select(element => element.Name.Text)
                    .SequenceEqual(["A", "B"]),
            "a missing keyed-loop opener consumed later siblings after a generic comparison key."
        );

        var missingOpen = LuiParser.Parse("internal component X() { if (ok) <A /> <B /> }");
        Assert(
            missingOpen.Component!.Body.OfType<LuiIfSyntax>().Single().OpenBrace.IsMissing
                && missingOpen.Component.Body.OfType<LuiElementSyntax>().Count() == 2,
            "missing if opener consumed later siblings."
        );
        var missingElementClose = LuiParser.Parse(
            "internal component X() { <A><B /> } style S { P: X; }"
        );
        Assert(
            missingElementClose
                .Component!.Body.OfType<LuiElementSyntax>()
                .Single()
                .CloseName.IsMissing
                && missingElementClose.Styles.Single().Name.Text == "S",
            "missing element close consumed later top-level style."
        );
        var missingStyleClose = LuiParser.Parse("style S { P: X; internal component X() { <A /> }");
        Assert(
            missingStyleClose.Styles.Single().CloseBrace.IsMissing
                && missingStyleClose.Component!.Name.Text == "X",
            "missing style close consumed later component."
        );
        Assert(
            missingStyleClose
                .Diagnostics.Select(diagnostic => diagnostic.Span.Start)
                .SequenceEqual(
                    missingStyleClose
                        .Diagnostics.Select(diagnostic => diagnostic.Span.Start)
                        .OrderBy(start => start)
                ),
            "recovery diagnostics are not in source order."
        );

        var comments = LuiParser.Parse(
            "// header\nnamespace Sample;\n/* between */\nusing System;\n// component\ninternal component X() { <A>  Hello   world  </A> }\n// style\nstyle S { P: X; }"
        );
        Assert(
            comments.Diagnostics.Count == 0
                && comments.Comments.Select(comment => comment.Text).SequenceEqual(ExpectedComments)
                && ((LuiElementSyntax)comments.Component!.Body.Single())
                    .Children.OfType<LuiTextSyntax>()
                    .Single()
                    .Text == "Hello   world",
            "top-level comments or trimmed text were not retained."
        );
        var commentsFormatted = LuiFormatter.Format(comments.Source, LuiLineEnding.Lf);
        Assert(
            commentsFormatted.IndexOf("// header", StringComparison.Ordinal)
                < commentsFormatted.IndexOf("/* between */", StringComparison.Ordinal)
                && commentsFormatted.IndexOf("/* between */", StringComparison.Ordinal)
                    < commentsFormatted.IndexOf("// component", StringComparison.Ordinal)
                && commentsFormatted.IndexOf("// component", StringComparison.Ordinal)
                    < commentsFormatted.IndexOf("// style", StringComparison.Ordinal),
            "formatter changed top-level comment order."
        );

        var formatted = LuiFormatter.Format(Complete, LuiLineEnding.Lf);
        Assert(
            formatted == LuiFormatter.Format(formatted, LuiLineEnding.Lf)
                && formatted.Contains("else {\n", StringComparison.Ordinal)
                && !formatted.Contains("}\n    }\n}", StringComparison.Ordinal),
            "formatter is not idempotent or emitted an else brace."
        );
        Assert(
            LuiFormatter.Format("internal component X() { <A>", LuiLineEnding.Lf)
                == "internal component X() { <A>",
            "malformed formatting was destructive."
        );
        var element = conditional.ThenBody.OfType<LuiElementSyntax>().Single();
        var ranged = LuiFormatter.FormatRange(Complete, element.Span, LuiLineEnding.CrLf);
        Assert(
            ranged.Substring(0, element.Span.Start) == Complete.Substring(0, element.Span.Start)
                && ranged.Substring(ranged.Length - (Complete.Length - element.Span.End))
                    == Complete.Substring(element.Span.End),
            "range formatting changed text outside its selected node."
        );
        Assert(
            LuiFormatter.FormatRange(Complete, new LuiSpan(0, 1)) == Complete,
            "range formatting changed an incomplete selection."
        );
        var commentRangeSource = "// comment\ninternal component X() { <A /> }";
        var commentRange = LuiParser.Parse(commentRangeSource).Comments.Single().Span;
        var commentRanged = LuiFormatter.FormatRange(
            commentRangeSource,
            commentRange,
            LuiLineEnding.Lf
        );
        Assert(
            commentRanged
                == LuiFormatter.FormatRange(commentRanged, commentRange, LuiLineEnding.Lf),
            "comment range formatting threw or was not idempotent."
        );
        Assert(
            LuiFormatter
                .Format("internal component X() { if (x) { <A /> } }", LuiLineEnding.Lf)
                .Contains("if (x) {\n", StringComparison.Ordinal)
                && !LuiFormatter
                    .Format("internal component X() { if (x) { <A /> } }", LuiLineEnding.Lf)
                    .Contains(" else {", StringComparison.Ordinal),
            "formatter invented an else."
        );
        var styleNewlines = LuiParser.Parse(
            "internal component X() { <A /> } style S { P: X\nQ: Y; }"
        );
        Assert(
            styleNewlines.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1015")
                && styleNewlines.Styles.Single().Assignments.Count == 2
                && styleNewlines.Styles.Single().Assignments[1].Property.Text == "Q",
            "missing style semicolon did not report and recover at the next assignment."
        );
        var inlineStyleSemicolon = LuiParser.Parse(
            "internal component X() { <A style={Base with { P: X }} /> }"
        );
        Assert(
            inlineStyleSemicolon.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1015"),
            "missing inline style semicolon was accepted."
        );
        var adjacentStyles = LuiParser.Parse(
            "internal component X() { <A /> } style S { P: X Q: Y; }"
        );
        Assert(
            adjacentStyles.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1015")
                && adjacentStyles.Styles.Single().Assignments.Count == 2
                && adjacentStyles.Styles.Single().Assignments[1].Property.Text == "Q",
            "adjacent malformed style assignment swallowed Q."
        );
        var textRegions = LuiParser.Parse(
            "internal component X() { <Text>if only</Text> <Text>foreach item</Text> }"
        );
        Assert(
            textRegions
                .Component!.Body.OfType<LuiElementSyntax>()
                .SelectMany(element => element.Children)
                .OfType<LuiTextSyntax>()
                .Select(value => value.Text)
                .SequenceEqual(ExpectedTextRegions),
            "plain if/foreach text was parsed as a region."
        );
        var badRegion = LuiParser.Parse("internal component X() { if (ok { <A /> } <B /> }");
        Assert(
            badRegion
                .Component!.Body.OfType<LuiElementSyntax>()
                .Any(element => element.Name.Text == "B"),
            "missing region ')' consumed a later sibling."
        );
        var recordWith = LuiParser.Parse(
            "internal component X() { <A Style={recordValue /* ordinary */ with { Value = 2 }} /> }"
        );
        Assert(
            recordWith.Diagnostics.Count == 0
                && ((LuiElementSyntax)recordWith.Component!.Body.Single()).Attributes.Single().Value
                    is LuiExpressionSyntax,
            "ordinary record with was speculatively classified as style."
        );
        foreach (var expression in new[] { "from x in xs select x", "x switch { _ => x }" })
            Assert(
                LuiParser
                    .Parse("internal component X() { <A P={" + expression + "} /> }")
                    .Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1012"),
                "query/switch expression was accepted."
            );
        Assert(
            LuiParser
                .Parse("internal component X() { <A..B /> }")
                .Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1014"),
            "empty qualified-name segment was accepted."
        );
        Assert(
            LuiParser.Parse("internal component X() { <A\u0301 /> }").Diagnostics.Count == 0,
            "combining-mark identifier was rejected."
        );
        var ordered = LuiParser.Parse("internal component X( { <A P={await x} /> }");
        Assert(
            ordered
                .Diagnostics.Select(diagnostic => diagnostic.Span.Start)
                .SequenceEqual(
                    ordered
                        .Diagnostics.Select(diagnostic => diagnostic.Span.Start)
                        .OrderBy(start => start)
                ),
            "diagnostics were not stable-sorted."
        );
        var deep =
            "internal component X() { "
            + string.Concat(Enumerable.Repeat("<A>", 5000))
            + string.Concat(Enumerable.Repeat("</A>", 5000))
            + " <B /> }";
        var deepDocument = LuiParser.Parse(deep);
        Assert(
            deepDocument.Diagnostics.Count(diagnostic => diagnostic.Id == "LUI1018") == 1
                && deepDocument
                    .Component!.Body.OfType<LuiElementSyntax>()
                    .Any(element => element.Name.Text == "B"),
            "deep nesting did not recover to the later sibling."
        );
        var owned = LuiParser.Parse(
            "namespace Sample; using System; internal component X(int x, string y) { <A P=\"x\" Q={x} Style={Panel with { P: X; }} /> } style S { when Hover { P: X; } }"
        );
        var ownedStyle = owned.Styles.Single().Assignments.Single();
        var ownedInline = (LuiStyleWithSyntax)
            ((LuiElementSyntax)owned.Component!.Body.Single())
                .Attributes.Single(attribute => attribute.Name.Text == "Style")
                .Value;
        var ownedGroup = owned.Styles.Single().Members.OfType<LuiVariantGroupSyntax>().Single();
        var ownedRoot = (LuiElementSyntax)owned.Component.Body.Single();
        var ownedScalar = (LuiScalarSyntax)
            ownedRoot.Attributes.Single(attribute => attribute.Name.Text == "P").Value;
        var ownedExpression = (LuiExpressionSyntax)
            ownedRoot.Attributes.Single(attribute => attribute.Name.Text == "Q").Value;
        Assert(
            !((LuiNamespaceSyntax)owned.TopLevel.OfType<LuiNamespaceSyntax>().Single())
                .Keyword
                .IsMissing
                && !((LuiNamespaceSyntax)owned.TopLevel.OfType<LuiNamespaceSyntax>().Single())
                    .Semicolon
                    .IsMissing
                && !((LuiUsingSyntax)owned.TopLevel.OfType<LuiUsingSyntax>().Single())
                    .Semicolon
                    .IsMissing
                && !owned.Component.Parameters[0].Separator.IsMissing
                && !ownedScalar.OpenQuote.IsMissing
                && !ownedExpression.CloseBrace.IsMissing
                && !ownedInline.OuterOpenBrace.IsMissing
                && !ownedInline.WithKeyword.IsMissing
                && !ownedInline.OpenBrace.IsMissing
                && !ownedInline.CloseBrace.IsMissing
                && !ownedInline.OuterCloseBrace.IsMissing
                && !ownedGroup.WhenKeyword.IsMissing
                && !ownedGroup.OpenBrace.IsMissing
                && !ownedGroup.CloseBrace.IsMissing,
            "remaining structural tokens were not owned."
        );
        var missingTokens = LuiParser.Parse(
            "namespace Sample\nusing System\ninternal component X() { <A P=\"x }"
        );
        var missingScalar = (LuiScalarSyntax)
            ((LuiElementSyntax)missingTokens.Component!.Body.Single()).Attributes.Single().Value;
        var missingExpression = (LuiExpressionSyntax)
            (
                (LuiElementSyntax)
                    LuiParser.Parse("internal component X() { <A P={x").Component!.Body.Single()
            )
                .Attributes.Single()
                .Value;
        var missingGroup = LuiParser
            .Parse("internal component X() { <A /> } style S { when Hover { P: X;")
            .Styles.Single()
            .Members.OfType<LuiVariantGroupSyntax>()
            .Single();
        var missingOuterStyle = (LuiStyleWithSyntax)
            (
                (LuiElementSyntax)
                    LuiParser
                        .Parse("internal component X() { <A Style={Panel with { P: X; }")
                        .Component!.Body.Single()
            )
                .Attributes.Single()
                .Value;
        Assert(
            ((LuiNamespaceSyntax)missingTokens.TopLevel.OfType<LuiNamespaceSyntax>().Single())
                .Semicolon
                .IsMissing
                && ((LuiUsingSyntax)missingTokens.TopLevel.OfType<LuiUsingSyntax>().Single())
                    .Semicolon
                    .IsMissing
                && missingScalar.CloseQuote.IsMissing
                && missingExpression.CloseBrace.IsMissing
                && missingGroup.CloseBrace.IsMissing
                && missingOuterStyle.OuterCloseBrace.IsMissing,
            "missing directive, attribute, or variant token was not represented."
        );
        foreach (
            var expression in new[]
            {
                "$\"{x = 1}\"",
                "$\"{await x}\"",
                "$\"{from x in xs select x}\"",
                "$\"{x switch { _ => x }}\"",
                "$\"{x, x = 1}\"",
            }
        )
            Assert(
                LuiParser
                    .Parse("internal component X() { <A P={" + expression + "} /> }")
                    .Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1012"),
                "disallowed interpolation was accepted: " + expression
            );
        Assert(
            LuiParser
                .Parse("internal component X() { <A P={$\"{x, x + 1}\"} /> }")
                .Diagnostics.Count == 0,
            "simple interpolation was rejected."
        );
        var conditionalStyle = LuiParser.Parse(
            "internal component X() { <A /> } style S { P: ok ? X : Y; }"
        );
        Assert(
            conditionalStyle.Diagnostics.Count == 0
                && conditionalStyle.Styles.Single().Assignments.Single().Expression.Text
                    == "ok ? X : Y",
            "conditional style expression was split as an assignment."
        );
        var formatterSemanticSource =
            "internal component X(string? label = null, int count = 2) { <A style={Base with { P: count; when Selected | FocusVisible { Q: label; R: count; } }} /> }";
        var formatterSemantic = LuiFormatter.Format(formatterSemanticSource, LuiLineEnding.Lf);
        var formatterSemanticDocument = LuiParser.Parse(formatterSemantic);
        Assert(
            formatterSemantic == LuiFormatter.Format(formatterSemantic, LuiLineEnding.Lf)
                && formatterSemantic.Contains(
                    "string? label = null, int count = 2",
                    StringComparison.Ordinal
                )
                && formatterSemantic.Contains(
                    "when Selected | FocusVisible { P: count",
                    StringComparison.Ordinal
                ) == false
                && formatterSemantic.Contains(
                    "when Selected | FocusVisible { Q: label; R: count; }"
                )
                && (
                    (LuiStyleWithSyntax)
                        ((LuiElementSyntax)formatterSemanticDocument.Component!.Body.Single())
                            .Attributes.Single()
                            .Value
                )
                    .Members.OfType<LuiVariantGroupSyntax>()
                    .Single()
                    .Assignments.Count == 2,
            "formatter flattened inline style structure or parameter declarations."
        );
        var variantFirst = LuiFormatter.Format(
            "internal component X() { <A style={Base with { when Hover { P: x; } when Selected { Q: x; } R: x; }} /> }",
            LuiLineEnding.Lf
        );
        var variantFirstDocument = LuiParser.Parse(variantFirst);
        var variantMembers = (
            (LuiStyleWithSyntax)
                ((LuiElementSyntax)variantFirstDocument.Component!.Body.Single())
                    .Attributes.Single()
                    .Value
        ).Members;
        Assert(
            variantFirst == LuiFormatter.Format(variantFirst, LuiLineEnding.Lf)
                && !variantFirst.Contains("};", StringComparison.Ordinal)
                && variantMembers.Count == 3
                && variantMembers[0] is LuiVariantGroupSyntax
                && variantMembers[1] is LuiVariantGroupSyntax
                && variantMembers[2] is LuiStyleAssignmentSyntax,
            "inline variant separators changed ordering or formatter semantics."
        );
    }

    [TestMethod]
    public void ParameterizedStylesRetainReactiveNestingAndLiveBindings()
    {
        var source = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
using static Lucent.Core.VisualProperties;
using static Lucent.Core.LayoutProperties;
internal component Example(float width) {
    <Row style={Workspace(width)} />
}
style Workspace(float width) {
    Width: width;
    when (width >= 820) {
        Opacity: .5f;
        when Hover { Opacity: .75f; }
    }
}
""";
        var document = LuiParser.Parse(source);
        Assert(
            document.Diagnostics.Count == 0,
            "parameterized style parse: " + Diagnostics(document)
        );
        var style = document.Styles.Single();
        var reactive = style.Members.OfType<LuiVariantGroupSyntax>().Single();
        var nested = reactive.Members.OfType<LuiVariantGroupSyntax>().Single();
        Assert(
            style.Parameters.Count == 1
                && style.Parameters[0].Name.Text == "width"
                && reactive.ConditionExpression?.Text == "width >= 820"
                && nested.ConditionExpression is null
                && style.Assignments.Count == 3,
            "parameterized style parameters or nested members were not retained."
        );

        var formatted = LuiFormatter.Format(source, LuiLineEnding.Lf);
        Assert(
            formatted == LuiFormatter.Format(formatted, LuiLineEnding.Lf)
                && formatted.Contains("style Workspace(float width)", StringComparison.Ordinal)
                && formatted.Contains("when (width >= 820)", StringComparison.Ordinal)
                && formatted.Contains("when Hover", StringComparison.Ordinal),
            "parameterized and nested style syntax was not formatted idempotently."
        );

        var result = LuiCompiler.Compile(
            document,
            CSharpCompilation.Create("parameterized-style", references: References()),
            new LuiFreshnessIdentity(
                "parameterized-style",
                "parameterized-style",
                new LuiDocumentIdentity("ParameterizedStyle.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            result.Success,
            "parameterized style lowering failed: "
                + String.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message))
        );
        Assert(
            result.Source!.Contains(
                "private static global::Lucent.Core.Style __luiStyle_",
                StringComparison.Ordinal
            )
                && result.Source.Contains(".Bind<float?>(", StringComparison.Ordinal)
                && result.Source.Contains(".When(() =>", StringComparison.Ordinal)
                && result.Source.Contains("width >= 820", StringComparison.Ordinal)
                && result.Source.Contains(
                    ".When(global::Lucent.Core.VariantState.Hover",
                    StringComparison.Ordinal
                ),
            "parameterized styles did not lower through live bindings and nested conditions.\n"
                + result.Source
        );
        Assert(
            result
                .Map.FromSource(reactive.ConditionExpression!.Span)
                .Any(entry => entry.Generated.Length != 0),
            "reactive condition did not receive a generated source-map entry."
        );
    }

    [TestMethod]
    public void ParameterizedStyleLiteralsDoNotBecomeLiveBindings()
    {
        const string source = """
namespace Sample;
using System;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Example(float width) { <Row style={Workspace(width)} /> }
style Workspace(float width) {
    Axis: LayoutAxis.Column;
    Height: Math.Max(1f, 2f);
    Width: width;
    when Hover { Opacity: .5f; }
}
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create("parameterized-style-literals", references: References()),
            new LuiFreshnessIdentity(
                "parameterized-style-literals",
                "parameterized-style-literals",
                new LuiDocumentIdentity("ParameterizedStyleLiterals.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            result.Success,
            "parameterized literal style did not compile: "
                + String.Join(" | ", result.Diagnostics.Select(item => item.Message))
        );
        Assert(
            result.Source!.Contains(
                ".Set(global::Lucent.Core.LayoutProperties.Axis",
                StringComparison.Ordinal
            )
                && result.Source.Contains(
                    ".Bind<float?>(global::Lucent.Core.LayoutProperties.Width",
                    StringComparison.Ordinal
                )
                && result.Source.Contains(
                    ".Bind<float?>(global::Lucent.Core.LayoutProperties.Height",
                    StringComparison.Ordinal
                )
                && result.Source.Contains("LayoutAxis.Column", StringComparison.Ordinal)
                && result.Source.Contains(
                    ".Set(global::Lucent.Core.VisualProperties.Opacity",
                    StringComparison.Ordinal
                )
                && result.Source.Contains(".5f", StringComparison.Ordinal),
            "literal parameterized assignments were lowered as live bindings.\n" + result.Source
        );
    }

    [TestMethod]
    public void ParameterizedStyleNullableNullsUseResolvedPropertyTypes()
    {
        const string propertyApi = """
namespace Sample;
using Lucent.Core;
public static class Props {
    public static readonly Property<string?> Optional = new("optional", null);
    public static readonly Property<string?> OptionalFromConst = new("optional-from-const", null);
    public const string? NullValue = null;
}
""";
        const string source = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
using static Lucent.Core.LayoutProperties;
using static Sample.Props;
internal component Example() { <Row style={Workspace(1)} /> }
style Workspace(int value) {
    Width: null;
    Optional: (null);
    OptionalFromConst: NullValue;
}
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create(
                "parameterized-style-null",
                [
                    CSharpSyntaxTree.ParseText(
                        propertyApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "parameterized-style-null",
                "parameterized-style-null",
                new LuiDocumentIdentity("ParameterizedStyleNull.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            result.Success,
            "nullable static style nulls did not compile: "
                + String.Join(" | ", result.Diagnostics.Select(item => item.Message))
        );
        Assert(
            result.Source!.Contains(
                ".Set(global::Lucent.Core.LayoutProperties.Width, (float?)",
                StringComparison.Ordinal
            )
                && result.Source.Contains(
                    ".Set(global::Sample.Props.Optional, (string?)",
                    StringComparison.Ordinal
                )
                && result.Source.Contains("(null)", StringComparison.Ordinal)
                && result.Source.Contains(
                    ".Set(global::Sample.Props.OptionalFromConst, (string?)",
                    StringComparison.Ordinal
                )
                && result.Source.Contains("NullValue", StringComparison.Ordinal),
            "nullable static style nulls were not emitted with resolved property casts.\n"
                + result.Source
        );
    }

    [TestMethod]
    public void GrammarRecoveryAndFormatterContracts()
    {
        const string members = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Members() {
    // a member comment
    public void Toggle() { }
    /* another member comment */
    internal int count = 0;
    // the retained root follows the members
    <Text>{count}</Text>
}
style Commented {
    // a style comment
    Width: 1f;
    /* a block style comment */
    when Hover {
        Height: 2f;
    }
}
""";
        var memberDocument = LuiParser.Parse(members);
        Assert(
            memberDocument.Diagnostics.Count == 0
                && memberDocument.Component!.Body.OfType<LuiCommentSyntax>().Count() == 3
                && memberDocument.Styles.Single().Members.OfType<LuiStyleCommentSyntax>().Count()
                    == 2,
            "comments or member modifiers were not recovered locally: "
                + Diagnostics(memberDocument)
        );
        var memberFormatted = LuiFormatter.Format(members, LuiLineEnding.Lf);
        Assert(
            memberFormatted.Contains("// a member comment", StringComparison.Ordinal)
                && memberFormatted.Contains("/* a block style comment */", StringComparison.Ordinal)
                && memberFormatted == LuiFormatter.Format(memberFormatted, LuiLineEnding.Lf),
            "member/style comments were lost or formatter output was not idempotent."
        );

        const string text =
            "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; "
            + "internal component TextBody() { <Text>Motif (x); Retry if (needed); foreach (item)</Text> }";
        var textDocument = LuiParser.Parse(text);
        Assert(
            textDocument.Diagnostics.Count == 0
                && ((LuiElementSyntax)textDocument.Component!.Body.Single())
                    .Children.OfType<LuiTextSyntax>()
                    .Single()
                    .Text == "Motif (x); Retry if (needed); foreach (item)",
            "control-keyword text was split into structural regions: " + Diagnostics(textDocument)
        );

        const string islands = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Islands(object? value) {
    <Text content={value?.ToString() ?? global::System.String.Empty} />
}
""";
        var islandDocument = LuiParser.Parse(islands);
        Assert(
            !islandDocument.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1012"),
            "conditional/global-qualified expression islands were rejected: "
                + Diagnostics(islandDocument)
        );
        const string spread =
            "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; "
            + "internal component Spread(System.Collections.Generic.IEnumerable<int> values) "
            + "{ <Text content={[..values].Length.ToString()} /> }";
        var spreadDocument = LuiParser.Parse(spread);
        Assert(
            !spreadDocument.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1012"),
            "collection spread expression islands were rejected: " + Diagnostics(spreadDocument)
        );

        const string elseIf = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Chain(bool first, bool second) {
    if (first) { <Text>one</Text> } else if (second) { <Text>two</Text> } else { <Text>three</Text> }
}
""";
        var elseIfDocument = LuiParser.Parse(elseIf);
        Assert(
            elseIfDocument.Diagnostics.Count(diagnostic => diagnostic.Id == "LUI1022") == 1
                && elseIfDocument.Diagnostics.Count == 1
                && elseIfDocument.Diagnostics[0].Span.Start
                    == elseIf.IndexOf("if (second)", StringComparison.Ordinal),
            "unsupported else-if did not produce one local actionable diagnostic: "
                + Diagnostics(elseIfDocument)
        );

        const string rangeInput = "internal component X() { <Column><Row><Text /></Row></Column> }";
        var rangeSource = LuiFormatter.Format(rangeInput, LuiLineEnding.Lf);
        var nestedChild = LuiParser
            .Parse(rangeSource)
            .Component!.Body.OfType<LuiElementSyntax>()
            .Single()
            .Children.OfType<LuiElementSyntax>()
            .Single();
        var ranged = LuiFormatter.FormatRange(rangeSource, nestedChild.Span, LuiLineEnding.Lf);
        Assert(
            ranged.Contains("\n        </Row>", StringComparison.Ordinal),
            "range formatting reset the selected node's authored indentation:\n" + ranged
        );
    }

    [TestMethod]
    public void QuotedScalarInputsCanUseLiveReaderOverloads()
    {
        const string source = """
namespace ScalarLive;
using System;
using Lucent.Core;
using static ScalarLive.TestComponents;
internal component Host() { <Caption label="Hi" /> }
""";
        const string api = """
namespace ScalarLive;
using System;
using Lucent.Core;
public static class TestComponents
{
    [LucentComponent]
    public static ComponentRecipe Caption(Func<string> label) =>
        ComponentRecipe.Create("caption", (_, _) => { });
}
""";
        var compilation = CSharpCompilation.Create(
            "quoted-live",
            [CSharpSyntaxTree.ParseText(api, new CSharpParseOptions(LanguageVersion.Preview))],
            References()
        );
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            compilation,
            new LuiFreshnessIdentity(
                "quoted-live",
                "quoted-live",
                new LuiDocumentIdentity("QuotedLive.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            result.Success
                && result.Source!.Contains("label: () => \"Hi\"", StringComparison.Ordinal),
            "quoted scalar live-reader inference was not lowered: "
                + String.Join(
                    " | ",
                    result.Diagnostics.Select(diagnostic =>
                        diagnostic.Id + ":" + diagnostic.Message
                    )
                )
                + "\n"
                + result.Source
        );
    }

    [TestMethod]
    public void ReactiveStyleConditionsAndStyleParametersDiagnoseSafely()
    {
        var nonBoolean = LuiParser.Parse(
            "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Example() { <Row style={Broken} /> } style Broken { when (1) { Width: 1f; } }"
        );
        var result = LuiCompiler.Compile(
            nonBoolean,
            CSharpCompilation.Create("non-boolean-style", references: References()),
            new LuiFreshnessIdentity(
                "non-boolean-style",
                "non-boolean-style",
                new LuiDocumentIdentity("NonBooleanStyle.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !result.Success && result.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2021"),
            "a non-bool named style condition was not rejected: "
                + String.Join(
                    " | ",
                    result.Diagnostics.Select(diagnostic =>
                        diagnostic.Id + ":" + diagnostic.Message
                    )
                )
        );

        var invalidParameters = LuiParser.Parse(
            "style Broken([DefaultContent] Style style, ref int value) { Width: 1f; }"
        );
        Assert(
            invalidParameters.Diagnostics.Count(diagnostic => diagnostic.Id == "LUI3004") == 2,
            "unsupported style parameter forms did not produce one diagnostic each: "
                + Diagnostics(invalidParameters)
        );

        var inlineNonBoolean = LuiParser.Parse(
            "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Example() { <Row style={Style.Empty with { when (1) { Width: 1f; } }} /> }"
        );
        var inlineResult = LuiCompiler.Compile(
            inlineNonBoolean,
            CSharpCompilation.Create("inline-non-boolean-style", references: References()),
            new LuiFreshnessIdentity(
                "inline-non-boolean-style",
                "inline-non-boolean-style",
                new LuiDocumentIdentity("InlineNonBooleanStyle.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !inlineResult.Success
                && inlineResult.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2021"),
            "an inline non-bool style condition was not rejected."
        );
    }

    [TestMethod]
    public void RecoveredSyntaxRetainsIndependentBindingDiagnostics()
    {
        var source = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Example() { <Text content={MissingValue} /> }
style Broken { Width: 1f
""";
        var document = LuiParser.Parse(source);
        var result = LuiCompiler.Compile(
            document,
            CSharpCompilation.Create("recovered-bindings", references: References()),
            new LuiFreshnessIdentity(
                "recovered-bindings",
                "recovered-bindings",
                new LuiDocumentIdentity("RecoveredBindings.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            document.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1015"),
            "the malformed style assignment did not retain its parser diagnostic."
        );
        Assert(
            result.Diagnostics.Any(diagnostic =>
                diagnostic.Id == "LUI2000"
                && diagnostic.Message.Contains("MissingValue", StringComparison.Ordinal)
            ),
            "an unrelated unresolved expression was masked by the recovered syntax diagnostic: "
                + String.Join(
                    " | ",
                    result.Diagnostics.Select(diagnostic =>
                        diagnostic.Id + ":" + diagnostic.Message
                    )
                )
        );
    }

    [TestMethod]
    public void ErrorTypedReactiveConditionReportsUnderlyingBindingFailure()
    {
        var source =
            "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; "
            + "internal component Example() { <Row style={Broken} /> } "
            + "style Broken { when (MissingFlag) { Width: 1f; } }";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create("error-typed-condition", references: References()),
            new LuiFreshnessIdentity(
                "error-typed-condition",
                "error-typed-condition",
                new LuiDocumentIdentity("ErrorTypedCondition.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !result.Success
                && !result.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2021")
                && result.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2000"
                    && diagnostic.Message.Contains("MissingFlag", StringComparison.Ordinal)
                ),
            "an error-typed condition was reported as non-bool instead of preserving its binding failure: "
                + String.Join(
                    " | ",
                    result.Diagnostics.Select(diagnostic =>
                        diagnostic.Id + ":" + diagnostic.Message
                    )
                )
        );
    }

    [TestMethod]
    public void CompilerDiagnostics()
    {
        var lintSource = """
namespace Sample;
using System;
using System.Collections.Generic;
using Lucent.Core;
using static Lucent.Core.Components;
internal component X(IEnumerable<string> items) {
    <Row>foreach (var item in items) keyed by Guid.NewGuid() { <Text content={item} /> }</Row>
}
style Unused { Spacing: 1f; }
""";
        var linted = LuiCompiler.Compile(
            LuiParser.Parse(lintSource),
            CSharpCompilation.Create("lints", references: References()),
            new LuiFreshnessIdentity(
                "lints",
                "lints",
                new LuiDocumentIdentity("Lints.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            linted.Success
                && linted.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI5001"
                    && diagnostic.Span.Start
                        == lintSource.IndexOf("Guid.NewGuid", StringComparison.Ordinal)
                )
                && linted.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI5002"
                    && diagnostic.Span.Start
                        == lintSource.IndexOf("Unused", StringComparison.Ordinal)
                ),
            "objective key/style lints lost their stable IDs or authored spans: "
                + string.Join(
                    " | ",
                    linted.Diagnostics.Select(diagnostic =>
                        diagnostic.Id + "@" + diagnostic.Span.Start
                    )
                )
        );
        var stableKeySource = lintSource.Replace(
            "Guid.NewGuid()",
            "FakeGuid.NewGuid()",
            StringComparison.Ordinal
        );
        var stableKey = LuiCompiler.Compile(
            LuiParser.Parse(stableKeySource),
            CSharpCompilation.Create(
                "stable-key",
                [
                    CSharpSyntaxTree.ParseText(
                        "public static class FakeGuid { public static System.Guid NewGuid() => default; }"
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "stable-key",
                "stable-key",
                new LuiDocumentIdentity("Stable.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            stableKey.Success
                && !stableKey.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI5001"),
            "semantic unstable-key lint matched an unrelated member name."
        );
    }

    [TestMethod]
    public void FluentStyleReceiverCountsAsUse()
    {
        const string source = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Styled() {
    <Row style={EditorPaneStyle.Padding(Insets.Uniform(4)).Participation(ElementParticipation.Visible)} />
}
style EditorPaneStyle { Opacity: .5f; }
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create("fluent-style-use", references: References()),
            new LuiFreshnessIdentity(
                "fluent-style-use",
                "fluent-style-use",
                new LuiDocumentIdentity("FluentStyleUse.lui"),
                "v1",
                "preview"
            )
        );

        Assert(
            result.Success
                && !result.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI5002")
                && result.Source!.Contains("__luiStyle_", StringComparison.Ordinal)
                && result.Source.Contains(".Padding(", StringComparison.Ordinal)
                && result.Source.Contains(".Participation(")
                && LuiFormatter.Format(source, LuiLineEnding.Lf)
                    == LuiFormatter.Format(
                        LuiFormatter.Format(source, LuiLineEnding.Lf),
                        LuiLineEnding.Lf
                    ),
            "a private style used as a fluent-call receiver was reported unused: "
                + string.Join(
                    " | ",
                    result.Diagnostics.Select(diagnostic =>
                        diagnostic.Id + ":" + diagnostic.Message
                    )
                )
        );

        const string shadowedSource = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Shadowed(Style EditorPaneStyle) {
    <Row style={EditorPaneStyle} />
}
style EditorPaneStyle { Opacity: .5f; }
""";
        var shadowed = LuiCompiler.Compile(
            LuiParser.Parse(shadowedSource),
            CSharpCompilation.Create("shadowed-style-use", references: References()),
            new LuiFreshnessIdentity(
                "shadowed-style-use",
                "shadowed-style-use",
                new LuiDocumentIdentity("ShadowedStyleUse.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            shadowed.Success
                && shadowed.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI5002"
                    && diagnostic.Span.Start
                        == shadowedSource.LastIndexOf("EditorPaneStyle", StringComparison.Ordinal)
                ),
            "a same-named parameter incorrectly counted as use of a private style."
        );
    }

    [TestMethod]
    public void StyleNameRewriteRespectsExpressionLocalAndPatternShadowing()
    {
        const string source = """
namespace Sample;
using System;
using Lucent.Core;
using static Lucent.Core.Components;
internal component PatternShadow(object? value) {
    Setup(owner) { Style Foo() => Style.Empty; _ = nameof(Foo); }
    <Column>
        <Text content={value is int Foo ? Foo.ToString() : nameof(Foo)} />
        <Text content={nameof(Foo)} />
        <Text content="ready" style={true ? Foo : ((Func<Style, Style>)(Foo => Foo.With(Style.Empty)))(Style.Empty)} />
        <Text content="ready" style={true ? Foo : ((Func<object?, Style>)(candidate => candidate is int Foo ? Style.Empty : Style.Empty))(value)} />
    </Column>
}
style Foo { Opacity: .5f; }
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create("pattern-style-shadowing", references: References()),
            new LuiFreshnessIdentity(
                "pattern-style-shadowing",
                "pattern-style-shadowing",
                new LuiDocumentIdentity("PatternStyleShadowing.lui"),
                "v1",
                "preview"
            )
        );

        var generatedSource = result.Source ?? "";
        Assert(
            result.Success
                && generatedSource.Contains("Foo.ToString()", StringComparison.Ordinal)
                && generatedSource.Contains("nameof(Foo)", StringComparison.Ordinal)
                && generatedSource.Contains(
                    "#line (9,24)-(9,35) \"PatternStyleShadowing.lui\"",
                    StringComparison.Ordinal
                )
                && generatedSource.Contains("\"Foo\"", StringComparison.Ordinal)
                && generatedSource.Contains("true ? __luiStyle_", StringComparison.Ordinal)
                && generatedSource.Contains(
                    "(Foo => Foo.With(Style.Empty))",
                    StringComparison.Ordinal
                )
                && generatedSource.Contains("Style Foo() => Style.Empty", StringComparison.Ordinal),
            "pattern-local or nameof references were rewritten as the generated style member:\n"
                + String.Join(
                    " | ",
                    result.Diagnostics.Select(diagnostic =>
                        diagnostic.Id + ":" + diagnostic.Message
                    )
                )
                + "\n"
                + result.Source
        );
    }

    [TestMethod]
    public void ParameterDiagnostics()
    {
        var rejectedParameters = LuiParser.Parse(
            "internal component Rejected([Obsolete] ref string value, [System.CLSCompliant(true)] params int[] values, in int state, out int output) { <A /> }"
        );
        Assert(
            rejectedParameters
                .Diagnostics.Where(diagnostic => diagnostic.Id == "LUI3004")
                .Select(diagnostic => diagnostic.Span.Start)
                .SequenceEqual(
                    rejectedParameters
                        .Diagnostics.Where(diagnostic => diagnostic.Id == "LUI3004")
                        .Select(diagnostic => diagnostic.Span.Start)
                        .OrderBy(start => start)
                )
                && rejectedParameters.Diagnostics.Count(diagnostic => diagnostic.Id == "LUI3004")
                    == 4,
            "Roslyn parameter exclusions were not stable or complete."
        );
        var declaredDefault = LuiParser.Parse(
            "internal component Wrapper([DefaultContent] ComponentContent children) { <A /> }"
        );
        Assert(
            declaredDefault.Diagnostics.Count == 0
                && declaredDefault.Component!.Parameters.Single().IsDefaultContent
                && LuiFormatter.Format(declaredDefault.Source)
                    == LuiFormatter.Format(LuiFormatter.Format(declaredDefault.Source)),
            "the explicit [DefaultContent] parameter marker was not parsed and formatted."
        );
        var duplicateDefault = LuiParser.Parse(
            "internal component Wrapper([DefaultContent] ComponentContent first, [DefaultContent] ComponentContent second) { <A /> }"
        );
        Assert(
            duplicateDefault.Diagnostics.Count(diagnostic => diagnostic.Id == "LUI3004") == 1,
            "multiple explicit [DefaultContent] parameters did not fail closed once."
        );
        var roslynNames = LuiParser.Parse(
            "internal component X() { <global::Sample.A Alias::Sample.A=\"x\" Sample.\\u00C5ngström=\"y\" @verbatim=\"z\" Á=\"q\" /> }"
        );
        Assert(
            roslynNames.Diagnostics.Count == 0
                && ((LuiElementSyntax)roslynNames.Component!.Body.Single()).Name.Text
                    == "global::Sample.A"
                && ((LuiElementSyntax)roslynNames.Component.Body.Single()).Attributes.Any(
                    attribute => attribute.Name.Text == "Sample.\\u00C5ngström"
                ),
            "Roslyn name boundaries or source text changed."
        );
        for (var i = 0; i < 64; i++)
        {
            var source =
                "internal component X() { <A P={new[] { \"}\", "
                + i
                + " }.Length}> text "
                + i
                + "</A> }";
            var parsed = LuiParser.Parse(source);
            Assert(
                parsed.Diagnostics.Count == 0
                    && LuiFormatter.Format(source)
                        == LuiFormatter.Format(LuiFormatter.Format(source)),
                "adversarial corpus failed at " + i
            );
        }
    }

    [TestMethod]
    public void LoweringSourceMapsScalarStylesAndFreshness()
    {
        var loweredSource = """
namespace Sample;
using Lucent.Core;
using static Lucent.Core.Components;
internal component Widget(Style? style = null) {
    <Row name="root" style={style}><Text>Hello</Text></Row>
}
""";
        var loweredDocument = LuiParser.Parse(loweredSource);
        var loweredCompilation = CSharpCompilation.Create(
            "lowered",
            [
                CSharpSyntaxTree.ParseText(
                    "internal class C {}",
                    new CSharpParseOptions(LanguageVersion.Preview)
                ),
            ],
            References()
        );
        var lowered = LuiCompiler.Compile(
            loweredDocument,
            loweredCompilation,
            new LuiFreshnessIdentity(
                "42",
                "lowered",
                new LuiDocumentIdentity("Widget.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            lowered.Success
                && lowered.Source!.Contains(".Named(\"root\")")
                && lowered.Source.Contains("global::Lucent.Core.Components.Row(style:")
                && lowered.Source.Contains(
                    "content: [global::Lucent.Core.Components.Text(content: \"Hello\")]"
                )
                && lowered.Source.Contains("#line"),
            "recipe lowering failed: "
                + string.Join(" | ", lowered.Diagnostics.Select(diagnostic => diagnostic.Message))
        );
        Assert(
            lowered
                .Map.FromSource(
                    new LuiSpan(loweredSource.IndexOf("Hello", StringComparison.Ordinal), 5)
                )
                .Count > 0
                && lowered
                    .Map.FromGenerated(
                        lowered
                            .Map.Entries.First(entry => entry.Kind == LuiMapKind.Expression)
                            .Generated
                    )
                    .Count > 0
                && lowered.Map.Identity.MapIdentity == lowered.Identity.MapIdentity,
            "source map or freshness identity was incomplete."
        );
        var loweredText = lowered.Source!;
        var namedAttribute = (
            (LuiElementSyntax)loweredDocument.Component!.Body.Single()
        ).Attributes.Single(attribute => attribute.Name.Text == "name");
        var namedMap = lowered
            .Map.FromSource(namedAttribute.Name.Span)
            .Single(entry =>
                loweredText.Substring(entry.Generated.Start, entry.Generated.Length) == "Named"
            );
        Assert(
            namedMap.Source.Start == namedAttribute.Name.Span.Start
                && namedMap.Source.Length == namedAttribute.Name.Span.Length
                && lowered
                    .Map.FromGenerated(namedMap.Generated)
                    .Any(entry =>
                        entry.Source.Start == namedAttribute.Name.Span.Start
                        && entry.Source.Length == namedAttribute.Name.Span.Length
                    )
                && lowered.Map.Entries.Any(entry =>
                    entry.Hidden
                    && (
                        loweredText.Substring(entry.Generated.Start, entry.Generated.Length) == "."
                        || loweredText.Substring(entry.Generated.Start, entry.Generated.Length)
                            == "("
                    )
                ),
            "Named source mapping included generated punctuation or did not round-trip."
        );
        var matrixSource = """
namespace Sample;
using System.Collections.Generic;
using Lucent.Core;
using static Lucent.Core.Components;
using static Lucent.Core.LayoutProperties;
using static Lucent.Core.VisualProperties;
public component Matrix(bool show, float spacing, IEnumerable<string> items, Style? style = null) {
    <Column name="matrix" style={MatrixStyle with { Spacing: spacing; }}>
        <Text>Start</Text>
        if (show) { <Text>Visible</Text> } else { <Text>Hidden</Text> }
        foreach (var item in items) keyed by item { <Text content={item} /> }
    </Column>
}
style MatrixStyle { Spacing: 2f; when Hover { Opacity: .5f; } }
""";
        var matrix = LuiCompiler.Compile(
            LuiParser.Parse(matrixSource),
            CSharpCompilation.Create(
                "matrix",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "43",
                "matrix",
                new LuiDocumentIdentity("Matrix.lui"),
                "v2",
                "preview"
            )
        );
        Assert(
            matrix.Success
                && matrix.Source!.Contains(
                    "private static readonly global::Lucent.Core.Style __luiStyle_"
                )
                && matrix.Source.Contains(
                    "global::Lucent.Core.Style.Empty.Set(global::Lucent.Core.LayoutProperties.Spacing,"
                )
                && matrix.Source.Contains("ContentRecipe.Switch")
                && matrix.Source.Contains("ContentRecipe.ForEach")
                && matrix.Source.Contains("ContentRecipe.Switch(\"if-0\"")
                && matrix.Source.Contains("ContentRecipe.ForEach(\"foreach-1\"")
                && matrix.Source.Contains(".When(global::Lucent.Core.VariantState.Hover")
                && matrix.Source.Contains(
                    ".Bind<float>(global::Lucent.Core.LayoutProperties.Spacing, () =>"
                ),
            "structural/style matrix did not lower static/live values: "
                + string.Join(" | ", matrix.Diagnostics.Select(diagnostic => diagnostic.Message))
                + "\n"
                + matrix.Source
        );
        var condition = new LuiSpan(
            matrixSource.IndexOf("if (show", StringComparison.Ordinal) + 4,
            4
        );
        var mappedTree = CSharpSyntaxTree.ParseText(
            matrix.Source!,
            new CSharpParseOptions(LanguageVersion.Preview),
            "Matrix.g.cs"
        );
        var mappedExpression = matrix
            .Map.FromSource(condition)
            .First(entry => entry.Kind == LuiMapKind.Expression && !entry.Hidden);
        Assert(
            mappedTree
                .GetMappedLineSpan(
                    new Microsoft.CodeAnalysis.Text.TextSpan(
                        mappedExpression.Generated.Start,
                        mappedExpression.Generated.Length
                    )
                )
                .Path == "Matrix.lui",
            "enhanced #line did not map a generated expression to its logical .lui path."
        );
        Assert(
            matrix
                .Map.FromSource(condition)
                .Any(entry => entry.Kind == LuiMapKind.Expression && !entry.Hidden)
                && matrix
                    .Map.FromSource(condition)
                    .All(entry => matrix.Map.FromGenerated(entry.Generated).Any()),
            "conditional source map did not preserve exact round trips."
        );
        var matrixLoop = ((LuiElementSyntax)LuiParser.Parse(matrixSource).Component!.Body.Single())
            .Children.OfType<LuiForEachSyntax>()
            .Single();
        var foreachMaps = matrix
            .Map.FromSource(matrixLoop.Variable.Span)
            .Where(entry => !entry.Hidden)
            .ToArray();
        Assert(
            foreachMaps.Length == 2
                && foreachMaps.All(entry =>
                    entry.Kind == LuiMapKind.Local
                    && matrix.Source!.Substring(entry.Generated.Start, entry.Generated.Length)
                        == matrixLoop.Variable.Text
                    && matrix
                        .Map.FromGenerated(entry.Generated)
                        .Any(candidate =>
                            candidate.Source.Start == matrixLoop.Variable.Span.Start
                            && candidate.Source.Length == matrixLoop.Variable.Span.Length
                        )
                ),
            "foreach variable mapping included generated lambda punctuation or did not round-trip."
        );
        var deterministic = LuiCompiler.Compile(
            LuiParser.Parse(matrixSource),
            CSharpCompilation.Create(
                "matrix",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "43",
                "matrix",
                new LuiDocumentIdentity("Matrix.lui"),
                "v2",
                "preview"
            )
        );
        Assert(
            matrix.Source == deterministic.Source
                && string.Join(
                    "|",
                    matrix.Map.Entries.Select(entry =>
                        entry.Source.Start
                        + ":"
                        + entry.Generated.Start
                        + ":"
                        + entry.Kind
                        + ":"
                        + entry.Hidden
                    )
                )
                    == string.Join(
                        "|",
                        deterministic.Map.Entries.Select(entry =>
                            entry.Source.Start
                            + ":"
                            + entry.Generated.Start
                            + ":"
                            + entry.Kind
                            + ":"
                            + entry.Hidden
                        )
                    ),
            "generated source or map was nondeterministic."
        );
        var invalidAttributes = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Bad() { <Row Name=\"wrong\" /> }"
            ),
            CSharpCompilation.Create(
                "bad",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "44",
                "bad",
                new LuiDocumentIdentity("Bad.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !invalidAttributes.Success
                && invalidAttributes.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2000"),
            "camel-case attributes were not bound through C# semantics."
        );
        var customApi = """
namespace Sample;
using Lucent.Core;
public static class Custom
{
    [LucentComponent] public static ComponentRecipe Group([DefaultContent] ComponentContent children) => null!;
    [LucentComponent] public static ComponentRecipe Label([DefaultContent] string value) => null!;
    [LucentComponent] public static ComponentRecipe Plain(int count = 7) => null!;
    [LucentComponent] public static ComponentRecipe Magic(ComponentContent content) => null!;
    [LucentComponent] public static ComponentRecipe Escaped([DefaultContent] ComponentContent @event) => null!;
    [LucentComponent] public static ComponentRecipe Choice([DefaultContent] string value) => null!;
    [LucentComponent] public static ComponentRecipe Choice([DefaultContent] ComponentContent values) => null!;
    [LucentComponent] public static ComponentRecipe Differing([DefaultContent] string label) => null!;
    [LucentComponent] public static ComponentRecipe Differing([DefaultContent] ComponentContent children) => null!;
    [LucentComponent] public static ComponentRecipe Number([DefaultContent] long value) => null!;
    [LucentComponent] public static ComponentRecipe Reader([DefaultContent] global::System.Func<string> read) => null!;
    [LucentComponent] public static ComponentRecipe Ambiguous([DefaultContent] string value) => null!;
    [LucentComponent] public static ComponentRecipe Ambiguous([DefaultContent] global::System.Uri value) => null!;
    public static ComponentRecipe Unannotated() => null!;
    public static ComponentRecipe One(string value) => null!;
    public static ContentRecipe Contribution(string value) => One(value);
}
public sealed class Eligibility
{
    [LucentComponent] public ComponentRecipe Instance() => null!;
}
""";
        var unknownWithContent = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; internal component Test() { <Missing><Text>child</Text></Missing> }"
            ),
            CSharpCompilation.Create(
                "unknown-with-content",
                references: References(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            ),
            new LuiFreshnessIdentity(
                "45",
                "unknown-with-content",
                new LuiDocumentIdentity("UnknownWithContent.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !unknownWithContent.Success
                && unknownWithContent.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2001")
                && !unknownWithContent.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2011"),
            "an unresolved tag with children bypassed the existing LUI2001 diagnostic."
        );
        var customCompilation = CSharpCompilation.Create(
            "custom",
            [
                CSharpSyntaxTree.ParseText(
                    customApi,
                    new CSharpParseOptions(LanguageVersion.Preview)
                ),
            ],
            References()
        );
        var genericContent = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Group><Label>ok</Label></Group> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("Custom.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            genericContent.Success
                && genericContent.Source!.Contains(
                    "global::Sample.Custom.Group(children: [global::Sample.Custom.Label(value: \"ok\")])"
                ),
            "[DefaultContent] metadata did not determine scalar/collection parameter names."
        );
        var genericEmpty = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Group /> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("Empty.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            genericEmpty.Success && genericEmpty.Source!.Contains("Group(children: [])"),
            "empty ComponentContent did not lower through metadata."
        );
        var invalidContent = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Plain>bad</Plain> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("InvalidContent.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !invalidContent.Success
                && invalidContent.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2000"
                    || diagnostic.Id == "LUI2004"
                    || diagnostic.Id == "LUI2011"
                ),
            "content without [DefaultContent] was accepted."
        );
        var magicContentName = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Magic><Label>bad</Label></Magic> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("MagicContentName.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !magicContentName.Success
                && magicContentName.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2011"
                    && diagnostic.Message.Contains("[DefaultContent]", StringComparison.Ordinal)
                ),
            "the unannotated content parameter name remained an author-facing fallback."
        );
        var ambiguousContent = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Choice /> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("Ambiguous.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !ambiguousContent.Success
                && ambiguousContent.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2000"),
            "ambiguous [DefaultContent] overload was accepted."
        );
        var scalarOverload = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Differing>text</Differing> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("ScalarOverload.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            scalarOverload.Success && scalarOverload.Source!.Contains("Differing(label: \"text\")"),
            "scalar [DefaultContent] overload did not bind through its actual parameter."
        );
        var scalarExpressionSource =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(string value) { <Label>\n {/* ignored */}\n { value }\n</Label> }";
        var scalarExpressionDocument = LuiParser.Parse(scalarExpressionSource);
        var scalarExpressionElement = (LuiElementSyntax)
            scalarExpressionDocument.Component!.Body.Single();
        var scalarExpressionChildren = scalarExpressionElement.Children;
        var scalarExpressionNode = scalarExpressionChildren
            .OfType<LuiExpressionBodySyntax>()
            .Single();
        var scalarExpressionOffset =
            scalarExpressionSource.IndexOf("{ value", StringComparison.Ordinal) + 2;
        Assert(
            scalarExpressionDocument.Diagnostics.Count == 0
                && scalarExpressionChildren.Count == 2
                && scalarExpressionChildren.OfType<LuiCommentSyntax>().Count() == 1
                && scalarExpressionNode.Span.Start == scalarExpressionOffset
                && scalarExpressionNode.Span.Length == "value".Length
                && scalarExpressionNode.OpenBrace.Span.Start == scalarExpressionOffset - 2
                && scalarExpressionNode.CloseBrace.Span.Start
                    == scalarExpressionSource.IndexOf('}', scalarExpressionOffset),
            "body expression parsing did not preserve the scalar island and comment spans: diagnostics="
                + Diagnostics(scalarExpressionDocument)
                + " count="
                + scalarExpressionChildren.Count
                + " span="
                + scalarExpressionNode.Span.Start
                + "/"
                + scalarExpressionNode.Span.Length
                + " expected="
                + scalarExpressionOffset
        );
        var scalarExpressionFormatted = LuiFormatter.Format(
            scalarExpressionSource,
            LuiLineEnding.Lf
        );
        var expressionOnlyFormatted = LuiFormatter.Format(
            "internal component Test(string value) { <Label>{ value }</Label> }",
            LuiLineEnding.Lf
        );
        Assert(
            scalarExpressionFormatted.Contains(
                "{/* ignored */}\n        {value}",
                StringComparison.Ordinal
            )
                && scalarExpressionFormatted
                    == LuiFormatter.Format(scalarExpressionFormatted, LuiLineEnding.Lf)
                && expressionOnlyFormatted.Contains(
                    "<Label>{value}</Label>",
                    StringComparison.Ordinal
                )
                && expressionOnlyFormatted
                    == LuiFormatter.Format(expressionOnlyFormatted, LuiLineEnding.Lf),
            "body expression formatting was not stable for expression-only or comment-adjacent content: adjacent="
                + scalarExpressionFormatted.Replace("\n", "\\n", StringComparison.Ordinal)
                + " only="
                + expressionOnlyFormatted.Replace("\n", "\\n", StringComparison.Ordinal)
        );
        var scalarExpression = LuiCompiler.Compile(
            scalarExpressionDocument,
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("ScalarExpression.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            scalarExpression.Success
                && scalarExpression.Source!.Contains("Label(value:", StringComparison.Ordinal)
                && scalarExpression
                    .Map.FromSource(new LuiSpan(scalarExpressionOffset, "value".Length))
                    .Any(entry => entry.Kind == LuiMapKind.Expression)
                && scalarExpression
                    .Map.FromSource(new LuiSpan(scalarExpressionOffset - 2, 1))
                    .Any(entry => entry.Kind == LuiMapKind.Structure)
                && scalarExpression
                    .Map.FromSource(
                        new LuiSpan(scalarExpressionSource.IndexOf('}', scalarExpressionOffset), 1)
                    )
                    .Any(entry => entry.Kind == LuiMapKind.Structure),
            "scalar body expression did not lower or preserve source-map spans: diagnostics="
                + string.Join(
                    " | ",
                    scalarExpression.Diagnostics.Select(d =>
                        d.Id + ":" + d.Message + "@" + d.Span.Start + "/" + d.Span.Length
                    )
                )
                + " source="
                + scalarExpression.Source
        );
        var mixedExpressionSource =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(string value) { <Label>prefix {value} suffix</Label> }";
        var mixedExpressionDocument = LuiParser.Parse(mixedExpressionSource);
        var mixedExpressionElement = (LuiElementSyntax)
            mixedExpressionDocument.Component!.Body.Single();
        var mixedExpression = LuiCompiler.Compile(
            mixedExpressionDocument,
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("MixedExpression.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            mixedExpressionDocument.Diagnostics.Count == 0
                && mixedExpressionElement.Children.Count == 3
                && !mixedExpression.Success
                && mixedExpression.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2004"),
            "mixed literal/expression content was accepted or not split: children="
                + mixedExpressionElement.Children.Count
                + " diagnostics="
                + string.Join(
                    " | ",
                    mixedExpression.Diagnostics.Select(d =>
                        d.Id + ":" + d.Message + "@" + d.Span.Start + "/" + d.Span.Length
                    )
                )
        );
        var multipleExpressionSource =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(string value) { <Label>{value}{value}</Label> }";
        var multipleExpressionDocument = LuiParser.Parse(multipleExpressionSource);
        var multipleExpression = LuiCompiler.Compile(
            multipleExpressionDocument,
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("MultipleExpression.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            multipleExpressionDocument.Diagnostics.Count == 0
                && ((LuiElementSyntax)multipleExpressionDocument.Component!.Body.Single())
                    .Children.OfType<LuiExpressionBodySyntax>()
                    .Count() == 2
                && !multipleExpression.Success
                && multipleExpression.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2004"),
            "multiple scalar body expressions were accepted."
        );
        foreach (var mixedBody in new[] { "prefix {value}", "{value} suffix" })
        {
            var mixedEdgeSource =
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(string value) { <Label>"
                + mixedBody
                + "</Label> }";
            var mixedEdgeDocument = LuiParser.Parse(mixedEdgeSource);
            var mixedEdgeResult = LuiCompiler.Compile(
                mixedEdgeDocument,
                customCompilation,
                new LuiFreshnessIdentity(
                    "45",
                    "custom",
                    new LuiDocumentIdentity("MixedEdgeExpression.lui"),
                    "v1",
                    "preview"
                )
            );
            var mixedEdgeFormatted = LuiFormatter.Format(mixedEdgeSource, LuiLineEnding.Lf);
            Assert(
                mixedEdgeDocument.Diagnostics.Count == 0
                    && ((LuiElementSyntax)mixedEdgeDocument.Component!.Body.Single()).Children.Count
                        == 2
                    && !mixedEdgeResult.Success
                    && mixedEdgeResult.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2004")
                    && mixedEdgeFormatted
                        == LuiFormatter.Format(mixedEdgeFormatted, LuiLineEnding.Lf),
                "leading or trailing mixed expression content was accepted or formatted unstably: "
                    + mixedBody
            );
        }
        foreach (
            var structuralBody in new[]
            {
                "<Label>nested</Label>{value}",
                "if (true) { <Label>nested</Label> }{value}",
                "foreach (var item in new[] { value }) keyed by item { <Label>nested</Label> }{value}",
            }
        )
        {
            var structuralExpressionSource =
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(string value) { <Label>"
                + structuralBody
                + "</Label> }";
            var structuralExpressionDocument = LuiParser.Parse(structuralExpressionSource);
            var structuralExpression = LuiCompiler.Compile(
                structuralExpressionDocument,
                customCompilation,
                new LuiFreshnessIdentity(
                    "45",
                    "custom",
                    new LuiDocumentIdentity("StructuralExpression.lui"),
                    "v1",
                    "preview"
                )
            );
            Assert(
                structuralExpressionDocument.Diagnostics.Count == 0
                    && !structuralExpression.Success
                    && structuralExpression.Diagnostics.Any(diagnostic =>
                        diagnostic.Id == "LUI2004"
                    ),
                "a structural sibling was accepted beside a scalar body expression: "
                    + structuralBody
            );
        }
        var incompleteBodyExpression = LuiParser.Parse(
            "internal component Test(string value) { <Label>{value"
        );
        Assert(
            incompleteBodyExpression.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI1013")
                && incompleteBodyExpression
                    .Component!.Body.OfType<LuiElementSyntax>()
                    .Single()
                    .Children.OfType<LuiExpressionBodySyntax>()
                    .Single()
                    .CloseBrace.IsMissing,
            "an unterminated body expression did not retain a missing close-brace recovery token."
        );
        var collectionExpressionSource =
            """namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test([DefaultContent] ComponentContent children) { <Group><Label>header</Label>{children}{One("one")}{Contribution("two")}<Label>footer</Label></Group> }""";
        var collectionExpression = LuiCompiler.Compile(
            LuiParser.Parse(collectionExpressionSource),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("CollectionExpression.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            collectionExpression.Success
                && CSharpSyntaxTree
                    .ParseText(
                        collectionExpression.Source!,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    )
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<CollectionExpressionSyntax>()
                    .Single()
                    .Elements.OfType<SpreadElementSyntax>()
                    .Single()
                    .Expression.ToString() == "children"
                && collectionExpression
                    .Map.FromSource(
                        new LuiSpan(
                            collectionExpressionSource.IndexOf(
                                "children}{",
                                StringComparison.Ordinal
                            ),
                            "children".Length
                        )
                    )
                    .Any(entry => !entry.Hidden && entry.Kind == LuiMapKind.Expression),
            "typed ComponentContent/ComponentRecipe/ContentRecipe expressions did not splice in authored order:\n"
                + string.Join(
                    " | ",
                    collectionExpression.Diagnostics.Select(item => item.Id + ":" + item.Message)
                )
                + "\n"
                + collectionExpression.ProjectionSource
        );
        var explicitCollectionAttribute = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(ComponentContent children) { <Group children={children} /> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("ExplicitCollectionAttribute.lui"),
                "v1",
                "preview"
            )
        );
        var explicitCollectionInvocation = CSharpSyntaxTree
            .ParseText(
                explicitCollectionAttribute.Source!,
                new CSharpParseOptions(LanguageVersion.Preview)
            )
            .GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation =>
                invocation.Expression.ToString().EndsWith(".Group", StringComparison.Ordinal)
            );
        Assert(
            explicitCollectionAttribute.Success
                && explicitCollectionInvocation.ArgumentList.Arguments.Count == 1
                && explicitCollectionInvocation
                    .ArgumentList.Arguments.Single()
                    .NameColon!.Name.Identifier.ValueText == "children"
                && explicitCollectionInvocation.ArgumentList.Arguments.Single().Expression
                    is IdentifierNameSyntax,
            "an explicit default-content attribute also received an invented empty collection."
        );
        var escapedDefault = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Escaped><Label>ok</Label></Escaped> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("EscapedDefault.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            escapedDefault.Success
                && escapedDefault.Source!.Contains("@event:", StringComparison.Ordinal),
            "an escaped [DefaultContent] parameter did not produce a valid implicit named argument."
        );
        foreach (
            var incompatibleExpression in new[]
            {
                "\"bad\"",
                "null",
                "System.Array.Empty<ContentRecipe>()",
            }
        )
        {
            var incompatibleContribution = LuiCompiler.Compile(
                LuiParser.Parse(
                    "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Group><Label>header</Label>{"
                        + incompatibleExpression
                        + "}</Group> }"
                ),
                customCompilation,
                new LuiFreshnessIdentity(
                    "45",
                    "custom",
                    new LuiDocumentIdentity("IncompatibleContribution.lui"),
                    incompatibleExpression,
                    "preview"
                )
            );
            Assert(
                !incompatibleContribution.Success
                    && incompatibleContribution.Diagnostics.Any(diagnostic =>
                        diagnostic.Id == "LUI3001"
                        && diagnostic.Message.Contains("ComponentContent", StringComparison.Ordinal)
                    ),
                "an incompatible component-content expression did not fail with a typed diagnostic: "
                    + incompatibleExpression
                    + " "
                    + string.Join(
                        " | ",
                        incompatibleContribution.Diagnostics.Select(item =>
                            item.Id + ":" + item.Message
                        )
                    )
            );
        }
        var duplicateDefaultSource =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(string value) { <Differing label=\"explicit\">{value}</Differing> }";
        var duplicateDefault = LuiCompiler.Compile(
            LuiParser.Parse(duplicateDefaultSource),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("DuplicateDefault.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !duplicateDefault.Success
                && duplicateDefault.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2008"
                    && diagnostic.Span.Start
                        == duplicateDefaultSource.IndexOf("label", StringComparison.Ordinal)
                ),
            "an explicit renamed default-content parameter was not rejected deterministically."
        );
        var nullableExpressionSource =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(string? value) { <Label>{value}</Label> }";
        var nullableCompilation = customCompilation.WithOptions(
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
        var nullableExpression = LuiCompiler.Compile(
            LuiParser.Parse(nullableExpressionSource),
            nullableCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("NullableExpression.lui"),
                "v1",
                "preview"
            )
        );
        var nullableExpressionPosition = nullableExpressionSource.LastIndexOf(
            "value",
            StringComparison.Ordinal
        );
        Assert(
            nullableExpression.Success
                && nullableExpression.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2000"
                    && diagnostic.Severity == DiagnosticSeverity.Warning
                    && diagnostic.Span.Equals(
                        new LuiSpan(nullableExpressionPosition, "value".Length)
                    )
                ),
            "nullable warning behavior changed for scalar body expressions: success="
                + nullableExpression.Success
                + " diagnostics="
                + string.Join(
                    " | ",
                    nullableExpression.Diagnostics.Select(d =>
                        d.Id
                        + ":"
                        + d.Severity
                        + ":"
                        + d.Message
                        + "@"
                        + d.Span.Start
                        + "/"
                        + d.Span.Length
                    )
                )
        );
        var conversionExpressionSource =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(int value) { <Number>{value}</Number> }";
        var conversionExpression = LuiCompiler.Compile(
            LuiParser.Parse(conversionExpressionSource),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("ConversionExpression.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            conversionExpression.Success
                && conversionExpression.Source!.Contains("Number(value:", StringComparison.Ordinal)
                && conversionExpression.Source.Contains(
                    "\nvalue\n#line hidden",
                    StringComparison.Ordinal
                ),
            "ordinary numeric conversion did not lower through the scalar body-expression call path."
        );
        var hardConversionSource =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(object value) { <Number>{value}</Number> }";
        var hardConversion = LuiCompiler.Compile(
            LuiParser.Parse(hardConversionSource),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("HardConversion.lui"),
                "v1",
                "preview"
            )
        );
        var hardConversionPosition = hardConversionSource.LastIndexOf(
            "value",
            StringComparison.Ordinal
        );
        Assert(
            !hardConversion.Success
                && hardConversion.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2000"
                    && diagnostic.Span.Equals(new LuiSpan(hardConversionPosition, "value".Length))
                    && diagnostic.Message.Contains(
                        "cannot convert",
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
            "a hard scalar conversion error did not retain the ordinary Roslyn diagnostic and exact span."
        );
        var delegateExpressionSource =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test(string value) { <Reader>{() => value}</Reader> }";
        var delegateExpression = LuiCompiler.Compile(
            LuiParser.Parse(delegateExpressionSource),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("DelegateExpression.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            delegateExpression.Success
                && delegateExpression.Source!.Contains("Reader(read:", StringComparison.Ordinal)
                && delegateExpression.Source.Contains("() => value", StringComparison.Ordinal),
            "an explicit delegate body expression did not preserve its authored live-reader semantics."
        );
        var implicitDelegateSource = delegateExpressionSource.Replace(
            "() => value",
            "value",
            StringComparison.Ordinal
        );
        var implicitDelegate = LuiCompiler.Compile(
            LuiParser.Parse(implicitDelegateSource),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("ImplicitDelegate.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            implicitDelegate.Success,
            "A compatible scalar expression should become a live reader: "
                + string.Join(" | ", implicitDelegate.Diagnostics.Select(d => d.Message))
        );
        var ambiguousExpressionSource =
            "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Ambiguous>{null}</Ambiguous> }";
        var ambiguousExpression = LuiCompiler.Compile(
            LuiParser.Parse(ambiguousExpressionSource),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("AmbiguousExpression.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !ambiguousExpression.Success
                && ambiguousExpression.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2009"
                    && diagnostic.Span.Equals(
                        new LuiSpan(
                            ambiguousExpressionSource.IndexOf("null", StringComparison.Ordinal),
                            "null".Length
                        )
                    )
                ),
            "ambiguous scalar default-content targets did not fail at the body expression: success="
                + ambiguousExpression.Success
                + " diagnostics="
                + string.Join(
                    " | ",
                    ambiguousExpression.Diagnostics.Select(d =>
                        d.Id + ":" + d.Message + "@" + d.Span.Start + "/" + d.Span.Length
                    )
                )
        );
        var collectionOverload = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Differing><Label>text</Label></Differing> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("CollectionOverload.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            collectionOverload.Success
                && collectionOverload.Source!.Contains(
                    "global::Sample.Custom.Differing(children: [global::Sample.Custom.Label(value: \"text\")])"
                ),
            "ComponentContent [DefaultContent] overload did not bind through its actual parameter:\n"
                + string.Join(" | ", collectionOverload.Diagnostics.Select(item => item.Message))
                + "\n"
                + collectionOverload.Source
        );
        var unannotatedNamed = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Custom; internal component Test() { <Unannotated name=\"x\" /> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("UnannotatedNamed.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !unannotatedNamed.Success
                && unannotatedNamed.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2001"),
            "an unannotated tag with .Named was accepted."
        );
        var instanceComponent = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; internal component Test() { <Eligibility.Instance /> }"
            ),
            customCompilation,
            new LuiFreshnessIdentity(
                "45",
                "custom",
                new LuiDocumentIdentity("InstanceComponent.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !instanceComponent.Success
                && instanceComponent.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2001"),
            "an instance [LucentComponent] method was accepted."
        );
        var inlineSource =
            "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; using static Lucent.Core.LayoutProperties; using static Lucent.Core.VisualProperties; internal component Inline(Style baseStyle, Signal<float> signal) { <Text style={baseStyle with { Width: 800f; Spacing: signal.Value; when Hover { Opacity: signal.Value; } }}>live</Text> }";
        var inline = LuiCompiler.Compile(
            LuiParser.Parse(inlineSource),
            CSharpCompilation.Create(
                "inline",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "46",
                "inline",
                new LuiDocumentIdentity("Inline.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            inline.Success
                && inline.Source!.Contains(
                    ".Bind<float?>(global::Lucent.Core.LayoutProperties.Width, () =>"
                )
                && inline.Source.Contains(
                    ".Bind<float>(global::Lucent.Core.LayoutProperties.Spacing, () =>"
                )
                && inline.Source.Contains(
                    ".When(global::Lucent.Core.VariantState.Hover, global::Lucent.Core.Style.Empty.Bind<float>(global::Lucent.Core.VisualProperties.Opacity, () =>"
                ),
            "inline assignments or live variants did not lower through public bindings."
        );
        var styleTailSource =
            "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; using static Lucent.Core.LayoutProperties; internal component Styled(Style? style = null) { <Row style={Base with style} /> } style Base { Spacing: 1f; }";
        var styleTailDocument = LuiParser.Parse(styleTailSource);
        var styleTail = LuiCompiler.Compile(
            styleTailDocument,
            CSharpCompilation.Create(
                "style-tail",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "46",
                "style-tail",
                new LuiDocumentIdentity("StyleTail.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            styleTail.Success
                && (
                    (LuiStyleWithSyntax)
                        ((LuiElementSyntax)styleTailDocument.Component!.Body.Single())
                            .Attributes.Single()
                            .Value
                )
                    .Tail
                    ?.Text == "style"
                && styleTail.Source!.Contains(
                    "Style.Empty.With(__luiStyle_",
                    StringComparison.Ordinal
                )
                && LuiFormatter
                    .Format(styleTailSource)
                    .Contains("style={Base with style}", StringComparison.Ordinal),
            "named/nullable style composition was not parsed, lowered, or formatted."
        );
        var customPropertyApi =
            "namespace Sample; using Lucent.Core; public static class Props { public static readonly Property<int> Custom = new(\"custom\", 0); public static readonly Property<string?> Optional = new(\"optional\", null); }";
        var customProperty = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; using static Sample.Props; internal component CustomStyle(int value, string? optional) { <Text style={Style.Empty with { Custom: value; Optional: optional; }}>ok</Text> }"
            ),
            CSharpCompilation.Create(
                "custom-property",
                [
                    CSharpSyntaxTree.ParseText(
                        customPropertyApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "47",
                "custom-property",
                new LuiDocumentIdentity("CustomStyle.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            customProperty.Success
                && customProperty.Source!.Contains(".Bind<int>(global::Sample.Props.Custom, () =>")
                && customProperty.Source.Contains(
                    ".Bind<string?>(global::Sample.Props.Optional, () =>"
                ),
            "custom Property<T> did not lower through public Style.Bind."
        );
        var implicitTokensApi =
            "namespace App; using Lucent.Core; internal static class Tokens { internal static readonly Token<float> DensitySpacing = new(\"density-spacing\", 8f); internal static readonly Token<float> @class = new(\"class-spacing\", 6f); internal static Token<float> @event { get; } = new(\"event-font-size\", 12f); internal static readonly Property<float> Rogue = new(\"rogue\", 0f); internal static readonly string Label = \"not-style\"; internal static readonly float NonTokenSpacing = 6f; }";
        var implicitTokensSource =
            "namespace App.Views; using Lucent.Core; internal component ImplicitTokens() { <Row style={Style.Empty with { Spacing: DensitySpacing; }} /> }";
        var implicitTokensDocument = LuiParser.Parse(implicitTokensSource);
        var implicitTokens = LuiCompiler.Compile(
            implicitTokensDocument,
            CSharpCompilation.Create(
                "implicit-tokens",
                [
                    CSharpSyntaxTree.ParseText(
                        implicitTokensApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "implicit-tokens",
                new LuiDocumentIdentity("ImplicitTokens.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        var tokenStart = implicitTokensSource.IndexOf("DensitySpacing", StringComparison.Ordinal);
        Assert(
            implicitTokens.Success
                && implicitTokens.Source!.Contains("global::App.Tokens.DensitySpacing")
                && !implicitTokens.Source.Contains("using static global::App.Tokens")
                && implicitTokens
                    .Map.FromSource(new LuiSpan(tokenStart, "DensitySpacing".Length))
                    .Any(entry =>
                        entry.Kind == LuiMapKind.Symbol
                        && implicitTokens.Source.Substring(
                            entry.Generated.Start,
                            entry.Generated.Length
                        ) == "global::App.Tokens.DensitySpacing"
                    ),
            "root Tokens were not confined to style binding and lowered as an exact symbol: "
                + string.Join(
                    " | ",
                    implicitTokens.Diagnostics.Select(diagnostic => diagnostic.Message)
                )
                + "\n"
                + implicitTokens.Source
        );
        var escapedTokens = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace App.Views; using Lucent.Core; internal component EscapedTokens() { <Row style={S} /> } style S { Spacing: @class; FontSize: @event; }"
            ),
            CSharpCompilation.Create(
                "escaped-tokens",
                [
                    CSharpSyntaxTree.ParseText(
                        implicitTokensApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "escaped-tokens",
                new LuiDocumentIdentity("EscapedTokens.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            escapedTokens.Success
                && escapedTokens.Source!.Contains("global::App.Tokens.@class")
                && escapedTokens.Source.Contains("global::App.Tokens.@event"),
            "escaped root token field/property identifiers did not lower as valid C#: "
                + string.Join(
                    " | ",
                    escapedTokens.Diagnostics.Select(diagnostic => diagnostic.Message)
                )
        );
        var parenthesizedToken = LuiCompiler.Compile(
            LuiParser.Parse(implicitTokensSource.Replace("DensitySpacing;", "(DensitySpacing);")),
            CSharpCompilation.Create(
                "parenthesized-token",
                [
                    CSharpSyntaxTree.ParseText(
                        implicitTokensApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "parenthesized-token",
                new LuiDocumentIdentity("ParenthesizedToken.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            parenthesizedToken.Success
                && parenthesizedToken.Source!.Contains(
                    ".BindValue<float>(global::Lucent.Core.LayoutProperties.Spacing,"
                )
                && parenthesizedToken.Source.Contains("(global::App.Tokens.DensitySpacing)")
                && !parenthesizedToken.Source.Contains(
                    ".Bind<float>(global::Lucent.Core.LayoutProperties.Spacing"
                ),
            "parenthesized root Token<T> did not retain live token semantics: "
                + string.Join(
                    " | ",
                    parenthesizedToken.Diagnostics.Select(diagnostic => diagnostic.Message)
                )
        );
        var propertyLeak = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace App.Views; using Lucent.Core; using static Lucent.Core.Components; internal component Leak() { <Row style={S} /> } style S { Rogue: 1; }"
            ),
            CSharpCompilation.Create(
                "property-leak",
                [
                    CSharpSyntaxTree.ParseText(
                        implicitTokensApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "property-leak",
                new LuiDocumentIdentity("PropertyLeak.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            !propertyLeak.Success
                && propertyLeak.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2000"),
            "root Tokens leaked a style property name into the implicit scope."
        );
        var ambiguousTokenApi =
            "namespace Other; using Lucent.Core; internal static class Tokens { internal static readonly Token<float> DensitySpacing = new(\"other-spacing\", 4f); }";
        var ambiguousToken = LuiCompiler.Compile(
            LuiParser.Parse("using static Other.Tokens; " + implicitTokensSource),
            CSharpCompilation.Create(
                "ambiguous-token",
                [
                    CSharpSyntaxTree.ParseText(
                        implicitTokensApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                    CSharpSyntaxTree.ParseText(
                        ambiguousTokenApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "ambiguous-token",
                new LuiDocumentIdentity("AmbiguousToken.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            !ambiguousToken.Success
                && ambiguousToken.Diagnostics.Any(diagnostic =>
                    diagnostic.Id == "LUI2000"
                    && diagnostic.Message.Contains("ambigu", StringComparison.OrdinalIgnoreCase)
                ),
            "ambiguous root and authored token imports did not fail closed: "
                + string.Join(
                    " | ",
                    ambiguousToken.Diagnostics.Select(diagnostic => diagnostic.Message)
                )
        );
        var namespaceTokenWins = LuiCompiler.Compile(
            LuiParser.Parse(
                implicitTokensSource.Replace(
                    "namespace App.Views;",
                    "namespace App.Views; using static Other.Tokens;"
                )
            ),
            CSharpCompilation.Create(
                "namespace-token-wins",
                [
                    CSharpSyntaxTree.ParseText(
                        implicitTokensApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                    CSharpSyntaxTree.ParseText(
                        ambiguousTokenApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "namespace-token-wins",
                new LuiDocumentIdentity("NamespaceTokenWins.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            namespaceTokenWins.Success,
            "ordinary namespace-scoped token precedence was rejected."
        );
        var nonTokenConflictApi =
            "namespace Other; internal static class Values { internal static readonly float NonTokenSpacing = 4f; }";
        var nonTokenConflict = LuiCompiler.Compile(
            LuiParser.Parse(
                "using static Other.Values; "
                    + implicitTokensSource.Replace("DensitySpacing;", "NonTokenSpacing;")
            ),
            CSharpCompilation.Create(
                "non-token-conflict",
                [
                    CSharpSyntaxTree.ParseText(
                        implicitTokensApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                    CSharpSyntaxTree.ParseText(
                        nonTokenConflictApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "non-token-conflict",
                new LuiDocumentIdentity("NonTokenConflict.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            nonTokenConflict.Success,
            "non-token root member participated in implicit ambiguity."
        );
        var missingImplicitTokens = LuiCompiler.Compile(
            implicitTokensDocument,
            CSharpCompilation.Create(
                "missing-implicit-tokens",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "missing-implicit-tokens",
                new LuiDocumentIdentity("MissingImplicitTokens.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            !missingImplicitTokens.Success
                && missingImplicitTokens.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2000"),
            "missing root Tokens did not fail through ordinary compilation."
        );
        var leakedToken = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace App.Views; using Lucent.Core; using static Lucent.Core.Components; internal component Leaked() { <Text content={Label} /> }"
            ),
            CSharpCompilation.Create(
                "leaked-token",
                [
                    CSharpSyntaxTree.ParseText(
                        implicitTokensApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "leaked-token",
                new LuiDocumentIdentity("LeakedToken.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            !leakedToken.Success
                && leakedToken.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2000"),
            "root Tokens leaked into component expressions."
        );
        var leakedProperty = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace App.Views; using Lucent.Core; using static Lucent.Core.Components; internal component LeakedProperty() { <Text content={Width} /> }"
            ),
            CSharpCompilation.Create("leaked-property", references: References()),
            new LuiFreshnessIdentity(
                "48",
                "leaked-property",
                new LuiDocumentIdentity("LeakedProperty.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            !leakedProperty.Success
                && leakedProperty.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2000"),
            "framework style properties leaked into component expressions."
        );
        var leakedComponent = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace App.Views; using Lucent.Core; internal component LeakedComponent() { <Text content={Row} /> }"
            ),
            CSharpCompilation.Create("leaked-component", references: References()),
            new LuiFreshnessIdentity(
                "48",
                "leaked-component",
                new LuiDocumentIdentity("LeakedComponent.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !leakedComponent.Success
                && leakedComponent.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2000"),
            "built-in Components leaked into component expressions."
        );
        var variants = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; internal component Variants() { <Text style={Style.Empty with { when Selected | FocusVisible { Opacity: .5f; } }}>ok</Text> }"
            ),
            CSharpCompilation.Create(
                "variants",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "47",
                "variants",
                new LuiDocumentIdentity("Variants.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            variants.Success
                && variants.Source!.Contains(
                    "VariantState.Selected | global::Lucent.Core.VariantState.FocusVisible"
                ),
            "compound VariantState condition did not lower."
        );
        var duplicateVariantSource =
            "namespace Sample; using Lucent.Core; internal component DuplicateVariants() { <Text style={Style.Empty with { when Hover | Hover { Opacity: .5f; } }}>ok</Text> }";
        var duplicateVariants = LuiCompiler.Compile(
            LuiParser.Parse(duplicateVariantSource),
            CSharpCompilation.Create(
                "duplicate-variants",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "47",
                "duplicate-variants",
                new LuiDocumentIdentity("DuplicateVariants.lui"),
                "v1",
                "preview"
            )
        );
        var firstHover = duplicateVariantSource.IndexOf("Hover", StringComparison.Ordinal);
        var secondHover = duplicateVariantSource.IndexOf(
            "Hover",
            firstHover + 1,
            StringComparison.Ordinal
        );
        Assert(
            duplicateVariants.Success
                && new[] { firstHover, secondHover }.All(start =>
                    duplicateVariants
                        .Map.FromSource(new LuiSpan(start, "Hover".Length))
                        .Any(entry =>
                            !entry.Hidden
                            && duplicateVariants.Source!.Substring(
                                entry.Generated.Start,
                                entry.Generated.Length
                            ) == "Hover"
                        )
                ),
            "duplicate VariantState tokens did not map independently."
        );
        var invalidVariant = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; internal component InvalidVariant() { <Text style={Style.Empty with { when Selected | 1 { Opacity: .5f; } }}>ok</Text> }"
            ),
            CSharpCompilation.Create(
                "invalid-variant",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "47",
                "invalid-variant",
                new LuiDocumentIdentity("InvalidVariant.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !invalidVariant.Success
                && invalidVariant.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2006"),
            "non-VariantState condition was accepted."
        );
        var runtimeApi =
            "namespace Sample; public static class Runtime { public static System.Linq.Expressions.Expression<System.Func<string>> Tree => () => \"ok\"; public static System.Delegate Callback => (System.Func<string>)(() => \"ok\"); public static int Compile() => 1; }";
        var runtimeCompilation = CSharpCompilation.Create(
            "runtime",
            [
                CSharpSyntaxTree.ParseText(
                    runtimeApi,
                    new CSharpParseOptions(LanguageVersion.Preview)
                ),
            ],
            References()
        );
        var codeDomCompilation = CSharpCompilation.Create(
            "code-dom",
            [
                CSharpSyntaxTree.ParseText(
                    "namespace System.CodeDom.Compiler { public abstract class CodeDomProvider { public static CodeDomProvider CreateProvider(string language) => null!; } }",
                    new CSharpParseOptions(LanguageVersion.Preview)
                ),
            ],
            References()
        );
        foreach (
            var reflectionExpression in new[]
            {
                "typeof(string).GetMethod(\"ToString\")",
                "value.GetType().Name",
                "System.Activator.CreateInstance(typeof(System.Text.StringBuilder)).ToString()",
                "System.Type.EmptyTypes.Length",
                "System.Reflection.BindingFlags.Public.ToString()",
                "Runtime.Tree.Compile()()",
                "Runtime.Callback.DynamicInvoke().ToString()",
            }
        )
        {
            var reflectionDocument = LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Reflection(object value) { <Text content={"
                    + reflectionExpression
                    + "} /> }"
            );
            var reflection = LuiCompiler.Compile(
                reflectionDocument,
                runtimeCompilation,
                new LuiFreshnessIdentity(
                    "47",
                    "reflection",
                    new LuiDocumentIdentity("Reflection.lui"),
                    "v1",
                    "preview"
                )
            );
            Assert(
                reflectionDocument.Diagnostics.Count == 0
                    && !reflection.Success
                    && reflection.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2007"),
                "reflection expression was not semantically rejected: " + reflectionExpression
            );
        }
        var ordinaryExpression = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Ordinary() { <Text content={System.Math.Abs(-1).ToString() + Runtime.Compile().ToString()} /> }"
            ),
            runtimeCompilation,
            new LuiFreshnessIdentity(
                "47",
                "ordinary",
                new LuiDocumentIdentity("Ordinary.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            ordinaryExpression.Success,
            "ordinary expression APIs were rejected with reflection APIs."
        );
        foreach (
            var (name, source, compilation) in new[]
            {
                (
                    "dynamic value",
                    "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Dynamic(dynamic dynamicValue) { <Text content={dynamicValue} /> }",
                    runtimeCompilation
                ),
                (
                    "dynamic conversion",
                    "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Dynamic(object value) { <Text content={(dynamic)value} /> }",
                    runtimeCompilation
                ),
                (
                    "dynamic-to-string conversion",
                    "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Dynamic(dynamic dynamicValue) { <Text content={(string)dynamicValue} /> }",
                    runtimeCompilation
                ),
                (
                    "Roslyn compilation",
                    "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Roslyn() { <Text content={Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(\"runtime\").AssemblyName!} /> }",
                    runtimeCompilation
                ),
                (
                    "Roslyn emit",
                    "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Roslyn(Microsoft.CodeAnalysis.Compilation compilation) { <Text content={compilation.Emit().Success} /> }",
                    runtimeCompilation
                ),
                (
                    "CodeDOM provider",
                    "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component CodeDom() { <Text content={System.CodeDom.Compiler.CodeDomProvider.CreateProvider(\"CSharp\").ToString()} /> }",
                    codeDomCompilation
                ),
            }
        )
        {
            var prohibited = LuiCompiler.Compile(
                LuiParser.Parse(source),
                compilation,
                new LuiFreshnessIdentity(
                    "48",
                    name,
                    new LuiDocumentIdentity(name + ".lui"),
                    "v1",
                    "preview"
                )
            );
            Assert(
                !prohibited.Success
                    && prohibited.Diagnostics.Any(diagnostic => diagnostic.Id == "LUI2007"),
                "runtime/compiler expression was not semantically rejected: "
                    + name
                    + " ("
                    + string.Join(
                        " | ",
                        prohibited.Diagnostics.Select(diagnostic =>
                            diagnostic.Id + ":" + diagnostic.Message
                        )
                    )
                    + ")"
            );
        }
        var guarded = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Alias = System.String; using Lucent.Core; using static Lucent.Core.Components; internal component Guards(System.DateTime value, int? optional, string? maybe, string required, Alias alias) { <Text>ok</Text> }"
            ),
            CSharpCompilation.Create(
                "guards",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "47",
                "guards",
                new LuiDocumentIdentity("Guards.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            guarded.Success
                && guarded.Source!.Contains("ThrowIfNull(required)")
                && guarded.Source.Contains("ThrowIfNull(alias)")
                && !guarded.Source.Contains("ThrowIfNull(value)")
                && !guarded.Source.Contains("ThrowIfNull(optional)")
                && !guarded.Source.Contains("ThrowIfNull(maybe)"),
            "null guards did not follow bound nullability."
        );
        var multipleDefaultApi =
            "namespace Sample; using Lucent.Core; public static class Defaults { [LucentComponent] public static ComponentRecipe Multiple([DefaultContent] string first, [DefaultContent] string second) => null!; }";
        var multipleDefault = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using Lucent.Core; using static Sample.Defaults; internal component MultipleUse() { <Multiple>value</Multiple> }"
            ),
            CSharpCompilation.Create(
                "multiple-default",
                [
                    CSharpSyntaxTree.ParseText(
                        multipleDefaultApi,
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "47",
                "multiple-default",
                new LuiDocumentIdentity("Multiple.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !multipleDefault.Success
                && multipleDefault.Diagnostics.Count(diagnostic => diagnostic.Id == "LUI2005") == 1,
            "multiple [DefaultContent] annotations did not produce one stable LUI2005."
        );
        var patternSource =
            "namespace Sample; using Lucent.Core; using static Lucent.Core.Components; internal component Pattern(object? value, string required) { <Column>if (value is string text) { <Text content={text} /> } else { <Text>none</Text> }</Column> }";
        var pattern = LuiCompiler.Compile(
            LuiParser.Parse(patternSource),
            CSharpCompilation.Create(
                "pattern",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "47",
                "pattern",
                new LuiDocumentIdentity("Pattern.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            pattern.Success
                && pattern.Source!.Contains("ConditionalChoice.Create(1")
                && pattern.Source.Contains("ConditionalChoice(2")
                && pattern.Source.Contains("ThrowIfNull(required)"),
            "retained pattern branch or construction-time null guard did not lower."
        );
        var loweredElement = loweredDocument.Component!.Body.OfType<LuiElementSyntax>().Single();
        var closing = loweredElement.CloseAngle.Span;
        var pairedTagMaps = lowered
            .Map.FromSource(loweredElement.Name.Span)
            .Concat(lowered.Map.FromSource(loweredElement.CloseName.Span))
            .Where(entry => !entry.Hidden && entry.Kind == LuiMapKind.Symbol)
            .ToArray();
        Assert(
            lowered.Map.FromSource(closing).Any()
                && pairedTagMaps.Length == 2
                && pairedTagMaps.Select(entry => entry.Generated).Distinct().Count() == 1
                && pairedTagMaps.All(entry => entry.Generated.Length != 0),
            "paired tag names did not map to one targetable generated symbol."
        );
        var loweredNamespace = loweredDocument.TopLevel.OfType<LuiNamespaceSyntax>().Single();
        var loweredUsing = loweredDocument.TopLevel.OfType<LuiUsingSyntax>().First();
        Assert(
            !lowered.Source!.Contains("/*lui*/", StringComparison.Ordinal)
                && lowered
                    .Map.Entries.Where(entry => entry.Hidden)
                    .All(entry => entry.Source.Start < 0)
                && lowered
                    .Map.FromSource(loweredNamespace.Span)
                    .Any(entry => !entry.Hidden && entry.Generated.Length != 0)
                && lowered
                    .Map.FromSource(loweredUsing.Span)
                    .Any(entry => !entry.Hidden && entry.Generated.Length != 0)
                && lowered
                    .Map.FromSource(loweredDocument.Component!.ComponentKeyword.Span)
                    .Any(entry => !entry.Hidden && entry.Generated.Length == 0)
                && !lowered.Map.Entries.Any(entry =>
                    entry.Source.Start == loweredDocument.Span.Start
                    && entry.Source.Length == loweredDocument.Span.Length
                ),
            "source map retained a broad fallback instead of exact construct relations."
        );
        Assert(
            lowered
                .Map.Entries.Where(entry => !entry.Hidden)
                .All(entry =>
                    lowered
                        .Map.FromSource(entry.Source)
                        .Any(candidate =>
                            candidate.Generated.Start == entry.Generated.Start
                            && candidate.Generated.Length == entry.Generated.Length
                        )
                    && lowered
                        .Map.FromGenerated(entry.Generated)
                        .Any(candidate =>
                            candidate.Source.Start == entry.Source.Start
                            && candidate.Source.Length == entry.Source.Length
                        )
                ),
            "source-map entries do not round-trip exactly."
        );
        var commentsSource =
            "// top\nnamespace Sample;\nusing Lucent.Core;\nusing static Lucent.Core.Components;\n// component\ninternal component Comments(bool show) {\n    {/* root */}\n    <Column>{/* nested */} if (show) { {/* region */} <Text>{/* leaf */}on</Text> } else { <Text>off</Text> } </Column>\n}\n// tail";
        var commentsResult = LuiCompiler.Compile(
            LuiParser.Parse(commentsSource),
            CSharpCompilation.Create(
                "comments",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "47",
                "comments",
                new LuiDocumentIdentity("Comments.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            commentsResult.Success,
            "comment map source failed to lower: "
                + string.Join(
                    " | ",
                    commentsResult.Diagnostics.Select(diagnostic => diagnostic.Message)
                )
        );
        foreach (
            var comment in new[]
            {
                "// top",
                "// component",
                "{/* root */}",
                "{/* nested */}",
                "{/* region */}",
                "{/* leaf */}",
                "// tail",
            }
        )
        {
            var source = new LuiSpan(
                commentsSource.IndexOf(comment, StringComparison.Ordinal),
                comment.Length
            );
            var mapped = commentsResult
                .Map.FromSource(source)
                .Single(entry =>
                    entry.Source.Start == source.Start && entry.Source.Length == source.Length
                );
            Assert(
                !mapped.Hidden
                    && mapped.Generated.Length == 0
                    && commentsResult
                        .Map.FromGenerated(mapped.Generated)
                        .Any(entry =>
                            entry.Source.Start == source.Start
                            && entry.Source.Length == source.Length
                        ),
                "comment source map did not round-trip through its zero-width anchor: " + comment
            );
        }
        var points = new LuiSourceMap(
            lowered.Identity,
            [
                new LuiMapEntry(new LuiSpan(0, 1), new LuiSpan(0, 1), LuiMapKind.Symbol, false),
                new LuiMapEntry(new LuiSpan(1, 1), new LuiSpan(1, 1), LuiMapKind.Symbol, false),
                new LuiMapEntry(new LuiSpan(2, 0), new LuiSpan(2, 1), LuiMapKind.Structure, true),
            ]
        );
        Assert(
            points.FromSource(new LuiSpan(1, 0)).Single().Source.Start == 1
                && points.FromGenerated(new LuiSpan(1, 0)).Single().Generated.Start == 1
                && points.FromSource(new LuiSpan(2, 0)).Any(entry => entry.Source.Length == 0),
            "adjacent or zero-width lookup semantics are nondeterministic."
        );
        var diagnosticSource =
            "namespace Sample;\nusing Lucent.Core;\nusing static Lucent.Core.Components;\ninternal component Diagnostic(int value) { <Text content={value + missingName} /> }";
        var diagnosticResult = LuiCompiler.Compile(
            LuiParser.Parse(diagnosticSource),
            CSharpCompilation.Create(
                "diagnostic",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "48",
                "diagnostic",
                new LuiDocumentIdentity("Diagnostic.lui"),
                "v1",
                "preview"
            )
        );
        var missingDiagnostic = diagnosticResult.Diagnostics.Single(diagnostic =>
            diagnostic.Id == "LUI2000"
            && diagnostic.Message.Contains("missingName", StringComparison.Ordinal)
        );
        Assert(
            missingDiagnostic.Span.Start
                == diagnosticSource.IndexOf("missingName", StringComparison.Ordinal)
                && missingDiagnostic.Span.Length == "missingName".Length,
            "Roslyn diagnostic columns collapsed to the enclosing expression."
        );
        var replacementOne = SnapshotWithReference(
            ReplacementReference("public class Replacement { public int Value => 1; }")
        );
        var replacementTwo = SnapshotWithReference(
            ReplacementReference("public class Replacement { public int Value => 2; }")
        );
        Assert(
            replacementOne.ReferencesGeneration != replacementTwo.ReferencesGeneration,
            "same-path metadata replacement did not invalidate reference freshness."
        );
        var relocatedImage = ReplacementImage("public class Relocated { }");
        var relocatedOne = SnapshotWithReference(
            MetadataReference.CreateFromImage(relocatedImage, filePath: "C:/sdk-one/relocated.dll")
        );
        var relocatedTwoReference = MetadataReference.CreateFromImage(
            relocatedImage,
            filePath: "D:/sdk-two/relocated.dll"
        );
        var relocatedTwo = SnapshotWithReference(relocatedTwoReference);
        var embeddedInterop = SnapshotWithReference(
            relocatedTwoReference.WithEmbedInteropTypes(true)
        );
        Assert(
            relocatedOne.ReferencesGeneration == relocatedTwo.ReferencesGeneration
                && relocatedTwo.ReferencesGeneration != embeddedInterop.ReferencesGeneration,
            "reference freshness used installation paths or ignored embedded-interop semantics."
        );
        var parseOptionsOne = CSharpParseOptions.Default.WithPreprocessorSymbols("ONE");
        var parseOptionsTwo = CSharpParseOptions.Default.WithPreprocessorSymbols("TWO");
        var parseTreeText = "#if ONE\ninternal class Defined {}\n#endif";
        var parseSnapshotOne = LuiCompiler.Snapshot(
            new LuiFreshnessIdentity(
                "parse",
                "parse",
                new LuiDocumentIdentity("Parse.lui"),
                "v1",
                "preview"
            ),
            CSharpCompilation.Create(
                "parse",
                [CSharpSyntaxTree.ParseText(parseTreeText, parseOptionsOne, "Parse.cs")],
                References()
            )
        );
        var parseSnapshotTwo = LuiCompiler.Snapshot(
            new LuiFreshnessIdentity(
                "parse",
                "parse",
                new LuiDocumentIdentity("Parse.lui"),
                "v1",
                "preview"
            ),
            CSharpCompilation.Create(
                "parse",
                [CSharpSyntaxTree.ParseText(parseTreeText, parseOptionsTwo, "Parse.cs")],
                References()
            )
        );
        Assert(
            parseSnapshotOne.CompilationGeneration != parseSnapshotTwo.CompilationGeneration,
            "same source with different actual parse defines did not invalidate freshness."
        );
    }

    [TestMethod]
    public void NamedAttributesChooseDefaultContentOverload()
    {
        const string source = """
namespace Sample;
using System;
using Lucent.Core;
using static Lucent.Core.Components;
internal component MenuContent(ApplicationCommand command, Action action, Func<bool> enabled, bool archived) {
    <Menu>
        <MenuItem command={command}>Command</MenuItem>
        <MenuSeparator />
        <MenuItem onInvoke={action} enabled={enabled}>{archived ? "Restore to Inbox" : "Archive"}</MenuItem>
    </Menu>
}
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create(
                "menu-overloads",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "1",
                "menu-overloads",
                new LuiDocumentIdentity("MenuOverloads.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            result.Success,
            "named MenuItem attributes did not select their overloads: "
                + string.Join(
                    " | ",
                    result.Diagnostics.Select(diagnostic =>
                        diagnostic.Id + ":" + diagnostic.Message
                    )
                )
        );
        var generated = result.Source!;
        Assert(
            generated.Contains("MenuItem(command:")
                && generated.Contains(", content: \"Command\")")
                && generated.Contains("MenuItem(onInvoke:")
                && generated.Contains(", enabled:")
                && generated.Contains("archived ? \"Restore to Inbox\" : \"Archive\""),
            "named MenuItem overloads did not retain their authored default content and attributes."
        );
    }

    [TestMethod]
    public void QualifiedConditionalTokensRemainLiveWithoutRootTokens()
    {
        const string api = """
namespace App;
using Lucent.Core;
internal static class LightNotesTheme
{
    internal static readonly Token<FocusRing> KeyboardFocus = new("keyboard-focus", FocusRing.None);
    internal static readonly Token<FocusRing> NoFocusRing = new("no-focus-ring", FocusRing.None);
}
""";
        const string source = """
namespace App.Views;
using Lucent.Core;
using static Lucent.Core.Components;
internal component NoteRow(bool menuOpen)
{
    <Row style={Style.Empty with { FocusRing: menuOpen ? LightNotesTheme.KeyboardFocus : LightNotesTheme.NoFocusRing; }} />
}
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create(
                "qualified-token-expression",
                [CSharpSyntaxTree.ParseText(api, new CSharpParseOptions(LanguageVersion.Preview))],
                References()
            ),
            new LuiFreshnessIdentity(
                "1",
                "qualified-token-expression",
                new LuiDocumentIdentity("QualifiedTokenExpression.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            result.Success
                && result.Source!.Contains(
                    ".BindValue<global::Lucent.Core.FocusRing>(global::Lucent.Core.VisualProperties.FocusRing, () =>"
                )
                && result.Source.Contains(
                    "global::Lucent.Core.StyleValue.FromToken<global::Lucent.Core.FocusRing>(LightNotesTheme.KeyboardFocus)"
                )
                && result.Source.Contains(
                    "global::Lucent.Core.StyleValue.FromToken<global::Lucent.Core.FocusRing>(LightNotesTheme.NoFocusRing)"
                ),
            "qualified conditional Token<T> without App.Tokens did not lower through BindValue: "
                + string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message))
                + "\n"
                + result.Source
        );
    }

    [TestMethod]
    public void MixedTokenValueConditionalsUseBindValueAndSetValue()
    {
        const string api = """
namespace App;
using Lucent.Core;
internal static class LightNotesTheme
{
    internal static bool UseDense = true;
    internal static readonly Token<float> DenseSpacing = new("dense-spacing", 8f);
}
""";
        const string source = """
namespace App.Views;
using Lucent.Core;
using static Lucent.Core.Components;
internal component InlineMixed(bool menuOpen)
{
    <Row style={NamedMixed with { Opacity: menuOpen ? LightNotesTheme.DenseSpacing : 1; }} />
}
style NamedMixed {
    Opacity: LightNotesTheme.UseDense ? LightNotesTheme.DenseSpacing : 1;
}

""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create(
                "mixed-token-value",
                [CSharpSyntaxTree.ParseText(api, new CSharpParseOptions(LanguageVersion.Preview))],
                References()
            ),
            new LuiFreshnessIdentity(
                "1",
                "mixed-token-value",
                new LuiDocumentIdentity("MixedTokenValue.lui"),
                "v1",
                "",
                "",
                "preview",
                "",
                "",
                "",
                "",
                "",
                "App"
            )
        );
        Assert(
            result.Success
                && result.Source!.Contains(
                    ".BindValue<float>(global::Lucent.Core.VisualProperties.Opacity, () =>"
                )
                && result.Source.Contains(
                    "global::Lucent.Core.StyleValue.FromToken<float>(LightNotesTheme.DenseSpacing)"
                )
                && result.Source.Contains("global::Lucent.Core.StyleValue.FromValue<float>(1)")
                && result.Source.Contains(
                    ".SetValue<float>(global::Lucent.Core.VisualProperties.Opacity, "
                ),
            "mixed token/value conditionals did not lower through typed StyleValue bindings: "
                + string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message))
                + "\n"
                + result.Source
        );
    }

    [TestMethod]
    public void TokenChoicesPreserveContextualConversionsAndAuthoredDiagnostics()
    {
        const string api = """
namespace Sample;
using Lucent.Core;
public static class Palette {
    public static readonly Token<FocusRing> Ring = new("ring", FocusRing.None);
    public static readonly Token<Brush> Surface = new("surface", Color.Parse("#123456"));
    public static readonly Token<Insets> Padding = new("padding", Insets.Uniform(4));
    public static FocusRing Resolve(Token<FocusRing> token) => token.Fallback;
}
""";
        var compilation = CSharpCompilation.Create(
            "token-conversions",
            [CSharpSyntaxTree.ParseText(api, new CSharpParseOptions(LanguageVersion.Preview))],
            References()
        );
        foreach (
            var (property, expression) in new[]
            {
                ("FocusRing", "active ? Palette.Ring : default"),
                ("FocusRing", "active ? Palette.Ring : default(FocusRing)"),
                ("FocusRing", "active ? (active ? Palette.Ring : default) : FocusRing.None"),
                ("Background", "active ? Palette.Surface : Color.Parse(\"#ABCDEF\")"),
                ("Background", "active ? Palette.Surface : null"),
                ("Padding", "active ? Palette.Padding : new(1, 2, 3, 4)"),
            }
        )
        {
            var source =
                "namespace Sample; using Lucent.Core; public component Demo(bool active) { <Row style={Style.Empty with { "
                + property
                + ": "
                + expression
                + "; }} /> }";
            var result = LuiCompiler.Compile(
                LuiParser.Parse(source),
                compilation,
                new LuiFreshnessIdentity(
                    "1",
                    "tokens",
                    new LuiDocumentIdentity("Token.lui"),
                    "v1",
                    "preview"
                )
            );
            Assert(
                result.Success,
                expression
                    + ": "
                    + string.Join(" | ", result.Diagnostics.Select(item => item.Message))
                    + "\n"
                    + result.Source
            );
            Assert(
                result.Source!.Contains(".BindValue<"),
                "Token choice lost its live binding: " + expression
            );
            var tokenName =
                expression.Contains("Palette.Ring") ? "Palette.Ring"
                : expression.Contains("Palette.Surface") ? "Palette.Surface"
                : "Palette.Padding";
            var span = new LuiSpan(
                source.IndexOf(tokenName, StringComparison.Ordinal),
                tokenName.Length
            );
            Assert(
                result.Map.FromSource(span).Any(entry => entry.Kind != LuiMapKind.Scaffolding),
                "Author token branch lost its source map: " + expression
            );
        }
        foreach (var invalid in new[] { "\"wrong\"", "Palette.Padding", "null" })
        {
            var source =
                "namespace Sample; using Lucent.Core; public component Demo(bool active) { <Row style={Style.Empty with { FocusRing: active ? Palette.Ring : "
                + invalid
                + "; }} /> }";
            var result = LuiCompiler.Compile(
                LuiParser.Parse(source),
                compilation,
                new LuiFreshnessIdentity(
                    "1",
                    "tokens",
                    new LuiDocumentIdentity("Token.lui"),
                    "v1",
                    "preview"
                )
            );
            var start = source.LastIndexOf(invalid, StringComparison.Ordinal);
            Assert(
                !result.Success
                    && result.Diagnostics.Any(item =>
                        item.Span.Start >= start && item.Span.End <= start + invalid.Length
                    ),
                "Invalid concrete/token branch did not retain an authored diagnostic: "
                    + invalid
                    + " | "
                    + string.Join(
                        " | ",
                        result.Diagnostics.Select(item =>
                            item.Id + "@" + item.Span + ":" + item.Message
                        )
                    )
            );
        }
    }

    [TestMethod]
    public void DirectTokenStateIsLiveAndNestedTokenArgumentsDoNotRetypeConcreteValues()
    {
        const string source = """
namespace Sample;
using Lucent.Core;
public component Demo() {
    [Once] Token<FocusRing> selected = Palette.Ring;
    readonly Token<FocusRing> initial = Palette.Ring;
    void Change() { selected = Palette.Other; }
    <Column>
        <Button onInvoke={Change} style={Style.Empty with { FocusRing: selected; }}>Change</Button>
        <Row style={Style.Empty with { FocusRing: initial; }} />
        <Row style={Style.Empty with { FocusRing: Palette.Resolve(Palette.Ring); }} />
    </Column>
}
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create(
                "direct-token",
                [
                    CSharpSyntaxTree.ParseText(
                        """
namespace Sample; using Lucent.Core;
public static class Palette {
    public static readonly Token<FocusRing> Ring = new("ring", FocusRing.None);
    public static readonly Token<FocusRing> Other = new("other", FocusRing.None);
    public static FocusRing Resolve(Token<FocusRing> token) => token.Fallback;
}
""",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References()
            ),
            new LuiFreshnessIdentity(
                "1",
                "tokens",
                new LuiDocumentIdentity("Direct.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            result.Success,
            string.Join(" | ", result.Diagnostics.Select(item => item.Message))
                + "\n"
                + result.Source
        );
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            result.Source!,
            @"(?m)^#line.*$",
            ""
        );
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
        Assert(
            normalized.Contains(
                ".BindValue<global::Lucent.Core.FocusRing>(global::Lucent.Core.VisualProperties.FocusRing, () => selected "
            ),
            "Mutable token state lost its live reader: " + result.Source
        );
        Assert(
            normalized.Contains(
                ".Bind<global::Lucent.Core.FocusRing>(global::Lucent.Core.VisualProperties.FocusRing, () => Palette.Resolve(Palette.Ring)"
            ),
            "A nested token argument changed an ordinary concrete expression's target type: "
                + result.Source
        );
    }

    [TestMethod]
    public void RetainedStructuralLocalsLowerToCurrentReaders()
    {
        const string api = """
namespace Sample;
using System;
using Lucent.Core;
public sealed record Row(int Id, string Title);
public sealed record Pair(Row Left, Row Right);
public sealed class Holder { public string item = "member"; }
public static class TestComponents
{
    [LucentComponent]
    public static ComponentRecipe Probe(
        string snapshot,
        Func<string> live,
        string label,
        string direct,
        Func<Row, string> transform,
        string member) => ComponentRecipe.Create("probe", static (_, _) => { });
}
""";
        const string source = """
namespace Sample;
using System;
using System.Collections.Generic;
using Lucent.Core;
using static Lucent.Core.Components;
using static Sample.TestComponents;
internal component Current(IEnumerable<Row> rows, IEnumerable<Style> styles, object? candidate, Holder holder) {
    <Column>
        foreach (var item in rows) keyed by item.Id {
            <Probe snapshot={item.Title} live={() =>
 item.Title} label={nameof(item.Title)} direct={nameof(item)} transform={item => item.Title} member={holder.item} />
        }
        foreach (var styleItem in styles) keyed by styleItem.GetHashCode() {
            <Row style={styleItem with styleItem} />
        }
        if (candidate is Pair { Left: Row left, Right: Row right }) {
            <Probe snapshot={left.Title + right.Title} live={() => left.Title + right.Title} label={nameof(left.Title)} direct={nameof(left)} transform={left => left.Title} member={holder.item} />
        }
    </Column>
}
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create(
                "current-readers",
                [CSharpSyntaxTree.ParseText(api, new CSharpParseOptions(LanguageVersion.Preview))],
                References()
            ),
            new LuiFreshnessIdentity(
                "70",
                "current-readers",
                new LuiDocumentIdentity("Current.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            result.Success,
            "current-reader lowering did not compile: "
                + string.Join(
                    " | ",
                    result.Diagnostics.Select(item =>
                        item.Id + "@" + item.Span.Start + ":" + item.Message
                    )
                )
                + "\n"
                + result.ProjectionSource
        );
        var generated = result.Source!;
        Assert(
            generated.Contains("item.Value.Title", StringComparison.Ordinal)
                && generated.Contains("nameof(item.Value.Title)", StringComparison.Ordinal)
                && generated.Contains("nameof(item)", StringComparison.Ordinal)
                && generated.Contains("item => item.Title", StringComparison.Ordinal)
                && generated.Contains("holder.item", StringComparison.Ordinal)
                && generated.Contains(
                    ".With(styleItem.Value).With(styleItem.Value)",
                    StringComparison.Ordinal
                )
                && generated.Contains("ConditionalChoice.Create", StringComparison.Ordinal)
                && generated.Contains("__luiLocal1_left().Title", StringComparison.Ordinal)
                && generated.Contains("__luiLocal2_right().Title", StringComparison.Ordinal),
            "structural locals, shadowing, nameof, or member names were rewritten incorrectly:\n"
                + generated
        );
        var shadowed = LuiCompiler.Compile(
            LuiParser.Parse(
                "namespace Sample; using System.Collections.Generic; using Lucent.Core; using static Lucent.Core.Components; using static Sample.TestComponents; internal component Shadow(IEnumerable<Row> rows, object? candidate) { <Row>foreach (var item in rows) keyed by item.Id { <Probe snapshot={item.Title} live={() => candidate is Row item ? item.Title : string.Empty} label=\"x\" direct=\"x\" transform={value => value.Title} member=\"x\" /> }</Row> }"
            ),
            CSharpCompilation.Create(
                "shadowed-current-reader",
                [CSharpSyntaxTree.ParseText(api, new CSharpParseOptions(LanguageVersion.Preview))],
                References()
            ),
            new LuiFreshnessIdentity(
                "70",
                "shadowed-current-reader",
                new LuiDocumentIdentity("Shadow.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            !shadowed.Success && shadowed.Diagnostics.Any(item => item.Id == "LUI2010"),
            "a nested pattern shadowing a retained local did not fail closed."
        );
        var liveItem = source.IndexOf(" item.Title}", StringComparison.Ordinal) + 1;
        Assert(
            result
                .Map.FromSource(new LuiSpan(liveItem, "item".Length))
                .Any(entry =>
                    !entry.Hidden
                    && generated.Substring(entry.Generated.Start, entry.Generated.Length) == "item"
                ),
            "rewritten foreach local lost its authored source map."
        );
    }

    [TestMethod]
    public void RetainedStructuralLocalLiveAttributesUseTheAuthoredArgument()
    {
        const string api = """
namespace Sample;
using System;
using Lucent.Core;
public sealed record Row(int Id, string Title);
public sealed record Pair(Row Left, Row Right);
public static class TestComponents
{
    [LucentComponent]
    public static ComponentRecipe Probe(string snapshot, Func<string> live) =>
        ComponentRecipe.Create("probe", static (_, _) => { });
}
""";
        const string source = """
namespace Sample;
using System.Collections.Generic;
using Lucent.Core;
using static Sample.TestComponents;
internal component Current(IEnumerable<Row> rows, object? candidate) {
    <Column>
        foreach (var item in rows) keyed by item.Id {
            <Probe snapshot={item.Title} live={item.Title} />
        }
        if (candidate is Pair { Left: Row left, Right: Row right }) {
            <Probe snapshot={left.Title} live={left.Title} />
        }
    </Column>
}
""";
        var result = LuiCompiler.Compile(
            LuiParser.Parse(source),
            CSharpCompilation.Create(
                "retained-live-attributes",
                [CSharpSyntaxTree.ParseText(api, new CSharpParseOptions(LanguageVersion.Preview))],
                References()
            ),
            new LuiFreshnessIdentity(
                "126",
                "retained-live-attributes",
                new LuiDocumentIdentity("Current.lui"),
                "v1",
                "preview"
            )
        );
        Assert(
            result.Success,
            "retained local live attributes did not compile: "
                + string.Join(" | ", result.Diagnostics.Select(item => item.Message))
                + "\n"
                + result.ProjectionSource
        );
    }

    [TestMethod]
    public void TargetTypedStyleConstructionExplainsValueTokenAmbiguity()
    {
        var compilation = CSharpCompilation.Create("insets-diagnostic", references: References());
        foreach (var value in new[] { "new(16, 8)", "new(16, 8, 16, 8)" })
        {
            var source =
                "using Lucent.Core; style Panel { Padding: "
                + value
                + "; } internal component Test() { <Row style={Panel} /> }";
            var result = LuiCompiler.Compile(
                LuiParser.Parse(source),
                compilation,
                new LuiFreshnessIdentity(
                    "1",
                    "test",
                    new LuiDocumentIdentity("Insets.lui"),
                    "1",
                    "preview"
                )
            );
            var diagnostic = result.Diagnostics.Single(item => item.Id == "LUI2012");
            Assert(
                !result.Success
                    && diagnostic.Span.Start == source.IndexOf(value, StringComparison.Ordinal)
                    && diagnostic.Span.Length == value.Length
                    && diagnostic.Message.Contains("Insets.Symmetric", StringComparison.Ordinal)
                    && diagnostic.Message.Contains("new Insets", StringComparison.Ordinal)
                    && !result.Diagnostics.Any(item =>
                        item.Message.Contains("Style.Set", StringComparison.Ordinal)
                    ),
                "Target-typed style construction did not report an actionable error on its expression."
            );
        }
        foreach (var value in new[] { "Insets.Symmetric(16, 8)", "new Insets(16, 8, 16, 8)" })
        {
            var source =
                "using Lucent.Core; style Panel { Padding: "
                + value
                + "; } internal component Test() { <Row style={Panel} /> }";
            var result = LuiCompiler.Compile(
                LuiParser.Parse(source),
                compilation,
                new LuiFreshnessIdentity(
                    "1",
                    "test",
                    new LuiDocumentIdentity("Insets.lui"),
                    "1",
                    "preview"
                )
            );
            Assert(
                result.Success,
                "Explicit Insets construction stopped compiling: "
                    + String.Join("; ", result.Diagnostics.Select(item => item.Message))
            );
        }
    }

    static string Diagnostics(LuiDocumentSyntax document) =>
        string.Join(
            " | ",
            document.Diagnostics.Select(diagnostic => diagnostic.Id + "@" + diagnostic.Span.Start)
        );

    static MetadataReference[] References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(
                MetadataReference.CreateFromFile(
                    Path.Combine(AppContext.BaseDirectory, "Lucent.Core.dll")
                )
            )
            .ToArray();

    static LuiFreshnessIdentity SnapshotWithReference(MetadataReference reference) =>
        LuiCompiler.Snapshot(
            new LuiFreshnessIdentity(
                "reference",
                "reference",
                new LuiDocumentIdentity("Reference.lui"),
                "v1",
                "preview"
            ),
            CSharpCompilation.Create(
                "snapshot",
                [
                    CSharpSyntaxTree.ParseText(
                        "internal class C {}",
                        new CSharpParseOptions(LanguageVersion.Preview)
                    ),
                ],
                References().Append(reference)
            )
        );

    static PortableExecutableReference ReplacementReference(string source) =>
        MetadataReference.CreateFromImage(
            ReplacementImage(source),
            filePath: "C:/consumer/replaced.dll"
        );

    static byte[] ReplacementImage(string source)
    {
        var compilation = CSharpCompilation.Create(
            "replacement",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        using var stream = new MemoryStream();
        Assert(compilation.Emit(stream).Success, "replacement assembly did not emit.");
        return stream.ToArray();
    }

    static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
