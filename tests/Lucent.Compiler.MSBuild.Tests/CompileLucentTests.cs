using System.Collections;
using System.Diagnostics;
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
        StringAssert.Contains(generated, $"#line 10 \"{mappedSourcePath}\"");
        StringAssert.Contains(generated, "internal sealed class CounterComponent");
        StringAssert.Contains(generated, "Styles.Add(new global::Avalonia.Styling.Style");
        StringAssert.Contains(generated, "global::Avalonia.Animation.DoubleTransition");
        StringAssert.Contains(generated, "SetCount(_count + 1);");
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
    public async System.Threading.Tasks.Task Targets_consumer_builds_and_copies_the_runtime()
    {
        using var temporary = new TemporaryDirectory();
        var repository = FindRepositoryRoot();
        var sourcePath = Path.Combine(temporary.Path, "Main.lui");
        var projectPath = Path.Combine(temporary.Path, "Consumer.csproj");
        await File.WriteAllTextAsync(
            sourcePath,
            "namespace Demo; component Main() { Fragment Render() { return Border { }; } }");
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
                    "MainComponent.g.cs",
                    SearchOption.AllDirectories)
                .Any(),
            output);
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
