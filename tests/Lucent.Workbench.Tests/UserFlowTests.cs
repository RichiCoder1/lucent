using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Examples.Workbench;
using System.Reflection;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class UserFlowTests
{
    [TestMethod]
    public async Task Workbench_uses_persisted_sidebar_width_in_shell_grid()
    {
        await WithWorkbenchAsync(new TestHost(), async (root, _) =>
        {
            var shellGrid = root.GetVisualDescendants().OfType<Grid>()
                .Single(grid => grid.ColumnDefinitions.Count == 3 &&
                    grid.ColumnDefinitions[0].Width.Value == 360d);
            Assert.AreEqual(360d, shellGrid.ColumnDefinitions[0].Width.Value);
            await Task.CompletedTask;
        }, settings: new WorkbenchSettings("Lucent", 360, true));
    }

    [TestMethod]
    public async Task Flow_1_ctrl_o_cancelled_picker_preserves_workspace()
    {
        var host = new TestHost { Folders = [] };
        await WithWorkbenchAsync(host, async (root, _) =>
        {
            root.KeyPress(Key.O, RawInputModifiers.Control, PhysicalKey.O, "o");
            await DrainAsync(() => host.OpenCalls == 1);
            Assert.IsTrue(FindText(root, "Workspace: Lucent"));
        });
    }

    [TestMethod]
    public async Task Flow_2_quick_open_arrows_enter_and_escape_restore_editor_focus()
    {
        await WithWorkbenchAsync(new TestHost(), async (root, _) =>
        {
            var editor = root.GetVisualDescendants().OfType<AccessibleTextEditor>().Single();
            editor.Focus();
            root.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
            var quickOpen = root.GetVisualDescendants().OfType<ListBox>().Single(list =>
                list.ItemsSource is IEnumerable<QuickOpenItem>);
            quickOpen.Focus();
            Dispatcher.UIThread.RunJobs();
            root.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            root.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            await DrainAsync(() => quickOpen.SelectedIndex == 1);
            root.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            await DrainAsync(() => editor.Text.Contains("DocumentPane.lui", StringComparison.Ordinal));

            root.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
            root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Assert.IsTrue(IsInside(editor, root.FocusManager?.GetFocusedElement()));
            await DrainAsync(() => true);
        });
    }

    [TestMethod]
    public async Task Flow_3_palette_traps_tab_toggles_problems_and_restores_focus()
    {
        await WithWorkbenchAsync(new TestHost(), async (root, _) =>
        {
            var editor = root.GetVisualDescendants().OfType<AccessibleTextEditor>().Single();
            editor.Focus();
            root.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            var palette = root.GetVisualDescendants().OfType<Control>().Single(control =>
                Avalonia.Automation.AutomationProperties.GetAutomationId(control) == "CommandPalette.Dialog");
            root.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            Assert.IsTrue(IsInside(palette, root.FocusManager?.GetFocusedElement()));
            root.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
            Assert.IsTrue(IsInside(palette, root.FocusManager?.GetFocusedElement()));

            var toggle = palette.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Content?.ToString() == "Toggle problems");
            toggle.Focus();
            Dispatcher.UIThread.RunJobs();
            root.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            await DrainAsync(() => FindText(root, "Problems hidden") &&
                !root.GetVisualDescendants().Any(control =>
                    Avalonia.Automation.AutomationProperties.GetAutomationId(control) == "CommandPalette.Dialog"));
            Assert.IsFalse(root.GetVisualDescendants().Any(control =>
                Avalonia.Automation.AutomationProperties.GetAutomationId(control) == "CommandPalette.Dialog"));
            Assert.IsTrue(IsInside(editor, root.FocusManager?.GetFocusedElement()));
        });
    }

    [TestMethod]
    public async Task Flow_4_tree_keyboard_selection_and_editor_edit_commands_keep_editor_identity()
    {
        await WithWorkbenchAsync(new TestHost(), async (root, _) =>
        {
            var editor = root.GetVisualDescendants().OfType<AccessibleTextEditor>().Single();
            var workspace = root.GetVisualDescendants().OfType<ListBox>().Single(list =>
                list.ItemsSource is IEnumerable<WorkspaceRow>);
            root.KeyPress(Key.O, RawInputModifiers.Control, PhysicalKey.O, "o");
            await DrainAsync(() => FindText(root, "3 problems"));
            root.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, "b");
            workspace.Focus();
            Dispatcher.UIThread.RunJobs();
            for (var index = 0; index < 5; index++)
                root.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            await DrainAsync(() => workspace.SelectedItem is WorkspaceRow { Node.Id: "readme" });
            Assert.AreEqual("readme", ((WorkspaceRow)workspace.SelectedItem!).Node.Id);
            Assert.IsTrue(editor.Text.Contains("README.md", StringComparison.Ordinal));

            root.KeyPress(Key.E, RawInputModifiers.Control, PhysicalKey.E, "e");
            await DrainAsync(() => root.FocusManager?.GetFocusedElement() is Visual focused &&
                (ReferenceEquals(focused, editor) || focused.GetVisualAncestors().Contains(editor)));
            var beforeEdit = editor.Text;
            root.KeyTextInput(" typed");
            await DrainAsync(() => editor.Text != beforeEdit && editor.Text.Contains(" typed", StringComparison.Ordinal));
            var afterEdit = editor.Text;
            root.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            root.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            root.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
            Assert.IsTrue(ReferenceEquals(editor, root.GetVisualDescendants().OfType<AccessibleTextEditor>().Single()));
            Assert.AreEqual(afterEdit, editor.Text);

            var problems = root.GetVisualDescendants().OfType<ListBox>().Single(list =>
                list.ItemsSource is IEnumerable<ProblemItem>);
            problems.Focus();
            root.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            Assert.IsNotNull(problems.SelectedItem);
            Assert.IsTrue(editor.IsAttachedToVisualTree());
        });
    }

    [TestMethod]
    public async Task Flow_5_problem_loading_refresh_stale_failure_retry_and_recovery_are_physical()
    {
        var loader = new ControllableProblemLoader();
        await WithWorkbenchAsync(new TestHost(), async (root, _) =>
        {
            await DrainAsync(() => loader.Calls == 1 && HasClass(root, "problems-loading"));
            Assert.IsFalse(HasClass(root, "problems-pane"),
                "The authored loading branch must replace ProblemsPane before the first commit.");

            root.KeyPress(Key.O, RawInputModifiers.Control, PhysicalKey.O, "o");
            await DrainAsync(() => loader.Calls == 2 && HasClass(root, "problems-loading"));
            Assert.IsTrue(FindText(root, "Loading problems…"));
            Assert.IsTrue(HasClass(root, "problems-loading"));
            Assert.AreEqual(1, loader.CancellationCount,
                "Opening the workspace must cancel the superseded first load.");

            loader.Fail(new InvalidOperationException("initial problem load failed"));
            await DrainAsync(() => FindText(root, "initial problem load failed"));
            Assert.IsTrue(HasClass(root, "problem-error"));
            Assert.IsFalse(HasClass(root, "problems-loading"));

            var precommitRetry = root.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Content?.ToString() == "Retry problems");
            precommitRetry.Focus();
            Dispatcher.UIThread.RunJobs();
            root.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            await DrainAsync(() => loader.Calls == 3 && HasClass(root, "problems-loading"));
            loader.Complete([
                new ProblemItem("one", "WorkbenchApp.lui", 1, "first", ProblemSeverity.Warning),
                new ProblemItem("two", "DocumentPane.lui", 2, "second", ProblemSeverity.Info),
                new ProblemItem("three", "WorkspaceSidebar.lui", 3, "third", ProblemSeverity.Error),
            ]);
            await DrainAsync(() => FindText(root, "3 problems"));
            Assert.IsTrue(HasClass(root, "problems-pane"));

            root.KeyPress(Key.R, RawInputModifiers.Control | RawInputModifiers.Shift,
                PhysicalKey.R, "r");
            await DrainAsync(() => loader.Calls == 4);
            Assert.IsTrue(FindText(root, "3 problems"));
            Assert.IsTrue(HasClass(root, "problems-pane"));
            Assert.IsFalse(HasClass(root, "problems-loading"),
                "A committed refresh keeps stale content mounted.");
            loader.Fail(new InvalidOperationException("problem load failed"));
            await DrainAsync(() => FindText(root, "problem load failed"));
            Assert.IsTrue(HasClass(root, "problem-error"));
            Assert.IsFalse(HasClass(root, "problems-pane"));

            var retry = root.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Content?.ToString() == "Retry problems");
            retry.Focus();
            Dispatcher.UIThread.RunJobs();
            root.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            await DrainAsync(() => loader.Calls == 5 && HasClass(root, "problems-pane"));
            Assert.IsTrue(FindText(root, "3 problems"));
            loader.ThrowSynchronously = true;
            root.KeyPress(Key.R, RawInputModifiers.Control | RawInputModifiers.Shift,
                PhysicalKey.R, "r");
            await DrainAsync(() => FindText(root, "synchronous problem load failure"));
            loader.ThrowSynchronously = false;
            Assert.IsTrue(HasClass(root, "problem-error"));

            retry = root.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Content?.ToString() == "Retry problems");
            retry.Focus();
            Dispatcher.UIThread.RunJobs();
            root.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            await DrainAsync(() => loader.Calls == 7 && HasClass(root, "problems-pane"));
            Assert.AreEqual(2, loader.CancellationCount,
                "Replacing a pending retry must cancel exactly the superseded request.");
            loader.Complete([
                new ProblemItem("recovered", "WorkbenchApp.lui", 4, "recovered", ProblemSeverity.Info),
            ]);
            await DrainAsync(() => FindText(root, "1 problems"));
        }, loader);
    }

    [TestMethod]
    public async Task Flow_6_settings_edit_save_and_reopen_retains_values()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucent-workbench-{Guid.NewGuid():N}.json");
        var host = new TestHost();
        var repository = new JsonFileSettingsRepository(path, _ => { });
        using var coordinator = new SettingsSaveCoordinator(repository, CancellationToken.None);
        try
        {
            await WithWorkbenchAsync(host, async (root, _) =>
            {
                root.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
                await DrainAsync(() => host.LastDialog is not null);
                var dialog = host.LastDialog!;
                var text = dialog.GetVisualDescendants().OfType<TextBox>().Single();
                text.Focus();
                Dispatcher.UIThread.RunJobs();
                dialog.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                dialog.KeyTextInput("repo");
                var problems = dialog.GetVisualDescendants().OfType<CheckBox>().Single();
                problems.Focus();
                Dispatcher.UIThread.RunJobs();
                dialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                host.CloseDialog();
                await coordinator.Tail;

                root.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
                await DrainAsync(() => host.LastDialog is not null && host.LastDialog != dialog);
                var reopened = host.LastDialog!;
                Assert.AreEqual("repo", reopened.GetVisualDescendants().OfType<TextBox>().Single().Text);
                Assert.IsFalse(reopened.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked);
                host.CloseDialog();
            }, coordinator: coordinator);
        }
        finally
        {
            repository.Dispose();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public async Task Flow_7_close_detaches_app_session_and_leaves_no_post_close_editor_mutation()
    {
        var host = new TestHost();
        var session = new DocumentSession(new OpenDocument("Program.cs", "initial"), () => { });
        await WithWorkbenchAsync(host, async (root, _) =>
        {
            var editor = root.GetVisualDescendants().OfType<AccessibleTextEditor>().Single();
            Assert.AreSame(editor, session.Editor);
            root.Close();
            Assert.IsNull(session.Editor);
            Assert.IsFalse(editor.IsAttachedToVisualTree());
            await DrainAsync(() => !editor.IsAttachedToVisualTree());
            session.Dispose();
        }, session: session);
    }

    private static async Task WithWorkbenchAsync(
        TestHost host,
        Func<Window, CancellationTokenSource, Task> action,
        ControllableProblemLoader? loader = null,
        SettingsSaveCoordinator? coordinator = null,
        DocumentSession? session = null,
        WorkbenchSettings? settings = null)
    {
        await HeadlessTestHarness.RunUiAsync(async () =>
        {
            using var lifetime = new CancellationTokenSource();
            using var component = WorkbenchTestFactory.Create(host, lifetime.Token, settings: settings, loader: loader, session: session,
                saveCoordinator: coordinator);
            var root = component.MountRoot();
            try
            {
                root.Show();
                root.Activate();
                root.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                await DrainAsync(() => root.GetVisualDescendants().OfType<AccessibleTextEditor>().Any());
                await action(root, lifetime);
            }
            finally
            {
                if (root.IsVisible) root.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    private static bool FindText(Control root, string text) => root.GetVisualDescendants().OfType<TextBlock>()
        .Any(block => block.Text == text);

    private static bool HasClass(Control root, string className) => root.GetVisualDescendants()
        .OfType<Control>().Any(control => control.Classes.Contains(className));

    private static bool IsInside(Control root, IInputElement? element) => element is Visual visual &&
        (ReferenceEquals(visual, root) || visual.GetVisualAncestors().Contains(root));

    private static async Task DrainAsync(Func<bool> complete)
    {
        for (var attempt = 0; attempt < 100 && !complete(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.IsTrue(complete(), "The bounded UI drain did not complete.");
    }

    private sealed class TestHost : IWorkbenchDesktopHost
    {
        public IReadOnlyList<IStorageFolder> Folders { get; init; } = [CreateFolder("Workspace")];
        public int OpenCalls { get; private set; }
        public Window? LastDialog { get; private set; }
        public TaskCompletionSource<bool> DialogGate { get; private set; } = NewGate();
        public string? ClipboardText { get; private set; }

        public Task<IReadOnlyList<IStorageFolder>> PickWorkspaceAsync(Window owner)
        {
            OpenCalls++;
            return Task.FromResult(Folders);
        }

        public Task SetClipboardTextAsync(TopLevel owner, string text)
        {
            ClipboardText = text;
            return Task.CompletedTask;
        }

        public Task<TResult> ShowDialogAsync<TResult>(Window owner, Window dialog)
        {
            LastDialog = dialog;
            dialog.Show(owner);
            dialog.Activate();
            return DialogGate.Task.ContinueWith(task => (TResult)(object)task.Result);
        }

        public void ShowOwnedWindow(Window owner, Window child) => child.Show(owner);

        public void CloseDialog()
        {
            LastDialog?.Close(true);
            DialogGate.TrySetResult(true);
            DialogGate = NewGate();
        }

        private static TaskCompletionSource<bool> NewGate() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static IStorageFolder CreateFolder(string name)
        {
            var folder = DispatchProxy.Create<IStorageFolder, FolderProxy>();
            ((FolderProxy)(object)folder).Name = name;
            return folder;
        }
    }

    private sealed class ControllableProblemLoader : IProblemLoader
    {
        private readonly Queue<TaskCompletionSource<IReadOnlyList<ProblemItem>>> _pending = new();
        public int Calls { get; private set; }
        public int CancellationCount { get; private set; }
        public bool ThrowSynchronously { get; set; }

        public Task<IReadOnlyList<ProblemItem>> LoadAsync(string? workspace, CancellationToken cancellationToken)
        {
            Calls++;
            if (ThrowSynchronously)
                throw new InvalidOperationException("synchronous problem load failure");
            var result = new TaskCompletionSource<IReadOnlyList<ProblemItem>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(result);
            cancellationToken.Register(() =>
            {
                if (result.TrySetCanceled(cancellationToken)) CancellationCount++;
            });
            return result.Task;
        }

        public void Complete(IReadOnlyList<ProblemItem> problems) => Next().TrySetResult(problems);
        public void Fail(Exception error) => Next().TrySetException(error);

        private TaskCompletionSource<IReadOnlyList<ProblemItem>> Next()
        {
            while (_pending.Count > 0)
            {
                var next = _pending.Dequeue();
                if (!next.Task.IsCompleted) return next;
            }
            throw new InvalidOperationException("No pending problem load.");
        }
    }

    private class FolderProxy : DispatchProxy
    {
        public string Name { get; set; } = "Workspace";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_Name" => Name,
                "get_Path" => new Uri($"file:///tmp/{Name}"),
                "get_CanBookmark" => false,
                "Dispose" => null,
                "DeleteAsync" => Task.CompletedTask,
                _ => targetMethod?.ReturnType == typeof(Task) ? Task.CompletedTask : null,
            };
        }
    }
}
