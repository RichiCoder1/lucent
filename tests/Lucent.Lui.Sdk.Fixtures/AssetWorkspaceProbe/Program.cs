using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

if (args.Length != 1)
    throw new ArgumentException("Expected the asset fixture project path.");

var projectPath = Path.GetFullPath(args[0]);
if (!File.Exists(projectPath))
    throw new FileNotFoundException("The asset fixture project is unavailable.", projectPath);
var projectDirectory = Path.GetDirectoryName(projectPath)!;
if (
    Directory
        .EnumerateFiles(projectDirectory, "Lucent.Assets.g.cs", SearchOption.AllDirectories)
        .Any()
)
    throw new InvalidOperationException(
        "The workspace proof must start without generated asset source."
    );

if (!MSBuildLocator.IsRegistered)
    MSBuildLocator.RegisterDefaults();

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
using var workspace = MSBuildWorkspace.Create();
var workspaceFailures = new List<string>();
workspace.WorkspaceFailed += (_, failure) => workspaceFailures.Add(failure.Diagnostic.Message);

var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: timeout.Token);
var compilation =
    await project.GetCompilationAsync(timeout.Token)
    ?? throw new InvalidOperationException("MSBuildWorkspace did not create a compilation.");

var catalog =
    compilation.GetTypeByMetadataName("Fixture.Resources.Catalog")
    ?? throw new InvalidOperationException(
        "The generated asset catalog is absent from the cold workspace compilation."
    );
var images = catalog.GetTypeMembers("Images").SingleOrDefault();
if (images?.GetMembers("Brand").OfType<IPropertySymbol>().SingleOrDefault() is not { } brand)
    throw new InvalidOperationException("The generated Images.Brand asset accessor is absent.");
if (brand.Type.ToDisplayString() != "Lucent.Core.ImageSource")
    throw new InvalidOperationException($"Images.Brand has unexpected type '{brand.Type}'.");

var assetTree = compilation.SyntaxTrees.SingleOrDefault(tree =>
    tree.FilePath.EndsWith("Lucent.Assets.g.cs", StringComparison.OrdinalIgnoreCase)
);
if (assetTree is null)
    throw new InvalidOperationException(
        "The cold workspace compilation omitted Lucent.Assets.g.cs."
    );

var components =
    compilation.GetTypeByMetadataName("Fixture.Library.Components")
    ?? throw new InvalidOperationException(
        "AssetView.lui did not produce its generated Components type."
    );
if (
    components
        .GetMembers("AssetView")
        .OfType<IMethodSymbol>()
        .SingleOrDefault(method => method.Parameters.Length == 0)
    is null
)
    throw new InvalidOperationException(
        "AssetView.lui did not produce its generated component method."
    );

var errors = compilation
    .GetDiagnostics()
    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
    .ToArray();
if (errors.Length != 0)
    throw new InvalidOperationException(
        "The .lui generator did not bind the cold workspace asset accessor:\n"
            + string.Join("\n", errors.Select(error => error.ToString()))
    );

Console.WriteLine($"workspace-assets: PASS ({assetTree.FilePath})");
if (workspaceFailures.Count != 0)
    Console.WriteLine("workspace-diagnostics: " + string.Join(" | ", workspaceFailures));
