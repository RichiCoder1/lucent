using System;
using System.Collections.Generic;
using System.Linq;
using D = Lucent.Lui.Compiler.LuiLayoutDocument;

namespace Lucent.Lui.Compiler;

internal sealed class LuiDocumentLayout
{
    private readonly LuiDocumentSyntax document;
    private readonly LuiFormattingOptions options;
    private readonly LuiCSharpLayout csharp;
    private readonly LuiFormattingDirectives directives;
    private string Source => document.Source;

    internal LuiDocumentLayout(
        LuiDocumentSyntax document,
        LuiFormattingOptions options,
        LuiFormattingDirectives directives
    )
    {
        this.document = document;
        this.options = options;
        this.directives = directives;
        csharp = new(options);
    }

    internal string Format() =>
        D.Concat(TopLevel(), D.HardLine).Render(options, options.Newline(Source));

    internal string FormatNode(LuiSyntaxNode node, int indent)
    {
        var content = Node(node);
        for (var level = 0; level < indent; level++)
            content = D.Indent(content);
        return content.Render(options, options.Newline(Source));
    }

    private D TopLevel()
    {
        var result = new List<D>();
        LuiSyntaxNode? previous = null;
        foreach (var node in document.TopLevel)
        {
            if (previous is not null)
            {
                result.Add(D.HardLine);
                if (
                    Grouping(previous.Span.End, node.Span.Start)
                    || previous is LuiComponentSyntax or LuiStyleSyntax
                    || node is LuiComponentSyntax or LuiStyleSyntax
                        && previous is not LuiTopLevelCommentSyntax
                )
                    result.Add(D.HardLine);
            }
            result.Add(Node(node));
            previous = node;
        }
        return D.Concat(result.ToArray());
    }

    private D Node(LuiSyntaxNode node) =>
        directives.IsIgnored(node)
            ? D.Text(Slice(node.Span.Start, node.Span.End))
            : node switch
            {
                LuiNamespaceSyntax value => D.Text("namespace " + value.Value + ";"),
                LuiUsingSyntax value => D.Text("using " + value.Value + ";"),
                LuiTopLevelCommentSyntax value => D.Text(value.Text),
                LuiComponentSyntax value => Block(
                    D.Concat(
                        D.Text(
                            (value.Accessibility.IsMissing ? "" : value.Accessibility.Text + " ")
                                + "component "
                                + value.Name.Text
                        ),
                        csharp.Parameters(
                            Slice(value.OpenParameters.Span.Start, value.CloseParameters.Span.End)
                        )
                    ),
                    Body(value.Body, component: true)
                ),
                LuiStyleSyntax value => Block(
                    D.Concat(
                        D.Text("style " + value.Name.Text),
                        value.OpenParameters.IsMissing
                            ? D.Text("")
                            : csharp.Parameters(
                                Slice(
                                    value.OpenParameters.Span.Start,
                                    value.CloseParameters.Span.End
                                )
                            )
                    ),
                    Styles(value.Members)
                ),
                LuiStyleMemberSyntax value => StyleMember(value),
                LuiElementSyntax value => Element(value),
                LuiMemberSyntax value => csharp.Member(value),
                LuiRequirementSyntax value => D.Text(value.Text),
                LuiCommentSyntax value => D.Text(value.Text),
                LuiTextSyntax value => D.Text(value.Text),
                LuiExpressionBodySyntax value => Island(value.Text),
                LuiIfSyntax value => D.Concat(
                    Block(
                        D.Concat(
                            D.Text("if ("),
                            csharp.Expression(value.Condition.Text),
                            D.Text(")")
                        ),
                        Body(value.ThenBody)
                    ),
                    value.ElseKeyword.IsMissing
                        ? D.Text("")
                        : D.Concat(D.Text(" else"), Block(D.Text(""), Body(value.ElseBody)))
                ),
                LuiForEachSyntax value => Block(
                    D.Concat(
                        D.Text("foreach (var " + value.Variable.Text + " in "),
                        csharp.Expression(value.Source.Text),
                        D.Text(") keyed by "),
                        csharp.Expression(value.Key.Text)
                    ),
                    Body(value.Body)
                ),
                _ => throw new InvalidOperationException(
                    "Unsupported formatting node: " + node.GetType().Name
                ),
            };

    private D Body(IReadOnlyList<LuiBodySyntax> nodes, bool component = false)
    {
        var separator = -1;
        if (component)
        {
            var lastMember = -1;
            for (var index = 0; index < nodes.Count; index++)
            {
                if (nodes[index] is LuiMemberSyntax or LuiRequirementSyntax)
                    lastMember = index;
                else if (nodes[index] is not LuiCommentSyntax && lastMember >= 0)
                {
                    separator = lastMember + 1;
                    break;
                }
            }
        }
        var result = new List<D>();
        for (var index = 0; index < nodes.Count; index++)
        {
            if (index != 0)
            {
                result.Add(D.HardLine);
                if (
                    index == separator
                    || Grouping(nodes[index - 1].Span.End, nodes[index].Span.Start)
                )
                    result.Add(D.HardLine);
            }
            result.Add(Node(nodes[index]));
        }
        return D.Concat(result.ToArray());
    }

