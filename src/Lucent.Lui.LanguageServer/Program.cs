using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lucent.Lui.LanguageServer;

internal static class Program
{
    private static readonly object outputGate = new();

    private static async Task<int> Main()
    {
        LuiProjectContext? project = null;
        var openDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        HashSet<string> openDocuments
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
                return new HandlerResult(
                    context,
                    new { capabilities = new { definitionProvider = true, textDocumentSync = 1 } }
                );
            }
            case "textDocument/didOpen":
                if (project is not null)
                {
                    var openedUri = new Uri(
                        parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                    );
                    if (!project.Owns(openedUri))
                        return new HandlerResult(null, null);
                    openDocuments.Add(DocumentKey(openedUri));
                    project.ReplaceText(
                        openedUri,
                        parameters.GetProperty("textDocument").GetProperty("text").GetString()!
                    );
                }
                return new HandlerResult(null, null);
            case "textDocument/didChange":
                if (project is not null)
                {
                    var changedUri = new Uri(
                        parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                    );
                    if (
                        !openDocuments.Contains(DocumentKey(changedUri))
                        || !project.Owns(changedUri)
                    )
                        return new HandlerResult(null, null);
                    project.ReplaceText(
                        changedUri,
                        parameters.GetProperty("contentChanges")[0].GetProperty("text").GetString()!
                    );
                }
                return new HandlerResult(null, null);
            case "textDocument/didClose":
                if (project is not null)
                {
                    var closedUri = new Uri(
                        parameters.GetProperty("textDocument").GetProperty("uri").GetString()!
                    );
                    openDocuments.Remove(DocumentKey(closedUri));
                    if (project.Owns(closedUri))
                        project.Close(closedUri);
                }
                return new HandlerResult(null, null);
            case "textDocument/definition":
                if (project is null)
                    return new HandlerResult(null, null);
                var textDocument = parameters.GetProperty("textDocument");
                var uri = new Uri(textDocument.GetProperty("uri").GetString()!);
                var position = parameters.GetProperty("position");
                var target = await project
                    .NavigateAsync(
                        uri,
                        await OffsetAsync(project, uri, position).ConfigureAwait(false),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                return new HandlerResult(null, target is null ? null : Location(target));
            case "lucent/generatedText":
                if (project is null)
                    return new HandlerResult(null, null);
                var generatedUri = new Uri(parameters.GetProperty("uri").GetString()!);
                return new HandlerResult(null, project.GetGeneratedText(generatedUri));
            default:
                return new HandlerResult(null, null);
        }
    }

    private static string DocumentKey(Uri uri) =>
        uri.IsFile ? Path.GetFullPath(LuiProjectContext.FilePath(uri)) : uri.AbsoluteUri;

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
