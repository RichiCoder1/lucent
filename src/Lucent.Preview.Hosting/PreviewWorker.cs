using System.Globalization;
using System.Security.Cryptography;
using Lucent.Core;
using Lucent.Preview.Protocol;
using Lucent.Testing;
using Lucent.Testing.Skia;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace Lucent.Preview.Hosting;

/// <summary>Explicit development host for bounded capture or opt-in live sessions.</summary>
public static partial class PreviewWorker
{
    /// <summary>Dispatches --request or --live-request with an absolute local JSON launch file.</summary>
    /// <remarks>This is a supervised process entry-point lifetime, not an in-process session API.
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
        if (args.Length == 2 && args[0] == "--live-request")
            return await RunLiveAsync(catalog, args[1], options).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource(options.Timeout);
        var parentMonitor = new ParentMonitor(options.ParentInput, cancellation);
        parentMonitor.Start();
        var stage = "request";
        try
        {
            if (args.Length != 2 || args[0] != "--request")
                throw new InvalidDataException("Invalid worker arguments.");
            var requestPath = LocalPath(args[1], directory: false);
            var requestBytes = await ReadBoundedAsync(
                    requestPath,
                    PreviewProtocol.MaximumMessageBytes,
                    cancellation.Token
                )
                .ConfigureAwait(false);
            if (PreviewProtocol.ReadKind(requestBytes) == PreviewProtocol.CatalogRequestKind)
            {
                stage = "catalog";
                var discovery = PreviewProtocol.ReadCatalogRequest(requestBytes);
                var discoveryOutput = EmptyOutput(discovery.OutputDirectory);
                cancellation.Token.ThrowIfCancellationRequested();
                if (catalog.Scenarios.Count > PreviewProtocol.MaximumScenarios)
                    throw new InvalidDataException("Preview catalog count exceeds its bound.");
                var entries = new PreviewCatalogEntry[catalog.Scenarios.Count];
                for (var i = 0; i < entries.Length; i++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    entries[i] = Describe(catalog.Scenarios[i].Descriptor);
                }
                var discoveryResult = new PreviewCatalogResult
                {
                    ProtocolVersion = discovery.ProtocolVersion,
                    Kind = PreviewProtocol.CatalogResultKind,
                    SessionId = discovery.SessionId,
                    Generation = discovery.Generation,
                    RequestId = discovery.RequestId,
                    ProjectTargetDigest = discovery.ProjectTargetDigest,
                    InputDigest = discovery.InputDigest,
                    ArtifactDigest = discovery.ArtifactDigest,
                    Scenarios = entries,
                };
                await PublishMetadataAsync(
                        discoveryOutput,
                        "catalog.json",
                        PreviewProtocol.WriteCatalogResult(discoveryResult),
                        cancellation.Token
                    )
                    .ConfigureAwait(false);
                return 0;
            }
            var request = PreviewProtocol.ReadRequest(requestBytes);
            var output = EmptyOutput(request.OutputDirectory);
            var scenario = catalog.Get(request.ScenarioId);
            var defaults = scenario.Descriptor.Presentation;
            var appearance = new ThemeAppearance(
                request.ColorScheme == "light" ? ThemeColorScheme.Light : ThemeColorScheme.Dark,
                request.Contrast == "normal" ? ThemeContrast.Normal : ThemeContrast.High
            );
            var presentation = new PreviewPresentation(
                new LayoutViewport(
                    (float)request.LogicalWidth,
                    (float)request.LogicalHeight,
                    (float)request.Scale
                ),
                appearance,
                _ => defaults.ThemeFactory(appearance),
                (float)request.Density,
                defaults.Culture,
                defaults.UICulture,
                defaults.InitialTime
            );
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
                        builder =>
                            binding = scenario.Bind(
                                builder,
                                clock,
                                presentation,
                                cancellation.Token
                            ),
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
                Kind = PreviewProtocol.FrameResultKind,
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
                ColorScheme = request.ColorScheme,
                Contrast = request.Contrast,
                Density = presentation.Density,
                Culture = presentation.Culture.Name,
                UICulture = presentation.UICulture.Name,
                InitialTime = presentation
                    .InitialTime.ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture),
            };
            var resultBytes = PreviewProtocol.WriteResult(result);
            stage = "publication";
            _ = LocalPath(output, directory: true);
            await WriteNewAsync(Path.Combine(output, "frame.png"), png, cancellation.Token)
                .ConfigureAwait(false);
            await PublishMetadataAsync(output, "result.json", resultBytes, cancellation.Token)
                .ConfigureAwait(false);
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

    private static string EmptyOutput(string path)
    {
        var output = LocalPath(path, directory: true);
        if (Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidDataException("Worker output must be empty.");
        return output;
    }

    private static PreviewCatalogEntry Describe(PreviewScenarioDescriptor descriptor)
    {
        var presentation = descriptor.Presentation;
        var entry = new PreviewCatalogEntry
        {
            Id = descriptor.Id,
            Title = descriptor.Title,
            SourceProject = descriptor.Source.Project,
            SourceDocument = descriptor.Source.Document,
            SourceComponent = descriptor.Source.Component,
            LogicalWidth = presentation.Viewport.Width,
            LogicalHeight = presentation.Viewport.Height,
            Scale = presentation.Viewport.Scale,
            ColorScheme =
                presentation.Appearance.ColorScheme == ThemeColorScheme.Light ? "light" : "dark",
            Contrast = presentation.Appearance.Contrast == ThemeContrast.Normal ? "normal" : "high",
            Density = presentation.Density,
            Culture = presentation.Culture.Name,
            UICulture = presentation.UICulture.Name,
            InitialTime = presentation
                .InitialTime.ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
        };
        PreviewProtocol.Validate(entry);
        return entry;
    }

    private static async Task PublishMetadataAsync(
        string output,
        string name,
        byte[] bytes,
        CancellationToken token
    )
    {
        _ = LocalPath(output, directory: true);
        var temporary = Path.Combine(output, "result.tmp");
        await WriteNewAsync(temporary, bytes, token).ConfigureAwait(false);
        _ = LocalPath(output, directory: true);
        token.ThrowIfCancellationRequested();
        File.Move(temporary, Path.Combine(output, name), overwrite: false);
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
