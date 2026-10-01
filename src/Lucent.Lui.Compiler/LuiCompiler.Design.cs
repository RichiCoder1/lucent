using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler;

public static partial class LuiCompiler
{
    private sealed class DesignPlan
    {
        internal DesignPlan(string capture, IReadOnlyList<LuiDesignIntrinsic> intrinsics)
        {
            Capture = capture;
            Intrinsics = intrinsics;
        }

        internal string Capture { get; }
        internal IReadOnlyList<LuiDesignIntrinsic> Intrinsics { get; }

        internal int FirstReceiverAtOrAfter(int start)
        {
            var low = 0;
            var high = Intrinsics.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (Intrinsics[middle].Receiver.Start < start)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        internal bool HasReceiverIn(LuiSpan source)
        {
            var index = FirstReceiverAtOrAfter(source.Start);
            return index < Intrinsics.Count && Intrinsics[index].Receiver.End <= source.End;
        }
    }

    // Bind the ordinary receiver first. Even a symbol with no requested member owns
    // its normal C# diagnostic; an intrinsic must never change lexical lookup.
    private static DesignPlan? DesignReceivers(
        LuiDocumentSyntax document,
        SemanticModel model,
        SyntaxTree tree,
        LuiSourceMap map
    )
    {
        if (document.Component is not { } component)
            return null;
        var intrinsics = new List<LuiDesignIntrinsic>();
        var companionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var access in tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
        )
        {
            if (
                access.Expression is not IdentifierNameSyntax receiver
                || receiver.Identifier.ValueText != "Design"
            )
                continue;
            var source = Translate(map, new LuiSpan(receiver.SpanStart, receiver.Span.Length));
            if (
                source is null
                || source.Value.Start < component.OpenBrace.Span.End
                || source.Value.End > component.Span.End
                || document.Source.Substring(source.Value.Start, source.Value.Length)
                    != receiver.Identifier.Text
            )
                continue;
            var ordinary = model.GetSymbolInfo(receiver);
            if (
                ordinary.Symbol is not null
                || ordinary.CandidateSymbols.Length != 0
                || model.GetAliasInfo(receiver) is not null
                || model.LookupSymbols(receiver.SpanStart, name: "Design").Length != 0
            )
                continue;
            var member = access.Name.IsMissing
                ? new LuiSpan(source.Value.End + 1, 0)
                : Translate(map, new LuiSpan(access.Name.SpanStart, access.Name.Span.Length))
                    ?? new LuiSpan(source.Value.End + 1, 0);
            intrinsics.Add(new LuiDesignIntrinsic(source.Value, member));
            for (
                var type = model.GetEnclosingSymbol(receiver.SpanStart)?.ContainingType;
                type is not null;
                type = type.BaseType
            )
                foreach (var symbol in type.GetMembers())
                    companionNames.Add(symbol.Name);
        }
        if (intrinsics.Count == 0)
            return null;
        var name = "__luiDesign_" + map.Identity.Document.StableId;
        while (
            document.Source.Contains(name, StringComparison.Ordinal)
            || companionNames.Any(member => member.Contains(name, StringComparison.Ordinal))
        )
            name += "_";
        return new DesignPlan(
            name,
            intrinsics
                .GroupBy(static item => item.Receiver.Start)
                .Select(static group => group.First())
                .OrderBy(static item => item.Receiver.Start)
                .ToArray()
        );
    }
}
