using Lucent.Lui.Compiler;

var check = args.FirstOrDefault() == "--check";
var write = args.FirstOrDefault() == "--write";
var paths = (check || write ? args.Skip(1) : args).ToArray();
if (paths.Length == 0 || (!check && !write && paths.Length != 1))
{
    Console.Error.WriteLine("Usage: Lucent.Lui.Tooling [--check|--write] file.lui [...]");
    return 2;
}

var changed = false;
foreach (var path in paths)
{
    if (!path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
    {
        Console.Error.WriteLine("Expected an existing .lui file: " + path);
        return 2;
    }
    var source = File.ReadAllText(path);
    var formatted = LuiFormatter.Format(source);
    changed |= source != formatted;
    if (write && source != formatted)
        File.WriteAllText(path, formatted);
    else if (!check && !write)
        Console.Out.Write(formatted);
}
return check && changed ? 1 : 0;
