using Lucent.Lui.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.Compiler.Tests;

[TestClass]
public sealed class EditorConfigResolutionTests
{
    private static readonly int[] InvalidConfigurationLines = { 3, 4, 5, 6, 7, 8, 9 };

    [TestMethod]
    public void AppliesOuterToInnerSectionsAndStopsAtRoot()
    {
        using var files = new ConfigFixture();
        files.Write(
            ".editorconfig",
            """
            [*.lui]
            indent_size = 11
            """
        );
        files.Write(
            "project/.editorconfig",
            """
            root = true

            [*.lui]
            indent_size = 2
            max_line_length = 80

            [src/**/*.lui]
            indent_size = 3
            """
        );
        files.Write(
            "project/src/.editorconfig",
            """
            [*.lui]
            indent_size = 6
            indent_size = 8
            lucent_lui_declaration_order = styles_first

            [Special.lui]
            max_line_length = 120
            """
        );

        var result = LuiEditorConfigResolver.Resolve(files.Path("project/src/deep/Special.lui"));

        Assert.IsTrue(result.IsValid, Diagnostics(result));
        Assert.AreEqual(8, result.Options.IndentSize);
        Assert.AreEqual(120, result.Options.LineWidth);
        Assert.AreEqual(LuiDeclarationOrder.StylesFirst, result.DeclarationOrder);
        Assert.AreEqual(2, result.ConfigurationFiles.Count);
        Assert.AreEqual(files.Path("project/.editorconfig"), result.ConfigurationFiles[0]);
        Assert.AreEqual(files.Path("project/src/.editorconfig"), result.ConfigurationFiles[1]);
    }

    [TestMethod]
    public void UnsetReturnsToCanonicalFallbackInsteadOfEarlierValue()
    {
        using var files = new ConfigFixture();
        files.Write(
            ".editorconfig",
            """
            root = true
            [*.lui]
            indent_style = tab
            indent_size = 2
            tab_width = 3
            max_line_length = 72
            end_of_line = crlf
            lucent_lui_declaration_order = component_first
            dotnet_diagnostic.LUI5004.severity = error
            """
        );
        files.Write(
            "feature/.editorconfig",
            """
            [*.lui]
            indent_style = unset
            indent_size = unset
            tab_width = unset
            max_line_length = unset
            end_of_line = unset
            lucent_lui_declaration_order = unset
            dotnet_diagnostic.LUI5004.severity = unset
            dotnet_diagnostic.LUI5005.severity = warning
            """
        );

        var result = LuiEditorConfigResolver.Resolve(files.Path("feature/View.lui"));

        Assert.IsTrue(result.IsValid, Diagnostics(result));
        Assert.IsFalse(result.Options.UseTabs);
        Assert.AreEqual(4, result.Options.IndentSize);
        Assert.AreEqual(4, result.Options.TabWidth);
        Assert.AreEqual(100, result.Options.LineWidth);
        Assert.AreEqual(LuiLineEnding.Preserve, result.Options.LineEnding);
        Assert.AreEqual(LuiDeclarationOrder.None, result.DeclarationOrder);
        Assert.IsFalse(result.DiagnosticSeverities.ContainsKey("LUI5004"));
        Assert.AreEqual(ReportDiagnostic.Warn, result.DiagnosticSeverities["LUI5005"]);
    }

    [TestMethod]
    public void SupportsTabsUnlimitedWidthAndEveryStructuralLineEnding()
    {
        foreach (
            var (configuredEnding, expectedEnding) in new[]
            {
                ("lf", LuiLineEnding.Lf),
                ("crlf", LuiLineEnding.CrLf),
                ("cr", LuiLineEnding.Cr),
            }
        )
        {
            using var files = new ConfigFixture();
            files.Write(
                ".editorconfig",
                $"""
                root = true
                [*.lui]
                indent_style = tab
                indent_size = tab
                tab_width = 6
                max_line_length = off
                end_of_line = {configuredEnding}
                """
            );

            var result = LuiEditorConfigResolver.Resolve(files.Path("View.lui"));

            Assert.IsTrue(result.IsValid, Diagnostics(result));
            Assert.IsTrue(result.Options.UseTabs);
            Assert.AreEqual(6, result.Options.IndentSize);
            Assert.AreEqual(6, result.Options.TabWidth);
            Assert.AreEqual(Int32.MaxValue, result.Options.LineWidth);
            Assert.AreEqual(expectedEnding, result.Options.LineEnding);
        }
    }

    [TestMethod]
    public void IgnoresUnknownAndNonMatchingSettings()
    {
        using var files = new ConfigFixture();
        files.Write(
            ".editorconfig",
            """
            root = true
            [*.cs]
            indent_size = broken
            [*.lui]
            charset = made-up
            dotnet_diagnostic.CS2000.severity = banana
            custom_setting = invalid
            indent_size = 5
            """
        );

        var result = LuiEditorConfigResolver.Resolve(files.Path("View.lui"));

        Assert.IsTrue(result.IsValid, Diagnostics(result));
        Assert.AreEqual(5, result.Options.IndentSize);
    }

