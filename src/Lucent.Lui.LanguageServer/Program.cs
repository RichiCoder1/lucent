using System.Globalization;
using System.Text;
using System.Text.Json;
using Lucent.Lui.Compiler;

namespace Lucent.Lui.LanguageServer;

internal static class Program
{
    private static readonly object outputGate = new();
    private static readonly string[] signatureTriggers = ["(", ",", " "];
    private static readonly string[] completionTriggers = ["<", " ", ".", ":", "{"];

    private static async Task<int> Main()
    {
        LuiProjectContext? project = null;
        var openDocuments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var initializeReceived = false;
        var initialized = false;
        var shutdown = false;
        var exitCode = 1;
        try
        {
            while (await ReadMessageAsync().ConfigureAwait(false) is { } message)
            {
                using (message)
                {
                    var root = message.RootElement;
                    if (!root.TryGetProperty("method", out var method))
                        continue;
                    var id = root.TryGetProperty("id", out var requestId)
                        ? requestId.GetRawText()
                        : null;
                    var methodName = method.GetString() ?? "";
                    if (methodName == "exit")
                    {
                        if (id is not null)
                        {
                            WriteError(id, "exit must be a notification.");
                            continue;
                        }
                        exitCode = shutdown ? 0 : 1;
                        break;
                    }
                    if (shutdown)
                    {
                        if (id is not null)
                            WriteError(id, "The language server has shut down.");
                        continue;
                    }
                    if (methodName == "initialized")
                    {
                        if (id is not null || !initializeReceived || initialized)
                        {
                            if (id is not null)
                                WriteError(
                                    id,
                                    "initialized must be a notification in the expected state."
                                );
                            continue;
                        }
                        initialized = true;
                        if (id is not null)
                            WriteResponse(id, null);
                        continue;
                    }
                    if (methodName == "shutdown")
                    {
                        if (!initialized || id is null)
                        {
                            if (id is not null)
                                WriteError(id, "shutdown requires an initialized server.");
                            continue;
                        }
                        project?.Dispose();
                        project = null;
                        openDocuments.Clear();
                        shutdown = true;
                        WriteResponse(id, null);
                        continue;
                    }
                    if (methodName == "initialize" && initializeReceived)
                    {
                        if (id is not null)
                            WriteError(id, "initialize may be requested only once.");
                        continue;
                    }
                    if (methodName == "initialize" && id is null)
                        continue;
                    if (!initialized && methodName != "initialize")
                    {
                        if (id is not null)
                            WriteError(id, "The language server is not initialized.");
                        continue;
                    }
                    try
                    {
                        var result = await HandleAsync(
                                methodName,
                                root.TryGetProperty("params", out var parameters)
                                    ? parameters
                                    : default,
                                project,
                                openDocuments
                            )
                            .ConfigureAwait(false);
                        if (result.Project is not null)
                        {
                            project?.Dispose();
                            project = result.Project;
                            initializeReceived = true;
                            openDocuments.Clear();
                        }
                        if (id is not null)
                            WriteResponse(id, result.Value);
                    }
                    catch (Exception exception) when (id is not null)
                    {
                        WriteError(id, exception.Message);
                    }
                    catch (Exception) { }
                }
            }
        }
        finally
        {
            project?.Dispose();
        }
        return exitCode;
    }

