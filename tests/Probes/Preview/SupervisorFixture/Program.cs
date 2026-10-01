using System.Diagnostics;
using System.Text.Json;
using Windows.Win32;
using Windows.Win32.System.Threading;

if (args.Length == 0)
    return 2;
switch (args[0])
{
    case "arguments":
        Console.WriteLine(JsonSerializer.Serialize(args.Skip(1).ToArray()));
        Console.Error.WriteLine("separate-error");
        return 0;
    case "crash":
        return 23;
    case "hang":
        if (args.Length > 1)
            WriteReady(args[1]);
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    case "cooperative":
        WriteReady(args[1]);
        if (Console.ReadLine() == "stop")
        {
            File.WriteAllText(args[2], "disposed");
            return 0;
        }
        return 3;
    case "descendant":
    case "descendant-hang":
        using (var child = Start("hang", args[1]))
        {
            if (args[0] == "descendant-hang")
                await Task.Delay(Timeout.InfiniteTimeSpan);
            else
                // A ready signal is attributable to the descendant, before the root exits.
                while (!File.Exists(args[1]))
                    await Task.Delay(5);
            return 0;
        }
    case "output":
        for (var index = 0; index < 2048; index++)
        {
            Console.Out.Write(new string('o', 1024));
            Console.Error.Write(new string('e', 1024));
        }
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    case "breakaway":
        return TryBreakaway(args[1]);
    default:
        return 2;
}

static Process Start(params string[] arguments)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    foreach (var argument in arguments)
        start.ArgumentList.Add(argument);
    return Process.Start(start)
        ?? throw new InvalidOperationException("Fixture child did not start.");
}

static void WriteReady(string path)
{
    var temporary = path + ".tmp";
    File.WriteAllText(
        temporary,
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
    );
    File.Move(temporary, path);
}

static unsafe int TryBreakaway(string marker)
{
    var program = Environment.ProcessPath!;
    var command = ('"' + program + "\" hang \"" + marker + "\"\0").ToCharArray();
    var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
    PROCESS_INFORMATION information;
    fixed (char* executable = program)
    fixed (char* commandLine = command)
    {
        if (
            !PInvoke.CreateProcess(
                executable,
                commandLine,
                null,
                null,
                false,
                PROCESS_CREATION_FLAGS.CREATE_BREAKAWAY_FROM_JOB
                    | PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW,
                null,
                null,
                &startup,
                &information
            )
        )
        {
            Console.WriteLine("breakaway-denied");
            return 0;
        }
    }
    // Fail safely if the supervisor ever mistakenly allows escape.
    PInvoke.TerminateProcess(information.hProcess, 99);
    PInvoke.CloseHandle(information.hThread);
    PInvoke.CloseHandle(information.hProcess);
    Console.WriteLine("breakaway-allowed");
    return 7;
}
