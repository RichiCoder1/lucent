using System.Security.Cryptography;
using Lucent.Core;
using Lucent.Preview.Protocol;
using Lucent.Testing;
using Lucent.Testing.Skia;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace Lucent.Preview.Hosting;

/// <summary>One-shot explicit development host; never discovers or executes a production entry point.</summary>
public static class PreviewWorker
{
    /// <summary>Captures one compiled scenario from --request absolute.json, publishing success after cleanup.</summary>
    /// <remarks>This is a one-shot process entry-point lifetime, not an in-process session API.
    /// The background parent reader ends with the process. Noncooperative author code requires
    /// the external supervisor's hard process deadline.</remarks>
    public static async Task<int> RunAsync(
        PreviewCatalog catalog,
        string[] args,
        PreviewWorkerOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(args);
        options ??= new PreviewWorkerOptions();
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(options));
        ArgumentNullException.ThrowIfNull(options.ParentInput);
        using var cancellation = new CancellationTokenSource(options.Timeout);
        var parentMonitor = new ParentMonitor(options.ParentInput, cancellation);
        parentMonitor.Start();
        var stage = "request";
        try
        {
            if (args.Length != 2 || args[0] != "--request")
                throw new InvalidDataException("Invalid worker arguments.");
            var requestPath = LocalPath(args[1], directory: false);
            var request = PreviewProtocol.ReadRequest(
                await ReadBoundedAsync(
                        requestPath,
                        PreviewProtocol.MaximumMessageBytes,
                        cancellation.Token
                    )
                    .ConfigureAwait(false)
            );
            var output = LocalPath(request.OutputDirectory, directory: true);
            if (Directory.EnumerateFileSystemEntries(output).Any())
                throw new InvalidDataException("Worker output must be empty.");
            var scenario = catalog.Get(request.ScenarioId);
            var presentation = scenario.Descriptor.Presentation;
            var width = MathF.Ceiling(presentation.Viewport.Width * presentation.Viewport.Scale);
            var height = MathF.Ceiling(presentation.Viewport.Height * presentation.Viewport.Scale);
            if (
                width is < 1 or > PreviewProtocol.MaximumDimension
                || height is < 1 or > PreviewProtocol.MaximumDimension
            )
                throw new InvalidDataException("Preview viewport exceeds the frame bound.");
            cancellation.Token.ThrowIfCancellationRequested();
            var clock = new FakeTimeProvider(presentation.InitialTime);
            PreviewScenarioBinding? binding = null;
            byte[] png;
            stage = "startup";
            await using (
                var application = await SkiaHeadlessApplication
                    .StartAsync(
                        context => binding!.CreateRoot(context.Session),
                        builder => binding = scenario.Bind(builder, clock, cancellation.Token),
                        new HeadlessApplicationOptions
                        {
                            Title = scenario.Descriptor.Title,
                            Purpose = CompositionPurpose.Preview,
                            Viewport = presentation.Viewport,
                            Appearance = presentation.Appearance,
                            ThemeFactory = presentation.ThemeFactory,
                            Culture = presentation.Culture,
                            UICulture = presentation.UICulture,
                            TimeProviderFactory = () => clock,
                        }
                    )
                    .ConfigureAwait(false)
            )
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (options.PrepareImagesAsync is { } prepare)
                {
                    stage = "image-readiness";
                    await prepare(application, cancellation.Token).ConfigureAwait(false);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                stage = "capture";
                png = await application.CapturePngAsync(showCaret: false).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                stage = "cleanup";
            }
            // Neither frame nor result is published until the host and author cleanup have succeeded.
            cancellation.Token.ThrowIfCancellationRequested();
            stage = "frame-validation";
            ValidatePng(png, (int)width, (int)height);
            var result = new PreviewWorkerResult
            {
                ProtocolVersion = request.ProtocolVersion,
                SessionId = request.SessionId,
                Generation = request.Generation,
                RequestId = request.RequestId,
                ProjectTargetDigest = request.ProjectTargetDigest,
                InputDigest = request.InputDigest,
                ArtifactDigest = request.ArtifactDigest,
                ScenarioId = request.ScenarioId,
                PresentationId = request.PresentationId,
                FrameSequence = 1,
                FileName = "frame.png",
                ByteLength = png.Length,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(png)),
                Width = (int)width,
                Height = (int)height,
                LogicalWidth = presentation.Viewport.Width,
                LogicalHeight = presentation.Viewport.Height,
                Scale = presentation.Viewport.Scale,
            };
            var resultBytes = PreviewProtocol.WriteResult(result);
            stage = "publication";
            _ = LocalPath(output, directory: true);
            await WriteNewAsync(Path.Combine(output, "frame.png"), png, cancellation.Token)
                .ConfigureAwait(false);
            var temporaryResult = Path.Combine(output, "result.tmp");
            await WriteNewAsync(temporaryResult, resultBytes, cancellation.Token)
                .ConfigureAwait(false);
            _ = LocalPath(output, directory: true);
            cancellation.Token.ThrowIfCancellationRequested();
            File.Move(temporaryResult, Path.Combine(output, "result.json"), overwrite: false);
            return 0;
        }
        catch (Exception error)
        {
            // Raw author errors can include paths, environment values or credentials.
            await Console
                .Error.WriteLineAsync(
                    error is OperationCanceledException
                        ? "Preview worker cancelled."
                        : "Preview worker failed during " + stage + "."
                )
                .ConfigureAwait(false);
            return 1;
        }
        finally
        {
            parentMonitor.Disarm();
        }
    }

    private sealed class ParentMonitor(TextReader input, CancellationTokenSource operation)
    {
        private readonly object _gate = new();
        private bool _armed = true;

        internal void Start() =>
            new Thread(Read)
            {
                IsBackground = true,
                Name = "Lucent preview parent channel",
            }.Start();

        internal void Disarm()
        {
            lock (_gate)
                _armed = false;
        }

        private void Cancel()
        {
            lock (_gate)
            {
                if (_armed)
                {
                    try
                    {
                        operation.Cancel();
                    }
                    catch (AggregateException)
                    { /* Cancellation remains requested even if author callbacks fail. */
                    }
                }
            }
        }

        private void Read()
        {
            try
            {
                var line = new System.Text.StringBuilder();
                int character;
                while ((character = input.Read()) >= 0)
                {
                    lock (_gate)
                    {
                        if (!_armed)
                            return;
                    }
                    if (character == '\n')
                    {
                        if (line.ToString().TrimEnd('\r') == "stop")
                        {
                            Cancel();
                            return;
                        }
                        line.Clear();
                    }
                    else
                    {
                        if (line.Length >= 16)
                        {
                            Cancel();
                            return;
                        }
                        line.Append((char)character);
                    }
                }
                Cancel();
            }
            catch (Exception)
            {
                Cancel();
            }
        }
    }

    private static string LocalPath(string path, bool directory)
    {
        if (
            !OperatingSystem.IsWindows()
            || !Path.IsPathFullyQualified(path)
            || path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.AsSpan(2).Contains(':')
        )
            throw new InvalidDataException("Worker requires a local Windows path.");
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        if (new DriveInfo(root).DriveType != DriveType.Fixed)
            throw new InvalidDataException("Worker requires a fixed local drive.");
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Worker paths cannot traverse reparse points.");
        }
        if (directory ? !Directory.Exists(full) : !File.Exists(full))
            throw new InvalidDataException("Worker path is missing.");
        return full;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        int maximum,
        CancellationToken token
    )
    {
        await using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous
        );
        if (file.Length is < 2 || file.Length > maximum)
            throw new InvalidDataException("Worker request exceeds its bound.");
        var bytes = new byte[(int)file.Length];
        await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return bytes;
    }

    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken token)
    {
        await using var file = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous
        );
        await file.WriteAsync(bytes, token).ConfigureAwait(false);
        await file.FlushAsync(token).ConfigureAwait(false);
    }

    private static void ValidatePng(byte[] bytes, int width, int height)
    {
        if (bytes.Length is < 33 or > PreviewProtocol.MaximumFrameBytes)
            throw new InvalidDataException("Preview PNG exceeds its bound.");
        using var codec = SKCodec.Create(new MemoryStream(bytes, writable: false));
        if (
            codec is null
            || codec.EncodedFormat != SKEncodedImageFormat.Png
            || codec.Info.Width != width
            || codec.Info.Height != height
            || codec.FrameCount > 1
        )
            throw new InvalidDataException("Preview PNG metadata is invalid.");
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("Preview PNG decode failed.");
    }
}
