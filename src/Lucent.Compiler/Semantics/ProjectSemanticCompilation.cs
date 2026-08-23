using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Compiler.Semantics;

internal sealed class ProjectSemanticCompilation
{
    // Project contexts are immutable snapshots. Keep a small process-wide LRU so
    // every open Lucent document shares the same Roslyn base without retaining a
    // compilation per document.
    private const int MaxCachedBases = 8;
    private const string GlobalUsingsPath = "Lucent.ProjectGlobalUsings.g.cs";
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, LinkedListNode<CachedBase>> BaseCache =
        new(StringComparer.Ordinal);
    private static readonly LinkedList<CachedBase> BaseLru = [];
    private static readonly string[] DefaultNamespaces =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Threading.Tasks",
        "Avalonia",
        "Avalonia.Controls",
        "Avalonia.Layout",
        "Avalonia.Controls.Primitives",
        "Avalonia.Controls.Presenters",
    ];

    public ProjectSemanticCompilation(
        string componentNamespace,
        IReadOnlyList<string> usingDirectives,
        LucentProjectContext? context)
        : this(componentNamespace, usingDirectives, CreateBaseCompilation(context))
    {
    }

    public ProjectSemanticCompilation(
        string componentNamespace,
        IReadOnlyList<string> usingDirectives,
        CSharpCompilation compilation)
    {
        Imports = BuildImports(componentNamespace, usingDirectives);
        Compilation = compilation;
        ParseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
            ?? CSharpParseOptions.Default;
    }

    public CSharpCompilation Compilation { get; }

    /// <summary>The evaluated project's parse settings for every generated probe.</summary>
    public CSharpParseOptions ParseOptions { get; }

    public IReadOnlyList<string> Imports { get; }

    private static IReadOnlyList<string> BuildImports(
        string componentNamespace,
        IReadOnlyList<string> usingDirectives)
    {
        var imports = new List<string>();
        if (!string.IsNullOrWhiteSpace(componentNamespace))
        {
            imports.Add(componentNamespace);
        }

        imports.AddRange(DefaultNamespaces);
        foreach (var directive in usingDirectives)
        {
            var text = directive.Trim();
            if (text.StartsWith("using ", StringComparison.Ordinal))
            {
                text = text[6..].Trim();
            }

            text = text.TrimEnd(';').Trim();
            if (text.Length > 0 &&
                !text.StartsWith("static ", StringComparison.Ordinal) &&
                !text.Contains('=', StringComparison.Ordinal))
            {
                imports.Add(text);
            }
        }

        return imports.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static CSharpCompilation CreateBaseCompilation(LucentProjectContext? context)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("LUCENT_DISABLE_BASE_CACHE"), "1", StringComparison.Ordinal))
        {
            return CreateUncachedBaseCompilation(CreateSnapshot(context), null);
        }

        var snapshot = CreateSnapshot(context);
        var contentKey = GetContentKey(snapshot);
        lock (CacheLock)
        {
            if (BaseCache.TryGetValue(contentKey, out var existing))
            {
                Touch(existing);
                return existing.Value.Compilation;
            }

            // Source-set changes intentionally miss the exact key, but may still
            // share unaffected trees and references with this project/configuration.
            var previous = BaseLru.FirstOrDefault(entry =>
                string.Equals(entry.ReuseKey, snapshot.ReuseKey, StringComparison.Ordinal));
            var compilation = CreateUncachedBaseCompilation(snapshot, previous);
            var entry = BaseLru.AddFirst(new CachedBase(contentKey, snapshot.ReuseKey,
                compilation, GetTreeTexts(snapshot)));
            BaseCache.Add(contentKey, entry);
            if (BaseLru.Count > MaxCachedBases)
            {
                var expired = BaseLru.Last!;
                BaseCache.Remove(expired.Value.Key);
                BaseLru.RemoveLast();
            }

            return compilation;
        }
    }

    internal static int CachedBaseCount
    {
        get { lock (CacheLock) return BaseCache.Count; }
    }

    internal static void ClearCachedBasesForTests()
    {
        lock (CacheLock)
        {
            BaseCache.Clear();
            BaseLru.Clear();
        }
    }

    private static void Touch(LinkedListNode<CachedBase> entry)
    {
        BaseLru.Remove(entry);
        BaseLru.AddFirst(entry);
    }

    private static CSharpCompilation CreateUncachedBaseCompilation(
        ContextSnapshot snapshot,
        CachedBase? previous)
    {
        var references = previous?.Compilation.References ?? snapshot.ReferencePaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
        var priorTrees = previous?.Compilation.SyntaxTrees
            .ToDictionary(tree => tree.FilePath, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, SyntaxTree>(StringComparer.OrdinalIgnoreCase);
        var trees = new List<SyntaxTree>();

        if (snapshot.GlobalUsingsText is { } globalUsings)
        {
            trees.Add(ReuseOrParse(GlobalUsingsPath, globalUsings, snapshot.ParseOptions,
                previous, priorTrees));
        }

        foreach (var (path, text) in snapshot.SourceTexts)
        {
            trees.Add(ReuseOrParse(path, text, snapshot.ParseOptions, previous, priorTrees));
        }

        return CSharpCompilation.Create(
            "Lucent.ProjectSemantics",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(snapshot.Nullable));
    }

    private static IReadOnlyDictionary<string, string> GetTreeTexts(ContextSnapshot snapshot)
    {
        var texts = new Dictionary<string, string>(snapshot.SourceTexts, StringComparer.OrdinalIgnoreCase);
        if (snapshot.GlobalUsingsText is { } globalUsings)
            texts.Add(GlobalUsingsPath, globalUsings);
        return texts;
    }

    private static SyntaxTree ReuseOrParse(
        string path,
        string text,
        CSharpParseOptions parseOptions,
        CachedBase? previous,
        IReadOnlyDictionary<string, SyntaxTree> priorTrees)
    {
        if (previous?.SourceTexts.TryGetValue(path, out var previousText) == true &&
            string.Equals(previousText, text, StringComparison.Ordinal) &&
            priorTrees.TryGetValue(path, out var priorTree))
        {
            return priorTree;
        }

        return CSharpSyntaxTree.ParseText(text, parseOptions, path);
    }

    private static ContextSnapshot CreateSnapshot(LucentProjectContext? context)
    {
        var referencePaths = ResolveReferencePaths(context);
        var sourcePaths = (context?.Sources ?? [])
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceTexts = sourcePaths
            .Where(File.Exists)
            .ToDictionary(path => path, File.ReadAllText, StringComparer.OrdinalIgnoreCase);
        var parseOptions = CreateParseOptions(context);
        var globalUsings = context?.GlobalUsings.Count > 0
            ? string.Join(Environment.NewLine, context.GlobalUsings.Select(directive => $"global using {directive};"))
            : null;
        var reuseKey = GetReuseKey(context, referencePaths);
        return new ContextSnapshot(referencePaths, sourcePaths, sourceTexts, globalUsings, parseOptions,
            ParseNullable(context?.Nullable), reuseKey);
    }

    private static IReadOnlyList<string> ResolveReferencePaths(LucentProjectContext? context)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in context?.References ?? [])
        {
            if (File.Exists(path)) paths[Path.GetFileName(path)] = Path.GetFullPath(path);
        }

        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (paths.Count == 0 && !string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                paths.TryAdd(Path.GetFileName(path), path);
            }
        }

        if (!paths.ContainsKey("Avalonia.Base.dll"))
        {
            var avaloniaDirectory = new[]
                {
                    AppContext.BaseDirectory,
                    Path.GetDirectoryName(typeof(ProjectSemanticCompilation).Assembly.Location),
                }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .FirstOrDefault(path => File.Exists(Path.Combine(path!, "Avalonia.Controls.dll")));
            if (avaloniaDirectory is null)
            {
                throw new InvalidOperationException(
                    "The standalone Lucent context could not locate Avalonia.Controls.dll. " +
                    "Supply consuming-project reference paths through LucentProjectContext.");
            }

            foreach (var path in Directory.EnumerateFiles(avaloniaDirectory, "Avalonia*.dll"))
            {
                paths.TryAdd(Path.GetFileName(path), path);
            }
        }

        return paths.Values.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string GetReuseKey(LucentProjectContext? context, IReadOnlyList<string> references)
    {
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value));
            hash.AppendData([0]);
        }

        // The project identity prevents a shared reference/tree cache from
        // crossing independently evaluated projects with coincident inputs.
        Add(context?.ProjectPath is { } projectPath ? Path.GetFullPath(projectPath) : "<standalone>");
        Add(context?.TargetFramework ?? string.Empty);
        Add(context?.LanguageVersion ?? string.Empty);
        Add(context?.Nullable ?? string.Empty);
        Add(context?.DefineConstants ?? string.Empty);
        foreach (var directive in context?.GlobalUsings ?? []) Add(directive);
        foreach (var path in (context?.ProjectReferences ?? []).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            Add(FileIdentity(path));
        foreach (var path in references) Add(FileIdentity(path));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string GetContentKey(ContextSnapshot snapshot)
    {
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(snapshot.ReuseKey));
        hash.AppendData([0]);
        foreach (var path in snapshot.SourcePaths)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData([0]);
        }
        foreach (var (path, text) in snapshot.SourceTexts)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(text));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static NullableContextOptions ParseNullable(string? value) =>
        string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase) ? NullableContextOptions.Enable :
        string.Equals(value, "annotations", StringComparison.OrdinalIgnoreCase) ? NullableContextOptions.Annotations :
        string.Equals(value, "warnings", StringComparison.OrdinalIgnoreCase) ? NullableContextOptions.Warnings :
        NullableContextOptions.Disable;

    internal static CSharpParseOptions CreateParseOptions(LucentProjectContext? context)
    {
        var languageVersion = ParseLanguageVersion(context?.LanguageVersion);
        return CSharpParseOptions.Default
            .WithLanguageVersion(languageVersion)
            .WithPreprocessorSymbols(context?.PreprocessorSymbols ?? []);
    }

    internal static LanguageVersion ParseLanguageVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CSharpParseOptions.Default.LanguageVersion;

        return LanguageVersionFacts.TryParse(value.Trim(), out var parsed)
            ? parsed
            : CSharpParseOptions.Default.LanguageVersion;
    }

    private static string FileIdentity(string path)
    {
        var info = new FileInfo(path);
        return $"{Path.GetFullPath(path)}\0{info.Exists}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}";
    }

    private sealed record ContextSnapshot(
        IReadOnlyList<string> ReferencePaths,
        IReadOnlyList<string> SourcePaths,
        IReadOnlyDictionary<string, string> SourceTexts,
        string? GlobalUsingsText,
        CSharpParseOptions ParseOptions,
        NullableContextOptions Nullable,
        string ReuseKey);

    private sealed record CachedBase(
        string Key,
        string ReuseKey,
        CSharpCompilation Compilation,
        IReadOnlyDictionary<string, string> SourceTexts);
}
