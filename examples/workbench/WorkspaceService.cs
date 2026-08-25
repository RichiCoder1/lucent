using System.Collections.ObjectModel;
using Lucent.Compiler;

namespace Lucent.Examples.Workbench;

/// <summary>Small application adapter over the shared project evaluation and compiler APIs.</summary>
internal sealed class WorkspaceService
{
    // Experimental-app ceiling: a workspace view is bounded to 16 levels and 10,000 entries.
    internal const int MaxTreeDepth = 16;
    internal const int MaxTreeEntries = 10_000;
    private readonly ProjectContextLoader _projects = new();
    private readonly Func<string, CancellationToken, Task<string>> _read;
    private readonly Dictionary<string, string> _openTexts = new(PathComparer);
    private LucentProjectContext? _context;
    private CompilationResult? _activeResult;
    private string? _activeDocumentPath;
    private int _generation;
    private int _openRequest;
    private IReadOnlyList<string> _lucentFiles = [];

    public static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public string? RootPath { get; private set; }
    public IReadOnlyList<WorkspaceNode> Roots { get; private set; } = [];
    public IReadOnlyList<QuickOpenItem> QuickOpenItems { get; private set; } = [];
    public string GeneratedSource { get; private set; } = "// Open a .lui file to preview generated C#.\n";
    public LucentSourceMap? ActiveSourceMap => _activeResult?.SourceMap;

    internal WorkspaceService(Func<string, CancellationToken, Task<string>>? read = null) =>
        _read = read ?? File.ReadAllTextAsync;

    public void Close()
    {
        _openRequest++; RootPath = null; _context = null; _activeResult = null; _activeDocumentPath = null; _generation++;
        _openTexts.Clear(); Roots = []; QuickOpenItems = []; _lucentFiles = [];
        GeneratedSource = "// Open a .lui file to preview generated C#.\n";
    }

    public async Task OpenAsync(string rootPath, CancellationToken cancellationToken)
    {
        rootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(rootPath)) throw new DirectoryNotFoundException(rootPath);
        var request = ++_openRequest;
        var entries = 0;
        var roots = new[] { CreateNode(rootPath, 0, ref entries) };
        var discovered = EnumerateLucentFiles(rootPath);
        var context = discovered.Count == 0 ? null : await _projects.LoadAsync(discovered[0], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var lucentFiles = context is null ? discovered : context.LucentSources;
        if (lucentFiles.FirstOrDefault() is { } first) await _read(first, cancellationToken);
        if (request != _openRequest) return;
        RootPath = rootPath; _context = context; _activeResult = null; _activeDocumentPath = null; _generation++;
        _openTexts.Clear(); Roots = roots; _lucentFiles = lucentFiles;
        QuickOpenItems = lucentFiles.Select(path => new QuickOpenItem(path, Path.GetRelativePath(rootPath, path), path)).ToArray();
        GeneratedSource = "// Open a .lui file to preview generated C#.\n";
    }

    public async Task<string> ReadAsync(string path, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        if (_openTexts.TryGetValue(path, out var text)) return text;
        text = await _read(path, cancellationToken);
        _openTexts[path] = text;
        return text;
    }

    public void UpdateDocument(OpenDocument document)
    {
        if (RootPath is null || !Path.IsPathFullyQualified(document.Path)) return;
        _activeDocumentPath = Path.GetFullPath(document.Path);
        _openTexts[_activeDocumentPath] = document.Text;
        InvalidateGeneration();
    }

    public void ActivateDocument(OpenDocument document)
    {
        if (RootPath is null || !Path.IsPathFullyQualified(document.Path)) return;
        _activeDocumentPath = Path.GetFullPath(document.Path);
        _openTexts[_activeDocumentPath] = document.Text;
        InvalidateGeneration();
    }

