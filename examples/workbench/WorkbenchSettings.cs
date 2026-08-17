namespace Lucent.Examples.Workbench;

internal sealed record WorkbenchSettings(
    string? RecentWorkspace,
    double SidebarWidth,
    bool ProblemsVisible)
{
    public static WorkbenchSettings Defaults => new(null, 280, true);
}
