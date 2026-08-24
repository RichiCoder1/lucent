using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Lucent.Compiler.MSBuild;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Lucent.Compiler.MSBuild.Tests;

[TestClass]
public sealed class CompileLucentTests
{
    [TestMethod]
    public void Counter_is_generated_under_the_intermediate_output_directory()
    {
        using var temporary = new TemporaryDirectory();
        var (task, buildEngine) = CreateTask(temporary, CounterSourcePath());

        Assert.IsTrue(task.Execute());
        Assert.HasCount(1, task.GeneratedFiles);

        var generatedPath = Path.Combine(
            temporary.OutputDirectory,
            "CounterComponent.g.cs");
        Assert.AreEqual(generatedPath, task.GeneratedFiles.Single().ItemSpec);
        var generated = File.ReadAllText(generatedPath);
        var mappedSourcePath = CounterSourcePath()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        StringAssert.Contains(generated, $"#line 11 \"{mappedSourcePath}\"");
        StringAssert.Contains(generated, "internal sealed class CounterComponent");
        StringAssert.Contains(generated, "Styles.Add(new global::Avalonia.Styling.Style");
        StringAssert.Contains(generated, "global::Avalonia.Animation.DoubleTransition");
        StringAssert.Contains(generated, "__lucent_SetCount(__lucent_stateCount + 1);");
        Assert.HasCount(0, buildEngine.Errors);
    }

    [TestMethod]
    public void Unchanged_generation_does_not_rewrite_the_output()
    {
        using var temporary = new TemporaryDirectory();
        var (task, _) = CreateTask(temporary, CounterSourcePath());

        Assert.IsTrue(task.Execute());
        var generatedPath = Path.Combine(
            temporary.OutputDirectory,
            "CounterComponent.g.cs");
        var firstWriteTime = File.GetLastWriteTimeUtc(generatedPath);

        Thread.Sleep(50);
        Assert.IsTrue(task.Execute());

        Assert.AreEqual(firstWriteTime, File.GetLastWriteTimeUtc(generatedPath));
    }

