using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Threading.Channels;
using Lucent.Compiler;

namespace Lucent.LanguageServer;

/// <summary>
/// Minimal LSP server for the Lucent compiler frontend.
/// </summary>
public static class LanguageServer
{
    public static Task<int> RunAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        return new ServerSession(input, output).RunAsync(cancellationToken);
    }

    internal static Task<int> RunAsync(
        Stream input,
        Stream output,
        Action<LanguageServerRequestMetric> observe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observe);
        return new ServerSession(input, output, observe).RunAsync(cancellationToken);
    }

    internal static Task<int> RunAsync(
        Stream input,
        Stream output,
        Action<LanguageServerRequestMetric> observe,
        Func<string, CancellationToken, Task> beforeRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observe);
        ArgumentNullException.ThrowIfNull(beforeRequest);
        return new ServerSession(input, output, observe, beforeRequest)
            .RunAsync(cancellationToken);
    }

    private sealed class ServerSession(
        Stream input,
        Stream output,
        Action<LanguageServerRequestMetric>? observe = null,
        Func<string, CancellationToken, Task>? beforeRequest = null)
    {
        private const int MaxProjectAnalyses = 8;
        private readonly JsonRpcConnection _connection = new(input, output);
        private readonly Dictionary<string, DocumentState> _documents =
            new(StringComparer.Ordinal);
        private readonly ProjectContextLoader _projectContexts = new();
        private readonly Dictionary<string, ProjectAnalysis> _projectAnalyses =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _requestCancellationGate = new();
        private readonly Dictionary<string, CancellationTokenSource> _requestCancellations =
            new(StringComparer.Ordinal);

        private bool _shutdownRequested;

        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var messages = Channel.CreateUnbounded<InboundMessage>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            var reader = ReadMessagesAsync(messages.Writer, sessionCancellation.Token);

            try
            {
                await foreach (var inbound in messages.Reader.ReadAllAsync(cancellationToken))
                {
                    if (inbound.TransportException is not null)
                    {
                        LanguageServerLog.TransportFailed(
                            LanguageServerLog.Logger,
                            inbound.TransportException);
                        return 1;
                    }

                    if (inbound.EndOfStream)
                    {
                        return _shutdownRequested ? 0 : 1;
                    }

                    using var message = inbound.Message!;
                    using var requestCancellation = inbound.RequestCancellation;
                    // The benchmark drives this serial server in isolation. Per-thread
                    // counters are invalid across awaited continuations, so measure the
                    // process-wide monotonic counter only when a benchmark observer asks.
                    var allocated = observe is null ? 0 : GC.GetTotalAllocatedBytes(precise: true);
                    var started = Stopwatch.GetTimestamp();
                    int? result;
                    try
                    {
                        result = await HandleMessageAsync(
                            message.RootElement,
                            requestCancellation?.Token ?? cancellationToken);
                    }
                    catch (OperationCanceledException) when (
                        requestCancellation?.IsCancellationRequested == true &&
                        !cancellationToken.IsCancellationRequested)
                    {
                        result = null;
                        if (message.RootElement.TryGetProperty("id", out var cancelledId))
                        {
                            await _connection.WriteErrorAsync(
                                cancelledId,
                                -32800,
                                "Request cancelled.",
                                cancellationToken);
                        }
                    }
                    finally
                    {
                        if (inbound.RequestKey is not null)
                        {
                            lock (_requestCancellationGate)
                            {
                                _requestCancellations.Remove(inbound.RequestKey);
                            }
                        }

                        if (observe is not null)
                        {
                            var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
                            observe(new LanguageServerRequestMetric(
                                message.RootElement.TryGetProperty("method", out var method) &&
                                method.ValueKind == JsonValueKind.String ? method.GetString()! : "<invalid>",
                                Stopwatch.GetElapsedTime(started),
                                allocatedAfter - allocated,
                                _projectAnalyses.Count));
                        }
                    }

                    if (result.HasValue)
                    {
                        return result.Value;
                    }
                }

                return _shutdownRequested ? 0 : 1;
            }
            finally
            {
                sessionCancellation.Cancel();
                try
                {
                    await reader;
                }
                catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
                {
                }

                while (messages.Reader.TryRead(out var pending))
                {
                    pending.Message?.Dispose();
                    pending.RequestCancellation?.Dispose();
                }

                lock (_requestCancellationGate)
                {
                    _requestCancellations.Clear();
                }
            }
        }

        private async Task ReadMessagesAsync(
            ChannelWriter<InboundMessage> writer,
            CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    JsonDocument? message;
                    try
                    {
                        message = await _connection.ReadAsync(cancellationToken);
                    }
                    catch (JsonException exception)
                    {
                        LanguageServerLog.MalformedPayload(
                            LanguageServerLog.Logger,
                            exception);
                        // A malformed message cannot be associated with a request ID.
                        // Keep the stream alive so a client can recover with a later
                        // well-formed message.
                        continue;
                    }
                    catch (Exception exception) when (
                        exception is InvalidDataException or EndOfStreamException)
                    {
                        await writer.WriteAsync(
                            new InboundMessage(null, null, null, exception, false),
                            cancellationToken);
                        return;
                    }

                    if (message is null)
                    {
                        await writer.WriteAsync(
                            new InboundMessage(null, null, null, null, true),
                            cancellationToken);
                        return;
                    }

                    if (IsCancellationNotification(message.RootElement, out var cancelledKey))
                    {
                        lock (_requestCancellationGate)
                        {
                            if (_requestCancellations.TryGetValue(cancelledKey, out var request))
                            {
                                request.Cancel();
                            }
                        }

                        message.Dispose();
                        continue;
                    }

                    CancellationTokenSource? requestCancellation = null;
                    string? requestKey = null;
                    if (message.RootElement.TryGetProperty("id", out var id))
                    {
                        requestKey = id.GetRawText();
                        requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);
                        lock (_requestCancellationGate)
                        {
                            _requestCancellations[requestKey] = requestCancellation;
                        }
                    }

                    await writer.WriteAsync(
                        new InboundMessage(
                            message,
                            requestCancellation,
                            requestKey,
                            null,
                            false),
                        cancellationToken);
                }
            }
            finally
            {
                writer.TryComplete();
            }
        }

        private static bool IsCancellationNotification(
            JsonElement message,
            out string requestKey)
        {
            requestKey = string.Empty;
            if (!message.TryGetProperty("method", out var method) ||
                method.ValueKind != JsonValueKind.String ||
                method.GetString() != "$/cancelRequest" ||
                !message.TryGetProperty("params", out var parameters) ||
                !parameters.TryGetProperty("id", out var id))
            {
                return false;
            }

            requestKey = id.GetRawText();
            return true;
        }

        private async Task<int?> HandleMessageAsync(
            JsonElement message,
            CancellationToken cancellationToken)
        {
            if (!message.TryGetProperty("method", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String)
            {
                if (message.TryGetProperty("id", out var invalidId))
                {
                    await _connection.WriteErrorAsync(
                        invalidId,
                        -32600,
                        "The JSON-RPC message must contain a method.",
                        cancellationToken);
                }

                return null;
            }

            var method = methodElement.GetString()!;
            var hasId = message.TryGetProperty("id", out var idElement);
            var id = hasId ? idElement.Clone() : (JsonElement?)null;
            message.TryGetProperty("params", out var parameters);

            try
            {
                if (beforeRequest is not null)
                {
                    await beforeRequest(method, cancellationToken);
                }

                switch (method)
                {
                    case "initialize":
                        _projectContexts.Configure(parameters);
                        if (hasId)
                        {
                            await _connection.WriteResponseAsync(
                                id!.Value,
                                new
                                {
                                    capabilities = new
                                    {
                                        textDocumentSync = new
                                        {
                                            openClose = true,
                                            change = 1,
                                        },
                                        hoverProvider = true,
                                        definitionProvider = true,
                                        documentSymbolProvider = true,
                                        documentFormattingProvider = true,
                                        referencesProvider = true,
                                        renameProvider = new { prepareProvider = false },
                                        completionProvider = new
                                        {
                                            resolveProvider = false,
                                            triggerCharacters = new[] { ":", ".", "(", "," },
                                        },
                                        positionEncoding = "utf-16",
                                    },
                                    serverInfo = new
                                    {
                                        name = "Lucent Language Server",
                                        version = "0.1.0",
                                    },
                                },
                                cancellationToken);
                        }

                        break;

                    case "initialized":
                        break;

                    case "shutdown":
                        _shutdownRequested = true;
                        if (hasId)
                        {
                            await _connection.WriteResponseAsync(
                                id!.Value,
                                result: null,
                                cancellationToken);
                        }

                        break;

                    case "exit":
                        return _shutdownRequested ? 0 : 1;

                    case "textDocument/didOpen":
                        await DidOpenAsync(parameters, cancellationToken);
                        break;

                    case "textDocument/didChange":
                        await DidChangeAsync(parameters, cancellationToken);
                        break;

                    case "textDocument/didClose":
                        await DidCloseAsync(parameters, cancellationToken);
                        break;

                    case "workspace/didChangeWatchedFiles":
                        await DidChangeWatchedFilesAsync(parameters, cancellationToken);
                        break;

                    case "textDocument/hover":
                        if (hasId)
                        {
                            await HoverAsync(
                                id!.Value,
                                parameters,
                                cancellationToken);
                        }

                        break;

                    case "textDocument/definition":
                        if (hasId)
                        {
                            await DefinitionAsync(
                                id!.Value,
                                parameters,
                                cancellationToken);
                        }

                        break;

                    case "textDocument/completion":
                        if (hasId)
                        {
                            await CompletionAsync(
                                id!.Value,
                                parameters,
                                cancellationToken);
                        }

                        break;

                    case "textDocument/references":
                        if (hasId)
                        {
                            await ReferencesAsync(id!.Value, parameters, cancellationToken);
                        }
                        break;

                    case "textDocument/rename":
                        if (hasId)
                        {
                            await RenameAsync(id!.Value, parameters, cancellationToken);
                        }
                        break;

                    case "textDocument/documentSymbol":
                        if (hasId)
                        {
                            await DocumentSymbolsAsync(id!.Value, parameters, cancellationToken);
                        }
                        break;

                    case "textDocument/formatting":
                        if (hasId)
                        {
                            await FormattingAsync(id!.Value, parameters, cancellationToken);
                        }
                        break;

                    case "lucent/sourceMap":
                        if (hasId)
                        {
                            await SourceMapAsync(id!.Value, parameters, cancellationToken);
                        }
                        break;

                    default:
                        if (hasId)
                        {
                            await _connection.WriteErrorAsync(
                                id!.Value,
                                -32601,
                                $"Method '{method}' is not supported.",
                                cancellationToken);
                        }

                        break;
                }
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException)
            {
                if (hasId)
                {
                    await _connection.WriteErrorAsync(
                        id!.Value,
                        -32602,
                        "The request parameters were invalid.",
                        cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LanguageServerLog.RequestFailed(
                    LanguageServerLog.Logger,
                    method,
                    exception);
                if (hasId)
                {
                    await _connection.WriteErrorAsync(
                        id!.Value,
                        -32603,
                        "An internal error occurred while processing the request.",
                        cancellationToken);
                }
            }

            return null;
        }

        private async Task DidOpenAsync(
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var textDocument = parameters.GetProperty("textDocument");
            var uri = textDocument.GetProperty("uri").GetString()
                ?? throw new InvalidOperationException("A document URI is required.");
            var text = textDocument.GetProperty("text").GetString() ?? string.Empty;
            var version = textDocument.TryGetProperty("version", out var versionElement)
                ? versionElement.GetInt32()
                : (int?)null;

            var analysis = await AnalyzeAsync(uri, text, cancellationToken);
            _documents[uri] = new DocumentState(text, version, analysis, null);
            if (uri.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                await RefreshOpenLucentAnalysesAsync(cancellationToken);
            await RefreshCssIndexesAsync(cancellationToken);
            await PublishAllDiagnosticsAsync(cancellationToken);
        }

        private async Task DidChangeAsync(
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var textDocument = parameters.GetProperty("textDocument");
            var uri = textDocument.GetProperty("uri").GetString()
                ?? throw new InvalidOperationException("A document URI is required.");
            var current = _documents.TryGetValue(uri, out var document)
                ? document.Text
                : string.Empty;

            foreach (var change in parameters.GetProperty("contentChanges").EnumerateArray())
            {
                var replacement = change.GetProperty("text").GetString() ?? string.Empty;
                if (!change.TryGetProperty("range", out var range) ||
                    range.ValueKind == JsonValueKind.Null)
                {
                    current = replacement;
                    continue;
                }

                var start = GetOffset(current, range.GetProperty("start"));
                var end = GetOffset(current, range.GetProperty("end"));
                if (end < start)
                {
                    throw new InvalidOperationException(
                        "A text change range must end after it starts.");
                }

                current = string.Concat(
                    current.AsSpan(0, start),
                    replacement,
                    current.AsSpan(end));
            }

            var version = textDocument.TryGetProperty("version", out var versionElement)
                ? versionElement.GetInt32()
                : document?.Version;
            var analysis = await AnalyzeAsync(uri, current, cancellationToken);
            _documents[uri] = new DocumentState(current, version, analysis, document?.CssTokens);
            if (uri.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                await RefreshOpenLucentAnalysesAsync(cancellationToken);
            await RefreshCssIndexesAsync(cancellationToken);
            await PublishAllDiagnosticsAsync(cancellationToken);
        }

        private async Task DidCloseAsync(
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var uri = parameters
                .GetProperty("textDocument")
                .GetProperty("uri")
                .GetString()
                ?? throw new InvalidOperationException("A document URI is required.");

            _documents.Remove(uri);
            await RefreshCssIndexesAsync(cancellationToken);
            await _connection.WriteNotificationAsync(
                "textDocument/publishDiagnostics",
                new
                {
                    uri,
                    diagnostics = Array.Empty<object>(),
                },
                cancellationToken);
            foreach (var (remainingUri, remaining) in _documents.ToArray())
            {
                var analysis = await AnalyzeAsync(remainingUri, remaining.Text, cancellationToken);
                _documents[remainingUri] = remaining with { Analysis = analysis };
            }
            await PublishAllDiagnosticsAsync(cancellationToken);
        }

        private async Task DidChangeWatchedFilesAsync(
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            if (!parameters.TryGetProperty("changes", out var changes) ||
                changes.ValueKind != JsonValueKind.Array ||
                !changes.EnumerateArray().Any(change =>
                    change.TryGetProperty("uri", out var uri) &&
                    uri.ValueKind == JsonValueKind.String &&
                    IsSemanticProjectFile(uri.GetString())))
            {
                return;
            }

            // Watched-file notifications are the project-generation boundary.
            // Re-evaluate off the completion path, then publish one coherent new
            // snapshot to every open document in the affected workspace.
            _projectAnalyses.Clear();
            foreach (var (uri, document) in _documents.ToArray())
            {
                if (uri.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    continue;

                var analysis = await AnalyzeAsync(uri, document.Text, cancellationToken);
                _documents[uri] = document with { Analysis = analysis };
            }

            await RefreshCssIndexesAsync(cancellationToken);
            await PublishAllDiagnosticsAsync(cancellationToken);
        }

        private static bool IsSemanticProjectFile(string? uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return false;

            var path = GetSourcePath(uri);
            return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
        }

        private async Task PublishAllDiagnosticsAsync(CancellationToken cancellationToken)
        {
            foreach (var document in _documents)
            {
                var project = _projectAnalyses.Values.FirstOrDefault(analysis => PathsEqual(
                    analysis.DiagnosticSourcePath, GetSourcePath(document.Key)));
                await PublishDiagnosticsAsync(document.Key, document.Value.Text,
                    document.Value.Analysis, project?.ManifestDiagnostics ?? [], cancellationToken);
            }
        }

        private async Task PublishDiagnosticsAsync(
            string uri,
            string text,
            CompilationResult result,
            IReadOnlyList<string> manifestDiagnostics,
            CancellationToken cancellationToken)
        {
            var diagnostics = result.Diagnostics
                .Select(diagnostic => new PublishedDiagnostic(
                    ToRange(text, diagnostic.Span),
                    diagnostic.Severity == LucentDiagnosticSeverity.Error ? 1 : 2,
                    diagnostic.Code,
                    diagnostic.Message,
                    "lucent"))
                .ToList();
            diagnostics.AddRange(manifestDiagnostics.Select(message => new PublishedDiagnostic(
                ToRange(text, new SourceSpan(0, 0)), 2, "LUC9007", message, "lucent")));

            await _connection.WriteNotificationAsync(
                "textDocument/publishDiagnostics",
                new
                {
                    uri,
                    diagnostics,
                },
                cancellationToken);
        }

        private async Task<CompilationResult> AnalyzeAsync(
            string uri,
            string text,
            CancellationToken cancellationToken)
        {
            var sourcePath = GetSourcePath(uri);
            if (sourcePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                return new CompilationResult(null, null, []);
            }
            var projectContext = await _projectContexts.LoadAsync(
                sourcePath,
                cancellationToken);
            var inputs = new Dictionary<string, LucentSourceInput>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var path in projectContext?.LucentSources ?? [])
            {
                if (File.Exists(path))
                {
                    inputs[path] = new LucentSourceInput(path,
                        await File.ReadAllTextAsync(path, cancellationToken));
                }
            }
            foreach (var open in _documents)
            {
                var path = GetSourcePath(open.Key);
                if (!IsSourceInProject(path, sourcePath, projectContext))
                {
                    continue;
                }
                inputs[path] = new LucentSourceInput(path, open.Value.Text);
            }
            inputs[sourcePath] = new LucentSourceInput(sourcePath, text);
            foreach (var path in inputs.Keys.ToArray())
            {
                var cssPath = Path.ChangeExtension(path, ".css");
                string? styleText = null;
                if (_documents.TryGetValue(new Uri(cssPath).AbsoluteUri, out var openStyle))
                    styleText = openStyle.Text;
                else if (File.Exists(cssPath))
                    styleText = await File.ReadAllTextAsync(cssPath, cancellationToken);
                if (styleText is not null)
                    inputs[path] = inputs[path] with { StylePath = cssPath, StyleText = styleText };
            }
            var projectKey = projectContext?.ProjectPath ?? sourcePath;
            var generation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join("\n", inputs.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => item.Key + "\0" +
                        (_documents.Where(document => PathsEqual(GetSourcePath(document.Key), item.Key))
                            .Select(document => document.Value.Version).FirstOrDefault()?.ToString() ?? "0") +
                        "\0" + item.Value.SourceText + "\0" + item.Value.StyleText)))));
            if (!_projectAnalyses.TryGetValue(projectKey, out var cached) ||
                !string.Equals(cached.Generation, generation, StringComparison.Ordinal))
            {
                // Source-only generations reuse the immutable reference snapshot.
                // Watched project/reference changes clear _projectAnalyses first.
                var manifests = cached?.ReferencedManifests ??
                    LucentCompiler.LoadActiveThemeManifestSnapshot(projectContext, cancellationToken);
                var sourceTexts = inputs.ToDictionary(item => item.Key, item => item.Value.SourceText,
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (var input in inputs.Values.Where(input => input.StylePath is not null && input.StyleText is not null))
                    sourceTexts[input.StylePath!] = input.StyleText!;
                foreach (var path in projectContext?.Sources ?? [])
                    if (File.Exists(path)) sourceTexts[path] = File.ReadAllText(path);
                var localDiagnostics = new List<string>();
                if (projectContext is not null &&
                    !LucentModuleManifest.TryReadLocalSnapshot(projectContext, sourceTexts, cancellationToken,
                        out _, out var localError) && !string.IsNullOrWhiteSpace(localError))
                    localDiagnostics.Add(localError);
                var previous = cached?.Results.FirstOrDefault(source =>
                    PathsEqual(source.SourcePath, sourcePath));
                if (!string.Equals(Environment.GetEnvironmentVariable("LUCENT_DISABLE_INCREMENTAL_REBIND"), "1", StringComparison.Ordinal) &&
                    previous is not null && LucentCompiler.TryRecompileUnchangedComponentSignatures(
                        text, sourcePath, previous.Result, projectContext, out var updated))
                {
                    cached = new ProjectAnalysis(generation, cached!.Results
                        .Select(source => PathsEqual(source.SourcePath, sourcePath)
                            ? new LucentSourceCompilation(sourcePath, updated)
                            : source)
                        .ToArray(), sourcePath,
                        manifests,
                        manifests.Diagnostics.Concat(localDiagnostics).ToArray(),
                        sourceTexts);
                }
                else
                {
                    var projectResult = LucentCompiler.CompileProject(inputs.Values.ToArray(), projectContext);
                    cached = new ProjectAnalysis(generation, projectResult.Sources.ToArray(), sourcePath,
                        manifests, manifests.Diagnostics.Concat(localDiagnostics).ToArray(), sourceTexts);
                }
                _projectAnalyses[projectKey] = cached;
                while (_projectAnalyses.Count > MaxProjectAnalyses)
                    _projectAnalyses.Remove(_projectAnalyses.Keys.First());
            }
            foreach (var source in cached.Results)
            {
                var open = _documents.FirstOrDefault(document => PathsEqual(
                    GetSourcePath(document.Key), source.SourcePath));
                if (open.Key is not null)
                    _documents[open.Key] = open.Value with { Analysis = source.Result };
            }
            var result = cached.Results.First(source => PathsEqual(source.SourcePath, sourcePath)).Result;
            LanguageServerLog.DocumentAnalyzed(
                LanguageServerLog.Logger,
                sourcePath,
                projectContext?.ProjectPath,
                projectContext?.Sources.Count ?? 0,
                projectContext?.References.Count ?? 0,
                string.Join(',', result.Diagnostics.Select(item => item.Code)));
            return result;
        }

        private static bool IsSourceInProject(
            string path,
            string currentSourcePath,
            LucentProjectContext? projectContext)
        {
            if (PathsEqual(path, currentSourcePath))
            {
                return true;
            }

            return projectContext?.LucentSources.Any(source => PathsEqual(source, path)) == true;
        }

        private static IReadOnlyDictionary<string, string> WithSourceText(
            IReadOnlyDictionary<string, string> sourceTexts, string sourcePath, string text)
        {
            var result = new Dictionary<string, string>(sourceTexts,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            {
                [sourcePath] = text,
            };
            return result;
        }

        private static bool PathsEqual(string left, string right) =>
            string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);

        private async Task HoverAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var uri = parameters
                .GetProperty("textDocument")
                .GetProperty("uri")
                .GetString();
            if (uri is null || !_documents.TryGetValue(uri, out var document))
            {
                await _connection.WriteResponseAsync(id, null, cancellationToken);
                return;
            }

            var offset = GetOffset(document.Text, parameters.GetProperty("position"));
            var symbol = document.Analysis.Symbols
                .Where(candidate =>
                    offset >= candidate.ReferenceSpan.Start &&
                    offset <= candidate.ReferenceSpan.End)
                .OrderBy(candidate => candidate.ReferenceSpan.Length)
                .FirstOrDefault();
            if (symbol is null)
            {
                symbol = LucentCompiler.GetExpressionSymbol(document.Text, offset,
                    document.Analysis);
            }

            if (symbol is null)
            {
                await _connection.WriteResponseAsync(id, null, cancellationToken);
                return;
            }

            var documentation = string.IsNullOrWhiteSpace(symbol.Documentation)
                ? string.Empty
                : $"\n\n{symbol.Documentation}";
            await _connection.WriteResponseAsync(
                id,
                new
                {
                    contents = new
                    {
                        kind = "markdown",
                        value = $"```csharp\n{symbol.Display}\n```{documentation}",
                    },
                    range = ToRange(document.Text, symbol.ReferenceSpan),
                },
                cancellationToken);
        }

        private async Task CompletionAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var uri = parameters
                .GetProperty("textDocument")
                .GetProperty("uri")
                .GetString()
                ?? throw new InvalidOperationException("A document URI is required.");
            if (!_documents.TryGetValue(uri, out var document))
            {
                await _connection.WriteResponseAsync(id, Array.Empty<object>(), cancellationToken);
                return;
            }

            var offset = GetOffset(document.Text, parameters.GetProperty("position"));
            var completions = (uri.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                    ? LucentCompiler.GetCssCompletions(document.Text, offset, GetSourcePath(uri),
                        document.CssTokens ?? CssProjectTokenIndex.Create(
                            [new CssProjectDocument(GetSourcePath(uri), document.Text)]))
                    : document.CssTokens is { } cssTokens
                        ? LucentCompiler.GetCompletions(document.Text, offset, document.Analysis, cssTokens)
                        : LucentCompiler.GetCompletions(document.Text, offset, document.Analysis))
                .GroupBy(item => item.Label, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.SortText ?? item.Label, StringComparer.Ordinal)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .ToArray();
            await _connection.WriteResponseAsync(
                id,
                completions.Select(item => new
                {
                    label = item.Label,
                    kind = item.Kind switch
                    {
                        LucentCompletionItemKind.Property => 10,
                        LucentCompletionItemKind.Event => 23,
                        LucentCompletionItemKind.Value => 12,
                        LucentCompletionItemKind.Keyword => 14,
                        LucentCompletionItemKind.Variable => 6,
                        LucentCompletionItemKind.Field => 5,
                        LucentCompletionItemKind.Method => 2,
                        LucentCompletionItemKind.Type => 7,
                        _ => 1,
                    },
                    detail = item.Detail,
                    documentation = string.IsNullOrWhiteSpace(item.Documentation)
                        ? null
                        : new { kind = "markdown", value = item.Documentation },
                    insertText = item.InsertText,
                    insertTextFormat = item.IsSnippet ? 2 : 1,
                    sortText = item.SortText ?? item.Label,
                    filterText = item.FilterText ?? item.Label,
                    tags = item.IsDeprecated ? new[] { 1 } : null,
                    textEdit = item.ReplacementSpan is { } span ? new { range = ToRange(document.Text, span), newText = item.InsertText } : null,
                }).ToArray(),
                cancellationToken);
        }

        private async Task DocumentSymbolsAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString();
            if (uri is null || !_documents.TryGetValue(uri, out var document))
            {
                await _connection.WriteResponseAsync(id, Array.Empty<object>(), cancellationToken);
                return;
            }

            var symbols = document.Analysis.Symbols.Where(symbol => symbol.Kind is
                    LucentSemanticSymbolKind.Component or
                    LucentSemanticSymbolKind.ComponentParameter or
                    LucentSemanticSymbolKind.ComponentSlot)
                .Select(symbol => new
                {
                    name = symbol.Name,
                    detail = symbol.Display,
                    kind = symbol.Kind switch
                    {
                        LucentSemanticSymbolKind.Component => 5,
                        LucentSemanticSymbolKind.ComponentParameter => 13,
                        _ => 8,
                    },
                    range = ToRange(document.Text, symbol.ReferenceSpan),
                    selectionRange = ToRange(document.Text, symbol.ReferenceSpan),
                }).ToArray();
            await _connection.WriteResponseAsync(id, symbols, cancellationToken);
        }

        private async Task FormattingAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString();
            if (uri is null || !_documents.TryGetValue(uri, out var document))
            {
                await _connection.WriteResponseAsync(id, Array.Empty<object>(), cancellationToken);
                return;
            }

            var formatted = LucentCompiler.Format(document.Text);
            if (string.Equals(formatted, document.Text, StringComparison.Ordinal))
            {
                await _connection.WriteResponseAsync(id, Array.Empty<object>(), cancellationToken);
                return;
            }

            await _connection.WriteResponseAsync(id, new[]
            {
                new
                {
                    range = new LspRange(new LspPosition(0, 0), ToPosition(document.Text, document.Text.Length)),
                    newText = formatted,
                },
            }, cancellationToken);
        }

        private async Task SourceMapAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString();
            if (uri is null || !_documents.TryGetValue(uri, out var document) ||
                document.Analysis.SourceMap is not { } map ||
                !parameters.TryGetProperty("generatedHash", out var hash) ||
                !string.Equals(hash.GetString(), map.GeneratedHash, StringComparison.Ordinal) ||
                !parameters.TryGetProperty("lucentHash", out var lucentHash) ||
                !string.Equals(lucentHash.GetString(), map.LucentHash, StringComparison.Ordinal) ||
                !parameters.TryGetProperty("version", out var version) ||
                !version.TryGetInt32(out var requestedVersion) ||
                requestedVersion != document.Version)
            {
                await _connection.WriteResponseAsync(id, null, cancellationToken);
                return;
            }

            await _connection.WriteResponseAsync(id, new
            {
                lucentHash = map.LucentHash,
                generatedHash = map.GeneratedHash,
                version = document.Version,
                generatedToLucent = map.Entries.Select(entry => new
                {
                    generatedUri = entry.GeneratedUri,
                    generatedRange = ToProtocolRange(entry.GeneratedRange),
                    lucentUri = entry.LucentUri,
                    lucentRange = ToProtocolRange(entry.LucentRange),
                }),
                lucentToGenerated = map.Entries.GroupBy(entry => new
                    { entry.LucentUri, entry.LucentRange })
                    .OrderBy(group => group.Key.LucentUri, StringComparer.Ordinal)
                    .ThenBy(group => group.Key.LucentRange.StartLine)
                    .ThenBy(group => group.Key.LucentRange.StartCharacter)
                    .Select(group => new
                    {
                        lucentUri = group.Key.LucentUri,
                        lucentRange = ToProtocolRange(group.Key.LucentRange),
                        generated = group.OrderBy(entry => entry.GeneratedUri, StringComparer.Ordinal)
                            .ThenBy(entry => entry.GeneratedRange.StartLine)
                            .ThenBy(entry => entry.GeneratedRange.StartCharacter)
                            .Select(entry => new
                            {
                                generatedUri = entry.GeneratedUri,
                                generatedRange = ToProtocolRange(entry.GeneratedRange),
                            }),
                    }),
            }, cancellationToken);
        }

        private static LspRange ToProtocolRange(LucentSourceMapRange range) => new(
            new LspPosition(range.StartLine, range.StartCharacter),
            new LspPosition(range.EndLine, range.EndCharacter));

        private async Task DefinitionAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var requestUri = parameters.GetProperty("textDocument").GetProperty("uri").GetString();
            if (requestUri is not null && requestUri.EndsWith(".css", StringComparison.OrdinalIgnoreCase) &&
                _documents.TryGetValue(requestUri, out var cssDocument) &&
                FileUri.TryGetPath(requestUri, out var cssPath) &&
                LucentCompiler.GetCssDefinition(cssDocument.Text,
                    GetOffset(cssDocument.Text, parameters.GetProperty("position")), cssPath,
                    cssDocument.CssTokens) is { } css)
            {
                var definitionPath = string.IsNullOrWhiteSpace(css.SourcePath) ? cssPath : css.SourcePath;
                var cssDefinitionUri = new Uri(definitionPath).AbsoluteUri;
                var cssDefinitionText = cssDocument.CssTokens?.TryGetDocumentText(
                    definitionPath, out var indexedText) == true
                    ? indexedText
                    : _documents.TryGetValue(cssDefinitionUri, out var openCss)
                        ? openCss.Text
                        : string.Empty;
                await _connection.WriteResponseAsync(id, new
                {
                    uri = cssDefinitionUri,
                    range = ToRange(cssDefinitionText, css.Span),
                }, cancellationToken);
                return;
            }
            var (document, symbol) = FindSymbol(parameters);
            if (document is not null && symbol is null)
            {
                var uri = parameters
                    .GetProperty("textDocument")
                    .GetProperty("uri")
                    .GetString()!;
                symbol = LucentCompiler.GetExpressionSymbol(
                    document.Text,
                    GetOffset(document.Text, parameters.GetProperty("position")),
                    document.Analysis);
            }

            if (symbol?.Definition is not { } definition)
            {
                await _connection.WriteResponseAsync(id, null, cancellationToken);
                return;
            }

            var definitionUri = new Uri(definition.SourcePath).AbsoluteUri;
            var project = _projectAnalyses.Values.FirstOrDefault(analysis => analysis.SourceTexts.ContainsKey(definition.SourcePath));
            var definitionText = _documents.TryGetValue(definitionUri, out var openDocument)
                ? openDocument.Text
                : project?.SourceTexts.GetValueOrDefault(definition.SourcePath);
            if (definitionText is null)
            {
                await _connection.WriteResponseAsync(id, null, cancellationToken);
                return;
            }
            await _connection.WriteResponseAsync(
                id,
                new
                {
                    uri = definitionUri,
                    range = ToRange(definitionText, definition.Span),
                },
                cancellationToken);
        }

        private async Task<CssProjectTokenIndex> BuildCssTokenIndexAsync(
            string cssPath,
            CancellationToken cancellationToken)
        {
            var context = await _projectContexts.LoadAsync(cssPath, cancellationToken);
            var paths = new HashSet<string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            {
                Path.GetFullPath(cssPath),
            };
            if (cssPath.EndsWith(".lui", StringComparison.OrdinalIgnoreCase))
                paths.Add(Path.ChangeExtension(Path.GetFullPath(cssPath), ".css"));
            foreach (var lui in context?.LucentSources ?? [])
            {
                paths.Add(Path.GetFullPath(lui));
                paths.Add(Path.ChangeExtension(Path.GetFullPath(lui), ".css"));
            }

            var documents = new List<CssProjectDocument>();
            foreach (var path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var uri = new Uri(path).AbsoluteUri;
                if (_documents.TryGetValue(uri, out var open))
                {
                    documents.Add(new CssProjectDocument(path, open.Text));
                }
                else if (File.Exists(path))
                {
                    documents.Add(new CssProjectDocument(path,
                        await File.ReadAllTextAsync(path, cancellationToken)));
                }
            }
            // Manifest I/O belongs to this generation/index construction, never CompletionAsync.
            var manifests = context?.ProjectPath is { } projectPath &&
                _projectAnalyses.TryGetValue(projectPath, out var analysis)
                    ? analysis.ReferencedManifests
                    : LucentCompiler.LoadActiveThemeManifestSnapshot(context, cancellationToken);
            return CssProjectTokenIndex.Create(documents, manifests.Catalog.Entries);
        }

        private async Task RefreshCssIndexesAsync(CancellationToken cancellationToken)
        {
            foreach (var (uri, document) in _documents.ToArray())
            {
                var index = await BuildCssTokenIndexAsync(GetSourcePath(uri), cancellationToken);
                _documents[uri] = document with { CssTokens = index };
            }
        }

        private async Task RefreshOpenLucentAnalysesAsync(CancellationToken cancellationToken)
        {
            foreach (var (uri, document) in _documents.ToArray())
            {
                if (uri.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    continue;
                var analysis = await AnalyzeAsync(uri, document.Text, cancellationToken);
                _documents[uri] = document with { Analysis = analysis };
            }
        }

        private async Task ReferencesAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var references = await FindComponentReferencesAsync(parameters, cancellationToken);
            await _connection.WriteResponseAsync(id, references?.Select(reference => new
            {
                uri = new Uri(reference.SourcePath).AbsoluteUri,
                range = ToRange(reference.Text, reference.Symbol.ReferenceSpan),
            }).ToArray(), cancellationToken);
        }

        private async Task RenameAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var newName = parameters.TryGetProperty("newName", out var name) ? name.GetString() : null;
            if (string.IsNullOrWhiteSpace(newName) ||
                !System.Text.RegularExpressions.Regex.IsMatch(newName, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                await _connection.WriteResponseAsync(id, null, cancellationToken);
                return;
            }

            var references = await FindComponentReferencesAsync(parameters, cancellationToken);
            var project = references is null ? null : _projectAnalyses.Values.FirstOrDefault(analysis =>
                analysis.Results.Any(result => PathsEqual(result.SourcePath, references[0].SourcePath)));
            if (references is null || project is null || project.Results.SelectMany(result => result.Result.Symbols)
                    .Any(symbol => symbol.Kind == LucentSemanticSymbolKind.Component &&
                        string.Equals(symbol.Name, newName, StringComparison.Ordinal) &&
                        symbol.Definition is not null && !SameDefinition(symbol.Definition, references[0].Symbol.Definition)))
            {
                await _connection.WriteResponseAsync(id, null, cancellationToken);
                return;
            }

            var changes = references.GroupBy(reference => new Uri(reference.SourcePath).AbsoluteUri)
                .ToDictionary(group => group.Key, group => group.Select(reference => new
                {
                    range = ToRange(reference.Text, reference.Symbol.ReferenceSpan),
                    newText = newName,
                }).OrderByDescending(edit => edit.range.Start.Line)
                  .ThenByDescending(edit => edit.range.Start.Character).ToArray());
            await _connection.WriteResponseAsync(id, new { changes }, cancellationToken);
        }

        private Task<IReadOnlyList<ComponentReference>?> FindComponentReferencesAsync(
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString();
            if (uri is null)
                return Task.FromResult<IReadOnlyList<ComponentReference>?>(null);
            if (!_documents.TryGetValue(uri, out var document))
                return Task.FromResult<IReadOnlyList<ComponentReference>?>(null);
            var offset = GetOffset(document.Text, parameters.GetProperty("position"));
            var selected = document.Analysis.Symbols.Where(symbol =>
                    symbol.Kind == LucentSemanticSymbolKind.Component &&
                    offset >= symbol.ReferenceSpan.Start && offset <= symbol.ReferenceSpan.End)
                .OrderBy(symbol => symbol.ReferenceSpan.Length).FirstOrDefault();
            if (selected?.Definition is null)
                return Task.FromResult<IReadOnlyList<ComponentReference>?>(null);

            var project = _projectAnalyses.Values.FirstOrDefault(analysis => analysis.Results.Any(result =>
                PathsEqual(result.SourcePath, GetSourcePath(uri))));
            if (project is null)
                return Task.FromResult<IReadOnlyList<ComponentReference>?>(null);
            return Task.FromResult<IReadOnlyList<ComponentReference>?>(project.Results.SelectMany(result => result.Result.Symbols
                    .Where(symbol => symbol.Kind == LucentSemanticSymbolKind.Component &&
                        SameDefinition(symbol.Definition, selected.Definition))
                    .Select(symbol => new ComponentReference(result.SourcePath,
                        project.SourceTexts[result.SourcePath], symbol)))
                .OrderBy(reference => reference.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reference => reference.Symbol.ReferenceSpan.Start)
                .ToArray());
        }

        private static bool SameDefinition(LucentDefinition? left, LucentDefinition? right) =>
            left is not null && right is not null && PathsEqual(left.SourcePath, right.SourcePath) &&
            left.Span == right.Span;

        private (DocumentState? Document, LucentSemanticSymbol? Symbol) FindSymbol(
            JsonElement parameters)
        {
            var uri = parameters
                .GetProperty("textDocument")
                .GetProperty("uri")
                .GetString()
                ?? throw new InvalidOperationException("A document URI is required.");
            if (!_documents.TryGetValue(uri, out var document))
            {
                return (null, null);
            }

            var offset = GetOffset(document.Text, parameters.GetProperty("position"));
            var symbol = document.Analysis.Symbols
                .Where(candidate =>
                    offset >= candidate.ReferenceSpan.Start &&
                    offset <= candidate.ReferenceSpan.End)
                .OrderBy(candidate => candidate.ReferenceSpan.Length)
                .FirstOrDefault();
            return (document, symbol);
        }

        private static string GetSourcePath(string uri)
        {
            if (FileUri.TryGetPath(uri, out var path))
            {
                return path;
            }

            return uri;
        }

        private static LspRange ToRange(string text, SourceSpan span)
        {
            var start = Math.Clamp(span.Start, 0, text.Length);
            var end = Math.Clamp(span.End, start, text.Length);
            return new LspRange(
                ToPosition(text, start),
                ToPosition(text, end));
        }

        private static LspPosition ToPosition(string text, int offset)
        {
            var line = 0;
            var lineStart = 0;
            for (var index = 0; index < offset; index++)
            {
                if (text[index] == '\n')
                {
                    line++;
                    lineStart = index + 1;
                }
            }

            return new LspPosition(line, offset - lineStart);
        }

        private static int GetOffset(string text, JsonElement position)
        {
            var line = Math.Max(0, position.GetProperty("line").GetInt32());
            var character = Math.Max(
                0,
                position.GetProperty("character").GetInt32());
            var lineStart = 0;
            var currentLine = 0;

            for (var index = 0; index < text.Length && currentLine < line; index++)
            {
                if (text[index] == '\n')
                {
                    currentLine++;
                    lineStart = index + 1;
                }
            }

            if (currentLine < line)
            {
                return text.Length;
            }

            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            return Math.Min(lineStart + character, lineEnd);
        }

        private sealed record DocumentState(
            string Text,
            int? Version,
            CompilationResult Analysis,
            CssProjectTokenIndex? CssTokens);

        private sealed record ProjectAnalysis(
            string Generation,
            IReadOnlyList<LucentSourceCompilation> Results,
            string DiagnosticSourcePath,
            ReferencedManifestSnapshot ReferencedManifests,
            IReadOnlyList<string> ManifestDiagnostics,
            IReadOnlyDictionary<string, string> SourceTexts);

        private sealed record InboundMessage(
            JsonDocument? Message,
            CancellationTokenSource? RequestCancellation,
            string? RequestKey,
            Exception? TransportException,
            bool EndOfStream);

        private sealed record ComponentReference(
            string SourcePath,
            string Text,
            LucentSemanticSymbol Symbol);

        private sealed record LspPosition(int Line, int Character);

        private sealed record LspRange(LspPosition Start, LspPosition End);

        private sealed record PublishedDiagnostic(
            LspRange Range,
            int Severity,
            string Code,
            string Message,
            string Source);
    }
}