    public async Task<IReadOnlyList<ProblemItem>> LoadProblemsAsync(string? workspace, CancellationToken cancellationToken)
    {
        if (RootPath is null || !PathComparer.Equals(RootPath, workspace)) return [];
        var generation = _generation;
        await Task.Yield(); // allow a newer editor generation to supersede this request.
        cancellationToken.ThrowIfCancellationRequested();
        var inputs = new List<LucentSourceInput>();
        foreach (var path in _lucentFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path)) continue;
            var text = _openTexts.TryGetValue(path, out var open) ? open : await File.ReadAllTextAsync(path, cancellationToken);
            var stylePath = Path.ChangeExtension(path, ".css");
            inputs.Add(new LucentSourceInput(path, text, stylePath,
                File.Exists(stylePath) ? await File.ReadAllTextAsync(stylePath, cancellationToken) : null));
        }
        if (inputs.Count == 0) return [];
        cancellationToken.ThrowIfCancellationRequested();
        var result = LucentCompiler.CompileProject(inputs, _context);
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != _generation || RootPath is null) return [];
        var current = result.Sources.FirstOrDefault(source => PathComparer.Equals(source.SourcePath, _activeDocumentPath)) ?? result.Sources[0];
        _activeResult = current.Result;
        GeneratedSource = current.Result.GeneratedSource ?? "// Generation is unavailable while errors are present.\n";
        return result.Sources.SelectMany(source => source.Result.Diagnostics.Select((diagnostic, index) =>
            new ProblemItem($"{diagnostic.SourcePath}:{diagnostic.Span.Start}:{index}",
                Relative(diagnostic.SourcePath ?? source.SourcePath), diagnostic.Line, diagnostic.Message,
                diagnostic.Severity == LucentDiagnosticSeverity.Error ? ProblemSeverity.Error : ProblemSeverity.Warning,
                diagnostic.SourcePath ?? source.SourcePath, diagnostic.Column, diagnostic.Span.Length))).ToArray();
    }

    public bool TryMapGeneratedOffset(int offset, out string path, out int line, out int column)
        => TryMapGeneratedOffset(CaptureGeneratedPreview(), offset, out path, out line, out column);

    public GeneratedPreviewSnapshot CaptureGeneratedPreview() =>
        new(GeneratedSource, ActiveSourceMap, _generation);

    public bool TryMapGeneratedOffset(GeneratedPreviewSnapshot snapshot, int offset, out string path, out int line, out int column)
    {
        path = string.Empty; line = column = 0;
        if (snapshot.Generation != _generation || snapshot.SourceMap is not { } map) return false;
        var (generatedLine, generatedColumn) = Position(snapshot.Source, offset);
        var entry = map.Entries.FirstOrDefault(entry => Contains(entry.GeneratedRange, generatedLine, generatedColumn));
        if (entry is null || !Uri.TryCreate(entry.LucentUri, UriKind.Absolute, out var uri) || !uri.IsFile) return false;
        path = uri.LocalPath;
        line = entry.LucentRange.StartLine + 1;
        column = entry.LucentRange.StartCharacter + 1;
        return true;
    }

    private void InvalidateGeneration()
    {
        _generation++;
        _activeResult = null;
        GeneratedSource = "// Refreshing generated C#.\n";
    }

    private WorkspaceNode CreateNode(string path, int depth, ref int entries)
    {
        var children = new List<WorkspaceNode>();
        if (depth >= MaxTreeDepth || IsReparsePoint(path))
            return new WorkspaceNode(path, Path.GetFileName(path), [], path);
        foreach (var entry in Entries(path))
        {
            if (entries++ >= MaxTreeEntries) break;
            if (Ignored(Path.GetFileName(entry)) || IsReparsePoint(entry)) continue;
            if (Directory.Exists(entry)) children.Add(CreateNode(entry, depth + 1, ref entries));
            else children.Add(new WorkspaceNode(entry, Path.GetFileName(entry), [], entry));
        }
        return new WorkspaceNode(path, Path.GetFileName(path), new ObservableCollection<WorkspaceNode>(children
            .OrderBy(node => Directory.Exists(node.Path) ? 0 : 1).ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)), path);
    }

    private IReadOnlyList<string> EnumerateLucentFiles(string root)
    {
        var files = new List<string>();
        var entries = 0;
        var directories = new Stack<(string Path, int Depth)>();
        directories.Push((root, 0));
        while (directories.Count > 0 && files.Count < MaxTreeEntries)
        {
            var (directory, depth) = directories.Pop();
            if (depth > MaxTreeDepth || IsReparsePoint(directory)) continue;
            foreach (var entry in Entries(directory))
            {
                if (entries++ >= MaxTreeEntries) return files.OrderBy(path => path, PathComparer).ToArray();
                if (IsReparsePoint(entry) || Ignored(Path.GetFileName(entry))) continue;
                if (Directory.Exists(entry)) directories.Push((entry, depth + 1));
                else if (entry.EndsWith(".lui", StringComparison.OrdinalIgnoreCase)) files.Add(entry);
            }
        }
        return files.OrderBy(path => path, PathComparer).ToArray();
    }

    private static IEnumerable<string> Entries(string path)
    {
        using var entries = TryGetEntries(path);
        if (entries is null) yield break;
        while (TryMoveNext(entries, out var entry)) yield return entry;
    }

    private static IEnumerator<string>? TryGetEntries(string path)
    {
        try { return Directory.EnumerateFileSystemEntries(path).GetEnumerator(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool TryMoveNext(IEnumerator<string> entries, out string entry)
    {
        try
        {
            if (entries.MoveNext()) { entry = entries.Current; return true; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        entry = string.Empty;
        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; }
    }

    private static (int Line, int Column) Position(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 0; var start = 0;
        for (var index = 0; index < offset; index++) if (text[index] == '\n') { line++; start = index + 1; }
        return (line, offset - start);
    }

    private static bool Contains(LucentSourceMapRange range, int line, int column) =>
        (line > range.StartLine || line == range.StartLine && column >= range.StartCharacter) &&
        (line < range.EndLine || line == range.EndLine && column < range.EndCharacter);

    private string Relative(string path) => RootPath is null ? Path.GetFileName(path) : Path.GetRelativePath(RootPath, path);
    private static bool Ignored(string name) => name is "bin" or "obj" or ".git" or "node_modules";
}
