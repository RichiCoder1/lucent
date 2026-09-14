using System.Diagnostics;
using System.Xml.Linq;
using Lucent.Lui.LanguageServer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.LanguageServer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RequirementEditorContracts
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ReferencedAssemblyRequirementsRemainStaticAndRefreshWithTheReference()
    {
        var root = Path.GetFullPath(
            Path.Combine(
                Path.GetTempPath(),
                "lucent-requirement-metadata-" + Guid.NewGuid().ToString("N")
            )
        );
        Directory.CreateDirectory(root);
        try
        {
            var core = LanguageServerTests.CoreMetadataReference;
            var corePath = XElement.Parse(core).Element("HintPath")!.Value;
            var dotnetRoot = new DirectoryInfo(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!
            )
                .Parent!
                .Parent!
                .Parent!
                .FullName;
            var referenceDirectory = Directory
                .EnumerateDirectories(
                    Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref")
                )
                .OrderByDescending(path => Version.Parse(Path.GetFileName(path)))
                .Select(path => Path.Combine(path, "ref", "net10.0"))
                .First(Directory.Exists);
            var references = Directory
                .EnumerateFiles(referenceDirectory, "*.dll")
                .Append(corePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
            string Emit(string kind)
            {
                var path = Path.Combine(root, kind + ".dll");
                var source = $$"""
using Lucent.Core;
namespace PackageLibrary;
public interface IStore { }
public static class PublicComponents {
    [LucentComponent]
    [ComponentRequirement(typeof(IStore), ComponentRequirementKind.{{kind}}, "store", "Features/Packaged.lui", 3, 9)]
    public static ComponentRecipe Packaged() => throw new System.InvalidOperationException("Never execute component factories in the editor.");
}
""";
                var compilation = CSharpCompilation.Create(
                    "RequirementMetadata" + kind,
                    [CSharpSyntaxTree.ParseText(source)],
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                );
                var emitted = compilation.Emit(path);
                Assert.IsTrue(emitted.Success, String.Join("\n", emitted.Diagnostics));
                return path;
            }
            var project = Path.Combine(root, "Consumer.csproj");
            string Project(string library) =>
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup>{core}<Reference Include=\"RequirementMetadata\"><HintPath>{System.Security.SecurityElement.Escape(library)}</HintPath></Reference><AdditionalFiles Include=\"*.lui\"/></ItemGroup></Project>";
            await File.WriteAllTextAsync(project, Project(Emit("Inject")));
            const string source =
                "namespace Consumer; using static PackageLibrary.PublicComponents; public component Main() { <Packaged /> }";
            var document = Path.Combine(root, "Main.lui");
            await File.WriteAllTextAsync(document, source);
            using var context = await LuiProjectContext.LoadAsync(project, CancellationToken.None);
            var uri = new Uri(document);
            var offset = source.IndexOf("Packaged", StringComparison.Ordinal) + 2;
            var before = await context.CompileAsync(uri, CancellationToken.None);
            Assert.IsNotNull(
                before,
                String.Join(
                    "\n",
                    (await context.DiagnosticsAsync(uri, CancellationToken.None))?.Select(item =>
                        item.Code + ": " + item.Message
                    ) ?? []
                )
            );
            Assert.IsTrue(before.Result.Success);
            var hover = await context.HoverAsync(uri, offset, CancellationToken.None);
            Assert.IsNotNull(hover);
            StringAssert.Contains(hover.Documentation!, "Injected services (borrowed)");
            StringAssert.Contains(hover.Documentation!, "IStore store");
            await File.WriteAllTextAsync(project, Project(Emit("Context")));
            Assert.IsTrue(
                await context.ReloadIfRelevantAsync(new Uri(project), CancellationToken.None)
            );
            Assert.IsFalse(await context.IsCurrentAsync(before.Result, CancellationToken.None));
            var changed = await context.HoverAsync(uri, offset, CancellationToken.None);
            Assert.IsNotNull(changed);
            StringAssert.Contains(changed.Documentation!, "Required context (borrowed)");
            Assert.IsFalse(
                changed.Documentation!.Contains("Injected services", StringComparison.Ordinal)
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task RequirementsHaveStaticHoverNavigationRenameAndIncrementalInvalidation()
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        var root = Path.GetFullPath(
            Path.Combine(temp, "lucent-requirements-" + Guid.NewGuid().ToString("N"))
        );
        Assert.IsTrue(
            root.StartsWith(
                temp.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
        );
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "Sample.csproj");
            var core = LanguageServerTests.CoreMetadataReference;
            await File.WriteAllTextAsync(
                project,
                $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Sample</RootNamespace><LangVersion>preview</LangVersion><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>{core}<AdditionalFiles Include="*.lui" /></ItemGroup>
</Project>
"""
            );
            await File.WriteAllTextAsync(
                Path.Combine(root, "Models.cs"),
                """
namespace Sample;
public sealed record Workspace(string Name);
public interface IStore { string Read(); }
public static class ProductionServices {
    public static IStore Create() => throw new System.InvalidOperationException("Editor must never execute a factory.");
}
"""
            );
            const string child = """
namespace Sample; using Lucent.Core;
public component Child() {
    context Workspace workspace;
    inject IStore store;
    <Text>{workspace.Name}</Text>
}
""";
            const string caller =
                "namespace Sample; using Lucent.Core; public component Caller() { <Child /> }";
            var childPath = Path.Combine(root, "Child.lui");
            var callerPath = Path.Combine(root, "Caller.lui");
            await File.WriteAllTextAsync(childPath, child);
            await File.WriteAllTextAsync(callerPath, caller);
            using var context = await LuiProjectContext.LoadAsync(project, CancellationToken.None);
            var childUri = new Uri(childPath);
            var callerUri = new Uri(callerPath);
            var first = await context.CompileAsync(callerUri, CancellationToken.None);
            Assert.IsNotNull(first);
            Assert.IsTrue(
                first.Result.Success,
                String.Join("\n", first.Result.Diagnostics.Select(item => item.Message))
            );
            var invocationOffset = caller.IndexOf("Child", StringComparison.Ordinal) + 2;
            var hover = await context.HoverAsync(
                callerUri,
                invocationOffset,
                CancellationToken.None
            );
            Assert.IsNotNull(hover);
            StringAssert.Contains(hover.Documentation!, "Required context (borrowed)");
            StringAssert.Contains(hover.Documentation!, "Injected services (borrowed)");
            StringAssert.Contains(hover.Documentation!, "Workspace workspace");
            var memberOffset = child.LastIndexOf("workspace", StringComparison.Ordinal) + 2;
            var member = await context.HoverAsync(childUri, memberOffset, CancellationToken.None);
            Assert.IsNotNull(member);
            StringAssert.Contains(member.Value, "context Workspace workspace");
            var definition = await context.DefinitionAsync(
                childUri,
                memberOffset,
                CancellationToken.None
            );
            Assert.IsNotNull(definition);
            Assert.AreEqual(childUri, definition.Uri);
            Assert.AreEqual(
                child.IndexOf("workspace", StringComparison.Ordinal),
                definition.Span.Start
            );
            var references = await context.ReferencesAsync(
                childUri,
                memberOffset,
                true,
                CancellationToken.None
            );
            Assert.IsNotNull(references);
            Assert.IsTrue(references.Locations.Count >= 2);
            var rename = await context.RenameAsync(
                childUri,
                memberOffset,
                "currentWorkspace",
                CancellationToken.None
            );
            Assert.IsNotNull(rename);
            Assert.IsTrue(rename.Edits.Any(edit => edit.Uri == childUri && edit.Spans.Count >= 2));

            var members = await context.CompletionsAsync(
                childUri,
                child.LastIndexOf(".Name", StringComparison.Ordinal) + 1,
                CancellationToken.None
            );
            Assert.IsNotNull(members);
            Assert.IsTrue(members.Any(item => item.Label == "Name"));
            var tags = await context.CompletionsAsync(
                callerUri,
                invocationOffset,
                CancellationToken.None
            );
            Assert.IsTrue(tags.Any(item => item.Label == "Provide"));
            var symbols = await context.DocumentSymbolsAsync(childUri, CancellationToken.None);
            Assert.IsNotNull(symbols);
            Assert.IsTrue(
                symbols.SelectMany(item => item.Children).Any(item => item.Name == "workspace")
            );

            context.ReplaceText(
                childUri,
                child.Replace("workspace.Name", "workspace.Name + \"!\"", StringComparison.Ordinal)
            );
            var unchanged = await context.CompileAsync(callerUri, CancellationToken.None);
            Assert.IsNotNull(unchanged);
            Assert.AreSame(first.Result, unchanged.Result);
            context.ReplaceText(
                childUri,
                child.Replace(
                    "inject IStore store;",
                    "context IStore store;",
                    StringComparison.Ordinal
                )
            );
            var changed = await context.CompileAsync(callerUri, CancellationToken.None);
            Assert.IsNotNull(changed);
            Assert.AreNotSame(first.Result, changed.Result);
            var changedHover = await context.HoverAsync(
                callerUri,
                invocationOffset,
                CancellationToken.None
            );
            Assert.IsNotNull(changedHover);
            Assert.IsFalse(
                changedHover.Documentation!.Contains("Injected services", StringComparison.Ordinal)
            );
            StringAssert.Contains(changedHover.Documentation, "IStore store");
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < 10; i++)
                Assert.IsNotNull(
                    await context.HoverAsync(callerUri, invocationOffset, CancellationToken.None)
                );
            TestContext.WriteLine(
                $"Ten warm requirement invocation hovers: {timer.ElapsedMilliseconds} ms."
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
