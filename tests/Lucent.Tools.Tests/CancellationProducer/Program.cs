// A real producer that deliberately ignores stdin cancellation. No descendants.
if (args.Length != 3 || args[0] != "--project-requirements" || args[1] != "--trusted-project")
    return 2;
var temporaryMarker = args[2] + ".tmp";
File.WriteAllText(
    temporaryMarker,
    Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
);
File.Move(temporaryMarker, args[2]);
await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;
