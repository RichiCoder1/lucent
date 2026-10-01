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
        var catalog = PreviewProtocol.ReadCatalogResult(
            File.ReadAllBytes(Path.Combine(directory, "catalog-result-valid.json"))
        );
        Assert.AreEqual("card/empty", catalog.Scenarios.Single().Id);
        Assert.AreEqual("", catalog.Scenarios.Single().Culture);
        PreviewProtocol.ReadCatalogRequest(
            File.ReadAllBytes(Path.Combine(directory, "catalog-request-valid.json"))
        );
        var invalid = Directory.GetFiles(directory, "*-invalid-*.json");
        Assert.IsGreaterThanOrEqualTo(8, invalid.Length);
        foreach (var path in invalid)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.Throws<Exception>(
                () =>
                {
                    if (
                        Path.GetFileName(path)
                            .StartsWith("catalog-result-", StringComparison.Ordinal)
                    )
                        PreviewProtocol.ReadCatalogResult(bytes);
                    else if (
                        Path.GetFileName(path).StartsWith("request-", StringComparison.Ordinal)
                    )
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
        var request = Request(directory.Output) with { Scale = scale };
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

    [TestMethod]
    public async Task CatalogDiscoveryCopiesDefaultsWithoutExecutingAuthorCallbacks()
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        var catalog = Catalog(
            (_, _) => throw new InvalidOperationException("setup must not run"),
            theme: _ => throw new InvalidOperationException("theme must not run")
        );
        var request = PreviewProtocol.ReadCatalogRequest(
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog-request-valid.json")
            )
        ) with
        {
            OutputDirectory = directory.Output,
        };
        var path = Path.Combine(directory.Root, "catalog-request.json");
        File.WriteAllBytes(path, PreviewProtocol.WriteCatalogRequest(request));
        Assert.AreEqual(
            0,
            await PreviewWorker.RunAsync(
                catalog,
                ["--request", path],
                new PreviewWorkerOptions { ParentInput = parent }
            )
        );
        var result = PreviewProtocol.ReadCatalogResult(
            File.ReadAllBytes(Path.Combine(directory.Output, "catalog.json"))
        );
        Assert.AreEqual(1, result.Scenarios.Length);
        Assert.AreEqual("card/empty", result.Scenarios[0].Id);
        Assert.AreEqual("fixture.csproj", result.Scenarios[0].SourceProject);
        Assert.AreEqual(160, result.Scenarios[0].LogicalWidth);
        Assert.AreEqual(1, result.Scenarios[0].Density);
        Assert.AreEqual("1970-01-01T00:00:00.0000000+00:00", result.Scenarios[0].InitialTime);
        Assert.IsFalse(File.Exists(Path.Combine(directory.Output, "frame.png")));
    }

    [TestMethod]
    public async Task EffectivePresentationReachesSetupHostAndFrameWithoutChangingDefaults()
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        var setup = false;
        var catalog = Catalog(
            (context, _) =>
            {
                setup = true;
                Assert.AreEqual(2, context.Descriptor.Presentation.Density);
                Assert.AreEqual(
                    new LayoutViewport(200, 140, 1.25f),
                    context.Descriptor.Presentation.Viewport
                );
                Assert.AreEqual(
                    ThemeColorScheme.Dark,
                    context.Descriptor.Presentation.Appearance.ColorScheme
                );
                Assert.AreEqual(
                    ThemeContrast.High,
                    context.Descriptor.Presentation.Appearance.Contrast
                );
                Assert.AreEqual(ControlThemes.HighContrast.Name, context.Session.Theme.Theme.Name);
                Assert.AreEqual(DateTimeOffset.UnixEpoch, context.Clock.GetUtcNow());
                return ValueTask.FromResult("Override density 2");
            },
            theme: appearance =>
                appearance.Contrast == ThemeContrast.High
                    ? ControlThemes.HighContrast
                    : ControlThemes.Light
        );
        var request = Request(directory.Output) with
        {
            LogicalWidth = 200,
            LogicalHeight = 140,
            Scale = 1.25,
            ColorScheme = "dark",
            Contrast = "high",
            Density = 2,
        };
        Assert.AreEqual(
            0,
            await PreviewWorker.RunAsync(
                catalog,
                ["--request", directory.Write(request)],
                new PreviewWorkerOptions
                {
                    ParentInput = parent,
                    PrepareImagesAsync = async (application, _) =>
                    {
                        Assert.AreEqual(
                            new LayoutViewport(200, 140, 1.25f),
                            await application.InvokeAsync(context => context.Viewport)
                        );
                        using var snapshot = await application.SnapshotAsync();
                        snapshot.Require(SemanticRole.Text, "Override density 2");
                    },
                }
            )
        );
        Assert.IsTrue(setup);
        var result = PreviewProtocol.ReadResult(
            File.ReadAllBytes(Path.Combine(directory.Output, "result.json"))
        );
        Assert.AreEqual("dark", result.ColorScheme);
        Assert.AreEqual("high", result.Contrast);
        Assert.AreEqual(2, result.Density);
        Assert.AreEqual(250, result.Width);
        Assert.AreEqual(175, result.Height);
        Assert.AreEqual("", result.Culture);
        Assert.AreEqual("1970-01-01T00:00:00.0000000+00:00", result.InitialTime);
        Assert.AreEqual(
            new LayoutViewport(160, 120, 1),
            catalog.Get("card/empty").Descriptor.Presentation.Viewport
        );
        Assert.AreEqual(1, catalog.Get("card/empty").Descriptor.Presentation.Density);
    }

    [TestMethod]
    public void CatalogCountLabelsAndEncodedBudgetAreBounded()
    {
        var result = PreviewProtocol.ReadCatalogResult(
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog-result-valid.json")
            )
        );
        var entry = result.Scenarios.Single();
        Assert.Throws<InvalidDataException>(() =>
            PreviewProtocol.WriteCatalogResult(
                result with
                {
                    Scenarios = new PreviewCatalogEntry[65],
                }
            )
        );
        Assert.Throws<InvalidDataException>(() =>
            PreviewProtocol.WriteCatalogResult(
                result with
                {
                    Scenarios = [entry with { Title = new string('x', 257) }],
                }
            )
        );
        var many = Enumerable
            .Range(0, 64)
            .Select(index =>
                entry with
                {
                    Id = index.ToString(CultureInfo.InvariantCulture),
                    SourceProject = new string('x', 2048),
                }
            )
            .ToArray();
        Assert.Throws<InvalidDataException>(() =>
            PreviewProtocol.WriteCatalogResult(result with { Scenarios = many })
        );
        Assert.Throws<InvalidDataException>(() =>
            PreviewProtocol.WriteRequest(
                Request(@"C:\owned") with
                {
                    LogicalWidth = 8192,
                    LogicalHeight = 8192,
                }
            )
        );
    }

    [TestMethod]
    public async Task AggregatePixelBudgetRejectsBeforeFixtureSetup()
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        var setups = 0;
        var catalog = Catalog(
            (_, _) =>
            {
                setups++;
                return ValueTask.FromResult("root");
            }
        );
        var path = Path.Combine(directory.Root, "invalid-request.json");
        var json = System.Text.Encoding.UTF8.GetString(
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "request-invalid-pixels.json")
            )
        );
        json = json.Replace(
            @"C:\\preview-owned\\frame",
            directory.Output.Replace("\\", "\\\\", StringComparison.Ordinal),
            StringComparison.Ordinal
        );
        File.WriteAllText(path, json);
        Assert.AreEqual(
            1,
            await PreviewWorker.RunAsync(
                catalog,
                ["--request", path],
                new PreviewWorkerOptions { ParentInput = parent }
            )
        );
        Assert.AreEqual(0, setups);
        Assert.IsFalse(File.Exists(Path.Combine(directory.Output, "frame.png")));
    }

    private static PreviewCatalog Catalog(
        Func<PreviewSetupContext, CancellationToken, ValueTask<string>> setup,
        float scale = 1,
        Func<ThemeAppearance, Theme>? theme = null
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
                        theme ?? (static _ => ControlThemes.Light),
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
            ProtocolVersion = 2,
            Kind = PreviewProtocol.CaptureRequestKind,
            SessionId = "session",
            Generation = "generation",
            RequestId = "request",
            ProjectTargetDigest = new string('a', 64),
            InputDigest = new string('b', 64),
            ArtifactDigest = new string('c', 64),
            ScenarioId = "card/empty",
            PresentationId = "default",
            OutputDirectory = output,
            LogicalWidth = 160,
            LogicalHeight = 120,
            Scale = 1,
            ColorScheme = "light",
            Contrast = "normal",
            Density = 1,
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
        internal string Root => _root;

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
