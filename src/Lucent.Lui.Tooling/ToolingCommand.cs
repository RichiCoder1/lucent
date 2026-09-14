using Lucent.Lui.Compiler;
using Lucent.Lui.Tooling.Assets;

namespace Lucent.Lui.Tooling;

internal static class ToolingCommand
{
    private const string Usage =
        "Usage: Lucent.Lui.Tooling [--check|--write] file.lui [...]\n       Lucent.Lui.Tooling --lint [--project project.csproj] [--fix] file.lui [...]";

    public static int Run(string[] arguments, TextWriter output, TextWriter error)
    {
        if (arguments.FirstOrDefault() == "--generate-assets")
            return AssetCatalogGenerator.Run(arguments.Skip(1).ToArray(), output, error);

        if (!TryParse(arguments, error, out var command))
            return 2;
        return command.Mode == CommandMode.Lint
            ? RunLint(command, output, error)
            : RunFormatting(command, output, error);
    }

    private static int RunFormatting(Command command, TextWriter output, TextWriter error)
    {
        var changed = false;
        var failed = false;
        foreach (var path in command.Paths)
        {
            if (!IsExistingLui(path))
            {
                error.WriteLine("Expected an existing .lui file: " + path);
                failed = true;
                continue;
            }

            try
            {
                var snapshot = SourceFileSnapshot.Read(path);
                var configuration = LuiEditorConfigResolver.Resolve(path);
                if (!configuration.IsValid)
                {
                    failed = true;
                    WriteConfigurationDiagnostics(configuration, error);
                    continue;
                }

                var result = LuiFormatter.FormatDocument(snapshot.Text, configuration.Options);
                if (result.Status is LuiFormattingStatus.Unavailable or LuiFormattingStatus.Failed)
                {
                    failed = true;
                    foreach (var diagnostic in result.Diagnostics)
                        error.WriteLine(
                            $"{path}({diagnostic.Span.Start}): {diagnostic.Id}: {diagnostic.Message}"
                        );
                    continue;
                }

                changed |= result.Status == LuiFormattingStatus.Changed;
                if (
                    command.Mode == CommandMode.Check
                    && result.Status == LuiFormattingStatus.Changed
                )
                {
                    error.WriteLine($"{path}: needs formatting.");
                }
                if (
                    command.Mode == CommandMode.Write
                    && result.Status == LuiFormattingStatus.Changed
                )
                {
                    if (
                        SourceFileTransaction.Replace(path, snapshot, result.Text)
                        == SourceFileWriteStatus.ChangedSinceRead
                    )
                    {
                        failed = true;
                        error.WriteLine(
                            $"{path}: source changed after it was read; formatting was not written."
                        );
                    }
                }
                else if (command.Mode == CommandMode.StandardOutput)
                {
                    output.Write(result.Text);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                failed = true;
                error.WriteLine($"{path}: formatting failed: {exception.Message}");
            }
        }

        return failed ? 2
            : command.Mode == CommandMode.Check && changed ? 1
            : 0;
    }

    private static int RunLint(Command command, TextWriter output, TextWriter error)
    {
        _ = output;
        var failed = false;
        var contexts = new Dictionary<string, LuiLintProjectContext>(
            StringComparer.OrdinalIgnoreCase
        );
        try
        {
            foreach (var path in command.Paths)
            {
                if (!IsExistingLui(path))
                {
                    error.WriteLine("Expected an existing .lui file: " + path);
                    failed = true;
                    continue;
                }

                try
                {
                    var snapshot = SourceFileSnapshot.Read(path);
                    var configuration = LuiEditorConfigResolver.Resolve(path);
                    if (!configuration.IsValid)
                    {
                        failed = true;
                        WriteConfigurationDiagnostics(configuration, error);
                        continue;
                    }

                    var projectPath = Path.GetFullPath(
                        command.ProjectPath ?? LuiLintProjectContext.DiscoverProject(path)
                    );
                    if (!contexts.TryGetValue(projectPath, out var context))
                    {
                        context = LuiLintProjectContext
                            .LoadAsync(projectPath)
                            .GetAwaiter()
                            .GetResult();
                        contexts.Add(projectPath, context);
                    }

                    var result = context
                        .AnalyzeAsync(path, snapshot.Text, configuration.DeclarationOrder)
                        .GetAwaiter()
                        .GetResult();
                    if (result.Status != LuiLintAnalysisStatus.Complete)
                    {
                        failed = true;
                        WriteLintDiagnostics(
                            path,
                            result.Diagnostics,
                            configuration,
                            context,
                            error,
                            ref failed
                        );
                        continue;
                    }

                    if (command.Fix && result.Fixes.Count != 0)
                    {
                        var fixedSource = ApplyFixes(snapshot.Text, result.Fixes);
                        var verified = context
                            .AnalyzeAsync(path, fixedSource, configuration.DeclarationOrder)
                            .GetAwaiter()
                            .GetResult();
                        if (verified.Status != LuiLintAnalysisStatus.Complete)
                        {
                            failed = true;
                            WriteLintDiagnostics(
                                path,
                                verified.Diagnostics,
                                configuration,
                                context,
                                error,
                                ref failed
                            );
                            continue;
                        }

                        if (
                            SourceFileTransaction.Replace(path, snapshot, fixedSource)
                            == SourceFileWriteStatus.ChangedSinceRead
                        )
                        {
                            failed = true;
                            error.WriteLine(
                                $"{path}: source changed after it was read; lint fixes were not written."
                            );
                            continue;
                        }

                        result = verified;
                    }

                    WriteLintDiagnostics(
                        path,
                        result.Diagnostics,
                        configuration,
                        context,
                        error,
                        ref failed
                    );
                }
                catch (Exception exception)
                    when (exception
                            is IOException
                                or UnauthorizedAccessException
                                or ArgumentException
                                or InvalidOperationException
                    )
                {
                    failed = true;
                    error.WriteLine($"{path}: lint analysis failed: {exception.Message}");
                }
            }
        }
        finally
        {
            foreach (var context in contexts.Values)
                context.Dispose();
        }

        return failed ? 2 : 0;
    }

    private static string ApplyFixes(string source, IReadOnlyList<LuiLintFix> fixes)
    {
        var edits = fixes
            .SelectMany(fix => fix.Edits)
            .GroupBy(edit => (edit.Span.Start, edit.Span.Length, edit.NewText))
            .Select(group => group.First())
            .OrderByDescending(edit => edit.Span.Start)
            .ThenByDescending(edit => edit.Span.Length)
            .ToArray();
        var previousStart = source.Length;
        foreach (var edit in edits)
        {
            if (
                edit.Span.Start < 0
                || edit.Span.End > source.Length
                || edit.Span.End > previousStart
            )
                throw new InvalidOperationException(
                    "The independently proven lint fixes contain overlapping or stale source edits."
                );
            source = source
                .Remove(edit.Span.Start, edit.Span.Length)
                .Insert(edit.Span.Start, edit.NewText);
            previousStart = edit.Span.Start;
        }

        return source;
    }

    private static void WriteLintDiagnostics(
        string path,
        IReadOnlyList<LuiDiagnostic> diagnostics,
        LuiEditorConfigResolution configuration,
        LuiLintProjectContext context,
        TextWriter error,
        ref bool failed
    )
    {
        foreach (var diagnostic in diagnostics)
        {
            configuration.DiagnosticSeverities.TryGetValue(
                diagnostic.Id,
                out var configuredSeverity
            );
            var severity = context.EffectiveSeverity(path, diagnostic, configuredSeverity);
            if (
                severity
                is Microsoft.CodeAnalysis.ReportDiagnostic.Suppress
                    or Microsoft.CodeAnalysis.ReportDiagnostic.Hidden
            )
                continue;
            if (severity == Microsoft.CodeAnalysis.ReportDiagnostic.Error)
                failed = true;
            error.WriteLine(
                $"{path}({diagnostic.Span.Start}): {SeverityName(severity)} {diagnostic.Id}: {diagnostic.Message}"
            );
        }
    }

    private static string SeverityName(Microsoft.CodeAnalysis.ReportDiagnostic severity) =>
        severity switch
        {
            Microsoft.CodeAnalysis.ReportDiagnostic.Error => "error",
            Microsoft.CodeAnalysis.ReportDiagnostic.Warn => "warning",
            Microsoft.CodeAnalysis.ReportDiagnostic.Info => "info",
            Microsoft.CodeAnalysis.ReportDiagnostic.Hidden => "hidden",
            _ => "warning",
        };

    private static bool TryParse(string[] arguments, TextWriter error, out Command command)
    {
        var mode = CommandMode.StandardOutput;
        var modeWasExplicit = false;
        var fix = false;
        string? projectPath = null;
        var paths = new List<string>();
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument is "--check" or "--write" or "--lint")
            {
                if (modeWasExplicit)
                {
                    error.WriteLine(
                        "Formatting, linting and asset-generation modes are mutually exclusive."
                    );
                    error.WriteLine(Usage);
                    command = default;
                    return false;
                }

                modeWasExplicit = true;
                mode = argument switch
                {
                    "--check" => CommandMode.Check,
                    "--write" => CommandMode.Write,
                    _ => CommandMode.Lint,
                };
                continue;
            }

            if (argument == "--fix")
            {
                fix = true;
                continue;
            }

            if (argument == "--project")
            {
                if (projectPath is not null || index + 1 >= arguments.Length)
                {
                    error.WriteLine("--project requires exactly one project path.");
                    error.WriteLine(Usage);
                    command = default;
                    return false;
                }

                projectPath = arguments[++index];
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                error.WriteLine("Unknown option: " + argument);
                error.WriteLine(Usage);
                command = default;
                return false;
            }

            paths.Add(argument);
        }

        if (
            paths.Count == 0
            || (mode == CommandMode.StandardOutput && paths.Count != 1)
            || (mode != CommandMode.Lint && (fix || projectPath is not null))
        )
        {
            if (fix && mode != CommandMode.Lint)
                error.WriteLine("--fix requires --lint.");
            if (projectPath is not null && mode != CommandMode.Lint)
                error.WriteLine("--project requires --lint.");
            error.WriteLine(Usage);
            command = default;
            return false;
        }

        command = new Command(mode, paths.ToArray(), projectPath, fix);
        return true;
    }

    private static bool IsExistingLui(string path) =>
        path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    private static void WriteConfigurationDiagnostics(
        LuiEditorConfigResolution resolution,
        TextWriter error
    )
    {
        foreach (var diagnostic in resolution.Diagnostics)
            error.WriteLine(
                $"{diagnostic.FilePath}({diagnostic.Line}): {diagnostic.Id}: {diagnostic.Message}"
            );
    }

    private enum CommandMode
    {
        StandardOutput,
        Check,
        Write,
        Lint,
    }

    private readonly record struct Command(
        CommandMode Mode,
        string[] Paths,
        string? ProjectPath,
        bool Fix
    );
}
