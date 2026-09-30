using System.Collections.Concurrent;
using System.Text;
using Lucent.Platform.Windows.Activation;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var settings = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixture.txt"));
        if (settings.Length != 4)
            return 80;
        var options = new WindowsActivationOptions(settings[0], settings[1], "navigation");
        var output = settings[2];
        var stop = settings[3];
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException();

        if (args is ["--register"])
        {
            UnpackagedProtocolRegistration.Register(
                options,
                executable,
                executable + ",0",
                "Lucent isolated activation fixture"
            );
            return 0;
        }
        if (args is ["--unregister"])
        {
            UnpackagedProtocolRegistration.Unregister(options, executable);
            return 0;
        }

        Directory.CreateDirectory(output);
        void Publish(string name, string contents)
        {
            var destination = Path.Combine(output, name);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, contents);
            File.Move(temporary, destination);
        }

        var result = WindowsActivation.Run(
            options,
            inbox =>
            {
                var queued = new ConcurrentQueue<Action>();
                using var wake = new AutoResetEvent(false);
                var count = 0;
                void Record(ActivationEnvelope envelope)
                {
                    var number = ++count;
                    var lines = new[]
                    {
                        $"pid={Environment.ProcessId}",
                        $"kind={envelope.Kind}",
                        $"delivery={envelope.Delivery}",
                        $"provenance={envelope.Provenance}",
                        $"rejection={envelope.Rejection}",
                        "rawBase64="
                            + Convert.ToBase64String(Encoding.UTF8.GetBytes(envelope.RawUri ?? "")),
                        "routeBase64="
                            + Convert.ToBase64String(
                                Encoding.UTF8.GetBytes(envelope.EscapedPathAndQuery ?? "")
                            ),
                    };
                    Publish($"event-{number:D4}.txt", string.Join(Environment.NewLine, lines));
                }

                Record(inbox.TakeStartup() ?? throw new InvalidOperationException());
                inbox.Attach(
                    action =>
                    {
                        queued.Enqueue(action);
                        wake.Set();
                    },
                    Record
                );
                Publish("primary.pid", Environment.ProcessId.ToString());
                var deadline = Environment.TickCount64 + 20_000;
                while (count < 3 && !File.Exists(stop) && Environment.TickCount64 < deadline)
                {
                    while (queued.TryDequeue(out var action))
                        action();
                    if (count < 3)
                        wake.WaitOne(25);
                }
                return count == 3 ? 0 : 81;
            }
        );
        Publish(
            $"process-{Environment.ProcessId}.txt",
            $"kind={result.Kind}{Environment.NewLine}exit={result.ExitCode}{Environment.NewLine}failure={result.Failure}"
        );
        return result.ExitCode;
    }
}
