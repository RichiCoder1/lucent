using Lucent.Lui.Compiler;
using Lucent.Lui.Tooling.Assets;

if (args.FirstOrDefault() == "--generate-assets")
    return AssetCatalogGenerator.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);

var check = args.FirstOrDefault() == "--check";
var write = args.FirstOrDefault() == "--write";
var paths = (check || write ? args.Skip(1) : args).ToArray();
if (paths.Length == 0 || (!check && !write && paths.Length != 1))
{
    Console.Error.WriteLine("Usage: Lucent.Lui.Tooling [--check|--write] file.lui [...]");
    return 2;
}

var changed = false;
var failed = false;
foreach (var path in paths)
{
    if (!path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
    {
        Console.Error.WriteLine("Expected an existing .lui file: " + path);
        failed = true;
        continue;
    }
    try
    {
        var source = File.ReadAllText(path);
        var result = LuiFormatter.FormatDocument(source);
        if (result.Status is LuiFormattingStatus.Unavailable or LuiFormattingStatus.Failed)
        {
            failed = true;
            foreach (var diagnostic in result.Diagnostics)
                Console.Error.WriteLine(
                    $"{path}({diagnostic.Span.Start}): {diagnostic.Id}: {diagnostic.Message}"
                );
            continue;
        }
        changed |= result.Status == LuiFormattingStatus.Changed;
        if (write && result.Status == LuiFormattingStatus.Changed)
            File.WriteAllText(path, result.Text);
        else if (!check && !write)
            Console.Out.Write(result.Text);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        failed = true;
        Console.Error.WriteLine($"{path}: formatting failed: {error.Message}");
    }
}
return failed ? 2
    : check && changed ? 1
    : 0;
