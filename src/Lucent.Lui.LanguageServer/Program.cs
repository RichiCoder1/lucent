using System.Text.Json;
using Lucent.Lui.Compiler;

namespace Lucent.Lui.LanguageServer;

internal static class Program
{
    private static readonly object outputGate = new();
    private static readonly Stream protocolOutput = Console.OpenStandardOutput();
    private static readonly string[] signatureTriggers = ["(", ",", " "];
    private static readonly string[] completionTriggers = ["<", " ", ".", ":", "{"];

    private static async Task<int> Main()
    {
        LuiProjectContext? project = null;
        var openDocuments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var formattingDocuments = new Dictionary<string, FormattingDocument>(
            StringComparer.OrdinalIgnoreCase
        );
        var initializeReceived = false;
        var initialized = false;
        var shutdown = false;
        var exitCode = 1;
        var diagnosticsPending = false;
        Task<JsonDocument?>? read = null;
        try
        {
            while (true)
            {
                // Console header reads are blocking; keep one reader off the dispatch loop.
                read ??= Task.Run(ReadMessageAsync);
                if (diagnosticsPending && !read.IsCompleted)
                {
                    await Task.WhenAny(read, Task.Delay(150)).ConfigureAwait(false);
                    if (!read.IsCompleted && project is not null)
                    {
                        diagnosticsPending = false;
                        try
                        {
                            await PublishOpenDiagnosticsAsync(project, openDocuments)
                                .ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            Console.Error.WriteLine($"Idle diagnostics: {exception}");
                        }
                    }
                }
                var message = await read.ConfigureAwait(false);
                read = null;
                if (message is null)
                    break;
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
                        formattingDocuments.Clear();
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
                        if (
                            methodName == "textDocument/diagnostic"
                            && diagnosticsPending
                            && project is not null
                        )
                        {
                            diagnosticsPending = false;
                            await PublishOpenDiagnosticsAsync(project, openDocuments)
                                .ConfigureAwait(false);
                        }
                        var result = await HandleAsync(
                                methodName,
                                root.TryGetProperty("params", out var parameters)
                                    ? parameters
                                    : default,
                                project,
                                openDocuments,
                                formattingDocuments
                            )
                            .ConfigureAwait(false);
                        if (result.Project is not null)
                        {
                            project?.Dispose();
                            project = result.Project;
                            initializeReceived = true;
                            openDocuments.Clear();
                        }
                        if (methodName == "initialize")
                            initializeReceived = true;
                        diagnosticsPending |= result.DiagnosticsChanged;
                        if (id is not null)
                            WriteResponse(id, result.Value);
                    }
                    catch (Exception exception) when (id is not null)
                    {
                        WriteError(id, exception.Message);
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine($"{methodName}: {exception}");
                    }
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
        Dictionary<string, int> openDocuments,
        Dictionary<string, FormattingDocument> formattingDocuments
    )
    {
        if (method is "textDocument/didOpen" or "textDocument/didChange" or "textDocument/didClose")
        {
            var changedDocument = parameters.GetProperty("textDocument");
            var changedUri = new Uri(changedDocument.GetProperty("uri").GetString()!);
            var key = DocumentKey(changedUri);
            if (method == "textDocument/didClose")
                formattingDocuments.Remove(key);
            else if (
                changedUri.IsFile
                && changedUri.LocalPath.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)
            )
            {
                var version = changedDocument.GetProperty("version").GetInt32();
                if (
                    !formattingDocuments.TryGetValue(key, out var previous)
                    || version > previous.Version
                )
                    formattingDocuments[key] = new FormattingDocument(
                        version,
                        method == "textDocument/didOpen"
                            ? changedDocument.GetProperty("text").GetString()!
                            : parameters
                                .GetProperty("contentChanges")[0]
                                .GetProperty("text")
                                .GetString()!
                    );
            }
        }
        switch (method)
        {
            case "initialize":
            {
                var projectUri =
                    parameters.TryGetProperty(
                        "initializationOptions",
                        out var initializationOptions
                    )
                    && initializationOptions.TryGetProperty("projectUri", out var configuredProject)
                        ? configuredProject.GetString()
                        : null;
                var context = projectUri is null
                    ? null
                    : await LuiProjectContext
                        .LoadAsync(
                            LuiProjectContext.FilePath(new Uri(projectUri)),
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                WriteNotification(
                    "lucent/projectGraph",
                    new { directories = context?.ProjectDirectories() ?? [] }
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
                            completionProvider = new
                            {
                                triggerCharacters = completionTriggers,
                                resolveProvider = true,
                            },
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
                            codeActionProvider = new
                            {
                                resolveProvider = true,
                                codeActionKinds = (string[])["quickfix"],
                            },
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
                    return new HandlerResult(null, null, diagnosticsChanged: true);
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
                    return new HandlerResult(null, null, diagnosticsChanged: true);
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
                    return new HandlerResult(null, null, diagnosticsChanged: true);
                }
                return new HandlerResult(null, null);
            case "workspace/didChangeWatchedFiles":
                if (project is null || !parameters.TryGetProperty("changes", out var changes))
                    return new HandlerResult(null, null);
                var watchedUris = changes
                    .EnumerateArray()
                    .Where(change =>
                        change.TryGetProperty("uri", out var changedUri)
                        && changedUri.GetString() is not null
                    )
                    .Select(change => new Uri(change.GetProperty("uri").GetString()!))
                    .ToArray();
                var reloaded = await project
                    .ReloadIfRelevantAsync(watchedUris, CancellationToken.None)
                    .ConfigureAwait(false);
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
                var definitionOffset = await OffsetAsync(project, uri, position)
                    .ConfigureAwait(false);
                if (definitionOffset is null)
                    return new HandlerResult(null, null);
                var target = await project
                    .DefinitionAsync(uri, definitionOffset.Value, CancellationToken.None)
                    .ConfigureAwait(false);
                return new HandlerResult(null, target is null ? null : Location(target));
            case "textDocument/prepareRename":
                if (project is null)
                    return new HandlerResult(null, null);
                var prepareUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var prepareOffset = await OffsetAsync(
                        project,
                        prepareUri,
                        parameters.GetProperty("position")
                    )
                    .ConfigureAwait(false);
                if (prepareOffset is null)
                    return new HandlerResult(null, null);
                var prepared = await project
                    .PrepareRenameAsync(prepareUri, prepareOffset.Value, CancellationToken.None)
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
                var renameOffset = await OffsetAsync(
                        project,
                        renameUri,
                        parameters.GetProperty("position")
                    )
                    .ConfigureAwait(false);
                if (renameOffset is null)
                    return new HandlerResult(null, null);
                var renamed = await project
                    .RenameAsync(
                        renameUri,
                        renameOffset.Value,
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
                var referencesOffset = await OffsetAsync(
                        project,
                        referencesUri,
                        parameters.GetProperty("position")
                    )
                    .ConfigureAwait(false);
                if (referencesOffset is null)
                    return new HandlerResult(null, null);
                var references = await project
                    .ReferencesAsync(
                        referencesUri,
                        referencesOffset.Value,
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
                var formatUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                formattingDocuments.TryGetValue(DocumentKey(formatUri), out var snapshot);
                var formatText =
                    snapshot?.Text
                    ?? (
                        project is null
                            ? null
                            : await project
                                .GetTextAsync(formatUri, CancellationToken.None)
                                .ConfigureAwait(false)
                    );
                if (formatText is null)
                    throw new InvalidOperationException(
                        "Open the LUI document before requesting formatting."
                    );
                if (
                    parameters
                        .GetProperty("textDocument")
                        .TryGetProperty("version", out var requestedVersion)
                    && snapshot?.Version != requestedVersion.GetInt32()
                )
                    throw new InvalidOperationException(
                        "The formatting request refers to an obsolete document version."
                    );
                LuiSpan? formatRange = null;
                if (method == "textDocument/rangeFormatting")
                {
                    var range = parameters.GetProperty("range");
                    var start = FormattingOffset(formatText, range.GetProperty("start"));
                    var end = FormattingOffset(formatText, range.GetProperty("end"));
                    formatRange = LuiSpan.From(start, end);
                }
                var configuration = LuiEditorConfigResolver.Resolve(
                    LuiProjectContext.FilePath(formatUri)
                );
                if (!configuration.IsValid)
                    throw new InvalidOperationException(
                        string.Join(
                            " | ",
                            configuration.Diagnostics.Select(item => item.Id + ": " + item.Message)
                        )
                    );
                var format = formatRange is { } selectedRange
                    ? LuiFormatter.FormatSelection(formatText, selectedRange, configuration.Options)
                    : LuiFormatter.FormatDocument(formatText, configuration.Options);
                if (format.Status is LuiFormattingStatus.Unavailable or LuiFormattingStatus.Failed)
                    throw new InvalidOperationException(
                        string.Join(
                            " | ",
                            format.Diagnostics.Select(item => item.Id + ": " + item.Message)
                        )
                    );
                return new HandlerResult(
                    null,
                    format
                        .Edits.Select(edit => new
                        {
                            range = Range(formatText, edit.Span),
                            newText = edit.NewText,
                        })
                        .ToArray()
                );
            case "textDocument/codeAction":
            case "codeAction/resolve":
                return new HandlerResult(
                    null,
                    await CodeActionsAsync(
                            project,
                            parameters,
                            formattingDocuments,
                            method == "codeAction/resolve"
                        )
                        .ConfigureAwait(false)
                );
            case "textDocument/completion":
                if (project is null)
                    return new HandlerResult(null, null);
                var completionUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var completionOffset = await OffsetAsync(
                        project,
                        completionUri,
                        parameters.GetProperty("position")
                    )
                    .ConfigureAwait(false);
                if (completionOffset is null)
                    return new HandlerResult(null, null);
                var completionEpoch = project.CompletionEpoch;
                var completion = await project
                    .CompletionsAsync(
                        completionUri,
                        completionOffset.Value,
                        CancellationToken.None,
                        deferDocumentation: true
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
                            data = new
                            {
                                uri = completionUri.AbsoluteUri,
                                offset = completionOffset.Value,
                                epoch = completionEpoch,
                            },
                            documentation = item.Documentation is null
                                ? null
                                : new { kind = "plaintext", value = item.Documentation },
                        }),
                    }
                );
            case "completionItem/resolve":
                if (
                    project is null
                    || !parameters.TryGetProperty("data", out var completionData)
                    || completionData.GetProperty("epoch").GetInt64() != project.CompletionEpoch
                )
                    return new HandlerResult(null, parameters.Clone());
                var resolutionEpoch = project.CompletionEpoch;
                var resolvedItems = await project
                    .CompletionsAsync(
                        new Uri(completionData.GetProperty("uri").GetString()!),
                        completionData.GetProperty("offset").GetInt32(),
                        CancellationToken.None,
                        descriptionLabel: parameters.GetProperty("label").GetString()
                    )
                    .ConfigureAwait(false);
                var resolved = resolvedItems?.FirstOrDefault(item =>
                    item.Label == parameters.GetProperty("label").GetString()
                    && item.Kind == parameters.GetProperty("kind").GetInt32()
                );
                if (resolved is null || project.CompletionEpoch != resolutionEpoch)
                    return new HandlerResult(null, parameters.Clone());
                var resolvedResponse = System.Text.Json.Nodes.JsonNode.Parse(
                    parameters.GetRawText()
                )!;
                resolvedResponse["detail"] = resolved.Detail;
                if (resolved.Documentation is not null)
                    resolvedResponse["documentation"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["kind"] = "plaintext",
                        ["value"] = resolved.Documentation,
                    };
                return new HandlerResult(null, resolvedResponse);
            case "textDocument/hover":
                if (project is null)
                    return new HandlerResult(null, null);
                var hoverUri = new Uri(
                    parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                );
                var hoverOffset = await OffsetAsync(
                        project,
                        hoverUri,
                        parameters.GetProperty("position")
                    )
                    .ConfigureAwait(false);
                if (hoverOffset is null)
                    return new HandlerResult(null, null);
                var hover = await project
                    .HoverAsync(hoverUri, hoverOffset.Value, CancellationToken.None)
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
                var signatureOffset = await OffsetAsync(
                        project,
                        signatureUri,
                        parameters.GetProperty("position")
                    )
                    .ConfigureAwait(false);
                if (signatureOffset is null)
                    return new HandlerResult(null, null);
                var signature = await project
                    .SignatureHelpAsync(signatureUri, signatureOffset.Value, CancellationToken.None)
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

    private static async Task<int?> OffsetAsync(
        LuiProjectContext project,
        Uri uri,
        JsonElement position
    )
    {
        var text = await project.GetTextAsync(uri, CancellationToken.None).ConfigureAwait(false);
        if (text is null)
        {
            if (!project.CanEdit(uri))
                throw new InvalidOperationException(
                    $"Document '{uri}' is not included in the configured project '{project.ProjectPath}' or its project references. "
                        + "Set lucentLui.projectPath to the owning .csproj and reload the VS Code window. "
                        + "If that project is already selected, check that the file is included in its evaluated inputs."
                );
            throw new InvalidOperationException("The requested document is not current.");
        }
        if (text.Length == 0)
            return null;
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
        var position = Microsoft
            .CodeAnalysis.Text.SourceText.From(text)
            .Lines.GetLinePosition(offset);
        return (position.Line, position.Character);
    }

    private static int FormattingOffset(string text, JsonElement position)
    {
        var lines = Microsoft.CodeAnalysis.Text.SourceText.From(text).Lines;
        var line = position.GetProperty("line").GetInt32();
        var character = position.GetProperty("character").GetInt32();
        if (line < 0 || line >= lines.Count || character < 0 || character > lines[line].Span.Length)
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "The formatting position is outside the document."
            );
        return lines[line].Start + character;
    }

    private sealed record FormattingDocument(int Version, string Text);

    private static async Task<object> CodeActionsAsync(
        LuiProjectContext? project,
        JsonElement parameters,
        Dictionary<string, FormattingDocument> documents,
        bool resolve
    )
    {
        object Unavailable(string reason) =>
            resolve
                ? new
                {
                    title = parameters.GetProperty("title").GetString(),
                    kind = "quickfix",
                    disabled = new { reason },
                }
                : Array.Empty<object>();
        if (project is null)
            return Unavailable("Select a project to analyze semantic fixes.");
        var data = parameters.GetProperty(resolve ? "data" : "textDocument");
        var documentUri = data.GetProperty("uri").GetString()!;
        var uri = new Uri(documentUri);
        if (!documents.TryGetValue(DocumentKey(uri), out var document))
            return Unavailable("The document is no longer open.");
        if (
            data.TryGetProperty("version", out var requested)
            && requested.GetInt32() != document.Version
        )
            return Unavailable("The document changed; request a fresh code action.");
        if (resolve && data.GetProperty("epoch").GetInt64() != project.CompletionEpoch)
            return Unavailable("The project changed; request a fresh code action.");
        var configuration = LuiEditorConfigResolver.Resolve(LuiProjectContext.FilePath(uri));
        if (!configuration.IsValid)
            return Unavailable("Correct the source configuration before applying fixes.");
        var lint = await project
            .LintAsync(
                uri,
                new LuiLintOptions(configuration.DeclarationOrder),
                CancellationToken.None
            )
            .ConfigureAwait(false);
        if (
            lint is null
            || lint.Result.Status != LuiLintAnalysisStatus.Complete
            || lint.Source != document.Text
        )
            return Unavailable("A current, complete semantic analysis is required for this fix.");
        var diagnostics = await project
            .DiagnosticsAsync(uri, CancellationToken.None)
            .ConfigureAwait(false);
        if (diagnostics is null || project.CompletionEpoch != lint.Epoch)
            return Unavailable("The project changed during analysis.");
        var range =
            resolve ? (LuiSpan?)null
            : parameters.TryGetProperty("range", out var requestedRange)
                ? LuiSpan.From(
                    FormattingOffset(document.Text, requestedRange.GetProperty("start")),
                    FormattingOffset(document.Text, requestedRange.GetProperty("end"))
                )
            : null;
        var actions = new List<object>();
        foreach (var fix in lint.Result.Fixes)
        {
            if (
                resolve
                && (
                    data.GetProperty("rule").GetString() != fix.DiagnosticId
                    || data.GetProperty("offset").GetInt32() != fix.DiagnosticSpan.Start
                )
            )
                continue;
            if (
                range is { } selected
                && (
                    fix.DiagnosticSpan.End < selected.Start
                    || fix.DiagnosticSpan.Start > selected.End
                )
            )
                continue;
            var diagnostic = diagnostics.FirstOrDefault(item =>
                item.Code == fix.DiagnosticId
                && item.Span.Start == fix.DiagnosticSpan.Start
                && item.Span.Length == fix.DiagnosticSpan.Length
            );
            if (diagnostic is null)
                continue;
            actions.Add(
                new
                {
                    title = fix.Title + " (" + fix.DiagnosticId + ")",
                    kind = "quickfix",
                    diagnostics = new[]
                    {
                        new
                        {
                            range = Range(document.Text, diagnostic.Span),
                            code = diagnostic.Code,
                            message = diagnostic.Message,
                            severity = diagnostic.Severity,
                            source = "Lucent.Lui",
                        },
                    },
                    data = new
                    {
                        uri = documentUri,
                        version = document.Version,
                        epoch = lint.Epoch,
                        rule = fix.DiagnosticId,
                        offset = fix.DiagnosticSpan.Start,
                    },
                    edit = resolve
                        ? new
                        {
                            documentChanges = new[]
                            {
                                new
                                {
                                    textDocument = new
                                    {
                                        uri = documentUri,
                                        version = document.Version,
                                    },
                                    edits = fix
                                        .Edits.Select(edit => new
                                        {
                                            range = Range(document.Text, edit.Span),
                                            newText = edit.NewText,
                                        })
                                        .ToArray(),
                                },
                            },
                        }
                        : null,
                }
            );
        }
        return resolve
            ? actions.FirstOrDefault() ?? Unavailable("This fix is no longer applicable.")
            : actions;
    }

    private static Task<JsonDocument?> ReadMessageAsync() =>
        LspProtocol.ReadMessageAsync(Console.OpenStandardInput());

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
        lock (outputGate)
            LspProtocol.WriteMessage(protocolOutput, message);
    }

    private sealed class HandlerResult(
        LuiProjectContext? project,
        object? value,
        bool diagnosticsChanged = false
    )
    {
        internal LuiProjectContext? Project { get; } = project;
        internal object? Value { get; } = value;
        internal bool DiagnosticsChanged { get; } = diagnosticsChanged;
    }
}
