using System;
using System.Linq;
using Lucent.Core;
using Lucent.Lui.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Generator.Tests;

[TestClass]
public sealed class StyleFluencyGeneratorTests
{
    private const string SyntheticCore = """
        using System;
        namespace Lucent.Core
        {
        [Flags]
        public enum StyleAuthoringCapabilities { None = 0, Styled = 1, Accessible = 2 }
        [Flags]
        public enum StyleAuthoringInputForms { None = 0, Value = 1, Reader = 2, Token = 4 }
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class StylePropertyGroupAttribute : Attribute
        {
            public StyleAuthoringCapabilities Capabilities { get; set; } = StyleAuthoringCapabilities.Styled;
            public StyleAuthoringInputForms InputForms { get; set; } = StyleAuthoringInputForms.Value | StyleAuthoringInputForms.Reader | StyleAuthoringInputForms.Token;
            public bool PaintInvalidating { get; set; } = true;
        }
        [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
        public sealed class StylePropertyAttribute : Attribute
        {
            public string? Name { get; set; }
            public string[] Aliases { get; set; } = Array.Empty<string>();
            public StyleAuthoringCapabilities Capabilities { get; set; } = StyleAuthoringCapabilities.Styled;
            public StyleAuthoringInputForms InputForms { get; set; } = StyleAuthoringInputForms.Value | StyleAuthoringInputForms.Reader | StyleAuthoringInputForms.Token;
            public string? StyleTarget { get; set; }
            public string? SemanticTarget { get; set; }
            public bool TransitionEligible { get; set; }
            public bool PaintInvalidating { get; set; } = true;
        }
        public sealed class Property<T>
        {
            public Property(string name, T value) { }
        }
        public sealed class Token<T> { }
        public class Style
        {
            public Style Set<T>(Property<T> property, T value) => this;
            public Style Set<T>(Property<T> property, Token<T> value) => this;
            public Style Bind<T>(Property<T> property, Func<T> value) => this;
        }
        public static partial class StyleFluency { }
        }
        """;

