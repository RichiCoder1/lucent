using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Lucent.Compiler;
using Lucent.LanguageServer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.LanguageServer.Tests;

[TestClass]
public sealed class LanguageServerProtocolTests
{
    [TestMethod]
    public async Task Open_adjacent_css_revalidates_the_local_manifest_generation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-local-css-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "App.csproj");
            var lui = Path.Combine(directory, "App.lui");
            var css = Path.Combine(directory, "App.css");
            const string luiText = "namespace Demo; component App() => Border {};";
            const string cssText = "Border { width: 1; }";
            await File.WriteAllTextAsync(project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(lui, luiText);
            await File.WriteAllTextAsync(css, cssText);
            var target = Path.Combine(directory, "bin", "Debug", "net9.0", "App.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            WriteLocalManifestAssembly(target, project, lui, luiText, css, cssText);

            var luiUri = new Uri(lui).AbsoluteUri;
            var cssUri = new Uri(css).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = luiUri, languageId = "lucent", version = 1, text = luiText } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = cssUri, languageId = "css", version = 1, text = "Border { width: 2; }" } }),
                Notification("textDocument/didClose", new { textDocument = new { uri = cssUri } }),
                Request(2, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var diagnostics = PublishedDiagnostics(ReadMessages(output.ToArray()), luiUri).ToArray();
            Assert.IsTrue(diagnostics.Length > 0);
            Assert.IsTrue(diagnostics.Any(items => items.EnumerateArray().Any(item =>
                item.GetProperty("code").GetString() == "LUC9007" &&
                item.GetProperty("message").GetString()!.Contains("stale", StringComparison.Ordinal))));
            Assert.IsFalse(diagnostics[^1].EnumerateArray().Any(item =>
                item.GetProperty("code").GetString() == "LUC9007"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Referenced_manifest_classes_complete_only_in_css_selectors_from_the_generation_snapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-referenced-css-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var reference = Path.Combine(directory, "Package.dll");
            WriteReferencedManifestAssembly(reference, "package-button");
            var project = Path.Combine(directory, "App.csproj");
            var css = Path.Combine(directory, "App.css");
            var lui = Path.Combine(directory, "App.lui");
            const string cssText = ".package-";
            const string luiText = "namespace Demo; component App() => Border { Class: \"package-\"; };";
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include=\"Package\"><HintPath>Package.dll</HintPath></Reference><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(css, cssText);
            await File.WriteAllTextAsync(lui, luiText);
            var cssUri = new Uri(css).AbsoluteUri;
            var luiUri = new Uri(lui).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = cssUri, languageId = "css", version = 1, text = cssText } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = luiUri, languageId = "lucent", version = 1, text = luiText } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri = cssUri }, position = PositionAtOffset(cssText, cssText.Length) }),
                Request(3, "textDocument/completion", new { textDocument = new { uri = luiUri }, position = PositionAtOffset(luiText, luiText.IndexOf("package-", StringComparison.Ordinal) + "package-".Length) }),
                Request(4, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray().Any(item => item.GetProperty("label").GetString() == "package-button"));
            Assert.IsFalse(Response(messages, 3).GetProperty("result").EnumerateArray().Any(item => item.GetProperty("label").GetString() == "package-button"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Native_theme_classes_require_direct_application_style_installation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-native-theme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var reference = Path.Combine(directory, "Package.dll");
            WriteReferencedThemeAssembly(reference);
            var project = Path.Combine(directory, "App.csproj");
            var code = Path.Combine(directory, "App.cs");
            var lui = Path.Combine(directory, "App.lui");
            const string source = "namespace Demo; component App() => Button { Class: \"se\"; };";
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Avalonia\" Version=\"12.1.1\" /><Reference Include=\"Package\"><HintPath>Package.dll</HintPath></Reference><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(code, "using Avalonia; using Package; namespace Demo; sealed class App : Application { void Install() => Styles.Add(new Theme()); }");
            await File.WriteAllTextAsync(lui, source);
            var uri = new Uri(lui).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = PositionAtOffset(source, source.IndexOf("se\"", StringComparison.Ordinal) + 2) }),
                Request(3, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var items = Response(ReadMessages(output.ToArray()), 2).GetProperty("result").EnumerateArray().ToArray();
            Assert.IsTrue(items.Any(item => item.GetProperty("label").GetString() == "secondary"),
                string.Join(", ", items.Select(item => item.GetProperty("label").GetString() + ":" + item.GetProperty("detail").GetString())));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Native_theme_classes_require_direct_app_axaml_installation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-native-theme-axaml-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            WriteReferencedThemeAssembly(Path.Combine(directory, "Package.dll"));
            var project = Path.Combine(directory, "App.csproj");
            var lui = Path.Combine(directory, "App.lui");
            const string source = "namespace Demo; component App() => Button { Class: \"se\"; };";
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Avalonia\" Version=\"12.1.1\" /><Reference Include=\"Package\"><HintPath>Package.dll</HintPath></Reference><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "App.axaml"), "<Application xmlns=\"https://github.com/avaloniaui\" xmlns:theme=\"using:Package\"><Application.Styles><theme:Theme /></Application.Styles></Application>");
            await File.WriteAllTextAsync(lui, source);
            var uri = new Uri(lui).AbsoluteUri;
            using var input = BuildInput(Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }), Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = PositionAtOffset(source, source.IndexOf("se\"", StringComparison.Ordinal) + 2) }),
                Request(3, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            Assert.IsTrue(Response(ReadMessages(output.ToArray()), 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "secondary"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Native_theme_activation_rejects_bare_invalid_and_wrong_namespace_evidence()
    {
        foreach (var (code, axaml) in new[]
        {
            ("using Avalonia; using Package; namespace Demo; sealed class App : Application { void Install() { var theme = new Theme(); } }", (string?)null),
            ("using Avalonia; using Package; namespace Demo; sealed class NotApp { public Styles Styles { get; } = new(); void Install() => Styles.Add(new Theme()); }", (string?)null),
            ("", "<Application xmlns=\"https://github.com/avaloniaui\" xmlns:theme=\"using:Package.Wrong\"><Application.Styles><theme:Theme /></Application.Styles></Application>"),
            ("", "<Application xmlns=\"https://github.com/avaloniaui\" xmlns:theme=\"using:Package\"><Application.Resources><theme:Theme /></Application.Resources></Application>"),
            ("", "<Application xmlns=\"https://github.com/avaloniaui\" xmlns:theme=\"using:Package\"><Application.Styles><ResourceDictionary><theme:Theme /></ResourceDictionary></Application.Styles></Application>")
        })
            Assert.IsFalse((await ThemeCompletionLabelsAsync(code, axaml)).Contains("secondary"));
    }

    [TestMethod]
    public async Task Global_style_classes_require_their_exact_direct_catalog_installation()
    {
        var installed = await GlobalStyleCompletionItemsAsync("using Avalonia; using Package; namespace Demo; sealed class App : Application { void Install() => Styles.Add(new FirstLucentStyles()); }");
        Assert.IsTrue(installed.Any(item => item.Label == "first-global" && item.Detail == "Global CSS"));
        Assert.IsFalse(installed.Any(item => item.Label == "second-global"));

        foreach (var code in new[]
        {
            "using Avalonia; using Package; namespace Demo; sealed class App : Application { void Install() { var catalog = new FirstLucentStyles(); } }",
            "using Avalonia; using Package; using Catalog = Package.FirstLucentStyles; namespace Demo; sealed class App : Application { void Install() => Styles.Add(new Catalog()); }",
            "using Avalonia; using P = Package; namespace Demo; sealed class App : Application { void Install() => Styles.Add(new P.FirstLucentStyles()); }",
            "using Avalonia; using Package; namespace Demo; sealed class App : Application { void Install() { var styles = Styles; styles.Add(new FirstLucentStyles()); } }",
            "using Avalonia; using Package; namespace Demo; sealed class App : Application { public new Styles Styles { get; } = new(); void Install() => Styles.Add(new FirstLucentStyles()); }",
            "using Avalonia; namespace Demo; sealed class App : Application { void Install() => Styles.Add(new global::Package.FirstLucentStyles()); }",
            "using Avalonia; using Package; namespace Demo; sealed class App : Application { }",
        })
            Assert.IsFalse((await GlobalStyleCompletionItemsAsync(code)).Any(item => item.Label == "first-global"), code);
    }
    [TestMethod]
    public async Task Native_binding_protocol_completes_paths_and_explains_inherited_context()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lucent-binding-{Guid.NewGuid():N}.lui");
        const string source = "namespace Demo; using System; using Avalonia.Controls; component App() => ListBox { template ItemTemplate(Uri item) { TextBlock { Text: binding(item.Host); } } };";
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            var input = BuildInput(
                Request(1, "initialize", new { capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new
                {
                    textDocument = new { uri, languageId = "lucent", version = 1, text = source },
                }),
                Request(2, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source,
                        source.IndexOf("item.Host", StringComparison.Ordinal) + "item.H".Length),
                }),
                Request(3, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "binding"),
                }),
                Request(4, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Host"));
            StringAssert.Contains(HoverText(messages, 3), "inherited DataContext");
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public async Task Benchmark_metrics_use_nonnegative_process_wide_allocations()
    {
        const string source = "namespace Demo; component App() => TextBlock {};";
        var path = Path.Combine(Path.GetTempPath(), $"lucent-metrics-{Guid.NewGuid():N}.lui");
        await File.WriteAllTextAsync(path, source);
        try
        {
            var uri = new Uri(path).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = PositionOf(source, "TextBlock") }),
                Request(3, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();
            var metrics = new List<LanguageServerRequestMetric>();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output, metrics.Add));
            Assert.IsTrue(metrics.All(metric => metric.AllocatedBytes >= 0));
            Assert.IsTrue(metrics.Single(metric => metric.Method == "textDocument/completion").AllocatedBytes > 0);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task Project_implicit_usings_preserve_method_group_conversions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-implicit-usings",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "Demo.csproj");
            var sourcePath = Path.Combine(directory, "App.lui");
            await File.WriteAllTextAsync(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
                  <ItemGroup><PackageReference Include="Avalonia" Version="12.1.1" /><LucentSource Include="App.lui" /></ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, "DelegateCommand.cs"), """
                using System.Windows.Input;
                namespace Demo;
                internal sealed class DelegateCommand(Action execute, Func<bool>? canExecute = null) : ICommand
                {
                    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
                    public void Execute(object? parameter) => execute();
                    public event EventHandler? CanExecuteChanged;
                }
                """);
            const string source = """
                namespace Demo;
                component App()
                {
                    private readonly DelegateCommand command = new DelegateCommand(Execute, CanExecute);
                    private void Execute() { }
                    private bool CanExecute() => true;
                    Fragment Render() => Border {};
                }
                """;
            await File.WriteAllTextAsync(sourcePath, source);
            var uri = new Uri(sourcePath).AbsoluteUri;
            var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new
                {
                    textDocument = new { uri, languageId = "lucent", version = 1, text = source },
                }),
                Request(2, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var diagnostics = PublishedDiagnostics(ReadMessages(output.ToArray()), uri)
                .SelectMany(items => items.EnumerateArray())
                .ToArray();
            Assert.IsFalse(diagnostics.Any(diagnostic =>
                diagnostic.GetProperty("message").GetString()?.Contains(
                    "cannot convert from 'method group'", StringComparison.Ordinal) == true),
                string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
                    diagnostic.GetProperty("message").GetString())));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Event_islands_complete_hover_and_define_ordinary_component_members()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lucent-members-{Guid.NewGuid():N}.lui");
        const string source = """
            namespace Demo;
            using System.Threading;
            component App()
            {
                private readonly CancellationTokenSource focusSidebar = new();
                Fragment Render() => Border {
                    Loaded: (sender, e) => { focusSidebar.Cancel(); };
                };
            }
            """;
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            var use = source.LastIndexOf("focusSidebar", StringComparison.Ordinal);
            var input = BuildInput(
                Request(1, "initialize", new { capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new
                {
                    textDocument = new { uri, languageId = "lucent", version = 1, text = source },
                }),
                Request(2, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, use + "focusS".Length),
                }),
                Request(3, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, use),
                }),
                Request(4, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, use),
                }),
                Request(5, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "focusSidebar"));
            StringAssert.Contains(HoverText(messages, 3), "focusSidebar");
            var definition = Response(messages, 4).GetProperty("result");
            Assert.AreEqual(uri, definition.GetProperty("uri").GetString());
            Assert.AreEqual(source[..source.IndexOf("focusSidebar", StringComparison.Ordinal)]
                    .Count(character => character == '\n'),
                definition.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public async Task Same_batch_generated_components_complete_and_hover_exact_mount_root()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-mount-root-lsp",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var hostPath = Path.Combine(directory, "Host.lui");
            var dialogPath = Path.Combine(directory, "Dialog.lui");
            await File.WriteAllTextAsync(Path.Combine(directory, "Demo.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Avalonia" Version="12.1.1" />
                    <LucentSource Include="Host.lui" />
                    <LucentSource Include="Dialog.lui" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(dialogPath,
                "namespace Demo; component Dialog() => Window { Title: \"Dialog\"; };");
            const string source = """
                namespace Demo;
                component Host()
                {
                    private void Show()
                    {
                        using var dialog = new DialogComponent();
                        dialog.MountRoot();
                    }
                    Fragment Render() => Border {};
                }
                """;
            await File.WriteAllTextAsync(hostPath, source);
            var uri = new Uri(hostPath).AbsoluteUri;
            var member = source.IndexOf("dialog.MountRoot", StringComparison.Ordinal) + "dialog.".Length;
            var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new
                {
                    textDocument = new { uri, languageId = "lucent", version = 1, text = source },
                }),
                Request(2, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, member),
                }),
                Request(3, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, member + 1),
                }),
                Request(4, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "MountRoot"));
            var hover = HoverText(messages, 3);
            StringAssert.Contains(hover, "MountRoot");
            StringAssert.Contains(hover, "Window DialogComponent.MountRoot()");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Attached_property_completion_uses_native_setter_symbols()
    {
        const string source = "namespace Demo; using Avalonia.Controls; component App() => Border { Grid.; };";
        var offset = source.IndexOf("Grid.", StringComparison.Ordinal) + "Grid.".Length;

        var items = LucentCompiler.GetCompletions(source, offset, "App.lui");

        Assert.IsTrue(items.Any(item => item.Label == "Row"));
    }

    [TestMethod]
    public async Task Attached_property_protocol_completion_hover_and_definition_use_project_symbols()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-attached-lsp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "Demo.csproj");
            var ownerPath = Path.Combine(directory, "TestOwner.cs");
            var sourcePath = Path.Combine(directory, "App.lui");
            await File.WriteAllTextAsync(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(ownerPath,
                "namespace Demo; public static class TestOwner { " +
                "public static readonly Avalonia.AvaloniaProperty GoodProperty = null!; " +
                "public static void SetGood(Avalonia.Controls.Control target, int value) { } }");
            const string source = "namespace Demo; using Avalonia.Controls; component App() => Border { TestOwner.Good: 1; };";
            await File.WriteAllTextAsync(sourcePath, source);
            var uri = new Uri(sourcePath).AbsoluteUri;
            var input = BuildInput(
                Request(1, "initialize", new { capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new
                {
                    textDocument = new { uri, languageId = "lucent", version = 1, text = source },
                }),
                Request(2, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, source.IndexOf("TestOwner.Good", StringComparison.Ordinal) + "TestOwner.".Length),
                }),
                Request(3, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "Good"),
                }),
                Request(4, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "Good"),
                }),
                Request(5, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Good"));
            StringAssert.Contains(HoverText(messages, 3), "TestOwner.SetGood");
            Assert.AreEqual(new Uri(ownerPath).AbsoluteUri,
                Response(messages, 4).GetProperty("result").GetProperty("uri").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [TestMethod]
    public void Windows_file_uris_do_not_duplicate_the_drive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.IsTrue(FileUri.TryGetPath(
            "file:///d%3A/src/richicoder1/lucent",
            out var path));
        Assert.AreEqual(
            @"D:\src\richicoder1\lucent",
            path,
            ignoreCase: true);
    }

    [TestMethod]
    public async Task Project_context_keeps_local_sources_when_design_time_build_fails()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "lucent-context-fallback-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var projectPath = Path.Combine(temporaryDirectory, "Example.csproj");
            var lucentPath = Path.Combine(temporaryDirectory, "MainWindow.lui");
            var counterPath = Path.Combine(temporaryDirectory, "Counter.cs");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net9.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><LucentSource Include=\"MainWindow.lui\" /></ItemGroup>" +
                "<Target Name=\"FailDesignTime\" BeforeTargets=\"ResolveReferences\">" +
                "<Error Text=\"forced design-time failure\" /></Target></Project>");
            await File.WriteAllTextAsync(lucentPath, "namespace Demo;");
            await File.WriteAllTextAsync(
                counterPath,
                "namespace Demo; internal sealed class Counter { }");
            var loader = new ProjectContextLoader();
            using var initialize = JsonDocument.Parse("{}");
            loader.Configure(initialize.RootElement);

            var context = await loader.LoadAsync(lucentPath, CancellationToken.None);

            Assert.IsNotNull(context);
            Assert.IsTrue(context.Sources.Contains(counterPath));
            Assert.AreEqual(0, context.References.Count);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Project_context_reloads_when_a_source_file_is_added()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "lucent-context-cache-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var projectPath = Path.Combine(temporaryDirectory, "Example.csproj");
            var lucentPath = Path.Combine(temporaryDirectory, "MainWindow.lui");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net9.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><LucentSource Include=\"MainWindow.lui\" /></ItemGroup>" +
                "</Project>");
            await File.WriteAllTextAsync(lucentPath, "namespace Demo;");
            var loader = new ProjectContextLoader();
            using var initialize = JsonDocument.Parse("{}");
            loader.Configure(initialize.RootElement);

            var first = await loader.LoadAsync(lucentPath, CancellationToken.None);
            Assert.IsNotNull(first);
            Assert.IsFalse(first.Sources.Any(path => path.EndsWith("Counter.cs")));

            var counterPath = Path.Combine(temporaryDirectory, "Counter.cs");
            await File.WriteAllTextAsync(
                counterPath,
                "namespace Demo; internal sealed class Counter { }");
            Directory.SetLastWriteTimeUtc(
                temporaryDirectory,
                DateTime.UtcNow.AddSeconds(1));

            var second = await loader.LoadAsync(lucentPath, CancellationToken.None);
            Assert.IsNotNull(second);
            Assert.IsTrue(second.Sources.Contains(counterPath));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Initialize_open_shutdown_and_exit_use_stdio_json_rpc()
    {
        var input = BuildInput(
            Request(1, "initialize", new
            {
                processId = (int?)null,
                rootUri = (string?)null,
                capabilities = new { },
            }),
            Notification("initialized", new { }),
            Notification("textDocument/didOpen", new
            {
                textDocument = new
                {
                    uri = "file:///Counter.lui",
                    languageId = "lucent",
                    version = 1,
                    text = InvalidSource,
                },
            }),
            Request(2, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);
        var messages = ReadMessages(output.ToArray());

        Assert.AreEqual(0, exitCode);
        var initialize = messages.Single(message =>
            message.RootElement.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.Number &&
            id.GetInt32() == 1);
        var textDocumentSync = initialize.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("textDocumentSync");
        Assert.IsTrue(textDocumentSync.GetProperty("openClose").GetBoolean());
        Assert.AreEqual(1, textDocumentSync.GetProperty("change").GetInt32());
        Assert.AreEqual(
            "utf-16",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("positionEncoding")
                .GetString());
        Assert.IsTrue(
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("hoverProvider")
                .GetBoolean());
        Assert.IsTrue(
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("definitionProvider")
                .GetBoolean());
        Assert.AreEqual(
            ":",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("completionProvider")
                .GetProperty("triggerCharacters")[0]
                .GetString());
        Assert.AreEqual(
            ".",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("completionProvider")
                .GetProperty("triggerCharacters")[1]
                .GetString());
        Assert.AreEqual(
            "(",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("completionProvider")
                .GetProperty("triggerCharacters")[2]
                .GetString());
        Assert.AreEqual(
            ",",
            initialize.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("completionProvider")
                .GetProperty("triggerCharacters")[3]
                .GetString());

        var published = messages.Single(message =>
            message.RootElement.TryGetProperty("method", out var method) &&
            method.GetString() == "textDocument/publishDiagnostics");
        var diagnostic = published.RootElement
            .GetProperty("params")
            .GetProperty("diagnostics")
            .EnumerateArray()
            .Single(item => item.GetProperty("code").GetString() == "LUC2001");
        var lines = InvalidSource.Split("\r\n");
        var sourceLineIndex = Array.FindIndex(
            lines,
            line => line.Contains("tooltip", StringComparison.Ordinal));
        var sourceLine = lines[sourceLineIndex];
        var tooltip = sourceLine.IndexOf("tooltip", StringComparison.Ordinal);
        Assert.AreEqual(sourceLineIndex, diagnostic.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.AreEqual(tooltip, diagnostic.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.AreEqual(1, diagnostic.GetProperty("severity").GetInt32());
        Assert.AreEqual("lucent", diagnostic.GetProperty("source").GetString());
    }

    [TestMethod]
    public async Task Every_advertised_completion_trigger_executes_a_protocol_completion()
    {
        const string uri = "file:///Triggers.lui";
        const string source = "namespace Demo; component App() => TextBlock { Text: string.Em; };";
        var position = PositionAtOffset(
            source,
            source.IndexOf("string.Em", StringComparison.Ordinal) + "string.Em".Length);
        using var input = BuildInput(
            Request(1, "initialize", new { capabilities = new { } }),
            Notification("initialized", new { }),
            Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
            Request(2, "textDocument/completion", new { textDocument = new { uri }, position, context = new { triggerKind = 2, triggerCharacter = ":" } }),
            Request(3, "textDocument/completion", new { textDocument = new { uri }, position, context = new { triggerKind = 2, triggerCharacter = "." } }),
            Request(4, "textDocument/completion", new { textDocument = new { uri }, position, context = new { triggerKind = 2, triggerCharacter = "(" } }),
            Request(5, "textDocument/completion", new { textDocument = new { uri }, position, context = new { triggerKind = 2, triggerCharacter = "," } }),
            Request(6, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
        var messages = ReadMessages(output.ToArray());
        for (var id = 2; id <= 5; id++)
        {
            Assert.IsTrue(Response(messages, id).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Empty"));
        }
    }

    [TestMethod]
    public async Task Did_change_republishes_diagnostics_and_close_clears_them()
    {
        var uri = "file:///Counter.lui";
        var input = BuildInput(
            Request(1, "initialize", new { capabilities = new { } }),
            Notification("textDocument/didOpen", new
            {
                textDocument = new
                {
                    uri,
                    languageId = "lucent",
                    version = 1,
                    text = "component",
                },
            }),
            Notification("textDocument/didChange", new
            {
                textDocument = new { uri, version = 2 },
                contentChanges = new[] { new { text = ValidSource } },
            }),
            Notification("textDocument/didClose", new
            {
                textDocument = new { uri },
            }),
            Request(2, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);
        var publishes = ReadMessages(output.ToArray())
            .Where(message =>
                message.RootElement.TryGetProperty("method", out var method) &&
                method.GetString() == "textDocument/publishDiagnostics")
            .Select(message => message.RootElement
                .GetProperty("params")
                .GetProperty("diagnostics")
                .GetArrayLength())
            .ToArray();

        Assert.AreEqual(0, exitCode);
        Assert.HasCount(3, publishes);
        Assert.IsGreaterThan(0, publishes[0]);
        Assert.AreEqual(0, publishes[1]);
        Assert.AreEqual(0, publishes[2]);
    }

    [TestMethod]
    public async Task Exit_without_shutdown_returns_failure_status()
    {
        using var input = BuildInput(Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(0, output.Length);
    }

    [TestMethod]
    public async Task Malformed_payload_does_not_prevent_later_requests()
    {
        var malformed = Encoding.UTF8.GetBytes("{");
        using var input = BuildInput(
            Frame(malformed),
            Request(1, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(
            JsonValueKind.Null,
            Response(ReadMessages(output.ToArray()), 1)
                .GetProperty("result")
                .ValueKind);
    }

    [TestMethod]
    public async Task Oversized_payload_is_rejected_before_allocation()
    {
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {JsonRpcConnection.MaxPayloadLength + 1}\r\n\r\n");
        using var input = new MemoryStream(header);
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(0, output.Length);
    }

    [TestMethod]
    public async Task Unexpected_request_failure_returns_internal_error()
    {
        using var input = BuildInput(
            Request(1, "initialize", new
            {
                workspaceFolders = new[]
                {
                    new { uri = "file:///C:/%00", name = "invalid" },
                },
                capabilities = new { },
            }),
            Request(2, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        var exitCode = await LanguageServer.RunAsync(input, output);
        var error = Response(ReadMessages(output.ToArray()), 1)
            .GetProperty("error");

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(-32603, error.GetProperty("code").GetInt32());
    }

    [TestMethod]
    public async Task Hover_and_definition_use_project_semantic_symbols()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "lucent-lsp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var projectDirectory = Path.Combine(temporaryDirectory, "src", "App");
            var exampleDirectory = Path.Combine(temporaryDirectory, "examples");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(exampleDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "Demo.sln"),
                string.Empty);
            var projectPath = Path.Combine(projectDirectory, "Demo.csproj");
            var controlPath = Path.Combine(projectDirectory, "FancyControl.cs");
            var sourcePath = Path.Combine(exampleDirectory, "Custom.lui");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net9.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><LucentSource Include=\"..\\..\\examples\\Custom.lui\" />" +
                "</ItemGroup></Project>");
            await File.WriteAllTextAsync(
                controlPath,
                "namespace Demo.Controls; public sealed class FancyControl : " +
                "Avalonia.Controls.ContentControl { public string? Accent { get; set; } } " +
                "public sealed record PackageInfo(string Name, string Id, string Description); " +
                "public static class PackageCatalog { public static System.Threading.Tasks.Task<PackageInfo[]> " +
                "Load(System.Threading.CancellationToken cancellationToken) => throw null!; }");
            const string source =
                "namespace Demo;\r\n" +
                "using Demo.Controls;\r\n" +
                "component Custom()\r\n" +
                "{\r\n" +
                "    private readonly State<string> query = new(\"lucent\");\r\n" +
                "    private readonly Computed<PackageInfo[]> packages = new(ct => PackageCatalog.Load(ct), []);\r\n" +
                "    Fragment Render()\r\n" +
                "    {\r\n" +
                "        return StackPanel {\r\n" +
                "            foreach (var package in packages.Value)\r\n" +
                "            keyed by package.Id {\r\n" +
                "                FancyControl { Accent: package.Description; }\r\n" +
                "            }\r\n" +
                "        };\r\n" +
                "    }\r\n" +
                "}\r\n";
            await File.WriteAllTextAsync(sourcePath, source);
            var uri = new Uri(sourcePath).AbsoluteUri;
            var accentPosition = PositionOf(source, "Accent");
            var expressionOffset = source.IndexOf("package.Description", StringComparison.Ordinal);
            var input = BuildInput(
                Request(1, "initialize", new
                {
                    capabilities = new { },
                }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new
                {
                    textDocument = new
                    {
                        uri,
                        languageId = "lucent",
                        version = 1,
                        text = source,
                    },
                }),
                Request(2, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = accentPosition,
                }),
                Request(3, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = accentPosition,
                }),
                Request(4, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, expressionOffset + "package.D".Length),
                }),
                Request(5, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(source, expressionOffset + "package.".Length),
                }),
                Request(6, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.IndexOf("packages.Value", StringComparison.Ordinal) +
                        "packages.V".Length),
                }),
                Request(7, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "packages ="),
                }),
                Request(8, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "State<string>"),
                }),
                Request(9, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "new(\"lucent\")"),
                }),
                Request(10, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "PackageCatalog.Load"),
                }),
                Request(11, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "Load(ct)"),
                }),
                Request(12, "textDocument/hover", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "ct =>"),
                }),
                Request(13, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.IndexOf("PackageCatalog.Load", StringComparison.Ordinal) +
                        "PackageCatalog.L".Length),
                }),
                Request(14, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = PositionOf(source, "PackageCatalog.Load"),
                }),
                Request(15, "textDocument/definition", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.LastIndexOf("packages.Value", StringComparison.Ordinal)),
                }),
                Request(16, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.IndexOf("PackageCatalog.Load", StringComparison.Ordinal) + 1),
                }),
                Request(17, "textDocument/completion", new
                {
                    textDocument = new { uri },
                    position = PositionAtOffset(
                        source,
                        source.IndexOf("Load(ct)", StringComparison.Ordinal) +
                        "Load(".Length),
                }),
                Request(18, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            var exitCode = await LanguageServer.RunAsync(input, output);
            var messages = ReadMessages(output.ToArray());

            Assert.AreEqual(0, exitCode);
            var hover = Response(messages, 2).GetProperty("result");
            StringAssert.Contains(
                hover.GetProperty("contents").GetProperty("value").GetString()!,
                "FancyControl.Accent");
            Assert.IsFalse(
                hover.GetProperty("contents").GetProperty("value").GetString()!
                    .Contains("Native Avalonia", StringComparison.Ordinal));
            var definition = Response(messages, 3).GetProperty("result");
            Assert.AreEqual(
                new Uri(controlPath).AbsoluteUri,
                definition.GetProperty("uri").GetString());
            Assert.AreEqual(
                0,
                definition.GetProperty("range")
                    .GetProperty("start")
                    .GetProperty("line")
                    .GetInt32());
            Assert.IsTrue(Response(messages, 4)
                .GetProperty("result")
                .EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Description"));
            StringAssert.Contains(
                Response(messages, 5)
                    .GetProperty("result")
                    .GetProperty("contents")
                    .GetProperty("value")
                    .GetString()!,
                "PackageInfo.Description");
            var computedMembers = Response(messages, 6)
                .GetProperty("result")
                .EnumerateArray()
                .Select(item => item.GetProperty("label").GetString())
                .ToArray();
            CollectionAssert.Contains(computedMembers, "Value");
            CollectionAssert.Contains(computedMembers, "IsPending");
            StringAssert.Contains(
                Response(messages, 7)
                    .GetProperty("result")
                    .GetProperty("contents")
                    .GetProperty("value")
                    .GetString()!,
                "private readonly Computed<PackageInfo[]> packages");
            StringAssert.Contains(HoverText(messages, 8), "class State<T>");
            StringAssert.Contains(HoverText(messages, 9), "State<string>.State");
            StringAssert.Contains(HoverText(messages, 10), "class Demo.Controls.PackageCatalog");
            StringAssert.Contains(HoverText(messages, 11), "PackageCatalog.Load");
            StringAssert.Contains(HoverText(messages, 12), "CancellationToken ct");
            Assert.IsTrue(Response(messages, 13)
                .GetProperty("result")
                .EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Load"));
            Assert.AreEqual(
                new Uri(controlPath).AbsoluteUri,
                Response(messages, 14)
                    .GetProperty("result")
                    .GetProperty("uri")
                    .GetString());
            Assert.AreEqual(
                uri,
                Response(messages, 15)
                    .GetProperty("result")
                    .GetProperty("uri")
                    .GetString());
            Assert.IsTrue(Response(messages, 16)
                .GetProperty("result")
                .EnumerateArray()
                .Any(item =>
                    item.GetProperty("label").GetString() == "PackageCatalog" &&
                    item.GetProperty("kind").GetInt32() == 7));
            var argumentCompletions = Response(messages, 17)
                .GetProperty("result")
                .EnumerateArray()
                .Select(item => item.GetProperty("label").GetString())
                .ToArray();
            CollectionAssert.Contains(argumentCompletions, "ct");
            CollectionAssert.Contains(argumentCompletions, "query");
            CollectionAssert.Contains(argumentCompletions, "PackageCatalog");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Completion_and_value_hover_use_the_native_property_type()
    {
        const string source =
            "namespace Demo;\r\n" +
            "using Avalonia.Layout;\r\n" +
            "component Main()\r\n" +
            "{\r\n" +
            "    Fragment Render()\r\n" +
            "    {\r\n" +
            "        return StackPanel {\r\n" +
            "            Orientation: Orientation.Horizontal;\r\n" +
            "            Button { Content: \"Go\"; }\r\n" +
            "        };\r\n" +
            "    }\r\n" +
            "}\r\n";
        const string uri = "file:///Completion.lui";
        var memberPosition = PositionAtOffset(
            source,
            source.IndexOf("Button {", StringComparison.Ordinal) + "Button {".Length);
        var valuePosition = PositionOf(source, "Orientation.Horizontal");
        var hoverPosition = PositionOf(source, "Horizontal");
        var input = BuildInput(
            Request(1, "initialize", new { capabilities = new { } }),
            Notification("initialized", new { }),
            Notification("textDocument/didOpen", new
            {
                textDocument = new
                {
                    uri,
                    languageId = "lucent",
                    version = 1,
                    text = source,
                },
            }),
            Request(2, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = memberPosition,
            }),
            Request(3, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = valuePosition,
            }),
            Request(4, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = hoverPosition,
            }),
            Request(5, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
        var messages = ReadMessages(output.ToArray());
        var members = Response(messages, 2).GetProperty("result").EnumerateArray().ToArray();
        Assert.IsTrue(members.Any(item => item.GetProperty("label").GetString() == "Click"));
        Assert.IsTrue(members.Any(item => item.GetProperty("label").GetString() == "Class"));
        var values = Response(messages, 3).GetProperty("result").EnumerateArray().ToArray();
        Assert.IsTrue(values.Any(item =>
            item.GetProperty("label").GetString() == "Orientation.Horizontal"));
        var valueHover = Response(messages, 4)
            .GetProperty("result")
            .GetProperty("contents")
            .GetProperty("value")
            .GetString()!;
        StringAssert.Contains(valueHover, "Orientation.Horizontal");
        Assert.IsFalse(valueHover.Contains("global::", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Project_context_keeps_non_lucent_generated_compile_inputs()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-generated-context", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "obj"));
        try
        {
            var project = Path.Combine(directory, "Example.csproj");
            var lucent = Path.Combine(directory, "Main.lui");
            var generated = Path.Combine(directory, "obj", "OtherGenerator.g.cs");
            await File.WriteAllTextAsync(lucent, "namespace Demo;");
            await File.WriteAllTextAsync(generated, "namespace Demo; public static class OtherGenerator { public static string Value => \"ok\"; }");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"Main.lui\"/><Compile Include=\"obj/OtherGenerator.g.cs\" AutoGen=\"true\"/></ItemGroup></Project>");
            var loader = new ProjectContextLoader();
            using var initialize = JsonDocument.Parse("{}");
            loader.Configure(initialize.RootElement);

            var context = await loader.LoadAsync(lucent, CancellationToken.None);

            Assert.IsNotNull(context);
            Assert.IsTrue(context.Sources.Contains(generated));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Conditional_branches_share_editor_semantics_and_locals_shadow_state()
    {
        const string source =
            "namespace Demo;\r\n" +
            "component Main()\r\n" +
            "{\r\n" +
            "    private readonly State<bool> visible = new(true);\r\n" +
            "    private readonly State<string> title = new(\"state\");\r\n" +
            "    Fragment Render()\r\n" +
            "    {\r\n" +
            "        return StackPanel {\r\n" +
            "            if (visible.Value) {\r\n" +
            "                Button { Click: (title, e) => Console.WriteLine(title.Content); }\r\n" +
            "            } else {\r\n" +
            "                TextBlock { Text: title.Value; }\r\n" +
            "            }\r\n" +
            "        };\r\n" +
            "    }\r\n" +
            "}\r\n";
        const string uri = "file:///Conditional.lui";
        var localOffset = source.IndexOf("title.Content", StringComparison.Ordinal);
        var stateOffset = source.LastIndexOf("title.Value", StringComparison.Ordinal);
        var input = BuildInput(
            Request(1, "initialize", new { capabilities = new { } }),
            Notification("initialized", new { }),
            Notification("textDocument/didOpen", new
            {
                textDocument = new { uri, languageId = "lucent", version = 1, text = source },
            }),
            Request(2, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, localOffset + "title.C".Length),
            }),
            Request(3, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, localOffset),
            }),
            Request(4, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, stateOffset),
            }),
            Request(5, "shutdown", null),
            Notification("exit", null));
        using var output = new MemoryStream();

        Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
        var messages = ReadMessages(output.ToArray());
        Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
            .Any(item => item.GetProperty("label").GetString() == "Content"));
        StringAssert.Contains(HoverText(messages, 3), "Button title");
        StringAssert.Contains(HoverText(messages, 4), "State<string> title");
    }

    [TestMethod]
    public async Task Async_boundary_catch_local_is_scoped_in_protocol_tooling()
    {
        const string source = """
            namespace Demo;
            using System;
            using System.Threading.Tasks;
            component Main()
            {
                private readonly Computed<int> packages = new(ct => Task.FromResult(1), 0);
                Fragment Render() => ContentControl {
                    try (packages) {
                        TextBlock { Text: packages.Value.ToString(); Tag: erro; }
                    }
                    loading {
                        ProgressBar { Value: 0; Tag: packages.; DefinitelyNotAProperty: 1; }
                    }
                    catch (Exception error) {
                        TextBlock { Text: error.Message; }
                    }
                };
            }
            """;
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lucent-boundary-{Guid.NewGuid():N}.lui");
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            var outside = source.IndexOf("erro;", StringComparison.Ordinal);
            var loadingControl = source.IndexOf("ProgressBar", StringComparison.Ordinal);
            var loadingSource = source.IndexOf("packages.;", StringComparison.Ordinal) + "packages.".Length;
            var declaration = source.IndexOf("error)", StringComparison.Ordinal);
            var use = source.LastIndexOf("error.Message", StringComparison.Ordinal);
            var input = BuildInput(
            Request(1, "initialize", new { capabilities = new { } }),
            Notification("initialized", new { }),
            Notification("textDocument/didOpen", new
            {
                textDocument = new { uri, languageId = "lucent", version = 1, text = source },
            }),
            Request(2, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, use + "error.M".Length),
            }),
            Request(3, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, use),
            }),
            Request(4, "textDocument/definition", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, use),
            }),
            Request(5, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, outside + "erro".Length),
            }),
            Request(6, "textDocument/hover", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, loadingControl),
            }),
            Request(7, "textDocument/completion", new
            {
                textDocument = new { uri },
                position = PositionAtOffset(source, loadingSource),
            }),
            Request(8, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Message"));
            StringAssert.Contains(HoverText(messages, 3), "Exception error");
            var definition = Response(messages, 4).GetProperty("result");
            Assert.AreEqual(uri, definition.GetProperty("uri").GetString());
            var declarationPrefix = source[..declaration];
            var definitionStart = definition.GetProperty("range").GetProperty("start");
            Assert.AreEqual(declarationPrefix.Count(character => character == '\n'),
                definitionStart.GetProperty("line").GetInt32());
            Assert.AreEqual(declaration - (declarationPrefix.LastIndexOf('\n') + 1),
                definitionStart.GetProperty("character").GetInt32());
            Assert.IsFalse(Response(messages, 5).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "error"));
            StringAssert.Contains(HoverText(messages, 6), "ProgressBar");
            Assert.IsTrue(Response(messages, 7).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "IsPending"));
            var loadingDiagnostic = PublishedDiagnostics(messages, uri)
                .SelectMany(batch => batch.EnumerateArray())
                .First(item => item.GetProperty("message").GetString()?.Contains(
                    "DefinitelyNotAProperty", StringComparison.Ordinal) == true);
            var diagnosticOffset = source.IndexOf("DefinitelyNotAProperty", StringComparison.Ordinal);
            var diagnosticPrefix = source[..diagnosticOffset];
            var actualPosition = loadingDiagnostic.GetProperty("range").GetProperty("start");
            Assert.AreEqual(diagnosticPrefix.Count(character => character == '\n'),
                actualPosition.GetProperty("line").GetInt32());
            Assert.AreEqual(diagnosticOffset - (diagnosticPrefix.LastIndexOf('\n') + 1),
                actualPosition.GetProperty("character").GetInt32());
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public async Task Cross_file_component_semantics_follow_unsaved_sibling_generations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-lsp-composition-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "Demo.csproj");
            var callerPath = Path.Combine(directory, "Main.lui");
            var calleePath = Path.Combine(directory, "Child.lui");
            var caller = "namespace Demo; component Main() => Window { Child(title: \"caller\") {} };";
            var callee = "namespace Demo; component Child(string title = \"disk\") => TextBlock { Text: title; };";
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"Main.lui\" /><LucentSource Include=\"Child.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(callerPath, caller);
            await File.WriteAllTextAsync(calleePath, callee);

            var callerUri = new Uri(callerPath).AbsoluteUri;
            var calleeUri = new Uri(calleePath).AbsoluteUri;
            var callPosition = PositionAtOffset(caller, caller.IndexOf("Child", StringComparison.Ordinal) + 5);
            var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = callerUri, languageId = "lucent", version = 1, text = caller } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = calleeUri, languageId = "lucent", version = 1, text = callee } }),
                Request(10, "textDocument/completion", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(11, "textDocument/hover", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(12, "textDocument/definition", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Notification("textDocument/didChange", new { textDocument = new { uri = calleeUri, version = 2 }, contentChanges = new[] { new { text = "namespace Demo; component Child(int count = 1) => TextBlock { Text: count.ToString(); };" } } }),
                Request(20, "textDocument/completion", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(21, "textDocument/hover", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(22, "textDocument/definition", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Notification("textDocument/didChange", new { textDocument = new { uri = calleeUri, version = 3 }, contentChanges = new[] { new { text = callee } } }),
                Request(30, "textDocument/definition", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Notification("textDocument/didChange", new { textDocument = new { uri = calleeUri, version = 4 }, contentChanges = new[] { new { text = "namespace Demo; component Child(int count = 1) => TextBlock { Text: count.ToString(); };" } } }),
                Notification("textDocument/didClose", new { textDocument = new { uri = calleeUri } }),
                Request(40, "textDocument/hover", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(41, "textDocument/definition", new { textDocument = new { uri = callerUri }, position = callPosition }),
                Request(99, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());

            StringAssert.Contains(HoverText(messages, 11), "component Demo.Child");
            Assert.AreEqual(calleeUri, Response(messages, 12).GetProperty("result").GetProperty("uri").GetString());
            Assert.IsTrue(Response(messages, 20).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "count"));
            StringAssert.Contains(HoverText(messages, 21), "component Demo.Child");
            Assert.AreEqual(calleeUri, Response(messages, 22).GetProperty("result").GetProperty("uri").GetString());
            Assert.IsTrue(PublishedDiagnostics(messages, callerUri).Any(diagnostics => diagnostics.GetArrayLength() > 0));
            var callerDiagnosticBatches = PublishedDiagnostics(messages, callerUri).ToArray();
            Assert.IsTrue(callerDiagnosticBatches.Any(diagnostics => diagnostics.GetArrayLength() == 0),
                string.Join("; ", callerDiagnosticBatches.Select(diagnostics => diagnostics.GetRawText())));
            Assert.AreEqual(calleeUri, Response(messages, 30).GetProperty("result").GetProperty("uri").GetString());
            StringAssert.Contains(HoverText(messages, 40), "component Demo.Child");
            Assert.AreEqual(calleeUri, Response(messages, 41).GetProperty("result").GetProperty("uri").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Project_analysis_does_not_resolve_components_from_another_project()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-lsp-isolation-tests",
            Guid.NewGuid().ToString("N"));
        var first = Path.Combine(directory, "First");
        var second = Path.Combine(directory, "Second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            var firstProject = Path.Combine(first, "First.csproj");
            var secondProject = Path.Combine(second, "Second.csproj");
            var firstMain = Path.Combine(first, "Main.lui");
            var firstChild = Path.Combine(first, "Child.lui");
            var secondMain = Path.Combine(second, "Main.lui");
            var secondChild = Path.Combine(second, "Child.lui");
            const string projectTemplate = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"Main.lui\" /><LucentSource Include=\"Child.lui\" /></ItemGroup></Project>";
            const string main = "namespace Demo; component Main() => Window { Child {} };";
            const string child = "namespace Demo; component Child() => TextBlock { Text: \"child\"; };";
            await File.WriteAllTextAsync(firstProject, projectTemplate);
            await File.WriteAllTextAsync(secondProject, projectTemplate);
            await File.WriteAllTextAsync(firstMain, main);
            await File.WriteAllTextAsync(firstChild, "namespace Demo; component Other() => TextBlock {};" );
            await File.WriteAllTextAsync(secondMain, main);
            await File.WriteAllTextAsync(secondChild, child);

            var firstMainUri = new Uri(firstMain).AbsoluteUri;
            var firstChildUri = new Uri(firstChild).AbsoluteUri;
            var secondMainUri = new Uri(secondMain).AbsoluteUri;
            var secondChildUri = new Uri(secondChild).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = firstMainUri, languageId = "lucent", version = 1, text = main } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = firstChildUri, languageId = "lucent", version = 1, text = "namespace Demo; component Other() => TextBlock {};" } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = secondMainUri, languageId = "lucent", version = 1, text = main } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = secondChildUri, languageId = "lucent", version = 1, text = child } }),
                Request(2, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var diagnostics = PublishedDiagnostics(ReadMessages(output.ToArray()), firstMainUri).ToArray();
            Assert.IsTrue(diagnostics.Any(batch => batch.EnumerateArray().Any(item =>
                item.GetProperty("code").GetString() == "LUC2001")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Formatting_is_advertised_and_idempotent()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lucent-format-{Guid.NewGuid():N}.lui");
        const string source = "namespace Demo;  \r\ncomponent App() => Border {};   \r\n";
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/formatting", new { textDocument = new { uri }, options = new { tabSize = 4, insertSpaces = true } }),
                Request(3, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            var capabilities = Response(messages, 1).GetProperty("result").GetProperty("capabilities");
            Assert.IsTrue(capabilities.GetProperty("documentFormattingProvider").GetBoolean());
            Assert.AreEqual("namespace Demo;\ncomponent App() => Border {};\n",
                Response(messages, 2).GetProperty("result")[0].GetProperty("newText").GetString());
        }
        finally { File.Delete(sourcePath); }
    }

    [TestMethod]
    public async Task Cancel_request_cancels_the_matching_queued_request_without_a_stale_result()
    {
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            $"lucent-cancel-{Guid.NewGuid():N}.lui");
        const string source = "namespace Demo;\ncomponent App() => Border {};\n";
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = new { line = 1, character = 28 } }),
                Notification("$/cancelRequest", new { id = 2 }),
                Request(3, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var response = Response(ReadMessages(output.ToArray()), 2);
            Assert.IsFalse(response.TryGetProperty("result", out _));
            Assert.AreEqual(-32800, response.GetProperty("error").GetProperty("code").GetInt32());
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public async Task Cancel_request_interrupts_an_in_flight_request_without_corrupting_framing()
    {
        const string uri = "file:///InFlightCancel.lui";
        const string source = "namespace Demo;\ncomponent App() => Border {};\n";
        using var input = new AppendableInputStream();
        using var output = new MemoryStream();
        var requestStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var server = LanguageServer.RunAsync(
            input,
            output,
            _ => { },
            async (method, cancellationToken) =>
            {
                if (method != "textDocument/completion")
                    return;

                requestStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        input.Append(
            Request(1, "initialize", new { capabilities = new { } }),
            Notification("initialized", new { }),
            Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
            Request(2, "textDocument/completion", new { textDocument = new { uri }, position = new { line = 1, character = 28 } }));
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        input.Append(
            Notification("$/cancelRequest", new { id = 2 }),
            Request(3, "shutdown", null),
            Notification("exit", null));
        input.CompleteWriting();

        Assert.AreEqual(0, await server.WaitAsync(TimeSpan.FromSeconds(10)));
        var messages = ReadMessages(output.ToArray());
        Assert.AreEqual(-32800,
            Response(messages, 2).GetProperty("error").GetProperty("code").GetInt32());
        Assert.AreEqual(JsonValueKind.Null,
            Response(messages, 3).GetProperty("result").ValueKind);
    }

    [TestMethod]
    public async Task Document_symbols_are_advertised_and_return_component_members()
    {
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            $"lucent-symbols-{Guid.NewGuid():N}.lui");
        const string source = "namespace Demo;\ncomponent Card(string Title, slot Body) => Border {};\n";
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/documentSymbol", new { textDocument = new { uri } }),
                Request(3, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 1).GetProperty("result").GetProperty("capabilities").GetProperty("documentSymbolProvider").GetBoolean());
            var names = Response(messages, 2).GetProperty("result").EnumerateArray()
                .Select(symbol => symbol.GetProperty("name").GetString())
                .ToArray();
            CollectionAssert.Contains(names, "Card");
            CollectionAssert.Contains(names, "Title");
            CollectionAssert.Contains(names, "Body");
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public async Task Watched_csharp_change_refreshes_completion_before_the_next_request()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"lucent-watched-csharp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Demo.csproj");
        var modelPath = Path.Combine(directory, "Feature.cs");
        var sourcePath = Path.Combine(directory, "App.lui");
        const string oldModel = "namespace Demo; public static class Feature { public static string Old => \"old\"; }";
        const string newModel = "namespace Demo; public static class Feature { public static string New => \"new\"; }";
        const string oldSource = "namespace Demo; component App() => TextBlock { Text: Feature.O; };";
        const string newSource = "namespace Demo; component App() => TextBlock { Text: Feature.N; };";
        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(modelPath, oldModel);
        await File.WriteAllTextAsync(sourcePath, oldSource);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            var modelUri = new Uri(modelPath).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = oldSource } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = PositionAtOffset(oldSource, oldSource.IndexOf("Feature.O", StringComparison.Ordinal) + "Feature.O".Length) }),
                Notification("textDocument/didChange", new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { text = newSource } } }),
                Notification("workspace/didChangeWatchedFiles", new { changes = new[] { new { uri = modelUri, type = 2 } } }),
                Request(3, "textDocument/completion", new { textDocument = new { uri }, position = PositionAtOffset(newSource, newSource.IndexOf("Feature.N", StringComparison.Ordinal) + "Feature.N".Length) }),
                Request(4, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();
            var firstCompletionObserved = 0;

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output, metric =>
            {
                if (metric.Method == "textDocument/completion" &&
                    Interlocked.Exchange(ref firstCompletionObserved, 1) == 0)
                {
                    File.WriteAllText(modelPath, newModel);
                }
            }));

            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Old"));
            Assert.IsTrue(Response(messages, 3).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "New"));
            Assert.IsFalse(Response(messages, 3).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "Old"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Source_maps_are_versioned_and_reject_stale_generated_content()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lucent-map-{Guid.NewGuid():N}.lui");
        const string source = """
            namespace Demo;
            using System;
            component App(Uri model) =>
                TextBlock {
                    Name: "😀";
                    Text: binding(model.Host);
                };
            """;
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var compilation = LucentCompiler.Compile(source, sourcePath);
            var map = compilation.SourceMap!;
            var mapGenerated = compilation.GeneratedSource!;
            var uri = new Uri(sourcePath).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 7, text = source } }),
                Request(2, "lucent/sourceMap", new { textDocument = new { uri }, generatedHash = map.GeneratedHash, lucentHash = map.LucentHash, version = 7 }),
                Request(3, "lucent/sourceMap", new { textDocument = new { uri }, generatedHash = "stale" }),
                Request(5, "lucent/sourceMap", new { textDocument = new { uri }, generatedHash = map.GeneratedHash, lucentHash = "stale", version = 7 }),
                Request(6, "lucent/sourceMap", new { textDocument = new { uri }, generatedHash = map.GeneratedHash, lucentHash = map.LucentHash, version = 8 }),
                Request(7, "lucent/sourceMap", new { textDocument = new { uri }, generatedHash = map.GeneratedHash, lucentHash = map.LucentHash }),
                Request(4, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            var response = Response(messages, 2).GetProperty("result");
            Assert.AreEqual(map.LucentHash, response.GetProperty("lucentHash").GetString());
            Assert.AreEqual(map.GeneratedHash, response.GetProperty("generatedHash").GetString());
            Assert.AreEqual(7, response.GetProperty("version").GetInt32());
            var forward = response.GetProperty("generatedToLucent").EnumerateArray().ToArray();
            Assert.HasCount(map.Entries.Count, forward);
            for (var index = 0; index < map.Entries.Count; index++)
            {
                var expected = map.Entries[index];
                var actual = forward[index];
                Assert.AreEqual(expected.GeneratedUri, actual.GetProperty("generatedUri").GetString());
                Assert.AreEqual(expected.LucentUri, actual.GetProperty("lucentUri").GetString());
                Assert.AreEqual(Slice(source, expected.LucentRange),
                    Slice(source, ReadRange(actual.GetProperty("lucentRange"))));
                Assert.AreEqual(Slice(mapGenerated, expected.GeneratedRange),
                    Slice(mapGenerated, ReadRange(actual.GetProperty("generatedRange"))));
            }
            Assert.IsTrue(forward.All(entry => entry.GetProperty("generatedUri").GetString() != entry.GetProperty("lucentUri").GetString()));
            Assert.IsTrue(forward.All(entry => !Slice(mapGenerated, ReadRange(entry.GetProperty("generatedRange"))).Contains("#line", StringComparison.Ordinal)));
            Assert.IsTrue(response.GetProperty("lucentToGenerated").GetArrayLength() > 0);
            Assert.AreEqual(JsonValueKind.Null, Response(messages, 3).GetProperty("result").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, Response(messages, 5).GetProperty("result").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, Response(messages, 6).GetProperty("result").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, Response(messages, 7).GetProperty("result").ValueKind);
        }
        finally { File.Delete(sourcePath); }
    }

    [TestMethod]
    public async Task Css_documents_use_the_shared_typed_catalog_for_completion()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lucent-css-{Guid.NewGuid():N}.css");
        const string source = ".card {  }";
        await File.WriteAllTextAsync(sourcePath, source);
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "css", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = PositionAtOffset(source, source.IndexOf("}" , StringComparison.Ordinal)) }),
                Request(3, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            Assert.IsTrue(Response(ReadMessages(output.ToArray()), 2).GetProperty("result").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "background"));
        }
        finally { File.Delete(sourcePath); }
    }

    [TestMethod]
    public async Task Css_documents_complete_classes_and_resource_tokens_without_lucent_class_value_completion()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-css-nav-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "App.css");
        var luiPath = Path.Combine(directory, "App.lui");
        var projectPath = Path.Combine(directory, "App.csproj");
        const string source = ".pan { background: resource(\"Lucent.\"); }\n.";
        await File.WriteAllTextAsync(sourcePath, source);
        await File.WriteAllTextAsync(luiPath, "namespace Demo; component App() => Border { Class: \"panel\"; };");
        await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
        try
        {
            var uri = new Uri(sourcePath).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "css", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = PositionAtOffset(source, source.IndexOf("Lucent.", StringComparison.Ordinal) + "Lucent.".Length) }),
                Request(3, "textDocument/completion", new { textDocument = new { uri }, position = PositionAtOffset(source, source.Length) }),
                Request(4, "textDocument/definition", new { textDocument = new { uri }, position = PositionAtOffset(source, source.IndexOf("Lucent.", StringComparison.Ordinal) + 2) }),
                Request(5, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.IsTrue(Response(messages, 2).GetProperty("result").EnumerateArray().Any(item => item.GetProperty("label").GetString() == "Lucent."));
            Assert.IsTrue(Response(messages, 3).GetProperty("result").EnumerateArray().Any(item => item.GetProperty("label").GetString() == "panel"));
            Assert.AreEqual(uri, Response(messages, 4).GetProperty("result").GetProperty("uri").GetString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Class_values_complete_live_adjacent_css_tokens_without_request_path_io()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-class-values-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var lui = Path.Combine(directory, "App.lui");
            var css = Path.Combine(directory, "App.css");
            var project = Path.Combine(directory, "App.csproj");
            const string source = "namespace Demo; component App() => Button { Class: \"existing se\"; };";
            const string style = "Button.secondary { background: #fff; }\nButton.existing { background: #fff; }\n.surface { background: #000; }";
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Avalonia\" Version=\"12.1.1\" /><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(lui, source);
            await File.WriteAllTextAsync(css, style);
            var luiUri = new Uri(lui).AbsoluteUri;
            var cssUri = new Uri(css).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = cssUri, languageId = "css", version = 1, text = style } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = luiUri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri = luiUri }, position = PositionAtOffset(source, source.IndexOf("se\"", StringComparison.Ordinal) + 2) }),
                Request(3, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var items = Response(ReadMessages(output.ToArray()), 2).GetProperty("result").EnumerateArray().ToArray();
            Assert.IsTrue(items.Any(item => item.GetProperty("label").GetString() == "secondary"));
            Assert.IsFalse(items.Any(item => item.GetProperty("label").GetString() == "existing"));
            Assert.IsTrue(items.All(item => item.GetProperty("label").GetString() != "surface"));
            var secondary = items.Single(item => item.GetProperty("label").GetString() == "secondary");
            var edit = secondary.GetProperty("textEdit");
            Assert.AreEqual(source.IndexOf("se\"", StringComparison.Ordinal), edit.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
            Assert.AreEqual(source.IndexOf("se\"", StringComparison.Ordinal) + 2, edit.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
            Assert.AreEqual("secondary", edit.GetProperty("newText").GetString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Css_token_index_is_project_scoped_overlay_aware_and_maps_each_class_literal_token()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-css-project-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var alphaProject = Path.Combine(directory, "Alpha.csproj");
            var betaProject = Path.Combine(directory, "Beta.csproj");
            var alphaLui = Path.Combine(directory, "Alpha.lui");
            var betaLui = Path.Combine(directory, "Beta.lui");
            var alphaCss = Path.Combine(directory, "Alpha.css");
            var betaCss = Path.Combine(directory, "Beta.css");
            const string alphaSource = "namespace Demo; component Alpha() => Border { Class: \"first second\"; };";
            const string alphaOverlay = "namespace Demo; component Alpha() => Border { Class: \"first overlay-second\"; };";
            const string betaSource = "namespace Other; component Beta() => Border { Class: \"beta-only\"; };";
            const string alphaStyle = ".second { background: resource(\"Lucent.Initial.Key\"); }\n.";
            const string alphaStyleOverlay = ".overlay-second { background: resource('Lucent.Updated.Key'); }\n.";
            const string betaStyle = ".beta-only { background: resource(\"Beta.Only\"); }";
            await File.WriteAllTextAsync(alphaProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"Alpha.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(betaProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"Beta.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(alphaLui, alphaSource);
            await File.WriteAllTextAsync(betaLui, betaSource);
            await File.WriteAllTextAsync(alphaCss, alphaStyle);
            await File.WriteAllTextAsync(betaCss, betaStyle);
            var alphaCssUri = new Uri(alphaCss).AbsoluteUri;
            var alphaLuiUri = new Uri(alphaLui).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = alphaLuiUri, languageId = "lucent", version = 1, text = alphaSource } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = alphaCssUri, languageId = "css", version = 1, text = alphaStyle } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri = alphaCssUri }, position = PositionAtOffset(alphaStyle, alphaStyle.Length) }),
                Request(3, "textDocument/definition", new { textDocument = new { uri = alphaCssUri }, position = PositionAtOffset(alphaStyle, alphaStyle.IndexOf("second", StringComparison.Ordinal) + 2) }),
                Notification("textDocument/didChange", new { textDocument = new { uri = alphaLuiUri, version = 2 }, contentChanges = new[] { new { text = alphaOverlay } } }),
                Notification("textDocument/didChange", new { textDocument = new { uri = alphaCssUri, version = 2 }, contentChanges = new[] { new { text = alphaStyleOverlay } } }),
                Request(4, "textDocument/completion", new { textDocument = new { uri = alphaCssUri }, position = PositionAtOffset(alphaStyleOverlay, alphaStyleOverlay.Length) }),
                Request(5, "textDocument/completion", new { textDocument = new { uri = alphaCssUri }, position = PositionAtOffset(alphaStyleOverlay, alphaStyleOverlay.IndexOf("Lucent.Updated", StringComparison.Ordinal) + "Lucent.Updated".Length) }),
                Request(6, "textDocument/definition", new { textDocument = new { uri = alphaCssUri }, position = PositionAtOffset(alphaStyleOverlay, alphaStyleOverlay.IndexOf("Lucent.Updated", StringComparison.Ordinal) + 8) }),
                Request(7, "textDocument/completion", new { textDocument = new { uri = alphaLuiUri }, position = PositionAtOffset(alphaSource, alphaSource.IndexOf("second", StringComparison.Ordinal) + 2) }),
                Request(8, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            var labels = Response(messages, 2).GetProperty("result").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
            CollectionAssert.Contains(labels, "first");
            CollectionAssert.Contains(labels, "second");
            CollectionAssert.DoesNotContain(labels, "beta-only");
            var definition = Response(messages, 3).GetProperty("result");
            Assert.AreEqual(alphaLuiUri, definition.GetProperty("uri").GetString());
            var expectedSecond = alphaSource.IndexOf("second", StringComparison.Ordinal);
            Assert.AreEqual(0, definition.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
            Assert.AreEqual(expectedSecond, definition.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
            var overlayLabels = Response(messages, 4).GetProperty("result").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
            CollectionAssert.Contains(overlayLabels, "overlay-second");
            CollectionAssert.DoesNotContain(overlayLabels, "second");
            var resources = Response(messages, 5).GetProperty("result").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
            CollectionAssert.Contains(resources, "Lucent.Updated.Key");
            var resourceDefinition = Response(messages, 6).GetProperty("result");
            Assert.AreEqual(alphaCssUri, resourceDefinition.GetProperty("uri").GetString());
            Assert.AreEqual(alphaStyleOverlay.IndexOf("Lucent.Updated.Key", StringComparison.Ordinal),
                resourceDefinition.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
            var lucentClassValueLabels = Response(messages, 7).GetProperty("result").EnumerateArray()
                .Select(item => item.GetProperty("label").GetString())
                .ToArray();
            CollectionAssert.DoesNotContain(lucentClassValueLabels, "first");
            CollectionAssert.DoesNotContain(lucentClassValueLabels, "second");
            CollectionAssert.Contains(lucentClassValueLabels, "overlay-second");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Component_references_and_rename_are_project_scoped_and_safe()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lucent-rename", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "Demo.csproj");
            var caller = Path.Combine(directory, "Main.lui");
            var child = Path.Combine(directory, "Child.lui");
            const string main = "namespace Demo; component Main() => Border { Child {}; };";
            const string leaf = "namespace Demo; component Child() => TextBlock {};";
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><LucentSource Include=\"Main.lui\"/><LucentSource Include=\"Child.lui\"/></ItemGroup></Project>");
            await File.WriteAllTextAsync(caller, main);
            await File.WriteAllTextAsync(child, leaf);
            var callerUri = new Uri(caller).AbsoluteUri;
            var childUri = new Uri(child).AbsoluteUri;
            using var input = BuildInput(
                Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = callerUri, languageId = "lucent", version = 1, text = main } }),
                Notification("textDocument/didOpen", new { textDocument = new { uri = childUri, languageId = "lucent", version = 1, text = leaf } }),
                Request(2, "textDocument/references", new { textDocument = new { uri = callerUri }, position = PositionOf(main, "Child") }),
                Request(3, "textDocument/rename", new { textDocument = new { uri = callerUri }, position = PositionOf(main, "Child"), newName = "Renamed" }),
                Request(4, "textDocument/rename", new { textDocument = new { uri = callerUri }, position = PositionOf(main, "Child"), newName = "not valid" }),
                Request(5, "textDocument/rename", new { textDocument = new { uri = callerUri }, position = PositionOf(main, "Child"), newName = "@Child" }),
                Request(6, "shutdown", null),
                Notification("exit", null));
            using var output = new MemoryStream();

            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            var messages = ReadMessages(output.ToArray());
            Assert.AreEqual(2, Response(messages, 2).GetProperty("result").GetArrayLength());
            var changes = Response(messages, 3).GetProperty("result").GetProperty("changes");
            Assert.AreEqual(2, changes.EnumerateObject().Count());
            Assert.AreEqual(JsonValueKind.Null, Response(messages, 4).GetProperty("result").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, Response(messages, 5).GetProperty("result").ValueKind);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static IEnumerable<JsonElement> PublishedDiagnostics(
        IReadOnlyList<JsonDocument> messages,
        string uri) => messages
        .Where(message => message.RootElement.TryGetProperty("method", out var method) &&
            method.GetString() == "textDocument/publishDiagnostics" &&
            message.RootElement.GetProperty("params").GetProperty("uri").GetString() == uri)
        .Select(message => message.RootElement.GetProperty("params").GetProperty("diagnostics"));

    private static MemoryStream BuildInput(params byte[][] messages) =>
        new(messages.SelectMany(message => message).ToArray());

    private static byte[] Request(int id, string method, object? parameters) =>
        Message(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters,
        });

    private static byte[] Notification(string method, object? parameters) =>
        Message(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters,
        });

    private static byte[] Message(object message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        return Frame(payload);
    }

    private static byte[] Frame(byte[] payload)
    {
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {payload.Length}\r\n\r\n");
        return header.Concat(payload).ToArray();
    }

    private sealed class AppendableInputStream : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        private byte[]? _current;
        private int _offset;

        public void Append(params byte[][] messages)
        {
            foreach (var message in messages)
                Assert.IsTrue(_chunks.Writer.TryWrite(message));
        }

        public void CompleteWriting() => _chunks.Writer.TryComplete();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            while (_current is null || _offset == _current.Length)
            {
                if (!_chunks.Reader.TryRead(out _current))
                {
                    try
                    {
                        _current = await _chunks.Reader.ReadAsync(cancellationToken);
                    }
                    catch (ChannelClosedException)
                    {
                        return 0;
                    }
                }

                _offset = 0;
            }

            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                CompleteWriting();
            base.Dispose(disposing);
        }
    }

    private static IReadOnlyList<JsonDocument> ReadMessages(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var messages = new List<JsonDocument>();
        var header = new List<byte>();

        while (input.Position < input.Length)
        {
            header.Clear();
            while (true)
            {
                var value = input.ReadByte();
                Assert.AreNotEqual(-1, value);
                header.Add((byte)value);
                if (header.Count >= 4 &&
                    header[^4..].SequenceEqual("\r\n\r\n"u8.ToArray()))
                {
                    break;
                }
            }

            var contentLength = int.Parse(
                Encoding.ASCII.GetString(header.ToArray())
                    .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                    .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    .Split(':', 2)[1]
                    .Trim());
            var payload = new byte[contentLength];
            var read = input.Read(payload, 0, payload.Length);
            Assert.AreEqual(contentLength, read);
            messages.Add(JsonDocument.Parse(payload));
        }

        return messages;
    }

    private static JsonElement Response(
        IReadOnlyList<JsonDocument> messages,
        int id) =>
        messages.Single(message =>
                message.RootElement.TryGetProperty("id", out var responseId) &&
                responseId.ValueKind == JsonValueKind.Number &&
                responseId.GetInt32() == id)
            .RootElement;

    private static string HoverText(
        IReadOnlyList<JsonDocument> messages,
        int id) =>
        Response(messages, id)
            .GetProperty("result")
            .GetProperty("contents")
            .GetProperty("value")
            .GetString()!;

    private static void WriteReferencedManifestAssembly(string path, string className)
    {
        var identity = new LucentAssemblyIdentity("Package", "0.0.0.0", "", "");
        var manifest = LucentModuleManifest.Serialize(new LucentModuleManifestModel(1, 0, 0, "1.0",
            identity, [], [new StyleClassEntry(className, null, StyleClassOrigin.LocalCss, null, "." + className)], []));
        var compilation = CSharpCompilation.Create("Package",
            [CSharpSyntaxTree.ParseText("public sealed class PackageMarker { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var emitted = compilation.Emit(stream, manifestResources: [new ResourceDescription(
            LucentModuleManifest.ResourceName, () => new MemoryStream(manifest), isPublic: true)]);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    private static async Task<string[]> ThemeCompletionLabelsAsync(string code, string? axaml)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-theme-negative-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            WriteReferencedThemeAssembly(Path.Combine(directory, "Package.dll"));
            const string source = "namespace Demo; component App() => Button { Class: \"se\"; };";
            await File.WriteAllTextAsync(Path.Combine(directory, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Avalonia\" Version=\"12.1.1\" /><Reference Include=\"Package\"><HintPath>Package.dll</HintPath></Reference><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "App.cs"), code);
            await File.WriteAllTextAsync(Path.Combine(directory, "App.lui"), source);
            if (axaml is not null) await File.WriteAllTextAsync(Path.Combine(directory, "App.axaml"), axaml);
            var uri = new Uri(Path.Combine(directory, "App.lui")).AbsoluteUri;
            using var input = BuildInput(Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }), Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = PositionAtOffset(source, source.IndexOf("se\"", StringComparison.Ordinal) + 2) }),
                Request(3, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            return Response(ReadMessages(output.ToArray()), 2).GetProperty("result").EnumerateArray()
                .Select(item => item.GetProperty("label").GetString()!).ToArray();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<(string Label, string Detail)[]> GlobalStyleCompletionItemsAsync(string code)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucent-global-style-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            WriteReferencedGlobalStyleAssembly(Path.Combine(directory, "Package.dll"));
            const string source = "namespace Demo; component App() => Border { Class: \"first-\"; };";
            await File.WriteAllTextAsync(Path.Combine(directory, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Avalonia\" Version=\"12.1.1\" /><Reference Include=\"Package\"><HintPath>Package.dll</HintPath></Reference><LucentSource Include=\"App.lui\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "App.cs"), code);
            await File.WriteAllTextAsync(Path.Combine(directory, "App.lui"), source);
            var uri = new Uri(Path.Combine(directory, "App.lui")).AbsoluteUri;
            using var input = BuildInput(Request(1, "initialize", new { rootUri = new Uri(directory).AbsoluteUri, capabilities = new { } }),
                Notification("initialized", new { }), Notification("textDocument/didOpen", new { textDocument = new { uri, languageId = "lucent", version = 1, text = source } }),
                Request(2, "textDocument/completion", new { textDocument = new { uri }, position = PositionAtOffset(source, source.IndexOf("first-", StringComparison.Ordinal) + "first-".Length) }),
                Request(3, "shutdown", null), Notification("exit", null));
            using var output = new MemoryStream();
            Assert.AreEqual(0, await LanguageServer.RunAsync(input, output));
            return Response(ReadMessages(output.ToArray()), 2).GetProperty("result").EnumerateArray()
                .Select(item => (item.GetProperty("label").GetString()!, item.GetProperty("detail").GetString()!)).ToArray();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteReferencedThemeAssembly(string path)
    {
        var identity = new LucentAssemblyIdentity("Package", "0.0.0.0", "", "");
        var manifest = LucentModuleManifest.Serialize(new LucentModuleManifestModel(1, 0, 0, "1.0",
            identity, ["Package.Theme"], [new StyleClassEntry("secondary", "Avalonia.Controls.Button",
                StyleClassOrigin.NativeTheme, null, "theme button")], []));
        Assert.IsTrue(LucentModuleManifest.TryReadNormalized(manifest, identity, out _, out var error), error);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(reference => MetadataReference.CreateFromFile(reference))
            .Append(MetadataReference.CreateFromFile(typeof(Avalonia.Styling.Styles).Assembly.Location));
        var compilation = CSharpCompilation.Create("Package",
            [CSharpSyntaxTree.ParseText("namespace Package; public sealed class Theme : Avalonia.Styling.Styles { }")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var emitted = compilation.Emit(stream, manifestResources: [new ResourceDescription(
            LucentModuleManifest.ResourceName, () => new MemoryStream(manifest), isPublic: true)]);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    private static void WriteReferencedGlobalStyleAssembly(string path)
    {
        var identity = new LucentAssemblyIdentity("Package", "0.0.0.0", "", "");
        var manifest = LucentModuleManifest.Serialize(new LucentModuleManifestModel(1, 0, 0, "1.0", identity,
            ["Package.FirstLucentStyles", "Package.SecondLucentStyles"],
            [new StyleClassEntry("first-global", "Avalonia.Controls.Border", StyleClassOrigin.GlobalStyle, null, "Border.first-global", "Package.FirstLucentStyles"),
             new StyleClassEntry("second-global", "Avalonia.Controls.Border", StyleClassOrigin.GlobalStyle, null, "Border.second-global", "Package.SecondLucentStyles")], []));
        Assert.IsTrue(LucentModuleManifest.TryReadNormalized(manifest, identity, out _, out var error), error);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(reference => MetadataReference.CreateFromFile(reference))
            .Append(MetadataReference.CreateFromFile(typeof(Avalonia.Styling.Styles).Assembly.Location));
        var compilation = CSharpCompilation.Create("Package",
            [CSharpSyntaxTree.ParseText("namespace Package; public sealed class FirstLucentStyles : Avalonia.Styling.Styles { } public sealed class SecondLucentStyles : Avalonia.Styling.Styles { }")],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var emitted = compilation.Emit(stream, manifestResources: [new ResourceDescription(
            LucentModuleManifest.ResourceName, () => new MemoryStream(manifest), isPublic: true)]);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    private static void WriteLocalManifestAssembly(
        string path,
        string projectPath,
        string luiPath,
        string luiText,
        string cssPath,
        string cssText)
    {
        var identity = new LucentAssemblyIdentity("App", "0.0.0.0", "", "");
        var manifest = LucentModuleManifest.Create(
            new LucentProjectContext(ProjectPath: projectPath),
            [new LucentSourceInput(luiPath, luiText, cssPath, cssText)],
            identity);
        var compilation = CSharpCompilation.Create("App",
            [CSharpSyntaxTree.ParseText("public sealed class AppMarker { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var emitted = compilation.Emit(stream, manifestResources: [new ResourceDescription(
            LucentModuleManifest.ResourceName, () => new MemoryStream(manifest), isPublic: true)]);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    private static object PositionOf(string text, string value)
    {
        var offset = text.IndexOf(value, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, offset);
        return PositionAtOffset(text, offset);
    }

    private static object PositionAtOffset(string text, int offset)
    {
        var prefix = text[..offset];
        var line = prefix.Count(character => character == '\n');
        var lineStart = prefix.LastIndexOf('\n') + 1;
        return new { line, character = offset - lineStart };
    }

    private static LucentSourceMapRange ReadRange(JsonElement range) => new(
        range.GetProperty("start").GetProperty("line").GetInt32(),
        range.GetProperty("start").GetProperty("character").GetInt32(),
        range.GetProperty("end").GetProperty("line").GetInt32(),
        range.GetProperty("end").GetProperty("character").GetInt32());

    private static string Slice(string text, LucentSourceMapRange range)
    {
        var start = Offset(text, range.StartLine, range.StartCharacter);
        var end = Offset(text, range.EndLine, range.EndCharacter);
        return text[start..end];
    }

    private static int Offset(string text, int line, int character)
    {
        var offset = 0;
        for (var current = 0; current < line; current++)
        {
            offset = text.IndexOf('\n', offset) + 1;
            Assert.IsGreaterThan(0, offset);
        }
        return offset + character;
    }

    private const string InvalidSource =
        "namespace N;\r\n" +
        "component Counter()\r\n" +
        "{\r\n" +
        "    private readonly State<int> count = new(0);\r\n" +
        "    Fragment Render()\r\n" +
        "    {\r\n" +
        "        return Column {\r\n" +
        "            Text { text: \"😀\"; tooltip: \"Not supported\"; }\r\n" +
        "            Button {\r\n" +
        "                Class: \"primary\";\r\n" +
        "                text: \"Increment\";\r\n" +
        "                onClick: () => count.Update(count.Value + 1);\r\n" +
        "            }\r\n" +
        "        };\r\n" +
        "    }\r\n" +
        "}\r\n";

    private const string ValidSource =
        "namespace N;\r\n" +
        "component Counter()\r\n" +
        "{\r\n" +
        "    private readonly State<int> count = new(0);\r\n" +
        "    Fragment Render()\r\n" +
        "    {\r\n" +
        "        return Column {\r\n" +
        "            Text { text: $\"Count: {count.Value}\"; }\r\n" +
        "            Button {\r\n" +
        "                Class: \"primary\";\r\n" +
        "                text: \"Increment\";\r\n" +
        "                onClick: () => count.Update(count.Value + 1);\r\n" +
        "            }\r\n" +
        "        };\r\n" +
        "    }\r\n" +
        "}\r\n";
}
