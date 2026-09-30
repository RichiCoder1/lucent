using System.Text.Json;

namespace Lucent.Tools;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        try
        {
            if (args.Length == 0 || args[0] != "doctor")
                throw new ArgumentException(
                    "Usage: lucent doctor [--json] [--workspace <absolute-directory>]"
                );
            var workspace = Directory.GetCurrentDirectory();
            for (var index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json":
                        break;
                    case "--workspace" when index + 1 < args.Length:
                        workspace = args[++index];
                        break;
                    default:
                        throw new ArgumentException("Unsupported doctor option.");
                }
            }
            var result = await Doctor
                .RunAsync(workspace, new DotnetProcessProbe())
                .ConfigureAwait(false);
            if (json)
                Console.Out.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else
                WriteHuman(result, Console.Out);
            return result.Status == "blocked" ? 1 : 0;
        }
        catch (Exception error)
            when (error
                    is ArgumentException
                        or IOException
                        or UnauthorizedAccessException
                        or OperationCanceledException
            )
        {
            if (json)
                Console.Out.WriteLine(
                    JsonSerializer.Serialize(
                        new
                        {
                            schemaVersion = 1,
                            kind = "environment-doctor",
                            scope = "static-offline",
                            status = "unavailable",
                            error = new
                            {
                                code = "doctor-invocation",
                                message = "The doctor could not inspect the requested workspace.",
                            },
                        },
                        JsonOptions
                    )
                );
            else
                Console.Error.WriteLine(
                    "The doctor could not inspect the requested workspace. "
                        + "Usage: lucent doctor [--json] [--workspace <absolute-directory>]"
                );
            return 2;
        }
    }

    private static void WriteHuman(DoctorResult result, TextWriter writer)
    {
        writer.WriteLine($"Lucent environment: {result.Status} ({result.Scope})");
        foreach (var capability in result.Capabilities)
            writer.WriteLine($"{capability.Name}: {capability.Status}");
        foreach (var check in result.Checks)
        {
            writer.WriteLine($"[{check.Status}] {check.Code}: {check.Summary}");
            if (check.Evidence is not null)
                writer.WriteLine("  " + check.Evidence);
            if (check.Remedy is not null)
                writer.WriteLine("  Next: " + check.Remedy);
        }
    }
}