    [TestMethod]
    public void GeneratesSameNameValueReaderAndTokenHelpersFromOneDescriptor()
    {
        var core = CSharpSyntaxTree.ParseText(SyntheticCore);
        var source = CSharpSyntaxTree.ParseText(
            """
            using Lucent.Core;
            namespace Sample;
            [StylePropertyGroup(InputForms = StyleAuthoringInputForms.Value | StyleAuthoringInputForms.Reader | StyleAuthoringInputForms.Token)]
            public static class CustomProperties
            {
                [StyleProperty(Name = "Inset", Aliases = new[] { "OldInset" })]
                public static readonly Property<int> Padding = new("sample-padding", 0);
                public static readonly Property<object> Broad = new("sample-broad", new object());
                public static readonly Property<Func<int>> Callback = new("sample-callback", () => 0);
            }
            [StylePropertyGroup(Capabilities = StyleAuthoringCapabilities.Accessible)]
            public static class SemanticProperties
            {
                public static readonly Property<int> Name = new("sample-name", 0);
            }

            [StylePropertyGroup]
            public class NonStaticProperties
            {
                public static readonly Property<int> Ignored = new("ignored", 0);
            }

            [StylePropertyGroup]
            public static class MutableProperties
            {
                public static Property<int> Ignored = new("mutable", 0);
            }
            """
        );
        var compilation = CSharpCompilation.Create(
            "Lucent.Core",
            new[] { core, source },
            References(false),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var first = Run(compilation);
        var generated = first
            .Results.Single()
            .GeneratedSources.Single(item => item.HintName == "Lucent.Core.StyleFluency.g.cs")
            .SourceText.ToString();

        Assert.IsFalse(
            first.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error),
            String.Join(" | ", first.Diagnostics.Select(item => item.ToString()))
        );
        StringAssert.Contains(generated, "global::Sample.CustomProperties.Padding");
        StringAssert.Contains(generated, "public static partial class StyleFluency");
        Assert.IsFalse(generated.Contains("GeneratedStyleFluency", StringComparison.Ordinal));
        StringAssert.Contains(generated, "Inset");
        StringAssert.Contains(generated, "OldInset");
        StringAssert.Contains(generated, "global::System.Func<int> value");
        StringAssert.Contains(generated, "global::Lucent.Core.Token<int> value");
        StringAssert.Contains(
            generated,
            "style.Bind(global::Sample.CustomProperties.Padding, value)"
        );
        StringAssert.Contains(
            generated,
            "style.Set(global::Sample.CustomProperties.Padding, value)"
        );
        StringAssert.Contains(
            generated,
            "global::System.Runtime.CompilerServices.OverloadResolutionPriority(1)"
        );
        Assert.IsFalse(generated.Contains("SemanticProperties", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("NonStaticProperties", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("MutableProperties", StringComparison.Ordinal));
        StringAssert.Contains(generated, "<summary>Applies Inset from a snapshot value.</summary>");

        var methods = CSharpSyntaxTree
            .ParseText(generated)
            .GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .ToArray();
        Assert.AreEqual(3, methods.Count(method => method.Identifier.ValueText == "Inset"));
        Assert.AreEqual(3, methods.Count(method => method.Identifier.ValueText == "OldInset"));
        Assert.IsTrue(
            methods
                .Where(method => method.Identifier.ValueText == "Inset")
                .Any(method =>
                    method.AttributeLists.Any(list =>
                        list.ToString()
                            .Contains("OverloadResolutionPriority(1)", StringComparison.Ordinal)
                    )
                ),
            "the proven scalar value overload did not receive priority."
        );
        Assert.IsFalse(
            methods
                .Where(method => method.Identifier.ValueText is "Broad" or "Callback")
                .SelectMany(method => method.AttributeLists)
                .Any(list =>
                    list.ToString().Contains("OverloadResolutionPriority", StringComparison.Ordinal)
                ),
            "broad object or callback values must not receive scalar priority."
        );

        var second = Run(compilation);
        var generatedAgain = second
            .Results.Single()
            .GeneratedSources.Single(item => item.HintName == "Lucent.Core.StyleFluency.g.cs")
            .SourceText.ToString();
        Assert.AreEqual(generated, generatedAgain, "style output was not deterministic.");
    }

    [TestMethod]
    public void RepeatedIncrementalRunsKeepOneBoundedFamilyAndStableOutput()
    {
        var core = CSharpSyntaxTree.ParseText(SyntheticCore);
        var source = CSharpSyntaxTree.ParseText(
            """
            using Lucent.Core;
            namespace Sample;
            [StylePropertyGroup]
            public static class CustomProperties
            {
                public static readonly Property<int> Padding = new("sample-padding", 0);
            }
            """
        );
        var compilation = CSharpCompilation.Create(
            "Lucent.Core",
            new[] { core, source },
            References(false),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new StyleFluencyGenerator().AsSourceGenerator()
        );
        driver = driver.RunGenerators(compilation);
        var first = driver.GetRunResult();
        driver = driver.RunGenerators(compilation);
        var second = driver.GetRunResult();
        var firstSource = first.Results.Single().GeneratedSources.Single().SourceText.ToString();
        var secondSource = second.Results.Single().GeneratedSources.Single().SourceText.ToString();

        Assert.AreEqual(firstSource, secondSource);
        Assert.AreEqual(
            3,
            CSharpSyntaxTree
                .ParseText(firstSource)
                .GetRoot()
                .DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                .Count()
        );
        Assert.IsFalse(firstSource.Contains("GeneratedStyleFluency", StringComparison.Ordinal));
        Assert.IsFalse(
            first.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error),
            String.Join(" | ", first.Diagnostics.Select(item => item.ToString()))
        );
        Assert.IsFalse(
            second.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error),
            String.Join(" | ", second.Diagnostics.Select(item => item.ToString()))
        );
    }

    [TestMethod]
    public void ConsumerDefinedGroupsDoNotShadowCoreStyleFluency()
    {
        var source = CSharpSyntaxTree.ParseText(
            """
            using Lucent.Core;
            namespace Sample;
            [StylePropertyGroup]
            public static class CustomProperties
            {
                public static readonly Property<int> Padding = new("sample-padding", 0);
            }
            """
        );
        var compilation = CSharpCompilation.Create(
            "consumer-style-fluency",
            new[] { source },
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var result = Run(compilation);
        Assert.AreEqual(0, result.Results.Single().GeneratedSources.Length);
        Assert.IsFalse(
            result.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error),
            String.Join(" | ", result.Diagnostics.Select(item => item.ToString()))
        );
    }

    private static GeneratorDriverRunResult Run(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new StyleFluencyGenerator().AsSourceGenerator()
        );
        return driver.RunGenerators(compilation).GetRunResult();
    }

    private static MetadataReference[] References(bool includeCore = true)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator)
            .Where(path =>
                includeCore || !path.EndsWith("Lucent.Core.dll", StringComparison.OrdinalIgnoreCase)
            )
            .Select(path => MetadataReference.CreateFromFile(path));
        return includeCore
            ? references
                .Append(MetadataReference.CreateFromFile(typeof(ComponentRecipe).Assembly.Location))
                .ToArray()
            : references.ToArray();
    }
}
