using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Lucent.Examples.Workbench;

internal sealed class AccessibleWorkspaceListBox : ListBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new WorkspacePeer(this);

    private sealed class WorkspacePeer(Control owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tree;
    }
}
