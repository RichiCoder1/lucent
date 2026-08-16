using System.Text;

namespace Lucent.Compiler.Cli;

public static class CompilerCli
{
    public const int SuccessExitCode = 0;
    public const int UsageExitCode = 1;
    public const int CompilationExitCode = 2;
    public const int StaleOutputExitCode = 3;
    public const int IoExitCode = 4;

    public static int Run(
        string[] args,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (!TryParseArguments(args, standardError, out var options))
        {
            return UsageExitCode;
        }

        try
        {
            var sourceText = File.ReadAllText(options.InputPath);
            var stylePath = Path.ChangeExtension(options.InputPath, ".css");
            var styleText = File.Exists(stylePath) ? File.ReadAllText(stylePath) : null;
            var result = LucentCompiler.Compile(
                sourceText,
                options.InputPath,
                projectContext: null,
                styleText,
                stylePath);

            foreach (var diagnostic in result.Diagnostics)
            {
                standardError.WriteLine(FormatDiagnostic(
                    diagnostic.SourcePath ?? options.InputPath,
                    diagnostic));
            }

            if (!result.Succeeded || result.GeneratedSource is null)
            {
                return CompilationExitCode;
            }

            return options.Command switch
            {
                CompilerCommand.Generate => Generate(
                    options.OutputPath,
                    result.GeneratedSource,
                    standardOutput),
                CompilerCommand.Verify => Verify(
                    options.OutputPath,
                    result.GeneratedSource,
                    standardOutput,
                    standardError),
                _ => throw new InvalidOperationException(
                    $"Unknown compiler command {options.Command}."),
            };
        }
        catch (IOException exception)
        {
            standardError.WriteLine($"lucentc: I/O error: {exception.Message}");
            return IoExitCode;
        }
        catch (UnauthorizedAccessException exception)
        {
            standardError.WriteLine($"lucentc: I/O error: {exception.Message}");
            return IoExitCode;
        }
    }

    private static int Generate(
        string outputPath,
        string generatedSource,
        TextWriter standardOutput)
    {
        if (File.Exists(outputPath) &&
            string.Equals(
                File.ReadAllText(outputPath),
                generatedSource,
                StringComparison.Ordinal))
        {
            standardOutput.WriteLine($"Unchanged {outputPath}");
            return SuccessExitCode;
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new IOException("The output path has no parent directory.");
        Directory.CreateDirectory(outputDirectory);

        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(
                temporaryPath,
                generatedSource,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        standardOutput.WriteLine($"Generated {outputPath}");
        return SuccessExitCode;
    }

    private static int Verify(
        string outputPath,
        string generatedSource,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (!File.Exists(outputPath) ||
            !string.Equals(
                File.ReadAllText(outputPath),
                generatedSource,
                StringComparison.Ordinal))
        {
            standardError.WriteLine(
                $"{outputPath}(1,1): error LUC9001: Generated output is stale. Run lucentc generate.");
            return StaleOutputExitCode;
        }

        standardOutput.WriteLine($"Verified {outputPath}");
        return SuccessExitCode;
    }

    private static string FormatDiagnostic(
        string path,
        LucentDiagnostic diagnostic) =>
        $"{path}({diagnostic.Line},{diagnostic.Column}): " +
        $"{diagnostic.Severity.ToString().ToLowerInvariant()} " +
        $"{diagnostic.Code}: {diagnostic.Message}";

    private static bool TryParseArguments(
        string[] args,
        TextWriter standardError,
        out CompilerOptions options)
    {
        options = default;
        if (args.Length == 0 ||
            !TryParseCommand(args[0], out var command))
        {
            WriteUsage(standardError);
            return false;
        }

        string? inputPath = null;
        string? outputPath = null;

        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is "--input" or "--output")
            {
                if (index + 1 >= args.Length)
                {
                    standardError.WriteLine($"lucentc: Missing value for {option}.");
                    WriteUsage(standardError);
                    return false;
                }

                var value = args[++index];
                if (option == "--input")
                {
                    inputPath = value;
                }
                else
                {
                    outputPath = value;
                }

                continue;
            }

            if (option == "--diagnostics-format")
            {
                if (index + 1 >= args.Length ||
                    !string.Equals(
                        args[++index],
                        "msbuild",
                        StringComparison.Ordinal))
                {
                    standardError.WriteLine(
                        "lucentc: The only supported diagnostics format is 'msbuild'.");
                    return false;
                }

                continue;
            }

            standardError.WriteLine($"lucentc: Unknown option '{option}'.");
            WriteUsage(standardError);
            return false;
        }

        if (string.IsNullOrWhiteSpace(inputPath) ||
            string.IsNullOrWhiteSpace(outputPath))
        {
            standardError.WriteLine(
                "lucentc: Both --input and --output are required.");
            WriteUsage(standardError);
            return false;
        }

        options = new CompilerOptions(command, inputPath, outputPath);
        return true;
    }

    private static bool TryParseCommand(
        string value,
        out CompilerCommand command)
    {
        if (string.Equals(value, "generate", StringComparison.Ordinal))
        {
            command = CompilerCommand.Generate;
            return true;
        }

        if (string.Equals(value, "verify", StringComparison.Ordinal))
        {
            command = CompilerCommand.Verify;
            return true;
        }

        command = default;
        return false;
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(
            "Usage: lucentc <generate|verify> --input <file.lui> --output <file.g.cs> " +
            "[--diagnostics-format msbuild]");
    }

    private enum CompilerCommand
    {
        Generate,
        Verify,
    }

    private readonly record struct CompilerOptions(
        CompilerCommand Command,
        string InputPath,
        string OutputPath);
}
