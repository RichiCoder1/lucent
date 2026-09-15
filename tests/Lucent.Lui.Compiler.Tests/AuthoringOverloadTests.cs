using System.Reflection;
using Lucent.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class AuthoringOverloadTests
{
    private static readonly MetadataReference[] References = (
        (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
    )
        .Split(Path.PathSeparator)
        .Append(typeof(ComponentRecipe).Assembly.Location)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();

    // These bounded proof families use the real property/token/binding runtime. Production
    // StyleFluency generation belongs to #268; the gate deliberately leaves it unchanged.
    private static readonly (string Name, string Type, string Property, string Value)[] Families =
    [
        ("Label", "string", "Labels", "\"value\""),
        ("Width", "float?", "LayoutProperties.Width", "42f"),
        ("Spacing", "float", "LayoutProperties.Spacing", "12f"),
        ("Axis", "LayoutAxis", "LayoutProperties.Axis", "LayoutAxis.Row"),
        ("Padding", "Insets", "LayoutProperties.Padding", "Insets.Uniform(8)"),
    ];

    [TestMethod]
    public void ConcreteFamiliesSelectValuesReadersTokensAndDefaultsWithoutEagerReads()
    {
        foreach (var (name, type, _, value) in Families)
        {
            AssertRoute(name, type, value, type);
            AssertRoute(name, type, "() => Read()", $"System.Func<{type}>");
            AssertRoute(name, type, "Read", $"System.Func<{type}>");
            AssertRoute(name, type, "token", $"Lucent.Core.Token<{type}>");
            AssertRoute(name, type, "default", type);
            AssertRoute(name, type, $"default({type})", type);
            AssertRoute(name, type, $"(Func<{type}>)null!", $"System.Func<{type}>");
            AssertRoute(name, type, $"(Token<{type}>)null!", $"Lucent.Core.Token<{type}>");
            ExecuteFamily(name, type, value);
        }
        AssertRoute("Width", "float?", "null", "float?");
        AssertRoute("Label", "string", "null!", "string");
        foreach (var name in new[] { "Spacing", "Axis", "Padding" })
            AssertRejected($"_ = Inputs.{name}(Style.Empty, null);", "CS0121");

        Execute(
            """
var graph = new ReactiveGraph();
using var composition = new Composition(graph, "overload-runtime");
using var theme = new ThemeContext(composition.Root.Scope, new Theme("proof"));
var signal = composition.Root.Scope.Signal(15f, "width");
var reads = 0;
float? ReadWidth() { reads++; return signal.Value; }
var snapshot = Inputs.Width(Style.Empty, signal.Value);
var live = Inputs.Width(Style.Empty, ReadWidth);
var token = new Token<float?>("proof-width", 21f);
var themed = Inputs.Width(Style.Empty, token);
if (reads != 0) throw new Exception("Reader evaluated during authoring.");
var a = composition.Child(composition.Root, "snapshot");
var b = composition.Child(composition.Root, "live");
var c = composition.Child(composition.Root, "token");
a.Present(theme, author: snapshot);
b.Present(theme, author: live);
c.Present(theme, author: themed);
graph.Drain();
signal.Value = 30f;
theme.Theme = theme.Theme.Set(token, 45f);
graph.Drain();
if (a.Resolve(LayoutProperties.Width).Value != 15f
    || b.Resolve(LayoutProperties.Width).Value != 30f
    || c.Resolve(LayoutProperties.Width).Value != 45f
    || !c.Resolve(LayoutProperties.Width).Winner.Source.Contains("proof-width"))
    throw new Exception("Snapshot/live/token identity changed.");
var nullable = composition.Child(composition.Root, "null-width");
nullable.Present(theme, author: Inputs.Width(Style.Empty.Width(99), null));
if (nullable.Resolve(LayoutProperties.Width).Value is not null)
    throw new Exception("Width(null) did not assign null.");
var zero = composition.Child(composition.Root, "zero-spacing");
zero.Present(theme, author: Inputs.Spacing(Style.Empty.Spacing(99), default));
if (zero.Resolve(LayoutProperties.Spacing).Value != 0f)
    throw new Exception("Spacing(default) did not select scalar zero.");
ExpectNull(() => Inputs.Width(Style.Empty, (Func<float?>)null!));
ExpectNull(() => Inputs.Width(Style.Empty, (Token<float?>)null!));
return "PASS";
"""
        );
    }

    [TestMethod]
    public void CapabilityAndDelegateBoundariesAreCompileTimeAndNullMetadataFailsClosed()
    {
        const string declarations = """
static AuthorRecipe<StyledCapability> Styled() => AuthorRecipe.Create("styled", AuthorRecipe.Target<StyledCapability>((_, _, _) => { }));
static AuthorRecipe<AccessibleCapability> Accessible() => AuthorRecipe.Create("accessible", AuthorRecipe.Target<AccessibleCapability>((_, _, _) => { }));
static AuthorRecipe<StyledAccessibleCapability> Both() => AuthorRecipe.Create("both", AuthorRecipe.Target<StyledAccessibleCapability>((_, _, _) => { }));
""";
        AssertRejected(declarations + "_ = Styled().Aria;", "CS9286", "CS1929");
        AssertRejected(declarations + "_ = Accessible().Style(Style.Empty);", "CS1929", "CS1061");
        AssertRejected(declarations + "Func<ComponentRecipe> erased = Both;", "CS0407");
        AssertRejected(
            declarations + "_ = Both().Aria.Name(new Token<string>(\"name\", \"value\"));",
            "CS1503"
        );
        AssertRejected(
            "_ = AuthorRecipe.Create(ComponentRecipe.Create(\"arbitrary\", (_, _) => { }), AuthorRecipe.Target<StyledCapability>((_, _, _) => { }));",
            "CS1503"
        );
        Execute(
            declarations
                + """
ComponentRecipe direct = Both();
ContentRecipe content = Both();
ContentRecipe[] collection = [Styled(), Accessible(), Both().Aria.Name("terminal")];
ComponentRecipe terminal = Both().Aria.Name("terminal");
AuthorRecipe<StyledAccessibleCapability> preserved = Both().Aria.Name("typed").End;
Func<ComponentRecipe> erased = () => Both();
_ = erased();
_ = ComponentRecipe.Defer("conditional", _ => true ? (ComponentRecipe)Both() : Styled());
ExpectInvalid(() => Both().Aria.Name((string)null!));
ExpectNull(() => Both().Aria.Name((Func<string>)null!));
return "PASS";
"""
        );
    }

    [TestMethod]
    public void BroadObjectPriorityWouldStealCallbacksAndIsExplicitlyExcluded()
    {
        var extra = """
public static class UnsafeInputs {
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static string Value(object value) => "eager";
    public static string Value(Func<int> value) => "reader";
}
public static class CallbackInputs {
    public static string Value(Func<string> value) => "callback";
    public static string Value(Func<int> value) => "reader";
}
""";
        Execute(
            "if (UnsafeInputs.Value(() => 12) != \"eager\") throw new Exception(\"Unsafe-shape characterization changed; reconsider exclusion.\"); return \"PASS\";",
            extra
        );
        AssertRejected("_ = CallbackInputs.Value(null);", extra, ["CS0121"]);
    }

    private static void AssertRoute(string family, string type, string expression, string expected)
    {
        var compilation = Compile(
            $"static {type} Read() => default!; var token = new Token<{type}>(\"proof\", default!); _ = Inputs.{family}(Style.Empty, {expression}); return \"PASS\";"
        );
        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.AreEqual(
            0,
            errors.Length,
            string.Join(Environment.NewLine, errors.Select(d => d.ToString()))
        );
        var tree = compilation.SyntaxTrees.Single();
        var call = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(node => node.Expression.ToString() == $"Inputs.{family}");
        var selected = (IMethodSymbol)
            compilation.GetSemanticModel(tree).GetSymbolInfo(call).Symbol!;
        var normalized = selected
            .Parameters[1]
            .Type.ToDisplayString()
            .Replace("Lucent.Core.LayoutAxis", "LayoutAxis", StringComparison.Ordinal)
            .Replace("Lucent.Core.Insets", "Insets", StringComparison.Ordinal);
        Assert.AreEqual(expected, normalized, $"{family}({expression})");
    }

    private static void ExecuteFamily(string family, string type, string value)
    {
        var property = Families.Single(entry => entry.Name == family).Property;
        if (property == "Labels")
            property = "Inputs.Labels";
        var changed = family switch
        {
            "Label" => "\"changed\"",
            "Axis" => "LayoutAxis.Column",
            "Padding" => "Insets.Uniform(16)",
            _ => "64f",
        };
        Execute(
            $$"""
var graph = new ReactiveGraph();
using var composition = new Composition(graph, "matrix-{{family}}");
using var theme = new ThemeContext(composition.Root.Scope, new Theme("matrix"));
var signal = composition.Root.Scope.Signal<{{type}}>({{value}}, "input");
var reads = 0;
{{type}} Read() { reads++; return signal.Value; }
var token = new Token<{{type}}>("matrix-token", {{value}});
var styles = new[] {
    Inputs.{{family}}(Style.Empty, {{value}}),
    Inputs.{{family}}(Style.Empty, () => Read()),
    Inputs.{{family}}(Style.Empty, Read),
    Inputs.{{family}}(Style.Empty, token),
    Inputs.{{family}}(Inputs.{{family}}(Style.Empty, {{value}}), default),
    Inputs.{{family}}(Inputs.{{family}}(Style.Empty, {{value}}), default({{type}})!)
};
if (reads != 0) throw new Exception("{{family}} read eagerly during authoring.");
var children = new Element[styles.Length];
for (var i = 0; i < styles.Length; i++) {
    children[i] = composition.Child(composition.Root, "case-" + i);
    children[i].Present(theme, author: styles[i]);
}
graph.Drain();
signal.Value = {{changed}};
theme.Theme = theme.Theme.Set(token, {{changed}});
graph.Drain();
{{type}}[] expected = [{{value}}, {{changed}}, {{changed}}, {{changed}}, default!, default!];
for (var i = 0; i < children.Length; i++)
    if (!System.Collections.Generic.EqualityComparer<{{type}}>.Default.Equals(expected[i], children[i].Resolve({{property}}).Value))
        throw new Exception("{{family}} runtime route failed: " + i);
if (reads == 0 || !children[3].Resolve({{property}}).Winner.Source.Contains("matrix-token"))
    throw new Exception("{{family}} lost reader or token provenance.");
ExpectNull(() => Inputs.{{family}}(Style.Empty, (Func<{{type}}>)null!));
ExpectNull(() => Inputs.{{family}}(Style.Empty, (Token<{{type}}>)null!));
return "PASS";
"""
        );
    }

    private static void AssertRejected(string body, params string[] codes) =>
        AssertRejected(body, "", codes);

    private static void AssertRejected(string body, string extra, string[] codes)
    {
        var errors = Compile(body + " return \"PASS\";", extra)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsTrue(
            errors.Any(d => codes.Contains(d.Id)),
            string.Join(Environment.NewLine, errors.Select(d => d.ToString()))
        );
    }

    private static void Execute(string body, string extra = "")
    {
        using var output = new MemoryStream();
        var result = Compile(body, extra).Emit(output);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        Assert.AreEqual("PASS", assembly.GetType("Harness")!.GetMethod("Run")!.Invoke(null, null));
    }

    private static CSharpCompilation Compile(string body, string extra = "")
    {
        var families = string.Join(
            Environment.NewLine,
            Families.Select(family =>
                $$"""
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static Style {{family.Name}}(Style style, {{family.Type}} value) => style.Set({{family.Property}}, value);
    public static Style {{family.Name}}(Style style, Func<{{family.Type}}> read) => style.Bind({{family.Property}}, read);
    public static Style {{family.Name}}(Style style, Token<{{family.Type}}> token) => style.Set({{family.Property}}, token);
"""
            )
        );
        var source = $$"""
#nullable enable
using System;
using Lucent.Core;
public static class Inputs {
    public static readonly Property<string> Labels = new("label", "default");
{{families}}
}
{{extra}}
public static class Harness {
    public static string Run() {
{{body}}
    }
    private static void ExpectNull(Action operation) {
        try { operation(); } catch (ArgumentNullException) { return; }
        throw new Exception("Typed null was accepted.");
    }
    private static void ExpectInvalid(Action operation) {
        try { operation(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid metadata was accepted.");
    }
}
""";
        return CSharpCompilation.Create(
            "authoring-overloads-" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }
}
