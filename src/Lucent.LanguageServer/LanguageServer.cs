using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
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

    private sealed class ServerSession(Stream input, Stream output)
    {
        private readonly JsonRpcConnection _connection = new(input, output);
        private readonly Dictionary<string, DocumentState> _documents =
            new(StringComparer.Ordinal);
        private readonly ProjectContextLoader _projectContexts = new();
        private readonly Dictionary<string, ProjectAnalysis> _projectAnalyses =
            new(StringComparer.OrdinalIgnoreCase);

        private bool _shutdownRequested;

        public async Task<int> RunAsync(CancellationToken cancellationToken)
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
                    LanguageServerLog.TransportFailed(
                        LanguageServerLog.Logger,
                        exception);
                    return 1;
                }

                if (message is null)
                {
                    return _shutdownRequested ? 0 : 1;
                }

                using (message)
                {
                    var result = await HandleMessageAsync(
                        message.RootElement,
                        cancellationToken);
                    if (result.HasValue)
                    {
                        return result.Value;
                    }
                }
            }
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

                    case "textDocument/documentSymbol":
                        if (hasId)
                        {
                            await DocumentSymbolsAsync(id!.Value, parameters, cancellationToken);
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
            _documents[uri] = new DocumentState(text, version, analysis);
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
            _documents[uri] = new DocumentState(current, version, analysis);
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

        private async Task PublishAllDiagnosticsAsync(CancellationToken cancellationToken)
        {
            foreach (var document in _documents)
            {
                await PublishDiagnosticsAsync(document.Key, document.Value.Text,
                    document.Value.Analysis, cancellationToken);
            }
        }

        private async Task PublishDiagnosticsAsync(
            string uri,
            string text,
            CompilationResult result,
            CancellationToken cancellationToken)
        {
            var diagnostics = result.Diagnostics
                .Select(diagnostic => new PublishedDiagnostic(
                    ToRange(text, diagnostic.Span),
                    diagnostic.Severity == LucentDiagnosticSeverity.Error ? 1 : 2,
                    diagnostic.Code,
                    diagnostic.Message,
                    "lucent"))
                .ToArray();

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
            var projectKey = projectContext?.ProjectPath ?? sourcePath;
            var generation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join("\n", inputs.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => item.Key + "\0" +
                        (_documents.Where(document => PathsEqual(GetSourcePath(document.Key), item.Key))
                            .Select(document => document.Value.Version).FirstOrDefault()?.ToString() ?? "0") +
                        "\0" + item.Value.SourceText)))));
            if (!_projectAnalyses.TryGetValue(projectKey, out var cached) ||
                !string.Equals(cached.Generation, generation, StringComparison.Ordinal))
            {
                var projectResult = LucentCompiler.CompileProject(inputs.Values.ToArray(), projectContext);
                cached = new ProjectAnalysis(generation, projectResult.Sources.ToArray());
                _projectAnalyses[projectKey] = cached;
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
            if (uri is not null)
            {
                await RefreshDocumentAnalysisAsync(uri, cancellationToken);
            }
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
            await RefreshDocumentAnalysisAsync(uri, cancellationToken);
            if (!_documents.TryGetValue(uri, out var document))
            {
                await _connection.WriteResponseAsync(id, Array.Empty<object>(), cancellationToken);
                return;
            }

            var offset = GetOffset(document.Text, parameters.GetProperty("position"));
            var completions = LucentCompiler.GetCompletions(document.Text, offset,
                    document.Analysis)
                .GroupBy(item => item.Label, StringComparer.Ordinal)
                .Select(group => group.First())
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
                }).ToArray(),
                cancellationToken);
        }

        private async Task DocumentSymbolsAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString();
            if (uri is not null)
            {
                await RefreshDocumentAnalysisAsync(uri, cancellationToken);
            }
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

        private async Task DefinitionAsync(
            JsonElement id,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            var requestUri = parameters.GetProperty("textDocument").GetProperty("uri").GetString();
            if (requestUri is not null)
            {
                await RefreshDocumentAnalysisAsync(requestUri, cancellationToken);
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

            if (symbol?.Definition is not { } definition ||
                !File.Exists(definition.SourcePath))
            {
                await _connection.WriteResponseAsync(id, null, cancellationToken);
                return;
            }

            var definitionUri = new Uri(definition.SourcePath).AbsoluteUri;
            var definitionText = _documents.TryGetValue(definitionUri, out var openDocument)
                ? openDocument.Text
                : await File.ReadAllTextAsync(
                    definition.SourcePath,
                    cancellationToken);
            await _connection.WriteResponseAsync(
                id,
                new
                {
                    uri = definitionUri,
                    range = ToRange(definitionText, definition.Span),
                },
                cancellationToken);
        }

        private async Task RefreshDocumentAnalysisAsync(
            string uri,
            CancellationToken cancellationToken)
        {
            if (_documents.TryGetValue(uri, out var document))
            {
                var analysis = await AnalyzeAsync(uri, document.Text, cancellationToken);
                _documents[uri] = document with { Analysis = analysis };
            }
        }

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
            CompilationResult Analysis);

        private sealed record ProjectAnalysis(
            string Generation,
            IReadOnlyList<LucentSourceCompilation> Results);

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
