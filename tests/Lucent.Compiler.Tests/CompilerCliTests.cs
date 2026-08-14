using Lucent.Compiler.Cli;

namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class CompilerCliTests
{
    [TestMethod]
    public void Generate_then_verify_succeeds_without_rewriting()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "Counter.lui");
        var outputPath = Path.Combine(temporary.Path, "CounterComponent.g.cs");
        File.Copy(RepositoryPaths.CounterSource, sourcePath);

        var output = new StringWriter();
        var error = new StringWriter();
        var generateExitCode = CompilerCli.Run(
            [
                "generate",
                "--input",
                sourcePath,
                "--output",
                outputPath,
                "--diagnostics-format",
                "msbuild",
            ],
            output,
            error);
        var firstWriteTime = File.GetLastWriteTimeUtc(outputPath);

        Thread.Sleep(20);
        output.GetStringBuilder().Clear();
        var secondGenerateExitCode = CompilerCli.Run(
            [
                "generate",
                "--input",
                sourcePath,
                "--output",
                outputPath,
            ],
            output,
            error);
        var secondWriteTime = File.GetLastWriteTimeUtc(outputPath);

        var verifyExitCode = CompilerCli.Run(
            [
                "verify",
                "--input",
                sourcePath,
                "--output",
                outputPath,
            ],
            output,
            error);

        Assert.AreEqual(CompilerCli.SuccessExitCode, generateExitCode);
        Assert.AreEqual(CompilerCli.SuccessExitCode, secondGenerateExitCode);
        Assert.AreEqual(CompilerCli.SuccessExitCode, verifyExitCode);
        Assert.AreEqual(firstWriteTime, secondWriteTime);
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public void Invalid_source_does_not_replace_the_previous_output()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "Invalid.lui");
        var outputPath = Path.Combine(temporary.Path, "Existing.g.cs");
        File.WriteAllText(sourcePath, "component");
        File.WriteAllText(outputPath, "sentinel");

        var exitCode = CompilerCli.Run(
            [
                "generate",
                "--input",
                sourcePath,
                "--output",
                outputPath,
            ],
            TextWriter.Null,
            TextWriter.Null);

        Assert.AreEqual(CompilerCli.CompilationExitCode, exitCode);
        Assert.AreEqual("sentinel", File.ReadAllText(outputPath));
    }

    [TestMethod]
    public void Verify_reports_stale_output_without_writing()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "Counter.lui");
        var outputPath = Path.Combine(temporary.Path, "CounterComponent.g.cs");
        File.Copy(RepositoryPaths.CounterSource, sourcePath);
        File.WriteAllText(outputPath, "stale");
        var error = new StringWriter();

        var exitCode = CompilerCli.Run(
            [
                "verify",
                "--input",
                sourcePath,
                "--output",
                outputPath,
            ],
            TextWriter.Null,
            error);

        Assert.AreEqual(CompilerCli.StaleOutputExitCode, exitCode);
        Assert.AreEqual("stale", File.ReadAllText(outputPath));
        StringAssert.Contains(error.ToString(), "LUC9001");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "lucent-compiler-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
