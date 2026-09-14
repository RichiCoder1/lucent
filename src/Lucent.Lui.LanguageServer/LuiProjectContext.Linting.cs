using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lucent.Lui.Compiler;

namespace Lucent.Lui.LanguageServer;

internal sealed partial class LuiProjectContext
{
    private readonly ConditionalWeakTable<LuiCompilationResult, LintCache> lintResults = new();

    internal async Task<LuiEditorLintSnapshot?> LintAsync(
        Uri uri,
        LuiLintOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(options);
        lock (gate)
            ThrowIfDisposed();
        if (!Owns(uri))
            return null;
        var snapshot = await SnapshotAsync(uri, cancellationToken).ConfigureAwait(false);
        if (snapshot.MetadataDiagnostic is not null)
            return null;
        var compiled = CompileSnapshot(snapshot, cancellationToken);
        var lint = LintSnapshot(snapshot, compiled, options, cancellationToken);
        if (!await CanPublishAsync(snapshot, cancellationToken).ConfigureAwait(false))
            return null;
        return new LuiEditorLintSnapshot(
            snapshot.Uri,
            snapshot.Document.Source,
            snapshot.Document.Version,
            snapshot.Epoch,
            lint
        );
    }

    private LuiLintResult LintSnapshot(
        Snapshot snapshot,
        LuiCompilationResult compiled,
        LuiLintOptions options,
        CancellationToken cancellationToken
    )
    {
        var cache = lintResults.GetValue(compiled, _ => new LintCache());
        var key = (options.DeclarationOrder, options.DefaultContentPlacement);
        lock (cache.Results)
        {
            if (cache.Results.TryGetValue(key, out var cached))
                return cached;
        }
        var result = LuiLintAnalyzer.AnalyzeCompiled(
            snapshot.Document.Syntax,
            snapshot.Compilation,
            snapshot.Identity,
            compiled,
            options,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();
        lock (cache.Results)
        {
            if (!cache.Results.TryGetValue(key, out var cached))
                cache.Results.Add(key, result);
            else
                result = cached;
        }
        return result;
    }

    private sealed class LintCache
    {
        internal Dictionary<(LuiDeclarationOrder, bool), LuiLintResult> Results { get; } = [];
    }
}

internal sealed class LuiEditorLintSnapshot
{
    internal LuiEditorLintSnapshot(
        Uri uri,
        string source,
        string documentVersion,
        long epoch,
        LuiLintResult result
    )
    {
        Uri = uri;
        Source = source;
        DocumentVersion = documentVersion;
        Epoch = epoch;
        Result = result;
    }

    internal Uri Uri { get; }
    internal string Source { get; }
    internal string DocumentVersion { get; }
    internal long Epoch { get; }
    internal LuiLintResult Result { get; }
}
