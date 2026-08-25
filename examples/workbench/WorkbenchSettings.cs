namespace Lucent.Examples.Workbench;

internal sealed record WorkbenchSettings(
    string? RecentWorkspace,
    double SidebarWidth,
    bool ProblemsVisible,
    string Theme = "System")
{
    public static WorkbenchSettings Defaults => new(null, 280, true);
}
