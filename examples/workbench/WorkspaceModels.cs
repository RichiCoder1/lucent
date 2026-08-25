using System.Collections.ObjectModel;
using Lucent.Compiler;

namespace Lucent.Examples.Workbench;

internal enum ProblemSeverity
{
    Info,
    Warning,
    Error,
}

internal sealed record WorkspaceNode(
    string Id,
    string Name,
    ObservableCollection<WorkspaceNode> Children,
    string? Path = null)
{
    public bool IsExpanded { get; set; } = true;
}

internal sealed record WorkspaceRow(WorkspaceNode Node, int Depth);

internal sealed record ProblemItem(
    string Id,
    string File,
    int Line,
    string Message,
    ProblemSeverity Severity,
    string? Path = null,
    int Column = 1,
    int SpanLength = 0);

internal sealed record QuickOpenItem(string Id, string DisplayName, string Path);

internal sealed record GeneratedPreviewSnapshot(string Source, LucentSourceMap? SourceMap, int Generation);

internal sealed class OpenDocument(string path, string text)
{
    public string Path { get; } = path;
    public string Text { get; set; } = text;
    public bool IsDirty { get; set; }
}
