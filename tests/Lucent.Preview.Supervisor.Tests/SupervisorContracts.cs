using System.Diagnostics;
using System.Text.Json;
using Lucent.Preview.Supervisor;

namespace Lucent.Preview.Supervisor.Tests;

[TestClass]
public sealed class SupervisorContracts
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ArgumentsAndSeparateLogsSurviveWindowsQuoting()
    {
        using var fixture = new Fixture();
        string[] arguments = ["", "space value", "quote\"value", "trailing\\", "unicode λ"];
        var result = await ProcessSupervisor.RunAsync(fixture.Request(["arguments", .. arguments]));
        Assert.AreEqual("completed", result.Status);
        Assert.IsTrue(result.TreeReaped);
        Assert.AreEqual("natural", result.Termination);
        Assert.AreEqual(0, result.ExitCode);
        CollectionAssert.AreEqual(
            arguments,
            JsonSerializer.Deserialize<string[]>(
                await File.ReadAllTextAsync(Path.Combine(fixture.Logs, "stdout.log"))
            )!
        );
        Assert.AreEqual(
            "separate-error",
            (await File.ReadAllTextAsync(Path.Combine(fixture.Logs, "stderr.log"))).Trim()
        );
    }

    [TestMethod]
    public async Task NormalRootExitReapsItsSurvivingDescendant()
    {
        using var fixture = new Fixture();
        var pid = Path.Combine(fixture.Root, "descendant.pid");
        var result = await ProcessSupervisor.RunAsync(fixture.Request(["descendant", pid]));
        Assert.AreEqual("completed", result.Status);
        Assert.AreEqual("forced", result.Termination);
        Assert.IsTrue(result.TreeReaped);
        Assert.IsFalse(
            Alive(
                int.Parse(
                    await File.ReadAllTextAsync(pid),
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
        );
    }

    [TestMethod]
    public async Task CancellationPreservesCooperativeCleanupEvidence()
    {
        using var fixture = new Fixture();
        using var cancel = new CancellationTokenSource();
        var ready = Path.Combine(fixture.Root, "ready.pid");
        var disposed = Path.Combine(fixture.Root, "disposed.txt");
        var task = ProcessSupervisor.RunAsync(
            fixture.Request(["cooperative", ready, disposed]),
            cancel.Token
        );
        await WaitForFile(ready, task);
        await cancel.CancelAsync();
        var result = await task;
        Assert.AreEqual("cancelled", result.Status);
        Assert.AreEqual("cooperative", result.Termination);
        Assert.IsTrue(result.TreeReaped);
        Assert.AreEqual("disposed", await File.ReadAllTextAsync(disposed));
    }

    [TestMethod]
    public async Task TimeoutReapsAChildThatIgnoresStop()
    {
        using var fixture = new Fixture();
        var pid = Path.Combine(fixture.Root, "descendant.pid");
        var request = fixture.Request(["descendant-hang", pid]) with
        {
            TimeoutMs = 1500,
            GraceMs = 25,
        };
        var result = await ProcessSupervisor.RunAsync(request);
        Assert.AreEqual("timeout", result.Status);
        Assert.AreEqual("forced", result.Termination);
        Assert.IsTrue(result.TreeReaped);
        Assert.IsFalse(
            Alive(
                int.Parse(
                    await File.ReadAllTextAsync(pid),
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
        );
    }

    [TestMethod]
    public async Task CrashRemainsAnExitFailureWithConfirmedCleanup()
    {
        using var fixture = new Fixture();
        var result = await ProcessSupervisor.RunAsync(fixture.Request(["crash"]));
        Assert.AreEqual("completed", result.Status);
        Assert.AreEqual(23, result.ExitCode);
        Assert.IsTrue(result.TreeReaped);
    }

    [TestMethod]
    public async Task MissingReapObservationBlocksSuccessEvenWhenTheActualChildIsTerminated()
    {
        using var fixture = new Fixture();
        using var stop = new CancellationTokenSource();
        var ready = Path.Combine(fixture.Root, "ready.pid");
        var task = ProcessSupervisor.RunAsync(
            fixture.Request(["hang", ready]) with
            {
                GraceMs = 0,
            },
            canObserveJob: () => false,
            stop.Token
        );
        await WaitForFile(ready, task);
        var pid = int.Parse(
            await File.ReadAllTextAsync(ready),
            System.Globalization.CultureInfo.InvariantCulture
        );
        await stop.CancelAsync();
        var result = await task;
        fixture.Retain();
        Assert.AreEqual("termination-failed", result.Status);
        Assert.AreEqual("unconfirmed", result.Termination);
        Assert.IsFalse(result.TreeReaped);
        var elapsed = Stopwatch.StartNew();
        while (Alive(pid) && elapsed.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10);
        Assert.IsFalse(Alive(pid), "The fault seam must not bypass real child termination.");
        Assert.IsTrue(
            Directory.Exists(fixture.Logs),
            "Unconfirmed cleanup must retain the generation files."
        );
    }

    [TestMethod]
    public async Task PreviouslyCancelledRequestDoesNotCreateLogsOrLaunch()
    {
        using var fixture = new Fixture();
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var result = await ProcessSupervisor.RunAsync(fixture.Request(["crash"]), stop.Token);
        Assert.AreEqual("cancelled", result.Status);
        Assert.IsTrue(result.TreeReaped);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Logs, "stdout.log")));
    }

    [TestMethod]
    public async Task OutputLimitBoundsBothLogsAndReapsTree()
    {
        using var fixture = new Fixture();
        var result = await ProcessSupervisor.RunAsync(
            fixture.Request(["output"]) with
            {
                MaxOutputBytes = 4096,
            }
        );
        Assert.AreEqual("output-limit", result.Status);
        Assert.IsTrue(result.TreeReaped);
        Assert.IsTrue(new FileInfo(Path.Combine(fixture.Logs, "stdout.log")).Length <= 4096);
        Assert.IsTrue(new FileInfo(Path.Combine(fixture.Logs, "stderr.log")).Length <= 4096);
        Assert.IsTrue(result.StdoutBytes > 4096 || result.StderrBytes > 4096);
    }

    [TestMethod]
    public async Task JobDoesNotPermitExplicitBreakaway()
    {
        using var fixture = new Fixture();
        var escape = Path.Combine(fixture.Root, "escape.pid");
        var result = await ProcessSupervisor.RunAsync(fixture.Request(["breakaway", escape]));
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(result.TreeReaped);
        Assert.AreEqual(
            "breakaway-denied",
            (await File.ReadAllTextAsync(Path.Combine(fixture.Logs, "stdout.log"))).Trim()
        );
        Assert.IsFalse(File.Exists(escape));
    }

    [TestMethod]
    public async Task SupervisorParentEofStopsAndReapsTheWorker()
    {
        using var fixture = new Fixture();
        var ready = Path.Combine(fixture.Root, "ready.pid");
        var request = fixture.Request(["hang", ready]);
        var host = Path.Combine(
            Path.GetFullPath(
                Path.Combine(
                    System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                    "..",
                    "..",
                    ".."
                )
            ),
            "dotnet.exe"
        );
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(
            Path.Combine(AppContext.BaseDirectory, "Supervisor", "Lucent.Preview.Supervisor.dll")
        );
        using var process = Process.Start(start)!;
        try
        {
            await process.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(request, JsonOptions)
            );
            await process.StandardInput.FlushAsync();
            await WaitForFile(ready, process.WaitForExitAsync());
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var json = await process.StandardOutput.ReadToEndAsync();
            var result = JsonSerializer.Deserialize<SupervisorResult>(json, JsonOptions)!;
            Assert.AreEqual("cancelled", result.Status);
            Assert.IsTrue(result.TreeReaped);
            Assert.IsFalse(
                Alive(
                    int.Parse(
                        await File.ReadAllTextAsync(ready),
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                )
            );
            Assert.AreEqual("", await process.StandardError.ReadToEndAsync());
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [TestMethod]
    public async Task SupervisorCrashClosesJobAndKillsItsChild()
    {
        using var fixture = new Fixture();
        var ready = Path.Combine(fixture.Root, "ready.pid");
        var start = new ProcessStartInfo(
            Path.Combine(AppContext.BaseDirectory, "Supervisor", "Lucent.Preview.Supervisor.exe")
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        try
        {
            await process.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(fixture.Request(["hang", ready]), JsonOptions)
            );
            await process.StandardInput.FlushAsync();
            await WaitForFile(ready, process.WaitForExitAsync());
            var pid = int.Parse(
                await File.ReadAllTextAsync(ready),
                System.Globalization.CultureInfo.InvariantCulture
            );
            // Kill the supervisor alone: job close, rather than a test-side tree killer, owns the child.
            process.Kill();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var elapsed = Stopwatch.StartNew();
            while (Alive(pid) && elapsed.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(10);
            Assert.IsFalse(Alive(pid), "Supervisor crash left its owned child alive.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [TestMethod]
    public void UnsupportedRequestCannotLaunchAChild()
    {
        using var fixture = new Fixture();
        Assert.ThrowsExactly<ArgumentException>(() =>
            (fixture.Request(["crash"]) with { GraceMs = 10_001 }).Validate()
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            (fixture.Request(["crash"]) with { Program = "relative.exe" }).Validate()
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            (
                fixture.Request(["crash"]) with
                {
                    Environment = new() { ["bad=name"] = "value" },
                }
            ).Validate()
        );
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Logs, "stdout.log")));
    }

    private static async Task WaitForFile(string path, Task operation)
    {
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (operation.IsCompleted || elapsed.Elapsed > TimeSpan.FromSeconds(8))
                Assert.Fail("The real fixture did not signal readiness before exit or deadline.");
            await Task.Delay(10);
        }
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private bool retained;
        internal string Root { get; } =
            Path.Combine(Path.GetTempPath(), "lucent-supervisor-" + Guid.NewGuid().ToString("N"));
        internal string Logs => Path.Combine(Root, "logs");

        internal Fixture() => Directory.CreateDirectory(Logs);

        internal SupervisorRequest Request(string[] args) =>
            new(
                1,
                "preview-supervisor-request",
                "supervisor-contract",
                Path.Combine(
                    AppContext.BaseDirectory,
                    "SupervisorFixture",
                    "SupervisorFixture.exe"
                ),
                args,
                Root,
                Logs,
                10_000,
                1000
            );

        internal void Retain() => retained = true;

        public void Dispose()
        {
            if (!retained)
                Directory.Delete(Root, recursive: true);
        }
    }
}