    private D Element(LuiElementSyntax element)
    {
        var open =
            element.Attributes.Count == 0
                ? D.Text("<" + element.Name.Text + (element.SelfClosing ? " />" : ">"))
                : D.Group(
                    D.Concat(
                        D.Text("<" + element.Name.Text),
                        D.Indent(
                            D.Concat(
                                D.Line,
                                D.Join(
                                    D.Line,
                                    element.Attributes.Select(attribute =>
                                        D.Concat(
                                            D.Text(attribute.Name.Text + "="),
                                            Value(attribute.Value)
                                        )
                                    )
                                )
                            )
                        ),
                        element.SelfClosing ? D.Line : D.SoftLine,
                        D.Text(element.SelfClosing ? "/>" : ">")
                    )
                );
        if (element.SelfClosing)
            return open;
        var close = D.Text("</" + element.CloseName.Text + ">");
        if (element.Children.Count == 0)
            return D.Concat(open, close);
        if (
            element.Children.Count == 1
            && element.Children[0] is LuiTextSyntax or LuiExpressionBodySyntax
        )
            return D.Group(
                D.Concat(
                    open,
                    D.Indent(D.Concat(D.SoftLine, Node(element.Children[0]))),
                    D.SoftLine,
                    close
                )
            );
        return D.Concat(
            open,
            D.Indent(D.Concat(D.HardLine, Body(element.Children))),
            D.HardLine,
            close
        );
    }

    private D Value(LuiValueSyntax value) =>
        value switch
        {
            LuiScalarSyntax scalar => D.Text(Slice(scalar.Span.Start, scalar.Span.End)),
            LuiExpressionSyntax expression => Island(expression.Text),
            LuiStyleWithSyntax style => style.Tail is { } tail
                ? D.Concat(
                    D.Text("{"),
                    csharp.Expression(style.Name.Text),
                    D.Text(" with "),
                    csharp.Expression(tail.Text),
                    D.Text("}")
                )
                : D.Concat(
                    D.Text("{"),
                    csharp.Expression(style.Name.Text),
                    D.Text(" with "),
                    style.Members.Count == 1 && style.Members[0] is LuiStyleAssignmentSyntax
                        ? D.Group(
                            D.Concat(
                                D.Text("{"),
                                D.Indent(D.Concat(D.Line, Styles(style.Members))),
                                D.Line,
                                D.Text("}")
                            )
                        )
                        : D.Concat(
                            D.Text("{"),
                            D.Indent(D.Concat(D.HardLine, Styles(style.Members))),
                            D.HardLine,
                            D.Text("}")
                        ),
                    D.Text("}")
                ),
            _ => throw new InvalidOperationException("Unsupported formatting value."),
        };

    private D Styles(IReadOnlyList<LuiStyleMemberSyntax> members)
    {
        var result = new List<D>();
        for (var index = 0; index < members.Count; index++)
        {
            if (index != 0)
            {
                result.Add(D.HardLine);
                if (Grouping(members[index - 1].Span.End, members[index].Span.Start))
                    result.Add(D.HardLine);
            }
            result.Add(StyleMember(members[index]));
        }
        return D.Concat(result.ToArray());
    }

    private D StyleMember(LuiStyleMemberSyntax member) =>
        directives.IsIgnored(member)
            ? D.Text(Slice(member.Span.Start, member.Span.End))
            : member switch
            {
                LuiStyleCommentSyntax comment => D.Text(comment.Text),
                LuiStyleAssignmentSyntax assignment => D.Concat(
                    D.Text(assignment.Property.Text + ": "),
                    csharp.Expression(assignment.Expression.Text),
                    D.Text(";")
                ),
                LuiStyleTransitionSyntax transition => D.Concat(
                    D.Text("transition " + transition.Property.Text + ": "),
                    csharp.Expression(transition.Expression.Text),
                    D.Text(";")
                ),
                LuiVariantGroupSyntax variant => Block(
                    D.Concat(
                        D.Text("when "),
                        variant.ConditionExpression is null
                            ? D.Text(variant.Condition.Text)
                            : D.Concat(
                                D.Text("("),
                                csharp.Expression(variant.Condition.Text),
                                D.Text(")")
                            )
                    ),
                    Styles(variant.Members)
                ),
                _ => throw new InvalidOperationException("Unsupported formatting style member."),
            };

    private D Island(string source) =>
        D.Group(
            D.Concat(
                D.Text("{"),
                D.Indent(D.Concat(D.SoftLine, csharp.Expression(source))),
                D.SoftLine,
                D.Text("}")
            )
        );

    private static D Block(D header, D body) =>
        D.Concat(
            header,
            D.Text(" {"),
            D.Indent(D.Concat(D.HardLine, body)),
            D.HardLine,
            D.Text("}")
        );

    private string Slice(int start, int end) => Source.Substring(start, end - start);

    private bool Grouping(int start, int end)
    {
        var lines = 0;
        for (var index = start; index < end; index++)
            if (
                Source[index] == '\n'
                || Source[index] == '\r' && (index + 1 >= end || Source[index + 1] != '\n')
            )
                lines++;
        return lines >= 2;
    }
}
