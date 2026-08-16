using System.Collections.ObjectModel;

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
    ObservableCollection<WorkspaceNode> Children)
{
    public bool IsExpanded { get; set; } = true;
}

internal sealed record WorkspaceRow(WorkspaceNode Node, int Depth);

internal sealed record ProblemItem(
    string Id,
    string File,
    int Line,
    string Message,
    ProblemSeverity Severity);

internal sealed record QuickOpenItem(string Id, string DisplayName, string Path);

internal sealed class OpenDocument(string path, string text)
{
    public string Path { get; } = path;
    public string Text { get; set; } = text;
    public bool IsDirty { get; set; }
}
