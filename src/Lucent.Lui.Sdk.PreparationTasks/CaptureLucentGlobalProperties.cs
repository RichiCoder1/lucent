using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace Lucent.Lui.Sdk.PreparationTasks;

public sealed class CaptureLucentGlobalProperties : Microsoft.Build.Utilities.Task
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Required]
    public string OutputPath { get; set; } = "";

    public override bool Execute()
    {
        if (BuildEngine is not IBuildEngine6 buildEngine)
        {
            Log.LogError("LUI7001: MSBuild does not expose the outer global-property snapshot.");
            return false;
        }
        var path = Path.GetFullPath(OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(buildEngine.GetGlobalProperties(), JsonOptions),
            new UTF8Encoding(false)
        );
        File.Move(temporary, path, overwrite: true);
        return true;
    }
}
