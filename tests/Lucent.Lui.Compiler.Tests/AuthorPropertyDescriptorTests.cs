using System;
using System.Linq;
using Lucent.Core;
using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class AuthorPropertyDescriptorTests
{
    private static readonly string[] ExpectedInsetAliases = ["OldInset"];
    private static readonly string[] ExpectedTransitions =
    [
        "global::Lucent.Core.VisualProperties.Background",
        "global::Lucent.Core.VisualProperties.Opacity",
        "global::Lucent.Core.TypographyProperties.TextColor",
    ];

    private static readonly string[] ExpectedOverflowAliases = ["Overflow"];

    [TestMethod]
    public void CoreGroupsAndOverridesProduceOneDeterministicDescriptorSet()
    {
        var compilation = CreateCompilation();
        var descriptors = LuiPropertyCatalog.Discover(compilation);
        var cachedDescriptors = LuiPropertyCatalog.Discover(compilation);
        Assert.IsTrue(
            ReferenceEquals(descriptors, cachedDescriptors),
            "one compilation should reuse its immutable descriptor snapshot."
        );
        var descriptorList =
            descriptors as System.Collections.Generic.IList<LuiAuthorPropertyDescriptor>;
        Assert.IsNotNull(descriptorList);
        Assert.IsTrue(descriptorList!.IsReadOnly);
        var identities = descriptors.Select(item => item.SymbolIdentity).ToArray();
        Assert.IsTrue(
            identities.SequenceEqual(identities.OrderBy(item => item, StringComparer.Ordinal)),
            "author property descriptors must be sorted by stable symbol identity."
        );

        var axis = descriptors.Single(item =>
            item.SymbolIdentity.EndsWith("LayoutProperties.Axis", StringComparison.Ordinal)
        );
        Assert.AreEqual("Axis", axis.AuthorName);
        Assert.AreEqual("global::Lucent.Core.LayoutProperties.Axis", axis.SymbolIdentity);
        Assert.AreEqual("global::Lucent.Core.LayoutAxis", axis.ValueTypeName);
        Assert.AreEqual(LuiAuthoringCapabilities.Styled, axis.Capabilities);
        Assert.AreEqual(
            LuiAuthoringInputForms.Value
                | LuiAuthoringInputForms.Reader
                | LuiAuthoringInputForms.Token,
            axis.InputForms
        );
        Assert.AreEqual(axis.SymbolIdentity, axis.StyleTarget);
        Assert.IsNull(axis.SemanticTarget);

        var overflow = descriptors.Single(item =>
            item.SymbolIdentity.EndsWith("TypographyProperties.Overflow", StringComparison.Ordinal)
        );
        CollectionAssert.AreEquivalent(ExpectedOverflowAliases, overflow.Aliases.ToArray());
        CollectionAssert.Contains(overflow.Names.ToArray(), "TextOverflow");
        CollectionAssert.Contains(overflow.Names.ToArray(), "Overflow");

        var transitions = descriptors
            .Where(item => item.TransitionEligible)
            .Select(item => item.SymbolIdentity)
            .ToArray();
        CollectionAssert.AreEquivalent(ExpectedTransitions, transitions);
        Assert.IsNotNull(
            descriptors.Single(item =>
                item.SymbolIdentity.EndsWith("ImageProperties.Fit", StringComparison.Ordinal)
            )
        );
        Assert.IsNotNull(
            descriptors.Single(item =>
                item.SymbolIdentity.EndsWith(
                    "ScrollBarProperties.Visibility",
                    StringComparison.Ordinal
                )
            )
        );
    }

    [TestMethod]
    public void SourceGroupOverridesInheritUnsetFieldMetadata()
    {
        var compilation = CreateCompilation(
            CSharpSyntaxTree.ParseText(
                """
                    using Lucent.Core;
                    namespace Sample;
                    [StylePropertyGroup(Capabilities = StyleAuthoringCapabilities.Styled, InputForms = StyleAuthoringInputForms.Value, PaintInvalidating = false)]
                    public static class CustomProperties
                    {
                        [StyleProperty(Name = "Inset", Aliases = new[] { "OldInset" }, SemanticTarget = "sample.semantic")]
                    public static readonly Property<int> Padding = new("sample-padding", 0);
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
            )
        );
        Assert.IsFalse(
            compilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error)
        );

        var descriptor = LuiPropertyCatalog
            .Discover(compilation)
            .Single(item =>
                item.SymbolIdentity.EndsWith("CustomProperties.Padding", StringComparison.Ordinal)
            );
        Assert.AreEqual("Inset", descriptor.AuthorName);
        CollectionAssert.AreEquivalent(ExpectedInsetAliases, descriptor.Aliases.ToArray());
        Assert.AreEqual(LuiAuthoringCapabilities.Styled, descriptor.Capabilities);
        Assert.AreEqual(LuiAuthoringInputForms.Value, descriptor.InputForms);
        Assert.AreEqual("sample.semantic", descriptor.SemanticTarget);
        Assert.IsFalse(descriptor.PaintInvalidating);
        Assert.IsFalse(descriptor.TransitionEligible);
        Assert.AreEqual("global::Sample.CustomProperties.Padding", descriptor.SymbolIdentity);
        Assert.IsFalse(
            LuiPropertyCatalog
                .Discover(compilation)
                .Any(item =>
                    item.SymbolIdentity.Contains("NonStaticProperties", StringComparison.Ordinal)
                )
        );
        Assert.IsFalse(
            LuiPropertyCatalog
                .Discover(compilation)
                .Any(item =>
                    item.SymbolIdentity.Contains("MutableProperties", StringComparison.Ordinal)
                )
        );
    }

    [TestMethod]
    public void FindResolvesFieldCanonicalAliasAndSymbolNames()
    {
        var compilation = CreateCompilation();
        var descriptors = LuiPropertyCatalog.Discover(compilation);
        var overflow = descriptors.Single(item =>
            item.SymbolIdentity.EndsWith("TypographyProperties.Overflow", StringComparison.Ordinal)
        );
        Assert.AreEqual(overflow, LuiPropertyCatalog.Find(compilation, overflow.SymbolIdentity));
        Assert.AreEqual(overflow, LuiPropertyCatalog.Find(compilation, "TextOverflow"));
        Assert.AreEqual(overflow, LuiPropertyCatalog.Find(compilation, "Overflow"));
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees) =>
        CSharpCompilation.Create(
            "author-property-descriptors",
            trees,
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

    private static MetadataReference[] References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ComponentRecipe).Assembly.Location))
            .ToArray();
}
