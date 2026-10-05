using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Preview.Hosting;
using Lucent.Preview.Protocol;

namespace Lucent.Preview.Hosting.Tests;

public sealed partial class WorkerContracts
{
    private const string LiveIdentity =
        "\"identity\":{\"sessionId\":\"session\",\"generation\":\"generation\",\"requestId\":\"request\",\"projectTargetDigest\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"inputDigest\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"artifactDigest\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\",\"scenarioId\":\"card/empty\",\"presentationId\":\"default\"}";

    [TestMethod]
    public async Task LivePacketsUseIndependentLittleEndianBytesAndBoundLengthsBeforeAllocation()
    {
        using var source = new MemoryStream([2, 0, 0, 0, 123, 125]);
        CollectionAssert.AreEqual(
            new byte[] { 123, 125 },
            await PreviewLiveProtocol.ReadPacketAsync(source, CancellationToken.None)
        );
        using var destination = new MemoryStream();
        await PreviewLiveProtocol.WritePacketAsync(
            destination,
            "{}"u8.ToArray(),
            CancellationToken.None
        );
        CollectionAssert.AreEqual(new byte[] { 2, 0, 0, 0, 123, 125 }, destination.ToArray());
        foreach (var prefix in new byte[][] { [1, 0, 0, 0], [1, 0, 1, 0], [255, 255, 255, 255] })
        {
            using var invalid = new MemoryStream(prefix);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                PreviewLiveProtocol.ReadPacketAsync(invalid, CancellationToken.None)
            );
        }
        using var truncated = new MemoryStream([3, 0, 0, 0, 123, 125]);
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            PreviewLiveProtocol.ReadPacketAsync(truncated, CancellationToken.None)
        );
    }

    [TestMethod]
    public void LiveCommandsRejectExtraDuplicateMissingAndMismatchedFields()
    {
        var request = Request(@"C:\preview-owned\frame");
        var json =
            "{\"protocolVersion\":2,\"kind\":\"preview-live-focus\","
            + LiveIdentity
            + ",\"inputSequence\":1,\"focused\":false}";
        var accepted = PreviewLiveProtocol.ReadCommand(Encoding.UTF8.GetBytes(json), request);
        Assert.IsFalse(accepted.Focused);
        Assert.AreEqual(1L, accepted.InputSequence);
        foreach (
            var invalid in new[]
            {
                json.Replace(
                    "\"focused\":false",
                    "\"focused\":false,\"extra\":0",
                    StringComparison.Ordinal
                ),
                json.Replace(
                    "\"focused\":false",
                    "\"focused\":false,\"focused\":true",
                    StringComparison.Ordinal
                ),
                json.Replace(",\"focused\":false", "", StringComparison.Ordinal),
                json.Replace(
                    "\"generation\":\"generation\"",
                    "\"generation\":\"stale\"",
                    StringComparison.Ordinal
                ),
                json.Replace(
                    "\"inputSequence\":1",
                    "\"inputSequence\":9007199254740992",
                    StringComparison.Ordinal
                ),
            }
        )
            Assert.Throws<Exception>(() =>
                PreviewLiveProtocol.ReadCommand(Encoding.UTF8.GetBytes(invalid), request)
            );
        var input =
            "{\"protocolVersion\":2,\"kind\":\"preview-live-input\","
            + LiveIdentity
            + ",\"frameSequence\":1,\"inputSequence\":1,\"event\":{\"type\":\"text\",\"text\":\"hello\"}}";
        Assert.AreEqual(
            "hello",
            PreviewLiveProtocol
                .ReadCommand(Encoding.UTF8.GetBytes(input), request)
                .Event.GetProperty("text")
                .GetString()
        );
        Assert.Throws<Exception>(() =>
            PreviewLiveProtocol.ReadCommand(
                Encoding.UTF8.GetBytes(
                    input.Replace(
                        "\"text\":\"hello\"",
                        "\"text\":\"hello\",\"preedit\":true",
                        StringComparison.Ordinal
                    )
                ),
                request
            )
        );
    }

    [TestMethod]
    [DataRow("stop")]
    [DataRow("eof")]
    [DataRow("disconnect")]
    public async Task LiveWorkerStopsWithUnacknowledgedFrameAndCleansUp(string stopMode)
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stopped = 0;
        var disposed = 0;
        var catalog = Catalog(
            (context, _) =>
            {
                context.OnStop(() =>
                {
                    Interlocked.Increment(ref stopped);
                    return ValueTask.CompletedTask;
                });
                context.OnDispose(() =>
                {
                    Interlocked.Increment(ref disposed);
                    return ValueTask.CompletedTask;
                });
                return ValueTask.FromResult("Retained live pixels");
            }
        );
        var pipeName = "lucent-preview-" + Guid.NewGuid().ToString("N");
        var requestPath = WriteLiveRequest(directory.Root, pipeName, Request(directory.Output));
        var worker = PreviewWorker.RunAsync(
            catalog,
            ["--live-request", requestPath],
            new PreviewWorkerOptions { ParentInput = parent, Timeout = TimeSpan.FromSeconds(5) }
        );
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        try
        {
            await client.ConnectAsync(deadline.Token);
            using var ready = JsonDocument.Parse(
                await ReadIndependentPacketAsync(client, deadline.Token)
            );
            Assert.AreEqual(
                "preview-live-ready",
                ready.RootElement.GetProperty("kind").GetString()
            );
            using var metadata = JsonDocument.Parse(
                await ReadIndependentPacketAsync(client, deadline.Token)
            );
            var frame = metadata.RootElement;
            Assert.AreEqual("preview-live-frame", frame.GetProperty("kind").GetString());
            Assert.AreEqual(1L, frame.GetProperty("frameSequence").GetInt64());
            var png = new byte[frame.GetProperty("byteLength").GetInt32()];
            await client.ReadExactlyAsync(png, deadline.Token);
            CollectionAssert.AreEqual(PngSignature, png[..8]);
            Assert.AreEqual(160, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            Assert.AreEqual(120, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
            Assert.AreEqual(
                Convert.ToHexStringLower(SHA256.HashData(png)),
                frame.GetProperty("sha256").GetString()
            );
            if (stopMode == "stop")
                parent.Stop();
            else if (stopMode == "eof")
                parent.End();
            else
                await client.DisposeAsync();
            Assert.AreEqual(0, await worker.WaitAsync(deadline.Token));
            Assert.AreEqual(1, stopped);
            Assert.AreEqual(1, disposed);
            Assert.AreEqual(0, Directory.GetFiles(directory.Output).Length);
        }
        finally
        {
            parent.End();
            await client.DisposeAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    [DataRow("identity")]
    [DataRow("ack")]
    [DataRow("input-sequence")]
    public async Task LiveWorkerRejectsInvalidCorrelationAckAndRepeatedInputSequence(string defect)
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeName = "lucent-preview-" + Guid.NewGuid().ToString("N");
        var path = WriteLiveRequest(directory.Root, pipeName, Request(directory.Output));
        var worker = PreviewWorker.RunAsync(
            Catalog((_, _) => ValueTask.FromResult("pixels")),
            ["--live-request", path],
            new PreviewWorkerOptions { ParentInput = parent }
        );
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        try
        {
            await client.ConnectAsync(deadline.Token);
            _ = await ReadIndependentPacketAsync(client, deadline.Token);
            using var metadata = JsonDocument.Parse(
                await ReadIndependentPacketAsync(client, deadline.Token)
            );
            var png = new byte[metadata.RootElement.GetProperty("byteLength").GetInt32()];
            await client.ReadExactlyAsync(png, deadline.Token);
            var identity =
                defect == "identity"
                    ? LiveIdentity.Replace(
                        "\"generation\":\"generation\"",
                        "\"generation\":\"stale\"",
                        StringComparison.Ordinal
                    )
                    : LiveIdentity;
            var command =
                defect == "ack"
                    ? "{\"protocolVersion\":2,\"kind\":\"preview-live-ack\","
                        + identity
                        + ",\"frameSequence\":2}"
                    : "{\"protocolVersion\":2,\"kind\":\"preview-live-focus\","
                        + identity
                        + ",\"inputSequence\":1,\"focused\":false}";
            await WriteIndependentPacketAsync(client, command, deadline.Token);
            if (defect == "input-sequence")
                await WriteIndependentPacketAsync(client, command, deadline.Token);
            Assert.AreEqual(1, await worker.WaitAsync(deadline.Token));
        }
        finally
        {
            parent.End();
            await client.DisposeAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task LiveWorkerReportsAuthorCleanupFailureDespiteRequestedStop(
        bool cancellationFailure
    )
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cleanupCalls = 0;
        var catalog = Catalog(
            (context, _) =>
            {
                context.OnDispose(() =>
                {
                    Interlocked.Increment(ref cleanupCalls);
                    if (cancellationFailure)
                        throw new OperationCanceledException("Authored cleanup failed.");
                    throw new InvalidOperationException("Authored cleanup failed.");
                });
                return ValueTask.FromResult("pixels");
            }
        );
        var pipeName = "lucent-preview-" + Guid.NewGuid().ToString("N");
        var path = WriteLiveRequest(directory.Root, pipeName, Request(directory.Output));
        var worker = PreviewWorker.RunAsync(
            catalog,
            ["--live-request", path],
            new PreviewWorkerOptions { ParentInput = parent }
        );
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        try
        {
            await client.ConnectAsync(deadline.Token);
            _ = await ReadIndependentPacketAsync(client, deadline.Token);
            using var metadata = JsonDocument.Parse(
                await ReadIndependentPacketAsync(client, deadline.Token)
            );
            var png = new byte[metadata.RootElement.GetProperty("byteLength").GetInt32()];
            await client.ReadExactlyAsync(png, deadline.Token);
            parent.Stop();
            Assert.AreEqual(
                1,
                await worker.WaitAsync(deadline.Token),
                "Requested Stop must not mask author cleanup failure."
            );
            Assert.AreEqual(1, cleanupCalls);
        }
        finally
        {
            parent.End();
            await client.DisposeAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LiveWorkerStartupStopDistinguishesCleanCancellationFromAuthorCleanupFailure(
        bool failCleanup
    )
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCalls = 0;
        var catalog = Catalog(
            async (context, token) =>
            {
                context.OnDispose(() =>
                {
                    Interlocked.Increment(ref cleanupCalls);
                    if (failCleanup)
                        throw new OperationCanceledException(
                            "Authored startup cleanup failed.",
                            token
                        );
                    return ValueTask.CompletedTask;
                });
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "never ready";
            }
        );
        var pipeName = "lucent-preview-" + Guid.NewGuid().ToString("N");
        var path = WriteLiveRequest(directory.Root, pipeName, Request(directory.Output));
        var worker = PreviewWorker.RunAsync(
            catalog,
            ["--live-request", path],
            new PreviewWorkerOptions { ParentInput = parent }
        );
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        try
        {
            await client.ConnectAsync(deadline.Token);
            await entered.Task.WaitAsync(deadline.Token);
            parent.Stop();
            Assert.AreEqual(failCleanup ? 1 : 0, await worker.WaitAsync(deadline.Token));
            Assert.AreEqual(
                1,
                cleanupCalls,
                "Startup Stop must finish owned cleanup exactly once."
            );
        }
        finally
        {
            parent.End();
            await client.DisposeAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task LiveReadySessionSurvivesStartupDeadlineAndFocusCleanupDoesNotRequireFrameAck()
    {
        using var directory = new OwnedDirectory();
        using var parent = new ParentReader();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var setups = 0;
        var pipeName = "lucent-preview-" + Guid.NewGuid().ToString("N");
        var path = WriteLiveRequest(directory.Root, pipeName, Request(directory.Output));
        var worker = PreviewWorker.RunAsync(
            Catalog(
                (_, _) =>
                {
                    Interlocked.Increment(ref setups);
                    return ValueTask.FromResult("pixels");
                }
            ),
            ["--live-request", path],
            new PreviewWorkerOptions { ParentInput = parent, Timeout = TimeSpan.FromSeconds(1) }
        );
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        try
        {
            await client.ConnectAsync(deadline.Token);
            _ = await ReadIndependentPacketAsync(client, deadline.Token);
            using var metadata = JsonDocument.Parse(
                await ReadIndependentPacketAsync(client, deadline.Token)
            );
            var png = new byte[metadata.RootElement.GetProperty("byteLength").GetInt32()];
            await client.ReadExactlyAsync(png, deadline.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(1100), deadline.Token);
            Assert.IsFalse(
                worker.IsCompleted,
                "A ready live session must outlive the cooperative startup deadline."
            );
            await WriteIndependentPacketAsync(
                client,
                "{\"protocolVersion\":2,\"kind\":\"preview-live-focus\","
                    + LiveIdentity
                    + ",\"inputSequence\":1,\"focused\":false}",
                deadline.Token
            );
            await WriteIndependentPacketAsync(
                client,
                "{\"protocolVersion\":2,\"kind\":\"preview-live-ack\","
                    + LiveIdentity
                    + ",\"frameSequence\":1}",
                deadline.Token
            );
            using var next = JsonDocument.Parse(
                await ReadIndependentPacketAsync(client, deadline.Token)
            );
            Assert.AreEqual(2L, next.RootElement.GetProperty("frameSequence").GetInt64());
            Assert.AreEqual(1, setups, "Display acknowledgment must not remount author setup.");
            parent.Stop();
            Assert.AreEqual(0, await worker.WaitAsync(deadline.Token));
        }
        finally
        {
            parent.End();
            await client.DisposeAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task WriteIndependentPacketAsync(
        Stream stream,
        string json,
        CancellationToken token
    )
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)bytes.Length);
        await stream.WriteAsync(prefix, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private static string WriteLiveRequest(
        string directory,
        string pipeName,
        PreviewWorkerRequest request
    )
    {
        var path = Path.Combine(directory, "live-request.json");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", 2);
            writer.WriteString("kind", "preview-live-request");
            writer.WriteString("pipeName", pipeName);
            writer.WritePropertyName("request");
            writer.WriteRawValue(PreviewProtocol.WriteRequest(request));
            writer.WriteEndObject();
        }
        File.WriteAllBytes(path, buffer.ToArray());
        return path;
    }

    private static async Task<byte[]> ReadIndependentPacketAsync(
        Stream stream,
        CancellationToken token
    )
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, token);
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        Assert.IsTrue(length is >= 2 and <= 65536);
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token);
        return bytes;
    }
}