    [TestMethod]
    public void Diagnostics_preserve_the_previous_output()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "Invalid.lui");
        var generatedPath = Path.Combine(
            temporary.OutputDirectory,
            "InvalidComponent.g.cs");
        File.WriteAllText(sourcePath, "component");
        Directory.CreateDirectory(temporary.OutputDirectory);
        File.WriteAllText(generatedPath, "sentinel");

        var (task, buildEngine) = CreateTask(temporary, sourcePath);

        Assert.IsFalse(task.Execute());
        Assert.AreEqual("sentinel", File.ReadAllText(generatedPath));
        Assert.IsTrue(buildEngine.Errors.Any(error => error.Code == "LUC1001"));
        Assert.IsFalse(buildEngine.Errors.Any(error => error.Code == "LUC9002"));
    }

    [TestMethod]
    public void Project_sources_are_available_to_native_symbol_binding()
    {
        using var temporary = new TemporaryDirectory();
        var controlPath = Path.Combine(temporary.Path, "FancyControl.cs");
        var sourcePath = Path.Combine(temporary.Path, "Custom.lui");
        File.WriteAllText(
            controlPath,
            "namespace Demo.Controls; public sealed class FancyControl : " +
            "Avalonia.Controls.ContentControl { public string? Accent { get; set; } }");
        File.WriteAllText(
            sourcePath,
            "namespace Demo; using Demo.Controls; component Custom() { " +
            "Fragment Render() { return FancyControl { Accent: \"blue\"; }; } }");
        var (task, buildEngine) = CreateTask(temporary, sourcePath);
        task.CSharpSources = [new TaskItem(controlPath)];
        task.ProjectPath = Path.Combine(temporary.Path, "Demo.csproj");

        Assert.IsTrue(task.Execute());
        Assert.HasCount(0, buildEngine.Errors);
        StringAssert.Contains(
            File.ReadAllText(task.GeneratedFiles.Single().ItemSpec),
            "new global::Demo.Controls.FancyControl()");
    }

    [TestMethod]
    public void Evaluated_project_semantic_settings_flow_to_the_compiler_task()
    {
        using var temporary = new TemporaryDirectory();
        var model = Path.Combine(temporary.Path, "Feature.cs");
        var source = Path.Combine(temporary.Path, "App.lui");
        File.WriteAllText(model, "#if FEATURE\nnamespace Demo; public static class Feature { public static string? Name => \"ok\"; }\n#endif");
        File.WriteAllText(source, "namespace Ui; component App() => TextBlock { Text: Feature.Name; };");
        var (task, engine) = CreateTask(temporary, source);
        task.ProjectPath = Path.Combine(temporary.Path, "Consumer.csproj");
        task.CSharpSources = [new TaskItem(model)];
        task.GlobalUsings = [new TaskItem("Demo")];
        task.TargetFramework = "net9.0";
        task.LanguageVersion = "12.0";
        task.Nullable = "enable";
        task.DefineConstants = "FEATURE";

        Assert.IsTrue(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
        Assert.IsEmpty(engine.Errors);
    }

    [TestMethod]
    public void Generated_manifest_is_atomic_and_contains_only_current_outputs()
    {
        using var temporary = new TemporaryDirectory();
        var first = Path.Combine(temporary.Path, "First.lui");
        var second = Path.Combine(temporary.Path, "Second.lui");
        File.WriteAllText(first, "namespace Demo; component First() => Border {}; ");
        File.WriteAllText(second, "namespace Demo; component Second() => Border {}; ");
        var engine = new CapturingBuildEngine();
        var task = new CompileLucent
        {
            BuildEngine = engine,
            Sources = [new TaskItem(first), new TaskItem(second)],
            OutputDirectory = temporary.OutputDirectory,
        };

        Assert.IsTrue(task.Execute());
        task.Sources = [new TaskItem(second)];
        Assert.IsTrue(task.Execute());

        var manifest = File.ReadAllText(Path.Combine(temporary.OutputDirectory, "Lucent.GeneratedFiles.props"));
        StringAssert.Contains(manifest, "SecondComponent.g.cs");
        Assert.IsFalse(manifest.Contains("FirstComponent.g.cs", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Module_manifest_is_deterministic_and_contains_no_machine_source_path()
    {
        using var temporary = new TemporaryDirectory();
        var source = Path.Combine(temporary.Path, "Card.lui");
        var style = Path.Combine(temporary.Path, "Card.css");
        File.WriteAllText(source, "namespace Demo; component Card() => Border {}; ");
        File.WriteAllText(style, ".card { background: #112233; }");
        var (task, engine) = CreateTask(temporary, source);
        task.ProjectPath = Path.Combine(temporary.Path, "Demo.csproj");
        task.AssemblyName = "Demo";
        task.AssemblyVersion = "1.2.3.4";

        Assert.IsTrue(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
        Assert.HasCount(1, task.ModuleManifestFiles);
        var path = task.ModuleManifestFiles.Single().ItemSpec;
        var first = File.ReadAllBytes(path);
        Assert.IsFalse(File.ReadAllText(path).Contains(temporary.Path, StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(File.ReadAllText(path), "\"name\":\"card\"");

        Assert.IsTrue(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
        CollectionAssert.AreEqual(first, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Module_manifest_canonicalizes_repeated_class_selectors_independent_of_rule_order()
    {
        using var temporary = new TemporaryDirectory();
        var source = Path.Combine(temporary.Path, "Counter.lui");
        var style = Path.ChangeExtension(source, ".css");
        File.WriteAllText(source, "namespace Demo; component Counter() => Border {}; ");
        File.WriteAllText(style, "Border.counter { width: 1; }\nBorder.counter { height: 1; }");
        var (task, engine) = CreateTask(temporary, source);
        task.ProjectPath = Path.Combine(temporary.Path, "Demo.csproj");
        task.AssemblyName = "Demo";

        Assert.IsTrue(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
        var first = File.ReadAllBytes(task.ModuleManifestFiles.Single().ItemSpec);
        using var firstJson = JsonDocument.Parse(first);
        Assert.AreEqual(1, firstJson.RootElement.GetProperty("styleClasses").GetArrayLength());

        File.WriteAllText(style, "Border.counter { height: 1; }\nBorder.counter { width: 1; }");
        Assert.IsTrue(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
        CollectionAssert.AreEqual(first, File.ReadAllBytes(task.ModuleManifestFiles.Single().ItemSpec));
    }

    [TestMethod]
    public void Module_manifest_rejects_blank_or_duplicate_style_catalog_type_names()
    {
        using var temporary = new TemporaryDirectory();
        var source = Path.Combine(temporary.Path, "Card.lui");
        File.WriteAllText(source, "namespace Demo; component Card() => Border {}; ");
        var (task, engine) = CreateTask(temporary, source);
        task.StyleCatalogTypes = [new TaskItem("Demo.Catalog"), new TaskItem(" Demo.Catalog ")];

        Assert.IsFalse(task.Execute());
        Assert.IsTrue(engine.Errors.Any(error => error.Code == "LUC9008"));
    }

    [TestMethod]
    public void Ordered_global_styles_generate_the_public_catalog_and_manifest_entries()
    {
        using var temporary = new TemporaryDirectory();
        var first = Path.Combine(temporary.Path, "first.css");
        var second = Path.Combine(temporary.Path, "second.css");
        File.WriteAllText(first, "Border.shared { background: #ff0000; }");
        File.WriteAllText(second, "Border.shared { background: #0000ff; }");
        var engine = new CapturingBuildEngine();
        var task = new CompileLucent
        {
            BuildEngine = engine, OutputDirectory = temporary.OutputDirectory,
            Styles = [new TaskItem(first), new TaskItem(second)], RootNamespace = "Demo", AssemblyName = "Demo",
        };

        Assert.IsTrue(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
        var generated = File.ReadAllText(Path.Combine(temporary.OutputDirectory, "LucentStyles.g.cs"));
        StringAssert.Contains(generated, "public sealed class LucentStyles");
        Assert.IsTrue(generated.IndexOf("0xFF", StringComparison.Ordinal) < generated.LastIndexOf("0x00", StringComparison.Ordinal));
        var manifest = File.ReadAllText(task.ModuleManifestFiles.Single().ItemSpec);
        StringAssert.Contains(manifest, "Demo.LucentStyles");
        StringAssert.Contains(manifest, "\"origin\":2");
        Assert.IsEmpty(engine.Errors);
    }

    [TestMethod]
    public void Global_styles_warn_only_for_an_executable_app_without_direct_installation()
    {
        using var temporary = new TemporaryDirectory();
        var style = Path.Combine(temporary.Path, "app.css");
        var app = Path.Combine(temporary.Path, "App.cs");
        File.WriteAllText(style, "Border.card { background: #ff0000; }");
        File.WriteAllText(app, "using Avalonia; namespace Demo; sealed class App : Application { }");
        var (task, engine) = CreateTask(temporary, app);
        task.Sources = [];
        task.Styles = [new TaskItem(style)]; task.RootNamespace = "Demo"; task.OutputType = "Exe";
        task.CSharpSources = [new TaskItem(app)];
        task.References = [new TaskItem(typeof(Avalonia.Application).Assembly.Location)];
        Assert.IsTrue(task.Execute());
        Assert.IsTrue(engine.Warnings.Any(warning => warning.Code == "LUC9009" &&
            warning.Message?.Contains("Styles.Add(new Demo.LucentStyles());", StringComparison.Ordinal) == true));

        engine.Warnings.Clear();
        File.WriteAllText(app, "using Avalonia; namespace Demo; sealed class App : Application { void Install() => Styles.Add(new LucentStyles()); }");
        Assert.IsTrue(task.Execute());
        Assert.IsFalse(engine.Warnings.Any(warning => warning.Code == "LUC9009"));

        foreach (var invalidInstall in new[]
        {
            "using Avalonia; using Catalog = Demo.LucentStyles; namespace Demo; sealed class App : Application { void Install() => Styles.Add(new Catalog()); }",
            "using Avalonia; using D = Demo; namespace Demo; sealed class App : Application { void Install() => Styles.Add(new D.LucentStyles()); }",
            "using Avalonia; namespace Demo; sealed class App : Application { void Install() => Styles.Add(new global::Demo.LucentStyles()); }",
            "using Avalonia; using Avalonia.Styling; namespace Demo; sealed class App : Application { public new Styles Styles { get; } = new(); void Install() => Styles.Add(new LucentStyles()); }",
        })
        {
            engine.Warnings.Clear();
            File.WriteAllText(app, invalidInstall);
            Assert.IsTrue(task.Execute());
            Assert.IsTrue(engine.Warnings.Any(warning => warning.Code == "LUC9009"), invalidInstall);
        }

        engine.Warnings.Clear(); task.OutputType = "Library";
        Assert.IsTrue(task.Execute());
        Assert.IsFalse(engine.Warnings.Any(warning => warning.Code == "LUC9009"));
    }

    [TestMethod]
    public void SignAssembly_false_ignores_a_key_file_for_manifest_identity()
    {
        using var temporary = new TemporaryDirectory();
        var source = Path.Combine(temporary.Path, "Card.lui");
        File.WriteAllText(source, "namespace Demo; component Card() => Border {}; ");
        var (task, engine) = CreateTask(temporary, source);
        task.ProjectPath = Path.Combine(temporary.Path, "Demo.csproj");
        task.SignAssembly = "false";
        task.AssemblyOriginatorKeyFile = "key-that-CoreCompile-will-not-use.snk";
        task.PublicSign = "true";
        task.DelaySign = "true";

        Assert.IsTrue(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
        StringAssert.Contains(File.ReadAllText(task.ModuleManifestFiles.Single().ItemSpec),
            "\"publicKeyToken\":\"\"");
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Manifest_paths_are_isolated_by_configuration_framework_and_rid()
    {
        using var temporary = new TemporaryDirectory();
        var project = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(project, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{{Path.Combine(FindRepositoryRoot(), "build", "Lucent.Compiler.props")}}" />
            </Project>
            """);

        var windows = await RunDotNetAsync(temporary.Path, "msbuild", project, "-getProperty:LucentCompilerOutputDirectory",
            "-p:Configuration=Release", "-p:TargetFramework=net9.0", "-p:RuntimeIdentifier=win-x64", "-nologo");
        var linux = await RunDotNetAsync(temporary.Path, "msbuild", project, "-getProperty:LucentCompilerOutputDirectory",
            "-p:Configuration=Debug", "-p:TargetFramework=net8.0", "-p:RuntimeIdentifier=linux-x64", "-nologo");

        Assert.AreEqual(0, windows.ExitCode, windows.Output);
        Assert.AreEqual(0, linux.ExitCode, linux.Output);
        StringAssert.Contains(windows.Output.Replace('\\', '/'), "obj/Release/net9.0/win-x64/Lucent/");
        StringAssert.Contains(linux.Output.Replace('\\', '/'), "obj/Debug/net8.0/linux-x64/Lucent/");
    }

    [TestMethod]
    public void Removing_the_last_source_replaces_manifest_with_no_compile_items()
    {
        using var temporary = new TemporaryDirectory();
        var source = Path.Combine(temporary.Path, "Only.lui");
        File.WriteAllText(source, "namespace Demo; component Only() => Border {}; ");
        var task = new CompileLucent
        {
            BuildEngine = new CapturingBuildEngine(),
            Sources = [new TaskItem(source)],
            OutputDirectory = temporary.OutputDirectory,
        };

        Assert.IsTrue(task.Execute());
        task.Sources = [];
        Assert.IsTrue(task.Execute());

        var manifest = File.ReadAllText(Path.Combine(temporary.OutputDirectory,
            "Lucent.GeneratedFiles.props"));
        Assert.IsFalse(manifest.Contains("Compile Include", StringComparison.Ordinal));
        Assert.HasCount(0, task.GeneratedFiles);
        Assert.HasCount(0, task.ModuleManifestFiles);
        Assert.IsFalse(File.Exists(Path.Combine(temporary.OutputDirectory, "Lucent.ModuleManifest.v1.json")));
    }

    [TestMethod]
    public void Caller_before_callee_is_compiled_as_one_batch()
    {
        using var temporary = new TemporaryDirectory();
        var caller = Path.Combine(temporary.Path, "Main.lui");
        var callee = Path.Combine(temporary.Path, "Child.lui");
        File.WriteAllText(caller, "namespace Demo; component Main() => Window { Child {} };");
        File.WriteAllText(callee, "namespace Demo; component Child() => TextBlock { Text: \"child\"; };");
        var engine = new CapturingBuildEngine();
        var task = new CompileLucent
        {
            BuildEngine = engine,
            Sources = [new TaskItem(caller), new TaskItem(callee)],
            OutputDirectory = temporary.OutputDirectory,
        };

        Assert.IsTrue(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
        Assert.HasCount(2, task.GeneratedFiles);
    }

    [TestMethod]
    public void Duplicate_sources_and_flat_outputs_fail_before_writing()
    {
        using var temporary = new TemporaryDirectory();
        var source = Path.Combine(temporary.Path, "Main.lui");
        File.WriteAllText(source, "namespace Demo; component Main() => Border {}; ");
        var engine = new CapturingBuildEngine();
        var duplicateTask = new CompileLucent
        {
            BuildEngine = engine,
            Sources = [new TaskItem(source), new TaskItem(source)],
            OutputDirectory = temporary.OutputDirectory,
        };

        Assert.IsFalse(duplicateTask.Execute());
        Assert.IsTrue(engine.Errors.Any(error => error.Code == "LUC9004"));
        Assert.IsEmpty(duplicateTask.GeneratedFiles);
    }

    [TestMethod]
    public void Distinct_same_named_sources_fail_output_preflight()
    {
        using var temporary = new TemporaryDirectory();
        var firstDirectory = Directory.CreateDirectory(Path.Combine(temporary.Path, "one")).FullName;
        var secondDirectory = Directory.CreateDirectory(Path.Combine(temporary.Path, "two")).FullName;
        var first = Path.Combine(firstDirectory, "Foo.lui");
        var second = Path.Combine(secondDirectory, "Foo.lui");
        File.WriteAllText(first, "namespace Demo; component One() => Border {}; ");
        File.WriteAllText(second, "namespace Demo; component Two() => Border {}; ");
        var engine = new CapturingBuildEngine();
        var task = new CompileLucent
        {
            BuildEngine = engine,
            Sources = [new TaskItem(first), new TaskItem(second)],
            OutputDirectory = temporary.OutputDirectory,
        };

        Assert.IsFalse(task.Execute());
        Assert.IsTrue(engine.Errors.Any(error => error.Code == "LUC9003"));
        Assert.IsEmpty(task.GeneratedFiles);
    }

    [TestMethod]
    public void Malformed_sibling_preserves_all_existing_outputs()
    {
        using var temporary = new TemporaryDirectory();
        var good = Path.Combine(temporary.Path, "Good.lui");
        var bad = Path.Combine(temporary.Path, "Bad.lui");
        Directory.CreateDirectory(temporary.OutputDirectory);
        File.WriteAllText(good, "namespace Demo; component Good() => Border {}; ");
        File.WriteAllText(bad, "component");
        var output = Path.Combine(temporary.OutputDirectory, "GoodComponent.g.cs");
        File.WriteAllText(output, "sentinel");
        var engine = new CapturingBuildEngine();
        var task = new CompileLucent
        {
            BuildEngine = engine,
            Sources = [new TaskItem(good), new TaskItem(bad)],
            OutputDirectory = temporary.OutputDirectory,
        };

        Assert.IsFalse(task.Execute());
        Assert.AreEqual("sentinel", File.ReadAllText(output));
        Assert.IsEmpty(task.GeneratedFiles);
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Targets_consumer_builds_and_copies_the_runtime()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        var sourcePath = Path.Combine(temporary.Path, "Main.lui");
        var childPath = Path.Combine(temporary.Path, "Child.lui");
        var projectPath = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(
            sourcePath,
            "namespace Demo; component Main() => Window { Child {} };");
        await File.WriteAllTextAsync(
            childPath,
            "namespace Demo; component Child() => Border { Class: \"surface\"; }; ");
        await File.WriteAllTextAsync(
            Path.Combine(temporary.Path, "Main.css"),
            ".surface { background: #112233; }");
        await File.WriteAllTextAsync(
            projectPath,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentSource Include="Main.lui" />
                <LucentSource Include="Child.lui" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = temporary.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--verbosity:minimal");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add("-p:UseArtifactsOutput=true");
        startInfo.ArgumentList.Add(
            $"-p:ArtifactsPath={Path.Combine(temporary.Path, "artifacts")}");

        using var process = Process.Start(startInfo)
            ?? throw new AssertFailedException("Could not start dotnet build.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput + await standardError;

        Assert.AreEqual(0, process.ExitCode, output);
        Assert.IsTrue(
            Directory.EnumerateFiles(
                    Path.Combine(temporary.Path, "artifacts", "bin"),
                    "Lucent.Runtime.dll",
                    SearchOption.AllDirectories)
                .Any(),
            output);
        Assert.IsTrue(
            Directory.EnumerateFiles(
                    Path.Combine(temporary.Path, "artifacts", "obj"),
                    "ChildComponent.g.cs",
                    SearchOption.AllDirectories)
                .Any(),
            output);
        Assert.IsTrue(
            Directory.EnumerateFiles(
                    Path.Combine(temporary.Path, "artifacts", "obj"),
                    "MainComponent.g.cs",
                    SearchOption.AllDirectories)
                .Any(),
            output);
        var promoted = Directory.EnumerateFiles(
                Path.Combine(temporary.Path, "artifacts", "obj"),
                "Lucent.ModuleManifest.v1.last-successful.json",
                SearchOption.AllDirectories)
            .Single();
        StringAssert.Contains(await File.ReadAllTextAsync(promoted), "\"fingerprint\"");
        var assembly = Directory.EnumerateFiles(
                Path.Combine(temporary.Path, "artifacts", "bin"), "Consumer.dll", SearchOption.AllDirectories)
            .Single();
        Assert.AreEqual(1, EmbeddedResourceCount(assembly, "Lucent.ModuleManifest.v1.json"));
        var staged = Directory.EnumerateFiles(Path.Combine(temporary.Path, "artifacts", "obj"),
                "Lucent.ModuleManifest.v1.json", SearchOption.AllDirectories)
            .Single();
        var embedded = EmbeddedResourceBytes(assembly, "Lucent.ModuleManifest.v1.json");
        CollectionAssert.AreEqual(await File.ReadAllBytesAsync(staged), embedded);
        StringAssert.Contains(Encoding.UTF8.GetString(embedded), "\"name\":\"Consumer\"");
        StringAssert.Contains(Encoding.UTF8.GetString(embedded), "\"version\":\"1.0.0.0\"");
        var lastSuccessful = await File.ReadAllBytesAsync(promoted);

        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "Broken.cs"), "this is not C#;");
        var failedCompile = await RunDotNetAsync(temporary.Path, "build", projectPath, "--nologo", "-nodeReuse:false",
            "-p:UseArtifactsOutput=true", $"-p:ArtifactsPath={Path.Combine(temporary.Path, "artifacts")}");
        Assert.AreNotEqual(0, failedCompile.ExitCode, failedCompile.Output);
        CollectionAssert.AreEqual(lastSuccessful, await File.ReadAllBytesAsync(promoted));

        File.Delete(Path.Combine(temporary.Path, "Broken.cs"));
        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "AssemblyInfo.cs"),
            "[assembly: System.Reflection.AssemblyVersion(\"9.9.9.9\")]");
        var projectText = await File.ReadAllTextAsync(projectPath);
        await File.WriteAllTextAsync(projectPath, projectText.Replace("<Nullable>enable</Nullable>",
            "<Nullable>enable</Nullable><GenerateAssemblyVersionAttribute>false</GenerateAssemblyVersionAttribute>"));
        var failedIdentity = await RunDotNetAsync(temporary.Path, "build", projectPath, "--nologo", "-nodeReuse:false",
            "-p:UseArtifactsOutput=true", $"-p:ArtifactsPath={Path.Combine(temporary.Path, "artifacts")}");
        Assert.AreNotEqual(0, failedIdentity.ExitCode, failedIdentity.Output);
        StringAssert.Contains(failedIdentity.Output, "LUC9006");
        CollectionAssert.AreEqual(lastSuccessful, await File.ReadAllBytesAsync(promoted));

        var clean = await RunDotNetAsync(temporary.Path, "clean", projectPath, "--nologo", "-nodeReuse:false",
            "-p:UseArtifactsOutput=true", $"-p:ArtifactsPath={Path.Combine(temporary.Path, "artifacts")}");
        Assert.AreEqual(0, clean.ExitCode, clean.Output);
        Assert.IsFalse(File.Exists(promoted));
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Generated_app_and_library_global_catalogs_run_through_public_Avalonia_apis()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        var library = Path.Combine(temporary.Path, "Library");
        var app = Path.Combine(temporary.Path, "App");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(app);
        await File.WriteAllTextAsync(Path.Combine(library, "library.css"), "Border.shared { background: #ff0000; }\nBorder.library-global { background: #ff0000; }");
        await File.WriteAllTextAsync(Path.Combine(library, "Library.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><RootNamespace>Fixture.Library</RootNamespace></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentStyle Include="library.css" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(app, "Generated.lui"),
            "namespace Fixture.App; component Generated() => Border { Class: \"shared\"; }; ");
        await File.WriteAllTextAsync(Path.Combine(app, "Generated.css"), "Border.shared { background: #00ff00; }");
        await File.WriteAllTextAsync(Path.Combine(app, "app.css"), "Border.shared { background: #0000ff; }\nBorder.app-global { background: #0000ff; }\n.untyped-global { background: #0000ff; }");
        await File.WriteAllTextAsync(Path.Combine(app, "Program.cs"),
            "using Avalonia; namespace Fixture.App; sealed class App : Application { public App() => Styles.Add(new LucentStyles()); } static class Program { static void Main() { } }");
        var appProject = Path.Combine(app, "App.csproj");
        await File.WriteAllTextAsync(appProject, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><RootNamespace>Fixture.App</RootNamespace></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <PackageReference Include="Avalonia.Headless" Version="12.1.1" />
                <ProjectReference Include="..{{Path.DirectorySeparatorChar}}Library{{Path.DirectorySeparatorChar}}Library.csproj" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentSource Include="Generated.lui" />
                <LucentStyle Include="app.css" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);

        var build = await RunDotNetAsync(app, "build", appProject, "--nologo", "-nodeReuse:false");
        Assert.AreEqual(0, build.ExitCode, build.Output);
        Assert.IsFalse(build.Output.Contains("LUC9009", StringComparison.Ordinal), build.Output);
        await RunGeneratedCatalogRuntimeAsync(
            Path.Combine(app, "bin", "Debug", "net9.0", "App.dll"),
            Path.Combine(library, "bin", "Debug", "net9.0", "Library.dll"));
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Packed_library_global_catalog_restores_from_an_isolated_local_feed()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        var producer = Path.Combine(temporary.Path, "producer");
        var packages = Path.Combine(temporary.Path, "packages");
        var consumer = Path.Combine(temporary.Path, "consumer");
        var cache = Path.Combine(temporary.Path, "package-cache");
        Directory.CreateDirectory(producer);
        Directory.CreateDirectory(packages);
        Directory.CreateDirectory(consumer);
        await File.WriteAllTextAsync(Path.Combine(producer, "library.css"),
            "Border.package-first { background: #ff0000; }\nBorder.package-second { background: #0000ff; }");
        var producerProject = Path.Combine(producer, "Producer.csproj");
        await File.WriteAllTextAsync(producerProject, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><RootNamespace>Fixture.Packaged.Library</RootNamespace><AssemblyName>Fixture.Packaged.Library</AssemblyName><PackageId>Fixture.Packaged.Library</PackageId><Version>1.0.0</Version></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentStyle Include="library.css" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);
        var build = await RunDotNetAsync(producer, "build", producerProject, "--nologo", "-nodeReuse:false");
        Assert.AreEqual(0, build.ExitCode, build.Output);
        var pack = await RunDotNetAsync(producer, "pack", producerProject, "--no-build", "--no-restore",
            "--configuration", "Debug", "-o", packages, "--nologo", "-nodeReuse:false", "-p:BuildProjectReferences=false");
        Assert.AreEqual(0, pack.ExitCode, pack.Output);

        using (var assets = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(producer, "obj", "project.assets.json"))))
        {
            var globalPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
            {
                var separator = library.Name.LastIndexOf('/');
                var id = library.Name[..separator].ToLowerInvariant();
                var version = library.Name[(separator + 1)..].ToLowerInvariant();
                var source = Path.Combine(globalPackages, id, version, $"{id}.{version}.nupkg");
                if (File.Exists(source)) File.Copy(source, Path.Combine(packages, Path.GetFileName(source)), overwrite: true);
            }
        }

        await File.WriteAllTextAsync(Path.Combine(consumer, "NuGet.Config"),
            $"<configuration><packageSources><clear /><add key=\"local\" value=\"{packages.Replace("\\", "/")}\" /></packageSources></configuration>");
        var consumerProject = Path.Combine(consumer, "Consumer.csproj");
        await File.WriteAllTextAsync(consumerProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Fixture.Packaged.Library\" Version=\"1.0.0\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(consumer, "Program.cs"), """
            using System;
            using System.Linq;
            using System.Text.Json;
            using Avalonia;
            using Avalonia.Styling;
            using Fixture.Packaged.Library;
            var catalog = new LucentStyles();
            var app = new ConsumerApp();
            app.Styles.Add(catalog);
            var selectors = catalog.OfType<Style>().Select(style => style.Selector!.ToString()).Order().ToArray();
            using var stream = typeof(LucentStyles).Assembly.GetManifestResourceStream("Lucent.ModuleManifest.v1.json") ?? throw new Exception("missing manifest");
            using var manifest = JsonDocument.Parse(stream);
            var entries = manifest.RootElement.GetProperty("styleClasses").EnumerateArray()
                .Where(entry => entry.GetProperty("origin").GetInt32() == 2 && entry.GetProperty("catalogType").GetString() == "Fixture.Packaged.Library.LucentStyles")
                .Select(entry => entry.GetProperty("detail").GetString()).Order().ToArray();
            if (!selectors.SequenceEqual(entries) || selectors.Length != 2) throw new Exception("catalog and manifest differ");
            Console.WriteLine("packaged catalog ok");
            sealed class ConsumerApp : Application { }
            """);
        var environment = new Dictionary<string, string> { ["NUGET_PACKAGES"] = cache };
        var restore = await RunDotNetAsync(consumer, ["restore", consumerProject, "--nologo", "-nodeReuse:false"], environment);
        Assert.AreEqual(0, restore.ExitCode, restore.Output);
        var consumerBuild = await RunDotNetAsync(consumer, ["build", consumerProject, "--no-restore", "--nologo", "-nodeReuse:false"], environment);
        Assert.AreEqual(0, consumerBuild.ExitCode, consumerBuild.Output);
        var run = await RunDotNetAsync(consumer, ["run", consumerProject, "--no-build", "--no-restore", "--nologo"], environment);
        Assert.AreEqual(0, run.ExitCode, run.Output);
        StringAssert.Contains(run.Output, "packaged catalog ok");
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Targets_embed_and_promote_manifests_for_each_signing_mode()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        var keyFile = Path.Combine(temporary.Path, "test.snk");
        using (var rsa = new RSACryptoServiceProvider())
            await File.WriteAllBytesAsync(keyFile, rsa.ExportCspBlob(true));

        foreach (var (name, properties, signed, signatureFilled) in new (string, string, bool, bool?)[]
        {
            ("Unsigned", "<SignAssembly>false</SignAssembly>", false, false),
            ("Signed", "<SignAssembly>true</SignAssembly>", true, true),
            ("PublicSigned", "<SignAssembly>true</SignAssembly><PublicSign>true</PublicSign>", true, null),
            ("DelaySigned", "<SignAssembly>true</SignAssembly><DelaySign>true</DelaySign>", true, false),
        })
        {
            var directory = Path.Combine(temporary.Path, name);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "Main.lui"),
                "namespace Demo; component Main() => Border {}; ");
            var project = Path.Combine(directory, "Consumer.csproj");
            await File.WriteAllTextAsync(project, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <AssemblyName>{{name}}Consumer</AssemblyName>
                    <Version>2.3.4.5</Version>
                    <AssemblyOriginatorKeyFile>{{keyFile}}</AssemblyOriginatorKeyFile>
                    {{properties}}
                  </PropertyGroup>
                  <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
                  <ItemGroup>
                    <PackageReference Include="Avalonia" Version="12.1.1" />
                    <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                    <LucentSource Include="Main.lui" />
                  </ItemGroup>
                  <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
                  <Target Name="RepeatLucentGeneration" BeforeTargets="CoreCompile" DependsOnTargets="GenerateLucent">
                    <CallTarget Targets="GenerateLucent" />
                  </Target>
                </Project>
                """);

            var build = await RunDotNetAsync(directory, "build", project, "--nologo", "-nodeReuse:false");
            Assert.AreEqual(0, build.ExitCode, $"{name}:{Environment.NewLine}{build.Output}");

            var assembly = Path.Combine(directory, "bin", "Debug", "net9.0", $"{name}Consumer.dll");
            var staged = Path.Combine(directory, "obj", "Debug", "net9.0", "Lucent", "Lucent.ModuleManifest.v1.json");
            var promoted = Path.Combine(directory, "obj", "Debug", "net9.0", "Lucent", "Lucent.ModuleManifest.v1.last-successful.json");
            Assert.AreEqual(1, EmbeddedResourceCount(assembly, "Lucent.ModuleManifest.v1.json"), name);
            var manifest = await File.ReadAllBytesAsync(staged);
            CollectionAssert.AreEqual(manifest, EmbeddedResourceBytes(assembly, "Lucent.ModuleManifest.v1.json"), name);
            AssertManifestMatchesAssembly(manifest, assembly, name);
            using var record = JsonDocument.Parse(await File.ReadAllBytesAsync(promoted));
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assembly))),
                record.RootElement.GetProperty("fingerprint").GetString(), name);
            Assert.AreEqual(signed, HasStrongNameSignature(assembly), name);
            if (signatureFilled is not null)
                Assert.AreEqual(signatureFilled, StrongNameSignatureIsFilled(assembly), name);
        }
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Targets_fail_for_a_consumer_owned_reserved_manifest_resource()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "Main.lui"), "namespace Demo; component Main() => Border {}; ");
        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "consumer.json"), "{}");
        var project = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(project, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <LucentSource Include="Main.lui" />
                <EmbeddedResource Include="consumer.json" LogicalName="Lucent.ModuleManifest.v1.json" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);
        var build = await RunDotNetAsync(temporary.Path, "build", project, "--nologo", "-nodeReuse:false");
        Assert.AreNotEqual(0, build.ExitCode, build.Output);
        StringAssert.Contains(build.Output, "cannot embed its module manifest");
        StringAssert.Contains(build.Output, "consumer.json");
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Incremental_build_restores_generated_items_and_tracks_deleted_css()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "Main.lui"),
            "namespace Demo; component Main() => Border { Class: \"card\"; }; ");
        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "Host.cs"),
            "namespace Demo; internal static class Host { internal static void Use(MainComponent component) { } }");
        var css = Path.Combine(temporary.Path, "Main.css");
        await File.WriteAllTextAsync(css, ".card { width: 8; }");
        var project = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(project, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentSource Include="Main.lui" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);

        var first = await RunDotNetAsync(temporary.Path, "build", project, "--nologo", "-nodeReuse:false");
        Assert.AreEqual(0, first.ExitCode, first.Output);
        var second = await RunDotNetAsync(temporary.Path, "build", project, "--no-restore", "--nologo", "-nodeReuse:false");
        Assert.AreEqual(0, second.ExitCode, second.Output);
        var assembly = Path.Combine(temporary.Path, "bin", "Debug", "net9.0", "Consumer.dll");
        Assert.AreEqual(1, EmbeddedResourceCount(assembly, "Lucent.ModuleManifest.v1.json"));

        var identityChange = await RunDotNetAsync(temporary.Path, "build", project, "--no-restore", "--nologo", "-nodeReuse:false", "-p:AssemblyVersion=2.0.0.0");
        Assert.AreEqual(0, identityChange.ExitCode, identityChange.Output);
        using (var changedIdentity = JsonDocument.Parse(EmbeddedResourceBytes(assembly, "Lucent.ModuleManifest.v1.json")))
            Assert.AreEqual("2.0.0.0", changedIdentity.RootElement.GetProperty("assembly").GetProperty("version").GetString());

        File.Delete(css);
        var afterDelete = await RunDotNetAsync(temporary.Path, "build", project, "--no-restore", "--nologo", "-nodeReuse:false", "-p:AssemblyVersion=2.0.0.0");
        Assert.AreEqual(0, afterDelete.ExitCode, afterDelete.Output);
        using var manifest = JsonDocument.Parse(EmbeddedResourceBytes(assembly, "Lucent.ModuleManifest.v1.json"));
        Assert.AreEqual(0, manifest.RootElement.GetProperty("styleClasses").GetArrayLength());
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Multi_target_manifests_are_isolated_embedded_promoted_and_culture_matched()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "Main.lui"),
            "namespace Demo; component Main() => Border {}; ");
        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "Culture.cs"),
            "[assembly: System.Reflection.AssemblyCulture(\"fr-FR\")]");
        var project = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(project, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net9.0;net9.0-windows</TargetFrameworks>
                <EnableWindowsTargeting>true</EnableWindowsTargeting>
                <AssemblyName>CultureConsumer</AssemblyName>
                <AssemblyCulture>fr-FR</AssemblyCulture>
                <GenerateAssemblyCultureAttribute>false</GenerateAssemblyCultureAttribute>
              </PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentSource Include="Main.lui" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);

        var build = await RunDotNetAsync(temporary.Path, "build", project, "--nologo", "-nodeReuse:false");
        Assert.AreEqual(0, build.ExitCode, build.Output);
        foreach (var framework in new[] { "net9.0", "net9.0-windows" })
        {
            var assembly = Path.Combine(temporary.Path, "bin", "Debug", framework, "CultureConsumer.dll");
            var manifestDirectory = Path.Combine(temporary.Path, "obj", "Debug", framework, "Lucent");
            var staged = Path.Combine(manifestDirectory, "Lucent.ModuleManifest.v1.json");
            var promoted = Path.Combine(manifestDirectory, "Lucent.ModuleManifest.v1.last-successful.json");
            Assert.AreEqual(1, EmbeddedResourceCount(assembly, "Lucent.ModuleManifest.v1.json"), framework);
            var manifest = await File.ReadAllBytesAsync(staged);
            CollectionAssert.AreEqual(manifest, EmbeddedResourceBytes(assembly, "Lucent.ModuleManifest.v1.json"), framework);
            AssertManifestMatchesAssembly(manifest, assembly, framework);
            using var record = JsonDocument.Parse(await File.ReadAllBytesAsync(promoted));
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assembly))),
                record.RootElement.GetProperty("fingerprint").GetString(), framework);
            Assert.AreEqual("fr-FR", record.RootElement.GetProperty("manifest").GetProperty("assembly")
                .GetProperty("culture").GetString(), framework);
        }
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Targets_consumer_can_use_the_exact_typed_mount_root()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        await File.WriteAllTextAsync(
            Path.Combine(temporary.Path, "Main.lui"),
            "namespace Demo; using System; using Avalonia.Controls; component Main(Uri model) { " +
            "private readonly Uri[] items = [new Uri(\"https://example.com/a\")]; " +
            "private readonly Uri @event = new(\"https://example.com/escaped\"); " +
            "Fragment Render() => Window { StackPanel { " +
            "TextBlock { Text: binding(@event.Host); } " +
            "ContentControl { if (true) { TextBlock { Text: binding(model.Host); } } } " +
            "StackPanel { foreach (var item in items) keyed by item { TextBlock { Text: binding(model.Host); } } } " +
            "} }; }" );
        await File.WriteAllTextAsync(
            Path.Combine(temporary.Path, "Host.cs"),
            "using Avalonia.Controls; namespace Demo; internal sealed class Host { " +
            "internal void Create() { using var component = new MainComponent(new System.Uri(\"https://example.com\")); var root = component.MountRoot(); root.Title = \"Shown\"; } }");
        var projectPath = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(projectPath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentSource Include="Main.lui" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);

        var build = await RunDotNetAsync(temporary.Path,
            "build", projectPath, "--nologo", "--verbosity:minimal", "-nodeReuse:false");
        Assert.AreEqual(0, build.ExitCode, build.Output);
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Targets_consumer_compiles_first_class_item_template_with_declared_local()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        await File.WriteAllTextAsync(
            Path.Combine(temporary.Path, "Main.lui"),
            "namespace Demo; using System; using Avalonia.Controls; component Main() => ListBox { " +
            "ItemsSource: new[] { new Uri(\"https://example.com\") }; " +
            "template ItemTemplate(Uri @class) { TextBlock { Text: binding(@class.Host); } } };" );
        var projectPath = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(projectPath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentSource Include="Main.lui" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);

        var build = await RunDotNetAsync(temporary.Path,
            "build", projectPath, "--nologo", "--verbosity:minimal", "-nodeReuse:false");
        Assert.AreEqual(0, build.ExitCode, build.Output);
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Design_time_compile_items_include_generated_components()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        var projectPath = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(
            Path.Combine(temporary.Path, "SettingsPane.lui"),
            "namespace Demo; component SettingsPane() => Border {}; ");
        await File.WriteAllTextAsync(
            Path.Combine(temporary.Path, "SettingsDialog.cs"),
            "namespace Demo; internal sealed class SettingsDialog { private readonly SettingsPaneComponent pane = new(); }");
        await File.WriteAllTextAsync(projectPath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentSource Include="SettingsPane.lui" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);

        var build = await RunDotNetAsync(temporary.Path,
            "build", projectPath, "--nologo", "--verbosity:minimal", "-nodeReuse:false");
        Assert.AreEqual(0, build.ExitCode, build.Output);

        var designTime = await RunDotNetAsync(temporary.Path,
            "msbuild", projectPath, "-getItem:Compile", "-p:DesignTimeBuild=true",
            "-p:Configuration=Debug", "-nologo");
        Assert.AreEqual(0, designTime.ExitCode, designTime.Output);
        StringAssert.Contains(designTime.Output, "SettingsPaneComponent.g.cs");
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Design_time_manifest_drops_renamed_and_deleted_final_lucent_sources()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        var project = Path.Combine(temporary.Path, "Consumer.csproj");
        var first = Path.Combine(temporary.Path, "First.lui");
        var second = Path.Combine(temporary.Path, "Second.lui");
        await File.WriteAllTextAsync(first, "namespace Demo; component First() => Border {}; ");

        async System.Threading.Tasks.Task WriteProjectAsync(string sources) => await File.WriteAllTextAsync(project, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                {{sources}}
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);

        await WriteProjectAsync("<LucentSource Include=\"First.lui\" />");
        var initial = await RunDotNetAsync(temporary.Path, "build", project, "--nologo", "-nodeReuse:false");
        Assert.AreEqual(0, initial.ExitCode, initial.Output);

        File.Move(first, second);
        await WriteProjectAsync("<LucentSource Include=\"Second.lui\" />");
        var renamed = await RunDotNetAsync(temporary.Path, "build", project, "--nologo", "-nodeReuse:false");
        Assert.AreEqual(0, renamed.ExitCode, renamed.Output);
        var designTime = await RunDotNetAsync(temporary.Path, "msbuild", project, "-getItem:Compile", "-p:DesignTimeBuild=true", "-nologo");
        Assert.AreEqual(0, designTime.ExitCode, designTime.Output);
        StringAssert.Contains(designTime.Output, "SecondComponent.g.cs");
        Assert.IsFalse(designTime.Output.Contains("FirstComponent.g.cs", StringComparison.Ordinal));

        File.Delete(second);
        await WriteProjectAsync(string.Empty);
        var deleted = await RunDotNetAsync(temporary.Path, "build", project, "--nologo", "-nodeReuse:false");
        Assert.AreEqual(0, deleted.ExitCode, deleted.Output);
        var manifestDirectory = Path.Combine(temporary.Path, "obj", "Debug", "net9.0", "Lucent");
        Assert.IsFalse(File.Exists(Path.Combine(manifestDirectory, "Lucent.ModuleManifest.v1.json")));
        Assert.IsFalse(File.Exists(Path.Combine(manifestDirectory, "Lucent.ModuleManifest.v1.last-successful.json")));
        Assert.IsFalse(File.Exists(Path.Combine(manifestDirectory, "Lucent.GeneratedFiles.props")));
        designTime = await RunDotNetAsync(temporary.Path, "msbuild", project, "-getItem:Compile", "-p:DesignTimeBuild=true", "-nologo");
        Assert.AreEqual(0, designTime.ExitCode, designTime.Output);
        Assert.IsFalse(designTime.Output.Contains("SecondComponent.g.cs", StringComparison.Ordinal));
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Targets_consumer_rejects_bad_sibling_without_replacing_output()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        var good = Path.Combine(temporary.Path, "Main.lui");
        var bad = Path.Combine(temporary.Path, "Broken.lui");
        var projectPath = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(good, "namespace Demo; component Main() => Window {}; ");
        await File.WriteAllTextAsync(bad, "component");
        await File.WriteAllTextAsync(projectPath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.props")}}" />
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <ProjectReference Include="{{Path.Combine(repository, "src", "Lucent.Compiler.MSBuild", "Lucent.Compiler.MSBuild.csproj")}}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <LucentSource Include="Main.lui" /><LucentSource Include="Broken.lui" />
              </ItemGroup>
              <Import Project="{{Path.Combine(repository, "build", "Lucent.Compiler.targets")}}" />
            </Project>
            """);

        var sentinel = Path.Combine(temporary.Path, "obj", "Debug", "net9.0", "Lucent", "MainComponent.g.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "sentinel");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = temporary.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "build", projectPath, "--nologo", "--verbosity:minimal", "-nodeReuse:false" })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout + await stderr;

        Assert.AreNotEqual(0, process.ExitCode, output);
        Assert.AreEqual("sentinel", await File.ReadAllTextAsync(sentinel));
        Assert.IsFalse(output.Contains("MainComponent.g.cs", StringComparison.Ordinal) &&
                       output.Contains("Compile", StringComparison.OrdinalIgnoreCase));
    }

    private static (CompileLucent Task, CapturingBuildEngine BuildEngine) CreateTask(
        TemporaryDirectory temporary,
        string sourcePath)
    {
        var buildEngine = new CapturingBuildEngine();
        var task = new CompileLucent
        {
            BuildEngine = buildEngine,
            Sources = [new TaskItem(sourcePath)],
            OutputDirectory = temporary.OutputDirectory,
        };

        return (task, buildEngine);
    }

    private static Task<(int ExitCode, string Output)> RunDotNetAsync(string workingDirectory,
        params string[] arguments) => RunDotNetAsync(workingDirectory, arguments, null);

    private static async Task<(int ExitCode, string Output)> RunDotNetAsync(string workingDirectory,
        string[] arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (name, value) in environment) startInfo.Environment[name] = value;
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }

    private static System.Threading.Tasks.Task RunGeneratedCatalogRuntimeAsync(string appPath, string libraryPath)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<GeneratedCatalogTestApp>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
                using var stop = new CancellationTokenSource();
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var loadContext = new AssemblyLoadContext("generated-catalog-test", isCollectible: true);
                        loadContext.Resolving += (_, name) =>
                        {
                            var path = Path.Combine(Path.GetDirectoryName(appPath)!, name.Name + ".dll");
                            return File.Exists(path) ? loadContext.LoadFromStream(new MemoryStream(File.ReadAllBytes(path))) : null;
                        };
                        var library = loadContext.LoadFromStream(new MemoryStream(File.ReadAllBytes(libraryPath)));
                        var app = loadContext.LoadFromStream(new MemoryStream(File.ReadAllBytes(appPath)));
                        var appCatalog = CreateCatalog(app, "Fixture.App.LucentStyles");
                        var libraryCatalog = CreateCatalog(library, "Fixture.Library.LucentStyles");
                        AssertCatalog(appCatalog, app, "Fixture.App.LucentStyles");
                        AssertCatalog(libraryCatalog, library, "Fixture.Library.LucentStyles");

                        Application.Current!.Styles.Add(CreateCatalog(library, "Fixture.Library.LucentStyles"));
                        Application.Current.Styles.Add(CreateCatalog(app, "Fixture.App.LucentStyles"));
                        Assert.AreEqual(Colors.Blue, ColorOf("shared"));

                        Application.Current.Styles.Clear();
                        Application.Current.Styles.Add(CreateCatalog(app, "Fixture.App.LucentStyles"));
                        Application.Current.Styles.Add(CreateCatalog(library, "Fixture.Library.LucentStyles"));
                        Assert.AreEqual(Colors.Red, ColorOf("shared"));

                        Application.Current.Styles.Clear();
                        Application.Current.Styles.Add(CreateCatalog(library, "Fixture.Library.LucentStyles"));
                        Application.Current.Styles.Add(CreateCatalog(app, "Fixture.App.LucentStyles"));
                        var component = app.GetType("Fixture.App.GeneratedComponent", true)!
                            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                            .Single(constructor => constructor.GetParameters() is var parameters && parameters.Length == 2 &&
                                parameters[0].ParameterType.Name == "IUiDispatcher").Invoke([null, null]);
                        var generated = (Border)component.GetType().GetMethod("MountRoot")!.Invoke(component, null)!;
                        Assert.AreEqual(Colors.Lime, ColorOf(generated));
                        generated.Styles.Clear();
                        Assert.AreEqual(Colors.Blue, ColorOf(generated));
                        ((IDisposable)component).Dispose();
                        Application.Current.Styles.Clear();
                        loadContext.Unload();
                    }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally { stop.Cancel(); }
                });
                Dispatcher.UIThread.MainLoop(stop.Token);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static Styles CreateCatalog(Assembly assembly, string typeName) =>
        (Styles)Activator.CreateInstance(assembly.GetType(typeName, true)!)!;

    private static void AssertCatalog(Styles catalog, Assembly assembly, string typeName)
    {
        Assert.IsTrue(catalog.GetType().IsPublic);
        Assert.AreEqual(typeName, catalog.GetType().FullName);
        using var stream = assembly.GetManifestResourceStream("Lucent.ModuleManifest.v1.json")!;
        using var manifest = JsonDocument.Parse(stream);
        var generated = catalog.OfType<Style>().Select(style => style.Selector!.ToString()).Order().ToArray();
        var entries = manifest.RootElement.GetProperty("styleClasses").EnumerateArray()
            .Where(item => item.GetProperty("origin").GetInt32() == 2 &&
                           item.GetProperty("catalogType").GetString() == typeName)
            .Select(item => item.GetProperty("detail").GetString()).Order().ToArray();
        CollectionAssert.AreEqual(entries, generated);
        Assert.AreEqual(entries.Length, generated.Length);
    }

    private static Color ColorOf(string @class)
    {
        var border = new Border();
        border.Classes.Add(@class);
        return ColorOf(border);
    }

    private static Color ColorOf(Border border)
    {
        var window = new Window { Content = border };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            return ((SolidColorBrush)border.Background!).Color;
        }
        finally
        {
            window.Close();
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static int EmbeddedResourceCount(string assemblyPath, string name)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.ManifestResources.Count(handle =>
            metadata.GetManifestResource(handle).Implementation.IsNil &&
            string.Equals(metadata.GetString(metadata.GetManifestResource(handle).Name), name,
                StringComparison.Ordinal));
    }

    private static byte[] EmbeddedResourceBytes(string assemblyPath, string name)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var resource = metadata.ManifestResources.Single(handle =>
        {
            var candidate = metadata.GetManifestResource(handle);
            return candidate.Implementation.IsNil &&
                string.Equals(metadata.GetString(candidate.Name), name, StringComparison.Ordinal);
        });
        var entry = metadata.GetManifestResource(resource);
        var directory = pe.PEHeaders.CorHeader!.ResourcesDirectory;
        var content = pe.GetSectionData(directory.RelativeVirtualAddress + (int)entry.Offset).GetContent();
        var length = BitConverter.ToInt32(content.AsSpan(0, sizeof(int)));
        return content.Slice(sizeof(int), length).ToArray();
    }

    private static void AssertManifestMatchesAssembly(byte[] manifest, string assemblyPath, string mode)
    {
        using var document = JsonDocument.Parse(manifest);
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();
        var identity = document.RootElement.GetProperty("assembly");
        Assert.AreEqual(reader.GetString(assembly.Name), identity.GetProperty("name").GetString(), mode);
        Assert.AreEqual(assembly.Version.ToString(), identity.GetProperty("version").GetString(), mode);
        Assert.AreEqual(assembly.Culture.IsNil ? string.Empty : reader.GetString(assembly.Culture),
            identity.GetProperty("culture").GetString(), mode);
        var publicKey = reader.GetBlobBytes(assembly.PublicKey);
        var token = publicKey.Length == 0 ? string.Empty : Convert.ToHexString(SHA1.HashData(publicKey)[^8..].Reverse().ToArray());
        Assert.AreEqual(token, identity.GetProperty("publicKeyToken").GetString(), mode);
    }

    private static bool HasStrongNameSignature(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        return pe.PEHeaders.CorHeader!.StrongNameSignatureDirectory.Size > 0;
    }

    private static bool StrongNameSignatureIsFilled(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var signature = pe.PEHeaders.CorHeader!.StrongNameSignatureDirectory;
        return signature.Size > 0 && pe.GetSectionData(signature.RelativeVirtualAddress)
            .GetContent(0, signature.Size).Any(value => value != 0);
    }

    private static string CounterSourcePath() =>
        Path.Combine(FindRepositoryRoot(), "examples", "counter", "Counter.lui");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lucent.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the test output directory.");
    }

    public sealed class GeneratedCatalogTestApp : Application
    {
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "lucent-msbuild-tests",
                Guid.NewGuid().ToString("N"));
            OutputDirectory = System.IO.Path.Combine(Path, "obj", "Debug", "net9.0", "Lucent");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string OutputDirectory { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class CapturingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];

        public List<BuildWarningEventArgs> Warnings { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "Lucent.Compiler.MSBuild.Tests";

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs) => true;

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
    }
}
