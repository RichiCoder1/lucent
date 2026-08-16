using System.CodeDom.Compiler;
using System.Collections.Immutable;
using Lucent.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Lucent.Analyzers.Tests;

[TestClass]
public sealed class MountLifetimeAnalyzerTests
{
    [TestMethod]
    public async Task Undisposed_local_mount_is_reported()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                void Show()
                {
                    var component = new DemoComponent();
                    component.MountRoot();
                }
            }
            """);

        Assert.IsTrue(diagnostics.Any(d => d.Id == MountLifetimeAnalyzer.UndisposedLocalId));
    }

    [TestMethod]
    public async Task Using_mount_is_not_reported()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                void Show()
                {
                    using var component = new DemoComponent();
                    component.MountRoot();
                }
            }
            """);

        Assert.IsFalse(diagnostics.Any(d => d.Id == MountLifetimeAnalyzer.UndisposedLocalId));
    }

    [TestMethod]
    public async Task Explicit_local_disposal_is_not_reported()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                void Show()
                {
                    var component = new DemoComponent();
                    component.MountRoot();
                    component.Dispose();
                }
            }
            """);

        Assert.IsFalse(diagnostics.Any(d => d.Id == MountLifetimeAnalyzer.UndisposedLocalId));
    }

    [TestMethod]
    public async Task Field_ownership_is_not_claimed_by_local_analysis()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                private DemoComponent? component;
                void Show()
                {
                    component = new DemoComponent();
                    component.MountRoot();
                }
            }
            """);

        Assert.IsFalse(diagnostics.Any(d => d.Id == MountLifetimeAnalyzer.UndisposedLocalId));
    }

    [TestMethod]
    public async Task Repeated_mount_and_root_escape_are_reported()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                Lucent.Runtime.Fragment Escape()
                {
                    using var component = new DemoComponent();
                    component.MountRoot();
                    component.Mount();
                    return component.MountRoot();
                }
            }
            """);

        Assert.IsTrue(diagnostics.Any(d => d.Id == MountLifetimeAnalyzer.RepeatedMountId));
        Assert.IsTrue(diagnostics.Any(d => d.Id == MountLifetimeAnalyzer.EscapedRootId));
    }

    [TestMethod]
    public async Task Mutually_exclusive_mounts_are_not_reported_as_repeated()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                void Show(bool condition)
                {
                    var component = new DemoComponent();
                    if (condition) component.MountRoot();
                    else component.MountRoot();
                    component.Dispose();
                }
            }
            """);

        Assert.IsFalse(diagnostics.Any(d => d.Id == MountLifetimeAnalyzer.RepeatedMountId));
    }

    [TestMethod]
    public async Task Early_return_without_disposal_is_reported()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                void Show(bool condition)
                {
                    var component = new DemoComponent();
                    if (condition)
                    {
                        component.MountRoot();
                        return;
                    }
                    component.Dispose();
                }
            }
            """);

        Assert.IsTrue(diagnostics.Any(d => d.Id == MountLifetimeAnalyzer.UndisposedLocalId));
    }

    [TestMethod]
    public async Task Assigned_root_return_is_reported_as_an_escape()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                Avalonia.Controls.Window Escape()
                {
                    using var component = new DemoComponent();
                    var root = component.MountRoot();
                    return root;
                }
            }
            """);

        Assert.IsTrue(diagnostics.Any(d => d.Id == MountLifetimeAnalyzer.EscapedRootId));
    }

    [TestMethod]
    public async Task Non_Lucent_generated_attribute_is_ignored()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                void Show()
                {
                    var component = new DemoComponent();
                    component.MountRoot();
                }
            }
            """, "Other.Tool");

        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Dropped_temporary_has_a_safe_using_code_fix()
    {
        var diagnostics = await Analyze("""
            class Host
            {
                void Show() { new DemoComponent().MountRoot(); }
            }
            """);
        var diagnostic = diagnostics.Single(d => d.Id == MountLifetimeAnalyzer.DroppedTemporaryId);
        var document = CreateDocument("""
            class Host
            {
                void Show() { new DemoComponent().MountRoot(); }
            }
            """);
        var actions = new List<CodeAction>();
        var provider = new MountLifetimeCodeFixProvider();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostic,
            (action, _) => actions.Add(action), CancellationToken.None));

        Assert.AreEqual(1, actions.Count);
        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var apply = operations.OfType<ApplyChangesOperation>().Single();
        var changed = apply.ChangedSolution.GetDocument(document.Id)!;
        var text = (await changed.GetTextAsync()).ToString();
        StringAssert.Contains(text, "using var __lucent_component = new DemoComponent();");
        StringAssert.Contains(text, "__lucent_component.MountRoot();");
    }

    [TestMethod]
    public async Task Dropped_temporary_mount_code_fix_preserves_mount_method()
    {
        var host = """
            class Host
            {
                void Show()
                {
                    using var __lucent_component = new DemoComponent();
                    new DemoComponent().Mount();
                }
            }
            """;
        var diagnostics = await Analyze(host);
        var diagnostic = diagnostics.Single(d => d.Id == MountLifetimeAnalyzer.DroppedTemporaryId);
        var document = CreateDocument(host);
        var actions = new List<CodeAction>();
        await new MountLifetimeCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostic,
            (action, _) => actions.Add(action), CancellationToken.None));

        var operations = await actions.Single().GetOperationsAsync(CancellationToken.None);
        var changed = ((ApplyChangesOperation)operations.Single()).ChangedSolution
            .GetDocument(document.Id)!;
        var text = (await changed.GetTextAsync()).ToString();
        StringAssert.Contains(text, "using var __lucent_component1 = new DemoComponent();");
        StringAssert.Contains(text, "__lucent_component1.Mount();");
        Assert.IsFalse(text.Contains("__lucent_component1.MountRoot();", StringComparison.Ordinal));
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(
        string host,
        string toolName = "Lucent.Compiler")
    {
        var compilation = CreateCompilation(host, toolName);
        return await compilation.WithAnalyzers(
            [new MountLifetimeAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string host, string toolName)
        => CSharpCompilation.Create(
            "AnalyzerTest",
            [CSharpSyntaxTree.ParseText(CreateSource(host, toolName))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static string CreateSource(string host, string toolName) => $$"""
            using System;
            using System.CodeDom.Compiler;
            using Avalonia.Controls;
            using Lucent.Runtime;

            [GeneratedCode("{{toolName}}", "1.0")]
            internal sealed class DemoComponent : IDisposable
            {
                public Fragment Mount() => Fragment.Empty;
                public Window MountRoot() => new Window();
                public void Dispose() { }
            }

            {{host}}
            """;

    private static Document CreateDocument(string host)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "AnalyzerTest", "AnalyzerTest",
            LanguageNames.CSharp, metadataReferences: References));
        return workspace.AddDocument(project.Id, "Host.cs", SourceText.From(CreateSource(host, "Lucent.Compiler")));
    }

    private static ImmutableArray<MetadataReference> References =>
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(GeneratedCodeAttribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Lucent.Runtime.Fragment).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Avalonia.Controls.Control).Assembly.Location),
    ];
}
