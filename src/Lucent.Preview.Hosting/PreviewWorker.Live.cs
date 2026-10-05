using System.IO.Pipes;
using System.Security.Cryptography;
using Lucent.Core;
using Lucent.Preview.Protocol;

namespace Lucent.Preview.Hosting;

public static partial class PreviewWorker
{
    private static async Task<int> RunLiveAsync(
        PreviewCatalog catalog,
        string requestPath,
        PreviewWorkerOptions options
    )
    {
        using var cancellation = new CancellationTokenSource(options.Timeout);
        var parent = new ParentMonitor(options.ParentInput, cancellation);
        PreviewRenderSession? initializedSession = null;
        Task<PreviewRenderSession>? startup = null;
        parent.Start();
        try
        {
            var path = LocalPath(requestPath, directory: false);
            var bytes = await ReadBoundedAsync(
                    path,
                    PreviewProtocol.MaximumMessageBytes,
                    cancellation.Token
                )
                .ConfigureAwait(false);
            var launch = PreviewLiveProtocol.ReadRequest(bytes);
            _ = EmptyOutput(launch.Request.OutputDirectory);
            if (options.PrepareImagesAsync is not null)
                throw new InvalidDataException(
                    "Bounded image preparation is not a live readiness contract."
                );
            await using var pipe = new NamedPipeServerStream(
                launch.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            );
            await pipe.WaitForConnectionAsync(cancellation.Token).ConfigureAwait(false);
            startup = PreviewRenderSession.StartAsync(
                catalog.Get(launch.Request.ScenarioId),
                launch.Request,
                cancellation.Token
            );
            await using var session = await startup.ConfigureAwait(false);
            initializedSession = session;
            cancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            await PreviewLiveProtocol
                .WritePacketAsync(
                    pipe,
                    PreviewLiveProtocol.WriteServerMessage(launch.Request),
                    cancellation.Token
                )
                .ConfigureAwait(false);
            await pipe.FlushAsync(cancellation.Token).ConfigureAwait(false);
            var read = ReadCommandsAsync(pipe, session, launch.Request, cancellation.Token);
            var send = SendFramesAsync(pipe, session, launch.Request, cancellation.Token);
            try
            {
                var ended = await Task.WhenAny(read, send, session.Completion)
                    .ConfigureAwait(false);
                await ended.ConfigureAwait(false);
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                // Drain both I/O owners before releasing the pipe and retained host.
                await ObserveTransportEndAsync(read).ConfigureAwait(false);
                await ObserveTransportEndAsync(send).ConfigureAwait(false);
            }
            return 0;
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested
                && (
                    startup is null
                    || startup.IsCanceled
                    || initializedSession?.Completion.IsCompletedSuccessfully == true
                )
            )
        {
            return 0;
        }
        catch (System.Threading.Channels.ChannelClosedException)
            when (cancellation.IsCancellationRequested
                && (
                    startup is null
                    || startup.IsCanceled
                    || initializedSession?.Completion.IsCompletedSuccessfully == true
                )
            )
        {
            return 0;
        }
        catch (Exception)
        {
            await Console.Error.WriteLineAsync("Preview live worker failed.").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            parent.Disarm();
        }
    }

    private static async Task ObserveTransportEndAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (System.Threading.Channels.ChannelClosedException) { }
    }

    private static async Task SendFramesAsync(
        Stream pipe,
        PreviewRenderSession session,
        PreviewWorkerRequest request,
        CancellationToken token
    )
    {
        while (true)
        {
            var frame = await session.ReadFrameAsync(token).ConfigureAwait(false);
            ValidatePng(frame.Png, frame.Width, frame.Height);
            var metadata = PreviewLiveProtocol.WriteServerMessage(
                request,
                frame.Token.Sequence,
                frame.Png.Length,
                Convert.ToHexStringLower(SHA256.HashData(frame.Png)),
                frame.Width,
                frame.Height
            );
            try
            {
                await PreviewLiveProtocol
                    .WritePacketAsync(pipe, metadata, token)
                    .ConfigureAwait(false);
                await pipe.WriteAsync(frame.Png, token).ConfigureAwait(false);
                await pipe.FlushAsync(token).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    private static async Task ReadCommandsAsync(
        Stream pipe,
        PreviewRenderSession session,
        PreviewWorkerRequest request,
        CancellationToken token
    )
    {
        long lastInput = 0;
        while (true)
        {
            byte[] bytes;
            try
            {
                bytes = await PreviewLiveProtocol
                    .ReadPacketAsync(pipe, token)
                    .ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
            var command = PreviewLiveProtocol.ReadCommand(bytes, request);
            if (command.Kind == "preview-live-ack")
            {
                var acknowledgment = await session
                    .AcknowledgeAsync(new(session.Identity, command.FrameSequence))
                    .ConfigureAwait(false);
                if (!acknowledgment.Accepted)
                    throw new InvalidDataException("Invalid live frame acknowledgment.");
                continue;
            }
            if (command.InputSequence <= lastInput)
                throw new InvalidDataException("Nonmonotonic live input.");
            lastInput = command.InputSequence;
            if (command.Kind == "preview-live-focus")
            {
                await session
                    .SetFocusAsync(session.Identity, command.Focused)
                    .ConfigureAwait(false);
                continue;
            }
            var frame = new PreviewFrameToken(session.Identity, command.FrameSequence);
            var value = command.Event;
            var type = value.GetProperty("type").GetString();
            float Number(string name) => (float)value.GetProperty(name).GetDouble();
            var modifiers =
                type == "text"
                    ? KeyModifiers.None
                    : (KeyModifiers)value.GetProperty("modifiers").GetInt32();
            switch (type)
            {
                case "pointer":
                    var phase = value.GetProperty("action").GetString() switch
                    {
                        "down" => PointerCommandKind.Down,
                        "up" => PointerCommandKind.Up,
                        "move" => PointerCommandKind.Move,
                        _ => PointerCommandKind.Cancel,
                    };
                    var button = value.GetProperty("button").GetString() switch
                    {
                        "primary" => PointerButton.Primary,
                        "secondary" => PointerButton.Secondary,
                        "middle" => PointerButton.Middle,
                        _ => PointerButton.None,
                    };
                    await session
                        .PointerAsync(
                            frame,
                            lastInput,
                            new(
                                phase,
                                value.GetProperty("pointerId").GetInt32(),
                                Number("x"),
                                Number("y"),
                                button,
                                modifiers
                            )
                        )
                        .ConfigureAwait(false);
                    break;
                case "wheel":
                    await session
                        .WheelAsync(
                            frame,
                            lastInput,
                            new(Number("x"), Number("y"), Number("deltaX"), Number("deltaY"))
                        )
                        .ConfigureAwait(false);
                    break;
                case "key":
                    await session
                        .KeyAsync(
                            frame,
                            lastInput,
                            new(
                                value.GetProperty("action").GetString() == "down"
                                    ? KeyCommandKind.Down
                                    : KeyCommandKind.Up,
                                Enum.Parse<Key>(value.GetProperty("key").GetString()!),
                                modifiers,
                                value.GetProperty("repeat").GetBoolean()
                            )
                        )
                        .ConfigureAwait(false);
                    break;
                case "text":
                    await session
                        .TextAsync(
                            frame,
                            lastInput,
                            new(TextInputKind.Commit, value.GetProperty("text").GetString()!)
                        )
                        .ConfigureAwait(false);
                    break;
            }
        }
    }
}
