using System.Runtime.InteropServices;
using System.Text;

var before = Native.Live();
var probe = Native.Create();
if (probe == 0 || Native.Live() != before + 1)
    throw new InvalidOperationException("Native probe allocation was not observable.");
try
{
    var buffer = new byte[4096];
    var length = Native.Run(probe, buffer, (nuint)buffer.Length);
    if (length <= 0)
        throw new InvalidOperationException("Native layout evaluation failed: " + length);
    Console.WriteLine(Encoding.UTF8.GetString(buffer, 0, length));
}
finally
{
    Native.Destroy(probe);
}
if (Native.Live() != before)
    throw new InvalidOperationException("Native probe disposal did not return to baseline.");
Console.WriteLine("native-aot-load-dispose=pass");

internal static partial class Native
{
    private const string Library = "lucent_taffy_probe";

    [LibraryImport(Library, EntryPoint = "lucent_taffy_create")]
    internal static partial nint Create();

    [LibraryImport(Library, EntryPoint = "lucent_taffy_destroy")]
    internal static partial void Destroy(nint probe);

    [LibraryImport(Library, EntryPoint = "lucent_taffy_live")]
    internal static partial nuint Live();

    [LibraryImport(Library, EntryPoint = "lucent_taffy_run")]
    internal static partial int Run(nint probe, byte[] output, nuint capacity);
}
