using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Lucent.Core;
using Lucent.Preview.Hosting;
using Lucent.Preview.Protocol;

namespace Lucent.Preview.Hosting.Tests;

[TestClass]
public sealed class WorkerContracts
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [TestMethod]
    public void SharedMessagesRejectMalformedCorrelationAndFrameMetadata()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var validRequest = PreviewProtocol.ReadRequest(
            File.ReadAllBytes(Path.Combine(directory, "request-valid.json"))
        );
        Assert.AreEqual("card/empty", validRequest.ScenarioId);
        Assert.AreEqual("generation-1", validRequest.Generation);
        var validResult = PreviewProtocol.ReadResult(
            File.ReadAllBytes(Path.Combine(directory, "result-valid.json"))
        );
        Assert.AreEqual(640, validResult.Width);
        Assert.AreEqual(480, validResult.Height);
        var fractionalResult = PreviewProtocol.ReadResult(
            File.ReadAllBytes(Path.Combine(directory, "result-valid-fractional.json"))
        );
        Assert.AreEqual(176, fractionalResult.Width);
        Assert.AreEqual(132, fractionalResult.Height);
        var invalid = Directory.GetFiles(directory, "*-invalid-*.json");
        Assert.IsGreaterThanOrEqualTo(8, invalid.Length);
        foreach (var path in invalid)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.Throws<Exception>(
                () =>
                {
                    if (Path.GetFileName(path).StartsWith("request-", StringComparison.Ordinal))
                        PreviewProtocol.ReadRequest(bytes);
                    else
                        PreviewProtocol.ReadResult(bytes);
                },
                Path.GetFileName(path)
            );
        }
    }

    [TestMethod]
    [DataRow(1f, 160, 120)]
    [DataRow(1.1f, 176, 132)]
    public async Task FramePublicationWaitsForImageReadinessAndSuccessfulCleanup(
        float scale,
        int width,
        int height
    )
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        var calls = new List<string>();
        var catalog = Catalog(
            (context, _) =>
            {
                context.OnStop(() =>
                {
                    calls.Add("stop");
                    return ValueTask.CompletedTask;
                });
                context.OnDispose(() =>
                {
                    calls.Add("dispose");
                    return ValueTask.CompletedTask;
                });
                return ValueTask.FromResult("Fixture pixels");
            },
            scale
        );
        var request = Request(directory.Output);
        var requestPath = directory.Write(request);
        var exit = await PreviewWorker.RunAsync(
            catalog,
            ["--request", requestPath],
            new PreviewWorkerOptions
            {
                ParentInput = parent,
                PrepareImagesAsync = async (application, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Assert.IsFalse(File.Exists(Path.Combine(directory.Output, "result.json")));
                    using var snapshot = await application.SnapshotAsync();
                    snapshot.Require(SemanticRole.Text, "Fixture pixels");
                    calls.Add("ready");
                },
            }
        );
        Assert.AreEqual(0, exit);
        Assert.AreEqual("ready,stop,dispose", string.Join(',', calls));
        var result = PreviewProtocol.ReadResult(
            File.ReadAllBytes(Path.Combine(directory.Output, "result.json"))
        );
        var png = File.ReadAllBytes(Path.Combine(directory.Output, "frame.png"));
        Assert.AreEqual(request.RequestId, result.RequestId);
        Assert.AreEqual(request.Generation, result.Generation);
        Assert.AreEqual(width, result.Width);
        Assert.AreEqual(height, result.Height);
        Assert.AreEqual(width, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.AreEqual(height, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        Assert.AreEqual(png.Length, result.ByteLength);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(png)), result.Sha256);
        CollectionAssert.AreEqual(PngSignature, png[..8]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StopAndEofCancelRunningSetupAndCompleteRegisteredCleanup(bool eof)
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        var catalog = Catalog(
            async (context, token) =>
            {
                context.OnDispose(() =>
                {
                    disposed = true;
                    return ValueTask.CompletedTask;
                });
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "unreachable";
            }
        );
        var running = PreviewWorker.RunAsync(
            catalog,
            ["--request", directory.Write(Request(directory.Output))],
            new PreviewWorkerOptions { ParentInput = parent }
        );
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (eof)
                parent.End();
            else
                parent.Stop();
            Assert.AreEqual(1, await running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(disposed);
            Assert.IsFalse(File.Exists(Path.Combine(directory.Output, "result.json")));
            Assert.IsFalse(File.Exists(Path.Combine(directory.Output, "frame.png")));
        }
        finally
        {
            parent.End();
            await running;
        }
    }

    [TestMethod]
    [DataRow("setup")]
    [DataRow("readiness")]
    [DataRow("cleanup")]
    public async Task FailureNeverPublishesSuccess(string stage)
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        var disposed = false;
        var catalog = Catalog(
            (context, _) =>
            {
                context.OnDispose(() =>
                {
                    disposed = true;
                    return stage == "cleanup"
                        ? ValueTask.FromException(new InvalidOperationException("cleanup"))
                        : ValueTask.CompletedTask;
                });
                return stage == "setup"
                    ? ValueTask.FromException<string>(new InvalidOperationException("setup"))
                    : ValueTask.FromResult("root");
            }
        );
        var exit = await PreviewWorker.RunAsync(
            catalog,
            ["--request", directory.Write(Request(directory.Output))],
            new PreviewWorkerOptions
            {
                ParentInput = parent,
                PrepareImagesAsync =
                    stage == "readiness"
                        ? (_, _) =>
                            ValueTask.FromException(new InvalidOperationException("readiness"))
                        : null,
            }
        );
        Assert.AreEqual(1, exit);
        Assert.IsTrue(disposed);
        Assert.IsFalse(File.Exists(Path.Combine(directory.Output, "result.json")));
        Assert.IsFalse(File.Exists(Path.Combine(directory.Output, "frame.png")));
    }

    [TestMethod]
    public async Task DeadlineCancelsDeclaredImagePreparationAndAwaitsCleanup()
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        var disposed = false;
        var entered = false;
        var catalog = Catalog(
            (context, _) =>
            {
                context.OnDispose(() =>
                {
                    disposed = true;
                    return ValueTask.CompletedTask;
                });
                return ValueTask.FromResult("root");
            }
        );
        var exit = await PreviewWorker
            .RunAsync(
                catalog,
                ["--request", directory.Write(Request(directory.Output))],
                new PreviewWorkerOptions
                {
                    ParentInput = parent,
                    Timeout = TimeSpan.FromSeconds(1),
                    PrepareImagesAsync = async (_, token) =>
                    {
                        entered = true;
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    },
                }
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, exit);
        Assert.IsTrue(entered);
        Assert.IsTrue(disposed);
        Assert.IsFalse(File.Exists(Path.Combine(directory.Output, "result.json")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingOutputAndOversizedRequestRejectBeforeAuthorExecution(bool oversized)
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        var executions = 0;
        var catalog = Catalog(
            (_, _) =>
            {
                executions++;
                return ValueTask.FromResult("root");
            }
        );
        var requestPath = directory.Write(Request(directory.Output));
        var sentinel = Path.Combine(directory.Output, "frame.png");
        if (oversized)
            File.WriteAllBytes(requestPath, new byte[PreviewProtocol.MaximumMessageBytes + 1]);
        else
            File.WriteAllText(sentinel, "preserve caller content");
        var exit = await PreviewWorker.RunAsync(
            catalog,
            ["--request", requestPath],
            new PreviewWorkerOptions { ParentInput = parent }
        );
        Assert.AreEqual(1, exit);
        Assert.AreEqual(0, executions);
        Assert.IsFalse(File.Exists(Path.Combine(directory.Output, "result.json")));
        if (!oversized)
            Assert.AreEqual("preserve caller content", File.ReadAllText(sentinel));
    }

    private static PreviewCatalog Catalog(
        Func<PreviewSetupContext, CancellationToken, ValueTask<string>> setup,
        float scale = 1
    ) =>
        new PreviewCatalogBuilder()
            .Add(
                new PreviewScenarioDescriptor(
                    "card/empty",
                    "Worker contract",
                    new PreviewSource("fixture.csproj", "Fixture.lui", "Fixture"),
                    new PreviewPresentation(
                        new LayoutViewport(160, 120, scale),
                        ThemeAppearance.Light,
                        static _ => ControlThemes.Light,
                        1,
                        CultureInfo.InvariantCulture,
                        CultureInfo.InvariantCulture,
                        DateTimeOffset.UnixEpoch
                    )
                ),
                setup,
                (content, _) => Components.Text(content)
            )
            .Build();

    private static PreviewWorkerRequest Request(string output) =>
        new()
        {
            ProtocolVersion = 1,
            SessionId = "session",
            Generation = "generation",
            RequestId = "request",
            ProjectTargetDigest = new string('a', 64),
            InputDigest = new string('b', 64),
            ArtifactDigest = new string('c', 64),
            ScenarioId = "card/empty",
            PresentationId = "default",
            OutputDirectory = output,
        };

    private sealed class OwnedDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "lucent-worker-" + Guid.NewGuid().ToString("N")
        );

        internal OwnedDirectory()
        {
            Directory.CreateDirectory(Output);
        }

        internal string Output => Path.Combine(_root, "output");

        internal string Write(PreviewWorkerRequest request)
        {
            var path = Path.Combine(_root, "request.json");
            File.WriteAllBytes(path, PreviewProtocol.WriteRequest(request));
            return path;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class ParentReader : TextReader
    {
        private readonly BlockingCollection<int> _characters = new();

        internal void Stop()
        {
            foreach (var character in "stop\n")
                _characters.Add(character);
        }

        internal void End()
        {
            if (!_characters.IsAddingCompleted)
                _characters.CompleteAdding();
        }

        public override int Read() =>
            _characters.TryTake(out var value, Timeout.Infinite) ? value : -1;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                End();
                _characters.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
