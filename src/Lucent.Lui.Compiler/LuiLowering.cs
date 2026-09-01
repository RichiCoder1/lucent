using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.Compiler;

/// <summary>Identity required to publish a bound/lowered document.</summary>
public sealed class LuiFreshnessIdentity : IEquatable<LuiFreshnessIdentity>
{
    public LuiFreshnessIdentity(
        string projectEpoch,
        string projectIdentity,
        LuiDocumentIdentity document,
        string documentVersion,
        string options
    )
        : this(
            projectEpoch,
            projectIdentity,
            document,
            documentVersion,
            "",
            "",
            "",
            "",
            "",
            "",
            options
        ) { }

    public LuiFreshnessIdentity(
        string projectEpoch,
        string projectIdentity,
        LuiDocumentIdentity document,
        string documentVersion,
        string compilationGeneration,
        string siblingIndexGeneration,
        string languageVersion,
        string compilerVersion,
        string referencesGeneration,
        string globalUsingsGeneration,
        string options
    )
        : this(
            projectEpoch,
            projectIdentity,
            document,
            documentVersion,
            compilationGeneration,
            siblingIndexGeneration,
            languageVersion,
            compilerVersion,
            referencesGeneration,
            globalUsingsGeneration,
            options,
            ""
        ) { }

    public LuiFreshnessIdentity(
        string projectEpoch,
        string projectIdentity,
        LuiDocumentIdentity document,
        string documentVersion,
        string compilationGeneration,
        string siblingIndexGeneration,
        string languageVersion,
        string compilerVersion,
        string referencesGeneration,
        string globalUsingsGeneration,
        string options,
        string defines
    )
    {
        ProjectEpoch = projectEpoch ?? throw new ArgumentNullException(nameof(projectEpoch));
        ProjectIdentity =
            projectIdentity ?? throw new ArgumentNullException(nameof(projectIdentity));
        Document = document ?? throw new ArgumentNullException(nameof(document));
        DocumentVersion =
            documentVersion ?? throw new ArgumentNullException(nameof(documentVersion));
        CompilationGeneration =
            compilationGeneration ?? throw new ArgumentNullException(nameof(compilationGeneration));
        SiblingIndexGeneration =
            siblingIndexGeneration
            ?? throw new ArgumentNullException(nameof(siblingIndexGeneration));
        LanguageVersion =
            languageVersion ?? throw new ArgumentNullException(nameof(languageVersion));
        CompilerVersion =
            compilerVersion ?? throw new ArgumentNullException(nameof(compilerVersion));
        ReferencesGeneration =
            referencesGeneration ?? throw new ArgumentNullException(nameof(referencesGeneration));
        GlobalUsingsGeneration =
            globalUsingsGeneration
            ?? throw new ArgumentNullException(nameof(globalUsingsGeneration));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Defines = defines ?? throw new ArgumentNullException(nameof(defines));
    }

    public string ProjectEpoch { get; }
    public string ProjectIdentity { get; }
    public LuiDocumentIdentity Document { get; }
    public string DocumentVersion { get; }
    public string CompilationGeneration { get; }
    public string SiblingIndexGeneration { get; }
    public string LanguageVersion { get; }
    public string CompilerVersion { get; }
    public string ReferencesGeneration { get; }
    public string GlobalUsingsGeneration { get; }
    public string Options { get; }
    public string Defines { get; }
    public string HintName => "Lucent.Lui." + Document.StableId + ".g.cs";
    public string MapIdentity =>
        LuiDocumentIdentity.Hash(
            ProjectEpoch
                + "\0"
                + ProjectIdentity
                + "\0"
                + Document.StableId
                + "\0"
                + DocumentVersion
                + "\0"
                + CompilationGeneration
                + "\0"
                + SiblingIndexGeneration
                + "\0"
                + LanguageVersion
                + "\0"
                + CompilerVersion
                + "\0"
                + ReferencesGeneration
                + "\0"
                + GlobalUsingsGeneration
                + "\0"
                + Options
                + "\0"
                + Defines
        );

    public bool Equals(LuiFreshnessIdentity? other) =>
        other is not null && MapIdentity == other.MapIdentity;

    public override bool Equals(object? obj) => Equals(obj as LuiFreshnessIdentity);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(MapIdentity);

    public bool CanPublishTo(LuiFreshnessIdentity current) => Equals(current);
}

public enum LuiMapKind
{
    Structure,
    Symbol,
    Expression,
    Scaffolding,
}

/// <summary>A compact source/generated relation. A source span may deliberately occur more than once.</summary>
public sealed class LuiMapEntry
{
    public LuiMapEntry(LuiSpan source, LuiSpan generated, LuiMapKind kind, bool hidden)
    {
        Source = source;
        Generated = generated;
        Kind = kind;
        Hidden = hidden;
    }

    public LuiSpan Source { get; }
    public LuiSpan Generated { get; }
    public LuiMapKind Kind { get; }
    public bool Hidden { get; }
}

public sealed class LuiSourceMap
{
    public LuiSourceMap(LuiFreshnessIdentity identity, IReadOnlyList<LuiMapEntry> entries)
    {
        Identity = identity;
        Entries = entries
            .OrderBy(entry => entry.Source.Start)
            .ThenBy(entry => entry.Source.Length)
            .ThenBy(entry => entry.Generated.Start)
            .ThenBy(entry => entry.Generated.Length)
            .ThenBy(entry => entry.Hidden)
            .ThenBy(entry => entry.Kind)
            .ToArray();
    }

    public LuiFreshnessIdentity Identity { get; }
    public IReadOnlyList<LuiMapEntry> Entries { get; }

    public IReadOnlyList<LuiMapEntry> FromSource(LuiSpan span) =>
        Entries.Where(entry => Intersects(entry.Source, span)).ToArray();

    public IReadOnlyList<LuiMapEntry> FromGenerated(LuiSpan span) =>
        Entries.Where(entry => Intersects(entry.Generated, span)).ToArray();

    // Half-open ranges contain their start and exclude their end. A point query selects
    // ranges containing that point plus entries anchored at that exact point.
    private static bool Intersects(LuiSpan left, LuiSpan right)
    {
        if (left.Start < 0 || right.Start < 0)
            return false;
        if (left.Length == 0 && right.Length == 0)
            return left.Start == right.Start;
        if (left.Length == 0)
            return right.Start <= left.Start && left.Start < right.End;
        if (right.Length == 0)
            return left.Start <= right.Start && right.Start < left.End;
        return left.Start < right.End && right.Start < left.End;
    }
}

public sealed class LuiCompilationResult
{
    public LuiCompilationResult(
        LuiFreshnessIdentity identity,
        string? source,
        LuiSourceMap map,
        IReadOnlyList<LuiDiagnostic> diagnostics
    )
    {
        Identity = identity;
        Source = source;
        Map = map;
        Diagnostics = diagnostics;
    }

    public LuiFreshnessIdentity Identity { get; }

    /// <summary>Null when binding failed; callers must never publish stale output.</summary>
    public string? Source { get; }
    public LuiSourceMap Map { get; }
    public IReadOnlyList<LuiDiagnostic> Diagnostics { get; }
    public bool Success => Source is not null && Diagnostics.Count == 0;
}
