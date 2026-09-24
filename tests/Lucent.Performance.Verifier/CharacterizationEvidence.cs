using System.Text.Json;

internal static class CharacterizationEvidence
{
    internal static int Run(string journalPath)
    {
        var evidence = Evaluate(journalPath);
        evidence.Write(Console.OpenStandardOutput());
        if (evidence.Passed)
            return 0;
        Console.Error.WriteLine(
            "Lucent characterization verifier: FAIL: "
                + (evidence.Failures.FirstOrDefault().Message ?? "Incomplete or failed evidence.")
        );
        return 1;
    }

    internal static PerformanceEvidence Evaluate(string journalPath)
    {
        var path = Path.GetFullPath(journalPath);
        var adjacentRaw = Path.Combine(Path.GetDirectoryName(path)!, "frames.log");
        PerformanceEvidence? evidence = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var app = root.TryGetProperty("app", out var appElement)
                ? appElement.GetString() ?? ""
                : "";
            var raw = root.GetProperty("rawLog").GetString();
            if (String.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("Characterization raw log path is missing.");
            evidence = new(app, raw, "issue-browser-characterization-v1")
            {
                CharacterizationJournalPath = path,
            };
            evidence.CollectCharacterization(root);
        }
        catch (Exception error)
            when (error
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidOperationException
                        or FormatException
                        or KeyNotFoundException
            )
        {
            evidence ??= new(
                "",
                File.Exists(adjacentRaw) ? adjacentRaw : "",
                "issue-browser-characterization-v1"
            )
            {
                CharacterizationJournalPath = path,
            };
            evidence.RecordFatal(error.Message);
        }
        return evidence;
    }
}
