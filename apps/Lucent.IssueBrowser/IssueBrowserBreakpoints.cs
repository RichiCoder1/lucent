using Lucent.Core;

namespace Lucent.IssueBrowser;

internal static class IssueBrowserBreakpoints
{
    public static readonly Breakpoint Wide = new("wide", 820);
    public static readonly BreakpointSet Set = BreakpointSet.Create(Wide);
}