    private static async Task<HandlerResult> HandleAsync(
        string method,
        JsonElement parameters,
        LuiProjectContext? project,
        Dictionary<string, int> openDocuments
    )
    {
        switch (method)
        {
            case "initialize":
            {
                var projectUri = parameters
                    .GetProperty("initializationOptions")
                    .GetProperty("projectUri")
                    .GetString();
                if (projectUri is null)
                    throw new InvalidOperationException(
                        "initialize requires initializationOptions.projectUri."
                    );
                var context = await LuiProjectContext
                    .LoadAsync(
                        LuiProjectContext.FilePath(new Uri(projectUri)),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                WriteNotification(
                    "lucent/projectGraph",
                    new { directories = context.ProjectDirectories() }
                );
                return new HandlerResult(
                    context,
                    new
                    {
                        capabilities = new
                        {
                            definitionProvider = true,
                            hoverProvider = true,
                            signatureHelpProvider = new { triggerCharacters = signatureTriggers },
                            completionProvider = new { triggerCharacters = completionTriggers },
                            documentSymbolProvider = true,
                            semanticTokensProvider = new
                            {
                                legend = new
                                {
                                    tokenTypes = LuiProjectContext.SemanticTokenTypes,
                                    tokenModifiers = Array.Empty<string>(),
                                },
                                full = true,
                            },
                            diagnosticProvider = new
                            {
                                identifier = "lucent-lui",
                                interFileDependencies = true,
                                workspaceDiagnostics = false,
                            },
                            renameProvider = new { prepareProvider = true },
                            referencesProvider = true,
                            documentFormattingProvider = true,
                            documentRangeFormattingProvider = true,
                            textDocumentSync = 1,
                        },
                    }
                );
            }
            case "textDocument/didOpen":
                if (project is not null)
                {
                    var opened = parameters.GetProperty("textDocument");
                    var openedUri = new Uri(opened.GetProperty("uri").GetString()!);
                    var version = opened.GetProperty("version").GetInt32();
                    if (!project.CanEdit(openedUri))
                    {
                        PublishEmptyDiagnostics(openedUri, version);
                        return new HandlerResult(null, null);
                    }
                    if (
                        openDocuments.TryGetValue(DocumentKey(openedUri), out var current)
                        && version <= current
                    )
                        return new HandlerResult(null, null);
                    openDocuments[DocumentKey(openedUri)] = version;
                    project.ReplaceText(openedUri, opened.GetProperty("text").GetString()!);
                    await PublishOpenDiagnosticsAsync(project, openDocuments).ConfigureAwait(false);
                }
                return new HandlerResult(null, null);
            case "textDocument/didChange":
                if (project is not null)
                {
                    var changed = parameters.GetProperty("textDocument");
                    var changedUri = new Uri(changed.GetProperty("uri").GetString()!);
                    var version = changed.GetProperty("version").GetInt32();
                    if (!project.CanEdit(changedUri))
                    {
                        PublishEmptyDiagnostics(changedUri, version);
                        return new HandlerResult(null, null);
                    }
                    if (
                        !openDocuments.TryGetValue(DocumentKey(changedUri), out var current)
                        || version <= current
                    )
                        return new HandlerResult(null, null);
                    openDocuments[DocumentKey(changedUri)] = version;
                    project.ReplaceText(
                        changedUri,
                        parameters.GetProperty("contentChanges")[0].GetProperty("text").GetString()!
                    );
                    await PublishOpenDiagnosticsAsync(project, openDocuments).ConfigureAwait(false);
                }
                return new HandlerResult(null, null);
            case "textDocument/didClose":
                if (project is not null)
                {
                    var closedUri = new Uri(
                        parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                    );
                    openDocuments.Remove(DocumentKey(closedUri), out var version);
                    if (project.CanEdit(closedUri))
                        project.Close(closedUri);
                    PublishEmptyDiagnostics(closedUri, version);
                    await PublishOpenDiagnosticsAsync(project, openDocuments).ConfigureAwait(false);
                }
                return new HandlerResult(null, null);
            case "workspace/didChangeWatchedFiles":
                if (project is null || !parameters.TryGetProperty("changes", out var changes))
                    return new HandlerResult(null, null);
                var reloaded = false;
                foreach (var change in changes.EnumerateArray())
                {
                    if (
                        change.TryGetProperty("uri", out var changedUri)
                        && changedUri.GetString() is { } value
                    )
                        reloaded |= await project
                            .ReloadIfRelevantAsync(new Uri(value), CancellationToken.None)
                            .ConfigureAwait(false);
                }
                if (reloaded)
                {
                    WriteNotification(
                        "lucent/projectGraph",
                        new { directories = project.ProjectDirectories() }
                    );
                    await PublishOpenDiagnosticsAsync(project, openDocuments).ConfigureAwait(false);
                }
                return new HandlerResult(null, null);
            case "textDocument/definition":
                if (project is null)
                    return new HandlerResult(null, null);
                var textDocument = parameters.GetProperty("textDocument");
                var uri = new Uri(textDocument.GetProperty("uri").GetString()!);
                var position = parameters.GetProperty("position");
                var target = await project
                    .DefinitionAsync(
                        uri,
                        await OffsetAsync(project, uri, position).ConfigureAwait(false),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                return new HandlerResult(null, target is null ? null : Location(target));
            case "textDocument/prepareRename":
                if (project is null)
                    return new HandlerResult(null, null);
                var prepareUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var prepared = await project
                    .PrepareRenameAsync(
                        prepareUri,
                        await OffsetAsync(project, prepareUri, parameters.GetProperty("position"))
                            .ConfigureAwait(false),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                var prepareText = await project
                    .GetTextAsync(prepareUri, CancellationToken.None)
                    .ConfigureAwait(false);
                return new HandlerResult(
                    null,
                    prepared is null || prepareText is null
                        ? null
                        : new
                        {
                            range = Range(prepareText, prepared.Span),
                            placeholder = prepareText.Substring(
                                prepared.Span.Start,
                                prepared.Span.Length
                            ),
                        }
                );
            case "textDocument/rename":
                if (project is null)
                    return new HandlerResult(null, null);
                var renameUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var renamed = await project
                    .RenameAsync(
                        renameUri,
                        await OffsetAsync(project, renameUri, parameters.GetProperty("position"))
                            .ConfigureAwait(false),
                        parameters.GetProperty("newName").GetString() ?? "",
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                return new HandlerResult(
                    null,
                    renamed is null ? null : WorkspaceEdit(project, renamed)
                );
            case "textDocument/references":
                if (project is null)
                    return new HandlerResult(null, null);
                var referencesUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var references = await project
                    .ReferencesAsync(
                        referencesUri,
                        await OffsetAsync(
                                project,
                                referencesUri,
                                parameters.GetProperty("position")
                            )
                            .ConfigureAwait(false),
                        parameters
                            .GetProperty("context")
                            .GetProperty("includeDeclaration")
                            .GetBoolean(),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                if (references is null)
                    return new HandlerResult(null, null);
                var referenceLocations = new List<object>();
                foreach (var location in references.Locations)
                {
                    var text = await project
                        .GetTextAsync(location.Uri, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (text is null)
                        return new HandlerResult(null, null);
                    referenceLocations.Add(
                        Location(new LuiNavigationTarget(location.Uri, location.Span, text))
                    );
                }
                return new HandlerResult(null, referenceLocations);
            case "textDocument/formatting":
            case "textDocument/rangeFormatting":
                if (project is null)
                    return new HandlerResult(null, null);
                var formatUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                LuiSpan? formatRange = null;
                if (method == "textDocument/rangeFormatting")
                {
                    var range = parameters.GetProperty("range");
                    var start = await OffsetAsync(project, formatUri, range.GetProperty("start"))
                        .ConfigureAwait(false);
                    var end = await OffsetAsync(project, formatUri, range.GetProperty("end"))
                        .ConfigureAwait(false);
                    formatRange = LuiSpan.From(start, end);
                }
                var format = await project
                    .FormatAsync(formatUri, formatRange, CancellationToken.None)
                    .ConfigureAwait(false);
                var formatText = await project
                    .GetTextAsync(formatUri, CancellationToken.None)
                    .ConfigureAwait(false);
                return new HandlerResult(
                    null,
                    format is null
                    || formatText is null
                    || format.Span.Length == 0 && format.NewText.Length == 0
                        ? Array.Empty<object>()
                        : new[]
                        {
                            new
                            {
                                range = Range(formatText, format.Span),
                                newText = format.NewText,
                            },
                        }
                );
            case "textDocument/completion":
                if (project is null)
                    return new HandlerResult(null, null);
                var completionUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var completion = await project
                    .CompletionsAsync(
                        completionUri,
                        await OffsetAsync(
                                project,
                                completionUri,
                                parameters.GetProperty("position")
                            )
                            .ConfigureAwait(false),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                return new HandlerResult(
                    null,
                    new
                    {
                        isIncomplete = false,
                        items = completion?.Select(item => new
                        {
                            label = item.Label,
                            kind = item.Kind,
                            detail = item.Detail,
                            documentation = item.Documentation is null
                                ? null
                                : new { kind = "plaintext", value = item.Documentation },
                        }),
                    }
                );
            case "textDocument/hover":
                if (project is null)
                    return new HandlerResult(null, null);
                var hoverUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var hover = await project
                    .HoverAsync(
                        hoverUri,
                        await OffsetAsync(project, hoverUri, parameters.GetProperty("position"))
                            .ConfigureAwait(false),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                return new HandlerResult(
                    null,
                    hover is null
                        ? null
                        : new
                        {
                            contents = new object[]
                            {
                                new { language = "csharp", value = hover.Value },
                                hover.Documentation is null
                                    ? null!
                                    : new { kind = "plaintext", value = hover.Documentation },
                            }.Where(item => item is not null),
                        }
                );
            case "textDocument/signatureHelp":
                if (project is null)
                    return new HandlerResult(null, null);
                var signatureUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var signature = await project
                    .SignatureHelpAsync(
                        signatureUri,
                        await OffsetAsync(project, signatureUri, parameters.GetProperty("position"))
                            .ConfigureAwait(false),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                return new HandlerResult(
                    null,
                    signature is null
                        ? null
                        : new
                        {
                            signatures = signature.Signatures.Select(item => new
                            {
                                label = item.Label,
                                documentation = item.Documentation is null
                                    ? null
                                    : new { kind = "plaintext", value = item.Documentation },
                                parameters = item.Parameters.Select(parameter => new
                                {
                                    label = parameter,
                                }),
                            }),
                            activeSignature = signature.ActiveSignature,
                            activeParameter = signature.ActiveParameter,
                        }
                );
            case "textDocument/documentSymbol":
                if (project is null)
                    return new HandlerResult(null, Array.Empty<object>());
                var symbolUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var symbols = await project
                    .DocumentSymbolsAsync(symbolUri, CancellationToken.None)
                    .ConfigureAwait(false);
                var symbolText = await project
                    .GetTextAsync(symbolUri, CancellationToken.None)
                    .ConfigureAwait(false);
                return new HandlerResult(
                    null,
                    symbols?.Select(symbol => DocumentSymbol(symbol, symbolText ?? ""))
                );
            case "textDocument/semanticTokens/full":
                if (project is null)
                    return new HandlerResult(null, new { data = Array.Empty<int>() });
                var semanticTokenUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var semanticTokens = await project
                    .SemanticTokensAsync(semanticTokenUri, CancellationToken.None)
                    .ConfigureAwait(false);
                return new HandlerResult(
                    null,
                    semanticTokens is null ? null : new { data = semanticTokens }
                );
            case "textDocument/diagnostic":
                if (project is null)
                    return new HandlerResult(
                        null,
                        new { kind = "full", items = Array.Empty<object>() }
                    );
                var diagnosticUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var diagnosticText = await project
                    .GetTextAsync(diagnosticUri, CancellationToken.None)
                    .ConfigureAwait(false);
                var diagnostics = await project
                    .DiagnosticsAsync(diagnosticUri, CancellationToken.None)
                    .ConfigureAwait(false);
                if (diagnostics is null)
                    return new HandlerResult(null, new { kind = "unchanged" });
                return new HandlerResult(
                    null,
                    new
                    {
                        kind = "full",
                        items = diagnostics.Select(diagnostic => new
                        {
                            range = Range(diagnosticText ?? "", diagnostic.Span),
                            severity = diagnostic.Severity,
                            code = diagnostic.Code,
                            source = diagnostic.Source,
                            message = diagnostic.Message,
                        }),
                    }
                );
            case "lucent/generatedText":
                if (project is null)
                    return new HandlerResult(null, null);
                var generatedUri = new Uri(parameters.GetProperty("uri").GetString()!);
                return new HandlerResult(null, project.GetGeneratedText(generatedUri));
            default:
                return new HandlerResult(null, null);
        }
    }

    private static string DocumentKey(Uri uri) => uri.AbsoluteUri;

    private static async Task<int> OffsetAsync(
        LuiProjectContext project,
        Uri uri,
        JsonElement position
    )
    {
        var text = await project.GetTextAsync(uri, CancellationToken.None).ConfigureAwait(false);
        if (text is null)
            throw new InvalidOperationException("The requested document is not current.");
        var line = position.GetProperty("line").GetInt32();
        var character = position.GetProperty("character").GetInt32();
        var start = 0;
        for (var current = 0; current < line; current++)
        {
            start = text.IndexOf('\n', start);
            if (start < 0)
                throw new ArgumentOutOfRangeException(nameof(position));
            start++;
        }
        var end = text.IndexOf('\n', start);
        if (end < 0)
            end = text.Length;
        else if (end > start && text[end - 1] == '\r')
            end--;
        if (line < 0 || character < 0 || character > end - start)
            throw new ArgumentOutOfRangeException(nameof(position));
        return start + character;
    }

    private static async Task PublishOpenDiagnosticsAsync(
        LuiProjectContext project,
        Dictionary<string, int> openDocuments
    )
    {
        foreach (var (key, version) in openDocuments.ToArray())
        {
            var uri = new Uri(key);
            if (project.Owns(uri))
                await PublishDiagnosticsAsync(project, uri, version).ConfigureAwait(false);
            else if (!project.CanEdit(uri))
            {
                PublishEmptyDiagnostics(uri, version);
                openDocuments.Remove(key);
            }
        }
    }

    private static async Task PublishDiagnosticsAsync(
        LuiProjectContext project,
        Uri uri,
        int version
    )
    {
        var text = await project.GetTextAsync(uri, CancellationToken.None).ConfigureAwait(false);
        if (text is null)
            return;
        var diagnostics = await project
            .DiagnosticsAsync(uri, CancellationToken.None)
            .ConfigureAwait(false);
        if (diagnostics is null)
            return;
        WriteNotification(
            "textDocument/publishDiagnostics",
            new
            {
                uri = uri.AbsoluteUri,
                version,
                diagnostics = diagnostics.Select(diagnostic => new
                {
                    range = Range(text, diagnostic.Span),
                    severity = diagnostic.Severity,
                    code = diagnostic.Code,
                    source = diagnostic.Source,
                    message = diagnostic.Message,
                }),
            }
        );
    }

    private static void PublishEmptyDiagnostics(Uri uri, int? version)
    {
        if (version is int value)
            WriteNotification(
                "textDocument/publishDiagnostics",
                new
                {
                    uri = uri.AbsoluteUri,
                    version = value,
                    diagnostics = Array.Empty<object>(),
                }
            );
        else
            WriteNotification(
                "textDocument/publishDiagnostics",
                new { uri = uri.AbsoluteUri, diagnostics = Array.Empty<object>() }
            );
    }

    private static object DocumentSymbol(LuiDocumentSymbol symbol, string text) =>
        new
        {
            name = symbol.Name,
            kind = symbol.Kind,
            range = Range(text, symbol.Span),
            selectionRange = Range(text, symbol.SelectionSpan),
            children = symbol.Children.Select(child => DocumentSymbol(child, text)),
        };

    private static object Range(string text, LuiSpan span)
    {
        var start = Position(text, Math.Clamp(span.Start, 0, text.Length));
        var end = Position(text, Math.Clamp(span.End, 0, text.Length));
        return new
        {
            start = new { line = start.Line, character = start.Character },
            end = new { line = end.Line, character = end.Character },
        };
    }

    private static object Location(LuiNavigationTarget target)
    {
        var start = Position(target.Text, target.Span.Start);
        var end = Position(target.Text, target.Span.End);
        return new
        {
            uri = target.Uri.AbsoluteUri,
            range = new
            {
                start = new { line = start.Line, character = start.Character },
                end = new { line = end.Line, character = end.Character },
            },
        };
    }

    private static object WorkspaceEdit(LuiProjectContext project, LuiRenameResult result)
    {
        var changes = new Dictionary<string, object>();
        foreach (var document in result.Edits)
        {
            var text = project
                .GetTextAsync(document.Uri, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (text is null)
                return null!;
            changes[document.Uri.AbsoluteUri] = document
                .Spans.Select(span => new { range = Range(text, span), newText = document.NewText })
                .ToArray();
        }
        return new { changes };
    }

    private static (int Line, int Character) Position(string text, int offset)
    {
        if (offset < 0 || offset > text.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var line = 0;
        var character = 0;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                character = 0;
            }
            else if (text[index] != '\r')
                character++;
        }
        return (line, character);
    }

    private static async Task<JsonDocument?> ReadMessageAsync()
    {
        var input = Console.OpenStandardInput();
        var header = new StringBuilder();
        while (true)
        {
            var value = input.ReadByte();
            if (value < 0)
                return null;
            header.Append((char)value);
            if (header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                break;
        }
        var length = header
            .ToString()
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts =>
                parts.Length == 2
                && parts[0].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
            )
            .Select(parts => Int32.Parse(parts[1], CultureInfo.InvariantCulture))
            .SingleOrDefault();
        if (length <= 0)
            throw new InvalidOperationException("LSP message has no Content-Length.");
        var body = new byte[length];
        for (var read = 0; read < body.Length; )
        {
            var count = await input.ReadAsync(body.AsMemory(read)).ConfigureAwait(false);
            if (count == 0)
                throw new EndOfStreamException("LSP message ended before its content.");
            read += count;
        }
        return JsonDocument.Parse(body);
    }

    private static void WriteResponse(string id, object? result) =>
        Write(
            "{\"jsonrpc\":\"2.0\",\"id\":"
                + id
                + ",\"result\":"
                + JsonSerializer.Serialize(result)
                + "}"
        );

    private static void WriteError(string id, string message) =>
        Write(
            "{\"jsonrpc\":\"2.0\",\"id\":"
                + id
                + ",\"error\":{\"code\":-32603,\"message\":"
                + JsonSerializer.Serialize(message)
                + "}}"
        );

    private static void WriteNotification(string method, object parameters) =>
        Write(
            "{\"jsonrpc\":\"2.0\",\"method\":"
                + JsonSerializer.Serialize(method)
                + ",\"params\":"
                + JsonSerializer.Serialize(parameters)
                + "}"
        );

    private static void Write(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        lock (outputGate)
        {
            Console.Out.Write(
                "Content-Length: "
                    + bytes.Length.ToString(CultureInfo.InvariantCulture)
                    + "\r\n\r\n"
            );
            Console.Out.Write(message);
            Console.Out.Flush();
        }
    }

    private sealed class HandlerResult(LuiProjectContext? project, object? value)
    {
        internal LuiProjectContext? Project { get; } = project;
        internal object? Value { get; } = value;
    }
}
