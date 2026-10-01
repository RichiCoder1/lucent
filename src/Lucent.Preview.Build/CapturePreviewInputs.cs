using System.Collections;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace Lucent.Preview.Build;

public sealed class CapturePreviewInputs : Microsoft.Build.Utilities.Task
{
    [Required]
    public string ProjectPath { get; set; } = "";

    [Required]
    public string Stage { get; set; } = "";

    [Required]
    public string OutputDirectory { get; set; } = "";
    public ITaskItem[] Items { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            if (Items.Length > BuildData.MaximumInputs)
                throw new InvalidOperationException(
                    "Consumed preview inputs exceed the supported bound."
                );
            var items = Items
                .Select(item => new EvaluatedItem(Stage, item.ItemSpec, Metadata(item)))
                .ToArray();
            if (
                Items.Any(item =>
                    Path.GetExtension(item.ItemSpec)
                        .Equals(".resx", StringComparison.OrdinalIgnoreCase)
                )
            )
                throw new InvalidOperationException(
                    "Resx resource inputs require an explicit external-file closure and are unsupported in the initial preview build."
                );
            var inputs = Items
                .Select(item => item.GetMetadata("FullPath"))
                .Where(path => !String.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(BuildData.Snapshot)
                .ToArray();
            if (inputs.Any(input => input.Sha256 is null))
                throw new InvalidOperationException("A consumed preview input is missing.");
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(
                Path.Combine(OutputDirectory, Stage + ".json"),
                JsonSerializer.Serialize(
                    new ConsumedSnapshot(
                        BuildData.CanonicalPath(ProjectPath),
                        Stage,
                        items,
                        inputs
                    ),
                    BuildData.Json
                )
            );
            return true;
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.LogError("Preview input capture failed: {0}", error.Message);
            return false;
        }
    }

    private static SortedDictionary<string, string> Metadata(ITaskItem item)
    {
        var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in item.CloneCustomMetadata())
            metadata.Add((string)entry.Key, (string)entry.Value!);
        return metadata;
    }
}
