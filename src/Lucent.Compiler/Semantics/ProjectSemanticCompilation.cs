using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Compiler.Semantics;

internal sealed class ProjectSemanticCompilation
{
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
    }

    public CSharpCompilation Compilation { get; }

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
        var referencePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in context?.References ?? [])
        {
            if (File.Exists(path))
            {
                referencePaths[Path.GetFileName(path)] = Path.GetFullPath(path);
            }
        }

        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (referencePaths.Count == 0 && !string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                referencePaths.TryAdd(Path.GetFileName(path), path);
            }
        }

        if (!referencePaths.ContainsKey("Avalonia.Base.dll"))
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
                referencePaths.TryAdd(Path.GetFileName(path), path);
            }
        }

        var references = referencePaths.Values
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
        var syntaxTrees = (context?.Sources ?? [])
            .Where(File.Exists)
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                path))
            .ToArray();

        return CSharpCompilation.Create(
            "Lucent.ProjectSemantics",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
