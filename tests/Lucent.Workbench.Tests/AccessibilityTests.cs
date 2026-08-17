using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lucent.Examples.Workbench;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class AccessibilityTests
{
    [TestMethod]
    public async Task Shown_shell_palette_and_settings_have_exact_accessibility_contract()
    {
        var host = new TestHost();
        await HeadlessTestHarness.RunWindowAsync(async _ =>
        {
            using var lifetime = new CancellationTokenSource();
            using var component = WorkbenchTestFactory.Create(host, lifetime.Token);
            var root = component.MountRoot();
            root.Show();
            root.UpdateLayout();
            await DrainAsync(() => root.GetVisualDescendants().OfType<AccessibleTextEditor>().Any());

            var controls = root.GetVisualDescendants().OfType<Control>().ToArray();
            AssertTarget(root, "Workbench.Window", "Lucent Workbench", AutomationControlType.Window);
            var workspace = controls.Single(control => AutomationProperties.GetAutomationId(control) == "Workspace.Tree");
            var editor = controls.Single(control => AutomationProperties.GetAutomationId(control) == "Document.Editor");
            var problems = controls.Single(control => AutomationProperties.GetAutomationId(control) == "Problems.List");
            AssertTarget(workspace, "Workspace.Tree", "Workspace", AutomationControlType.Tree);
            AssertTarget(editor, "Document.Editor", "Editor", AutomationControlType.Edit);
            AssertTarget(problems, "Problems.List", "Problems", AutomationControlType.List);

            editor.Focus();
            root.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, "b");
            workspace.Focus();
            Assert.IsTrue(IsFocused(workspace));
            root.KeyPress(Key.E, RawInputModifiers.Control, PhysicalKey.E, "e");
            Assert.IsTrue(IsFocused(editor));

            root.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            await DrainAsync(() => root.GetVisualDescendants().Any(control =>
                AutomationProperties.GetAutomationId(control) == "CommandPalette.Dialog"));
            var palette = root.GetVisualDescendants().OfType<Control>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "CommandPalette.Dialog");
            AssertTarget(palette, "CommandPalette.Dialog", "Command palette", AutomationControlType.Window);
            var quickOpen = root.GetVisualDescendants().OfType<ListBox>().Single(list =>
                list.ItemsSource is IEnumerable<QuickOpenItem>);
            Assert.IsTrue(IsFocused(quickOpen));
            root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");

            root.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
            await DrainAsync(() => host.LastDialog is not null);
            var settings = host.LastDialog!;
            AssertTarget(settings, "Settings.Dialog", "Settings", AutomationControlType.Window);
            Assert.IsTrue(IsFocused(settings.GetVisualDescendants().OfType<TextBox>().Single()));

            settings.Close(true);
            host.DialogGate.TrySetResult(true);
            await DrainAsync(() => !settings.IsVisible);
            root.Close();
        });
    }

    private static void AssertTarget(Control control, string id, string name, AutomationControlType role)
    {
        Assert.AreEqual(id, AutomationProperties.GetAutomationId(control));
        Assert.AreEqual(name, AutomationProperties.GetName(control));
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        Assert.IsNotNull(peer);
        Assert.AreEqual(role, peer!.GetAutomationControlType());
    }

    private static bool IsFocused(Control control) => control.IsFocused ||
        control.GetVisualDescendants().OfType<Control>().Any(child => child.IsFocused);

    private static async Task DrainAsync(Func<bool> complete)
    {
        for (var attempt = 0; attempt < 100 && !complete(); attempt++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.IsTrue(complete(), "The bounded UI drain did not complete.");
    }

    private sealed class TestHost : IWorkbenchDesktopHost
    {
        public Window? LastDialog { get; private set; }
        public TaskCompletionSource<bool> DialogGate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<Avalonia.Platform.Storage.IStorageFolder>> PickWorkspaceAsync(Window owner) =>
            Task.FromResult<IReadOnlyList<Avalonia.Platform.Storage.IStorageFolder>>([]);

        public Task SetClipboardTextAsync(TopLevel owner, string text) => Task.CompletedTask;

        public Task<TResult> ShowDialogAsync<TResult>(Window owner, Window dialog)
        {
            LastDialog = dialog;
            dialog.Show(owner);
            dialog.Activate();
            return DialogGate.Task.ContinueWith(task => (TResult)(object)task.Result);
        }

        public void ShowOwnedWindow(Window owner, Window child) { }
    }
}
