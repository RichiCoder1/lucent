using System.Reflection;
using System.Text;
using Lucent.Lui.Tooling;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Tooling.Tests;

[TestClass]
public sealed class ToolingCommandTests
{
    [TestMethod]
    public void AtomicReplacementPreservesEncodingAndBom()
    {
        const string original = "Original café, 日本語 and 😀.";
        const string replacement = "Replacement crème, Ελληνικά and 🧭.";
        foreach (var encoding in Encodings())
        {
            using var files = new ToolingFixture();
            var path = files.Path("View.lui");
            ToolingFixture.Write(path, original, encoding);
            var before = SourceFileSnapshot.Read(path);

            Assert.AreEqual(
                SourceFileWriteStatus.Updated,
                SourceFileTransaction.Replace(path, before, replacement)
            );

            var after = File.ReadAllBytes(path);
            var expected = encoding.GetPreamble().Concat(encoding.GetBytes(replacement)).ToArray();
            CollectionAssert.AreEqual(expected, after);
        }
    }

    [TestMethod]
    public void AtomicReplacementRejectsAConcurrentEdit()
    {
        using var files = new ToolingFixture();
        var path = files.Path("View.lui");
        File.WriteAllText(path, "original", new UTF8Encoding(false));
        var snapshot = SourceFileSnapshot.Read(path);
        File.WriteAllText(path, "newer edit", new UTF8Encoding(false));

        Assert.AreEqual(
            SourceFileWriteStatus.ChangedSinceRead,
            SourceFileTransaction.Replace(path, snapshot, "formatted")
        );
        Assert.AreEqual("newer edit", File.ReadAllText(path));
        Assert.AreEqual(0, Directory.GetFiles(files.Root, "*.tmp").Length);
    }

    [TestMethod]
    public void WriteUsesResolvedConfigurationAndCheckReportsDrift()
    {
        using var files = new ToolingFixture();
        files.WriteText(
            ".editorconfig",
            """
            root = true
            [*.lui]
            indent_size = 2
            end_of_line = lf
            """
        );
        files.WriteText(
            "View.lui",
            "internal component View(){<Column><Text>Hello</Text></Column>}"
        );
        var path = files.Path("View.lui");

        var drift = Run(["--check", path]);
        Assert.AreEqual(1, drift.ExitCode);
        StringAssert.Contains(drift.Error, path);
        StringAssert.Contains(drift.Error, "needs formatting");
        var written = Run(["--write", path]);

        Assert.AreEqual(0, written.ExitCode, written.Error);
        StringAssert.Contains(File.ReadAllText(path), "\n  <Column>");
        Assert.AreEqual(0, Run(["--check", path]).ExitCode);
    }