    [TestMethod]
    public void ReportsEveryInvalidSupportedValueAtItsAuthoredLine()
    {
        using var files = new ConfigFixture();
        files.Write(
            ".editorconfig",
            """
            root = true
            [*.lui]
            indent_style = mixed
            indent_size = 0
            tab_width = 17
            max_line_length = 19
            end_of_line = native
            lucent_lui_declaration_order = alphabetical
            dotnet_diagnostic.LUI5004.severity = banana
            """
        );

        var result = LuiEditorConfigResolver.Resolve(files.Path("View.lui"));

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(7, result.Diagnostics.Count, Diagnostics(result));
        CollectionAssert.AreEqual(
            InvalidConfigurationLines,
            result.Diagnostics.Select(item => item.Line).ToArray()
        );
        Assert.IsTrue(result.Diagnostics.All(item => item.Id == "LUI6102"));
        Assert.IsTrue(result.Diagnostics.All(item => item.FilePath == files.Path(".editorconfig")));
    }

    [TestMethod]
    public void MatchesEditorConfigWildcardsAgainstRelativePaths()
    {
        using var files = new ConfigFixture();
        files.Write(
            ".editorconfig",
            """
            root = true
            [*.lui]
            indent_size = 2
            [src/**/{View,Panel}?.lui]
            indent_size = 7
            [src/**/Panel[!0].lui]
            max_line_length = 140
            """
        );

        var view = LuiEditorConfigResolver.Resolve(files.Path("src/deep/View1.lui"));
        var panel = LuiEditorConfigResolver.Resolve(files.Path("src/nested/Panel2.lui"));
        var excludedPanel = LuiEditorConfigResolver.Resolve(files.Path("src/nested/Panel0.lui"));

        Assert.AreEqual(7, view.Options.IndentSize, Diagnostics(view));
        Assert.AreEqual(7, panel.Options.IndentSize, Diagnostics(panel));
        Assert.AreEqual(140, panel.Options.LineWidth, Diagnostics(panel));
        Assert.AreEqual(100, excludedPanel.Options.LineWidth, Diagnostics(excludedPanel));
    }

    [TestMethod]
    public void UsesRoslynNumericRangesAndEscapesWithoutRejectingIrrelevantSections()
    {
        using var files = new ConfigFixture();
        files.Write(
            ".editorconfig",
            """
            root = true
            [src/Item{1..3}.lui]
            indent_size = 7
            [src/Literal\{draft\}.lui]
            max_line_length = 130
            [src/{unterminated.lui]
            indent_style = mixed
            """
        );

        var ranged = LuiEditorConfigResolver.Resolve(files.Path("src/Item2.lui"));
        var escaped = LuiEditorConfigResolver.Resolve(files.Path("src/Literal{draft}.lui"));
        var outside = LuiEditorConfigResolver.Resolve(files.Path("src/Item8.lui"));

        Assert.IsTrue(ranged.IsValid, Diagnostics(ranged));
        Assert.AreEqual(7, ranged.Options.IndentSize);
        Assert.IsTrue(escaped.IsValid, Diagnostics(escaped));
        Assert.AreEqual(130, escaped.Options.LineWidth);
        Assert.IsTrue(outside.IsValid, Diagnostics(outside));
        Assert.AreEqual(4, outside.Options.IndentSize);
    }

    [TestMethod]
    public void ResolvesHostSnapshotsWithoutReadingFilesAndFiltersToTheAncestorChain()
    {
        using var files = new ConfigFixture();
        var result = LuiEditorConfigResolver.Resolve(
            files.Path("project/src/View.lui"),
            new[]
            {
                new LuiEditorConfigSnapshot(
                    files.Path("sibling/.editorconfig"),
                    "[*.lui]\nindent_size = invalid"
                ),
                new LuiEditorConfigSnapshot(
                    files.Path("project/src/.editorconfig"),
                    "[View.lui]\nindent_size = 6\ndotnet_diagnostic.LUI5004.severity = none"
                ),
                new LuiEditorConfigSnapshot(
                    files.Path(".editorconfig"),
                    "[*.lui]\nindent_size = 11"
                ),
                new LuiEditorConfigSnapshot(
                    files.Path("project/.editorconfig"),
                    "root = true\n[*.lui]\nindent_size = 2"
                ),
            }
        );

        Assert.IsTrue(result.IsValid, Diagnostics(result));
        Assert.AreEqual(6, result.Options.IndentSize);
        Assert.AreEqual(ReportDiagnostic.Suppress, result.DiagnosticSeverities["LUI5004"]);
        CollectionAssert.AreEqual(
            new[] { files.Path("project/.editorconfig"), files.Path("project/src/.editorconfig") },
            result.ConfigurationFiles.ToArray()
        );
    }

    private static string Diagnostics(LuiEditorConfigResolution result) =>
        String.Join(
            " | ",
            result.Diagnostics.Select(item =>
                $"{item.FilePath}({item.Line}): {item.Id}: {item.Message}"
            )
        );

    private sealed class ConfigFixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "lucent-editorconfig-" + Guid.NewGuid().ToString("N")
        );

        public ConfigFixture() => Directory.CreateDirectory(root);

        public string Path(string relativePath) =>
            System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    root,
                    relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)
                )
            );

        public void Write(string relativePath, string content)
        {
            var path = Path(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
