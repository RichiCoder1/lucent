using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Examples.Workbench;
using System.Collections.ObjectModel;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class VirtualizationTests
{
    [TestMethod]
    public void Workspace_selection_preserves_id_and_uses_sibling_parent_fallback()
    {
        var first = new WorkspaceNode("first", "First", new([
            new WorkspaceNode("one", "One", []),
            new WorkspaceNode("two", "Two", []),
        ]));
        var second = new WorkspaceNode("second", "Second", []);
        var roots = new ObservableCollection<WorkspaceNode>([first, second]);
        var selection = new WorkspaceSelectionController(roots);

        selection.SelectById("one");
        Assert.AreEqual("one", selection.SelectedId);
        selection.RemoveById("one");
        Assert.AreEqual("two", selection.SelectedId);
        selection.RemoveById("two");
        Assert.AreEqual("first", selection.SelectedId);

        selection.Roots.Move(0, 1);
        Assert.AreEqual("first", selection.SelectedId);
        Assert.AreEqual("second", selection.VisibleRows[0].Node.Id);
    }

    [TestMethod]
    public void Workspace_projection_tracks_incremental_collection_changes()
    {
        var children = new ObservableCollection<WorkspaceNode>([
            new WorkspaceNode("one", "One", []),
            new WorkspaceNode("two", "Two", []),
        ]);
        var selection = new WorkspaceSelectionController([
            new WorkspaceNode("root", "Root", children),
        ]);
        selection.SelectById("two");

        children.Insert(0, new WorkspaceNode("zero", "Zero", []));
        Assert.AreEqual("two", selection.SelectedId);
        Assert.AreEqual("zero", selection.VisibleRows[1].Node.Id);

        children.Move(2, 0);
        Assert.AreEqual("two", selection.SelectedId);
        Assert.AreEqual("two", selection.VisibleRows[1].Node.Id);

        children.RemoveAt(0);
        Assert.AreEqual("zero", selection.SelectedId);
    }

    [TestMethod]
    public void Removing_selected_ancestor_uses_ancestor_sibling_fallback()
    {
        var selected = new WorkspaceNode("selected", "Selected", []);
        var parent = new WorkspaceNode("parent", "Parent", new([selected]));
        var next = new WorkspaceNode("next", "Next", []);
        var selection = new WorkspaceSelectionController([parent, next]);
        selection.SelectById("selected");

        selection.RemoveById("parent");

        Assert.AreEqual("next", selection.SelectedId);
    }

    [TestMethod]
    public void Workspace_reset_restores_existing_id_and_clears_removed_id()
    {
        var roots = new ObservableCollection<WorkspaceNode>([
            new WorkspaceNode("a", "A", []),
            new WorkspaceNode("b", "B", []),
        ]);
        var selection = new WorkspaceSelectionController(roots);
        selection.SelectById("b");

        selection.ReplaceRoots([
            new WorkspaceNode("b", "B renamed", []),
            new WorkspaceNode("c", "C", []),
        ]);
        Assert.AreEqual("b", selection.SelectedId);

        selection.ReplaceRoots([new WorkspaceNode("c", "C", [])]);
        Assert.IsNull(selection.SelectedId);
    }

    [TestMethod]
    public void Workspace_projection_rebuild_preserves_selected_id()
    {
        var child = new WorkspaceNode("child", "Child", []);
        var root = new WorkspaceNode("root", "Root", new([child]));
        var selection = new WorkspaceSelectionController([root]);
        selection.SelectById("child");

        selection.SetExpanded("root", false);
        Assert.AreEqual("child", selection.SelectedId);
        selection.SetExpanded("root", true);
        Assert.AreEqual("child", selection.SelectedId);
        Assert.AreEqual("child", selection.VisibleRows[1].Node.Id);
    }

    [TestMethod]
    public void Workspace_selection_rejects_duplicate_or_empty_ids()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new WorkspaceSelectionController([
                new WorkspaceNode("same", "A", []),
                new WorkspaceNode("same", "B", []),
            ]));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new WorkspaceSelectionController([new WorkspaceNode("", "Missing", [])]));
    }

    [TestMethod]
    public async Task Workspace_listbox_selection_uses_stable_controller_id()
    {
        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var roots = new ObservableCollection<WorkspaceNode>([
                new WorkspaceNode("a", "A", []),
                new WorkspaceNode("b", "B", []),
            ]);
            var controller = new WorkspaceSelectionController(roots);
            var list = new ListBox { Width = 300, Height = 300 };
            window.Content = list;
            window.UpdateLayout();
            controller.Attach(list);
            controller.SelectById("b");

            Assert.AreEqual("b", controller.SelectedId);
            Assert.AreEqual("b", ((WorkspaceRow)list.SelectedItem!).Node.Id);
            controller.RemoveById("b");
            Assert.AreEqual("a", controller.SelectedId);
            Assert.AreEqual("a", ((WorkspaceRow)list.SelectedItem!).Node.Id);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Native_listboxes_realize_bounded_containers_at_both_ends()
    {
        var workspaceController = new WorkspaceSelectionController([
            new WorkspaceNode("workspace-root", "Workspace", new ObservableCollection<WorkspaceNode>(
                Enumerable.Range(0, 9_999).Select(index =>
                    new WorkspaceNode($"workspace-{index}", $"Workspace {index}", [])))),
        ]);
        var workspace = workspaceController.VisibleRows;
        var problems = new ObservableCollection<ProblemItem>(Enumerable.Range(0, 10_000)
            .Select(index => new ProblemItem($"problem-{index}", "WorkbenchApp.lui", index + 1, $"Problem {index}", ProblemSeverity.Warning)));
        var quickOpen = new ObservableCollection<QuickOpenItem>(Enumerable.Range(0, 10_000)
            .Select(index => new QuickOpenItem($"quick-{index}", $"Quick {index}", $"Quick{index}.lui")));

        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var workspaceList = CreateList(workspace, (row, _) => new TextBlock { Text = row.Node.Name });
            var problemList = CreateList(problems, (item, _) => new TextBlock { Text = item.Message });
            var quickList = CreateList(quickOpen, (item, _) => new TextBlock { Text = item.DisplayName });
            var grid = new Grid
            {
                RowDefinitions = new RowDefinitions("*,*,*"),
                Children = { workspaceList, problemList, quickList },
            };
            Grid.SetRow(workspaceList, 0);
            Grid.SetRow(problemList, 1);
            Grid.SetRow(quickList, 2);
            window.Content = grid;
            window.Measure(new Size(800, 600));
            window.Arrange(new Rect(0, 0, 800, 600));
            Dispatcher.UIThread.RunJobs();

            AssertBounded(workspaceList, workspace[0]);
            AssertBounded(problemList, problems[0]);
            AssertBounded(quickList, quickOpen[0]);

            workspaceList.ScrollIntoView(workspace[^1]);
            problemList.ScrollIntoView(problems[^1]);
            quickList.ScrollIntoView(quickOpen[^1]);
            Dispatcher.UIThread.RunJobs();
            AssertBounded(workspaceList, workspace[^1]);
            AssertBounded(problemList, problems[^1]);
            AssertBounded(quickList, quickOpen[^1]);

            workspaceList.ScrollIntoView(workspace[0]);
            problemList.ScrollIntoView(problems[0]);
            quickList.ScrollIntoView(quickOpen[0]);
            Dispatcher.UIThread.RunJobs();
            AssertBounded(workspaceList, workspace[0]);
            AssertBounded(problemList, problems[0]);
            AssertBounded(quickList, quickOpen[0]);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Recycling_interface_spike_records_realization_without_owner_contract()
    {
        var items = new ObservableCollection<int>(Enumerable.Range(0, 5_000));
        var template = new TrackingTemplate();

        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var list = new ListBox
            {
                Width = 400,
                Height = 240,
                ItemsSource = items,
                ItemTemplate = template,
            };
            window.Content = list;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            list.ScrollIntoView(items[^1]);
            Dispatcher.UIThread.RunJobs();
            items.RemoveAt(items.Count - 1);
            items.Insert(0, -1);
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(template.BuildCount > 0);
            Assert.AreEqual(0, template.RecycleCount,
                "The headless ListBox does not recycle item-template roots through Build(existing).");
            Assert.IsTrue(template.AttachedCount > 0);
            Assert.IsTrue(template.DetachedCount > 0,
                "Virtualized template roots detach when their containers leave the visual tree.");
            Assert.IsTrue(template.BuildCount < items.Count,
                "ListBox remained virtualized instead of realizing every item.");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Non_recycling_func_data_template_realizes_bounded_rows_and_detaches_roots()
    {
        var items = new ObservableCollection<int>(Enumerable.Range(0, 5_000));
        var buildCount = 0;
        var attachedCount = 0;
        var detachedCount = 0;
        var template = new FuncDataTemplate<int>((item, _) =>
        {
            buildCount++;
            var root = new TextBlock { Text = item.ToString() };
            root.AttachedToVisualTree += (_, _) => attachedCount++;
            root.DetachedFromVisualTree += (_, _) => detachedCount++;
            return root;
        }, false);

        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var list = new ListBox
            {
                Width = 400,
                Height = 240,
                ItemsSource = items,
                ItemTemplate = template,
            };
            window.Content = list;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            list.ScrollIntoView(items[^1]);
            Dispatcher.UIThread.RunJobs();
            items.RemoveAt(items.Count - 1);
            items.Insert(0, -1);
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(buildCount > 0);
            Assert.IsTrue(attachedCount > 0);
            Assert.IsTrue(detachedCount > 0,
                "Non-recycling template roots detach when virtualized containers leave the visual tree.");
            Assert.IsTrue(buildCount < items.Count,
                "ListBox remained virtualized instead of realizing every item.");
            return Task.CompletedTask;
        });
    }

    private static ListBox CreateList<T>(ObservableCollection<T> items, Func<T, INameScope, Control> build) =>
        new()
        {
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<T>(build, true),
        };

    private static void AssertBounded<T>(ListBox list, T expected)
    {
        var containers = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Console.WriteLine($"ListBox virtualization {typeof(T).Name}: realized={containers.Length}, expected={expected}");
        Assert.IsTrue(containers.Length > 0, $"No ListBoxItems realized for {typeof(T).Name}.");
        Assert.IsTrue(containers.Length < 200, $"{containers.Length} ListBoxItems realized for {typeof(T).Name}.");
        Assert.IsTrue(containers.Any(item => ReferenceEquals(item.DataContext, expected)) ||
            containers.Any(item => item.DataContext?.Equals(expected) == true),
            $"Expected endpoint was not realized for {typeof(T).Name}.");
    }

    private sealed class TrackingTemplate : IRecyclingDataTemplate
    {
        public int BuildCount { get; private set; }
        public int RecycleCount { get; private set; }
        public int AttachedCount { get; private set; }
        public int DetachedCount { get; private set; }

        public bool Match(object? data) => data is int;

        public Control? Build(object? data) => Build(data, null);

        public Control Build(object? data, Control? existing)
        {
            if (existing is TextBlock text)
            {
                RecycleCount++;
                text.Text = data?.ToString();
                return text;
            }

            BuildCount++;
            var root = new TextBlock { Text = data?.ToString() };
            root.AttachedToVisualTree += (_, _) => AttachedCount++;
            root.DetachedFromVisualTree += (_, _) => DetachedCount++;
            return root;
        }
    }
}
