using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Lui.Compiler;

/// <summary>Immutable freshness inputs required before publishing bound or lowered output.</summary>
/// <remarks>Build and editor hosts compare this value with their current snapshot to reject stale generated source; it has no runtime role.</remarks>
public sealed class LuiFreshnessIdentity : IEquatable<LuiFreshnessIdentity>
{
    /// <summary>Creates an identity for hosts that provide only project, document, and option generations.</summary>
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

    /// <summary>Creates an identity with explicit project, sibling-index, language, compiler, reference, and global-using generations.</summary>
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
            "",
            ""
        ) { }

    /// <summary>Creates the complete immutable identity used for generated-source and source-map freshness checks.</summary>
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
            defines,
            ""
        ) { }

    /// <summary>Creates the complete immutable identity including the evaluated C# root namespace used by style token binding.</summary>
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
        string defines,
        string rootNamespace
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
        RootNamespace = rootNamespace ?? throw new ArgumentNullException(nameof(rootNamespace));
    }

    /// <summary>Host generation identifying the evaluated project state.</summary>
    public string ProjectEpoch { get; }

    /// <summary>Stable identity of the evaluated project.</summary>
    public string ProjectIdentity { get; }

    /// <summary>Logical identity of the authored <c>.lui</c> document.</summary>
    public LuiDocumentIdentity Document { get; }

    /// <summary>Host generation for the document content.</summary>
    public string DocumentVersion { get; }

    /// <summary>Generation of the Roslyn compilation inputs.</summary>
    public string CompilationGeneration { get; }

    /// <summary>Generation of indexed sibling component declarations.</summary>
    public string SiblingIndexGeneration { get; }

    /// <summary>Effective C# language-version identity.</summary>
    public string LanguageVersion { get; }

    /// <summary>Compiler implementation version identity.</summary>
    public string CompilerVersion { get; }

    /// <summary>Generation of metadata references.</summary>
    public string ReferencesGeneration { get; }

    /// <summary>Generation of global using directives.</summary>
    public string GlobalUsingsGeneration { get; }

    /// <summary>Effective compiler-options identity.</summary>
    public string Options { get; }

    /// <summary>Effective conditional-compilation symbols identity.</summary>
    public string Defines { get; }

    /// <summary>Evaluated C# root namespace whose optional <c>Tokens</c> type is visible only to style values.</summary>
    public string RootNamespace { get; }

    /// <summary>Deterministic generated C# hint name for <see cref="Document"/>.</summary>
    public string HintName => "Lucent.Lui." + Document.StableId + ".g.cs";

    /// <summary>Deterministic hash covering every freshness input used by equality and publication.</summary>
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
                + "\0"
                + RootNamespace
        );

    /// <summary>Compares every freshness input through the deterministic map identity.</summary>
    public bool Equals(LuiFreshnessIdentity? other) =>
        other is not null && MapIdentity == other.MapIdentity;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LuiFreshnessIdentity);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(MapIdentity);

    /// <summary>Returns whether this result identity still matches the host's current identity.</summary>
    public bool CanPublishTo(LuiFreshnessIdentity current) => Equals(current);
}

/// <summary>Role of a source-map relation between authored <c>.lui</c> and generated C# spans.</summary>
public enum LuiMapKind
{
    /// <summary>Generated structure corresponding to authored markup or control-flow structure.</summary>
    Structure,

    /// <summary>Generated symbol corresponding to an authored identifier.</summary>
    Symbol,

    /// <summary>Generated expression corresponding to an authored C# island.</summary>
    Expression,

    /// <summary>Compiler-added generated text without a directly visible authored construct.</summary>
    Scaffolding,
}

/// <summary>A compact source/generated relation. A source span may deliberately occur more than once.</summary>
public sealed class LuiMapEntry
{
    /// <summary>Creates one immutable relation between authored and generated spans.</summary>
    public LuiMapEntry(LuiSpan source, LuiSpan generated, LuiMapKind kind, bool hidden)
    {
        Source = source;
        Generated = generated;
        Kind = kind;
        Hidden = hidden;
    }

    /// <summary>Authored <c>.lui</c> range measured against the parsed document source.</summary>
    public LuiSpan Source { get; }

    /// <summary>Generated C# range measured against the compilation result source.</summary>
    public LuiSpan Generated { get; }

    /// <summary>Semantic role of this correspondence.</summary>
    public LuiMapKind Kind { get; }

    /// <summary>Whether tooling should hide this generated relation from ordinary source navigation.</summary>
    public bool Hidden { get; }
}

/// <summary>Deterministic immutable bidirectional map between one <c>.lui</c> document and its generated C#.</summary>
/// <remarks>Source and generated spans are in different texts. Queries return all intersections because lowering can map one source range to several generated ranges.</remarks>
public sealed class LuiSourceMap
{
    /// <summary>Creates and deterministically orders source/generated relations for one freshness identity.</summary>
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

    /// <summary>Freshness identity of both mapped texts.</summary>
    public LuiFreshnessIdentity Identity { get; }

    /// <summary>Immutable entries sorted deterministically by source and generated span.</summary>
    public IReadOnlyList<LuiMapEntry> Entries { get; }

    /// <summary>Finds every generated relation whose authored source span intersects <paramref name="span"/>.</summary>
    /// <param name="span">Half-open range in the <c>.lui</c> source text.</param>
    /// <returns>All matching relations in deterministic order.</returns>
    public IReadOnlyList<LuiMapEntry> FromSource(LuiSpan span) =>
        Entries.Where(entry => Intersects(entry.Source, span)).ToArray();

    /// <summary>Finds every authored relation whose generated C# span intersects <paramref name="span"/>.</summary>
    /// <param name="span">Half-open range in the generated C# source text.</param>
    /// <returns>All matching relations in deterministic order.</returns>
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

/// <summary>Immutable lowering outcome consumed by build or editor tooling.</summary>
/// <remarks>Publish <see cref="Source"/> only when <see cref="Success"/> is true and <see cref="Identity"/> still matches the current host snapshot.</remarks>
public sealed class LuiCompilationResult
{
    /// <summary>Creates a lowering outcome with generated source when binding succeeds and diagnostics otherwise.</summary>
    public LuiCompilationResult(
        LuiFreshnessIdentity identity,
        string? source,
        LuiSourceMap map,
        IReadOnlyList<LuiDiagnostic> diagnostics,
        string? projectionSource = null
    )
    {
        Identity = identity;
        Source = source;
        Map = map;
        Diagnostics = diagnostics;
        ProjectionSource = projectionSource ?? source;
    }

    /// <summary>Freshness snapshot used to reject obsolete output.</summary>
    public LuiFreshnessIdentity Identity { get; }

    /// <summary>Null when binding failed; callers must never publish stale output.</summary>
    public string? Source { get; }

    /// <summary>Recovered generated C# used only by editor semantic projection; it is never publishable output.</summary>
    public string? ProjectionSource { get; }

    /// <summary>Bidirectional map between the document's authored spans and generated C# spans.</summary>
    public LuiSourceMap Map { get; }

    /// <summary>Immutable parser, binding, and lowering diagnostics measured against authored spans.</summary>
    public IReadOnlyList<LuiDiagnostic> Diagnostics { get; }

    /// <summary>Whether generated source is available with no diagnostics; callers must still perform freshness validation.</summary>
    public bool Success => Source is not null && Diagnostics.Count == 0;
}
