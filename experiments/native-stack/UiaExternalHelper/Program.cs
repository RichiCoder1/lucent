using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Automation;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new ArgumentException("Use <ready-json> <close-signal> <result-json>.");
            var ready = JsonSerializer.Deserialize<Ready>(File.ReadAllText(args[0])) ?? throw new InvalidOperationException("Missing host ready record.");
            if (!ready.Ok || ready.DynamicCodeSupported) throw new InvalidOperationException("Host was not a published NativeAOT executable.");
            var hwnd = (IntPtr)Convert.ToInt64(ready.Hwnd[2..], 16);
            var first = Read(hwnd);
            var second = Task.Run(() => Read(hwnd)).GetAwaiter().GetResult();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var third = Read(hwnd);
            var result = new Result(true, first, second, third,
                first == second && second == third && first.Name == "NativeStackProbe UIA root" &&
                first.AutomationId == "NativeStackProbe.Root");
            File.WriteAllText(args[2], JsonSerializer.Serialize(result));
            File.WriteAllText(args[1], "close");
            return result.Valid ? 0 : 1;
        }
        catch (Exception exception)
        {
            if (args.Length == 3) File.WriteAllText(args[2], JsonSerializer.Serialize(new Failure(false, exception.ToString())));
            return 1;
        }
    }

    private static Value Read(IntPtr hwnd)
    {
        var host = AutomationElement.FromHandle(hwnd) ?? throw new InvalidOperationException("UIA did not attach to the HWND.");
        var element = host;
        return new(element.Current.Name, element.Current.AutomationId, element.Current.ControlType.Id);
    }

    private sealed record Ready(bool Ok, string Hwnd, bool DynamicCodeSupported);
    private sealed record Value(string Name, string AutomationId, int ControlType);
    private sealed record Result(bool Ok, Value First, Value Second, Value Third, bool Valid);
    private sealed record Failure(bool Ok, string Error);
}
