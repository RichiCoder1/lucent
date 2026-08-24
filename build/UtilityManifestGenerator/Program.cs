using Lucent.Compiler;
using Lucent.Styles.Utilities;

if (args is not [var path, var assemblyName, var assemblyVersion])
    throw new ArgumentException("Expected manifest path, assembly name, and assembly version.");

var catalogType = "Lucent.Styles.Utilities.LucentStyles";
var classes = UtilitySpecification.Entries.OrderBy(entry => entry.Order).Select(entry =>
    new StyleClassEntry(entry.Name,
        "Avalonia.Controls." + (entry.Type is "TemplatedControl" or "ToggleButton" ? "Primitives." : "") + entry.Type,
        StyleClassOrigin.Utility, null, entry.Detail, catalogType)).ToArray();
var bytes = LucentModuleManifest.Serialize(new LucentModuleManifestModel(1, 0, 0,
    assemblyVersion.TrimEnd('.', '0'), new LucentAssemblyIdentity(assemblyName, assemblyVersion, "", ""),
    [catalogType], classes, []));
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