    [TestMethod]
    public void InvalidSupportedConfigurationFailsWithoutWriting()
    {
        using var files = new ToolingFixture();
        files.WriteText(
            ".editorconfig",
            """
            root = true
            [*.lui]
            indent_size = huge
            """
        );
        const string source = "internal component View(){<Text>Hello</Text>}";
        files.WriteText("View.lui", source);
        var path = files.Path("View.lui");

        var result = Run(["--write", path]);

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Error, "LUI6102");
        Assert.AreEqual(source, File.ReadAllText(path));
    }

    [TestMethod]
    public void BatchReportsFailureWithoutRollingBackCompletedFiles()
    {
        using var files = new ToolingFixture();
        files.WriteText("Valid.lui", "internal component Valid(){<Text>Valid</Text>}");
        const string malformed = "internal component Broken(){<Text>";
        files.WriteText("Broken.lui", malformed);

        var result = Run(["--write", files.Path("Valid.lui"), files.Path("Broken.lui")]);

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(
            File.ReadAllText(files.Path("Valid.lui")),
            "\n    <Text>Valid</Text>\n"
        );
        Assert.AreEqual(malformed, File.ReadAllText(files.Path("Broken.lui")));
    }

    [TestMethod]
    public void FormattingModesRemainExclusiveAndFixRequiresLint()
    {
        using var files = new ToolingFixture();
        files.WriteText("View.lui", "internal component View(){<Text>Hello</Text>}");
        var path = files.Path("View.lui");

        Assert.AreEqual(2, Run(["--check", "--write", path]).ExitCode);
        var fix = Run(["--fix", path]);
        Assert.AreEqual(2, fix.ExitCode);
        StringAssert.Contains(fix.Error, "--fix requires --lint");
        Assert.AreEqual(2, Run(["--project", files.Path("App.csproj"), path]).ExitCode);
    }

    [TestMethod]
    public void StandardOutputRetainsItsSingleFileContract()
    {
        using var files = new ToolingFixture();
        files.WriteText("First.lui", "internal component First(){<Text>First</Text>}");
        files.WriteText("Second.lui", "internal component Second(){<Text>Second</Text>}");

        var one = Run([files.Path("First.lui")]);
        Assert.AreEqual(0, one.ExitCode, one.Error);
        StringAssert.Contains(one.Output, "internal component First()");
        Assert.AreEqual(2, Run([files.Path("First.lui"), files.Path("Second.lui")]).ExitCode);
    }

    [TestMethod]
    [DoNotParallelize]
    public void SemanticLintUsesWarningsAsErrorsAndPerFileSeverity()
    {
        var configuration =
            typeof(ToolingCommandTests)
                .Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()
                ?.Configuration
            ?? throw new InvalidOperationException("Missing build configuration.");
        var previousConfiguration = Environment.GetEnvironmentVariable("Configuration");
        try
        {
            Environment.SetEnvironmentVariable("Configuration", configuration);
            VerifySemanticLint(configuration);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Configuration", previousConfiguration);
        }
    }

    private static void VerifySemanticLint(string configuration)
    {
        using var files = new ToolingFixture();
        var coreProject = System.IO.Path.Combine(
            RepositoryRoot(),
            "src",
            "Lucent.Core",
            "Lucent.Core.csproj"
        );
        files.WriteText(
            "Lint.csproj",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Configuration>{configuration}</Configuration>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <Using Include="Lucent.Core" />
                <ProjectReference Include="{coreProject}" AdditionalProperties="Configuration={configuration}" />
                <AdditionalFiles Include="*.lui" />
                <CompilerVisibleProperty Include="Configuration" />
              </ItemGroup>
            </Project>
            """
        );
        files.WriteText("Promoted.lui", LintSource("Promoted", "PromotedStyle"));
        files.WriteText(
            "ExplicitWarning.lui",
            LintSource("ExplicitWarning", "ExplicitWarningStyle")
        );
        files.WriteText("Suppressed.lui", LintSource("Suppressed", "SuppressedStyle"));
        files.WriteText(
            ".editorconfig",
            """
            root = true
            [*.lui]
            lucent_lui_declaration_order = component_first
            [ExplicitWarning.lui]
            dotnet_diagnostic.LUI5004.severity = warning
            [Suppressed.lui]
            dotnet_diagnostic.LUI5004.severity = none
            """
        );
        var promoted = Run([
            "--lint",
            "--project",
            files.Path("Lint.csproj"),
            files.Path("Promoted.lui"),
        ]);
        Assert.AreEqual(2, promoted.ExitCode, promoted.Error);
        StringAssert.Contains(promoted.Error, "error LUI5004");

        var overrides = Run([
            "--lint",
            "--project",
            files.Path("Lint.csproj"),
            files.Path("ExplicitWarning.lui"),
            files.Path("Suppressed.lui"),
        ]);
        Assert.AreEqual(0, overrides.ExitCode, overrides.Error);
        StringAssert.Contains(overrides.Error, "warning LUI5004");
        Assert.IsFalse(
            overrides.Error.Contains("Suppressed.lui", StringComparison.Ordinal),
            overrides.Error
        );

        using var workspace = MSBuildWorkspace.Create();
        var evaluated = workspace
            .OpenProjectAsync(files.Path("Lint.csproj"))
            .GetAwaiter()
            .GetResult();
        Assert.IsTrue(
            evaluated.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
                "build_property.Configuration",
                out var evaluatedConfiguration
            )
        );
        Assert.AreEqual(configuration, evaluatedConfiguration);
        var referencedCore = evaluated.Solution.GetProject(
            evaluated.ProjectReferences.Single().ProjectId
        )!;
        var analyzerPaths = referencedCore
            .AnalyzerReferences.Where(reference =>
                reference.FullPath?.EndsWith(
                    "Lucent.Lui.Compiler.dll",
                    StringComparison.OrdinalIgnoreCase
                ) == true
                || reference.FullPath?.EndsWith(
                    "Lucent.Lui.Generator.dll",
                    StringComparison.OrdinalIgnoreCase
                ) == true
            )
            .Select(reference => reference.FullPath ?? "")
            .ToArray();
        Assert.AreEqual(2, analyzerPaths.Length, String.Join(Environment.NewLine, analyzerPaths));
        Assert.IsTrue(
            analyzerPaths.All(path =>
                path.Contains(
                    $"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}{configuration}{System.IO.Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase
                ) && File.Exists(path)
            ),
            String.Join(Environment.NewLine, analyzerPaths)
        );
    }

    private static string LintSource(string componentName, string styleName) =>
        $$"""
            namespace Example;
            style {{styleName}} { Spacing: 4; }
            internal component {{componentName}}() {
                string Read() { return ""; }
                void Change(string value) { }
                <PasswordField label="Secret" value={Read} onChangeRequested={Change} style={{{styleName}}} />
            }
            """;

    private static IEnumerable<Encoding> Encodings()
    {
        yield return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        yield return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        yield return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        yield return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        yield return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        yield return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
    }

    private static CommandResult Run(string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = ToolingCommand.Run(arguments, output, error);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private static string RepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Lucent.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the Lucent repository root.");
    }

    private readonly record struct CommandResult(int ExitCode, string Output, string Error);

    private sealed class ToolingFixture : IDisposable
    {
        public ToolingFixture()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "lucent-tooling-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Path(string relativePath) =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relativePath));

        public void WriteText(string relativePath, string content) =>
            File.WriteAllText(Path(relativePath), content, new UTF8Encoding(false));

        public static void Write(string path, string content, Encoding encoding)
        {
            var bytes = encoding.GetBytes(content);
            var preamble = encoding.GetPreamble();
            using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None
            );
            stream.Write(preamble);
            stream.Write(bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
