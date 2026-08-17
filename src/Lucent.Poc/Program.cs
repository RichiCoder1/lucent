using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Examples.PackagePulse;
using Lucent.Examples.Todo;

namespace Lucent.Poc;

internal static class Program
{
    internal static string? SmokeTestName { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        return args.Contains("--smoke-test", StringComparer.Ordinal)
            ? RunSmokeTest(args)
            : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect();

    private static int RunSmokeTest(string[] args)
    {
        SmokeTestName = args.SkipWhile(argument => argument != "--smoke-test")
            .Skip(1)
            .FirstOrDefault() ?? "todo";
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        BuildAvaloniaApp().SetupWithLifetime(lifetime);

        var (window, component) = CreateSmokeTarget(SmokeTestName);
        lifetime.MainWindow = window;

        var opened = false;
        var loadedTurn = false;
        var sampleUpdated = false;
        var closed = false;
        var exited = false;

        lifetime.Exit += (_, _) =>
        {
            exited = true;
            MarkSmokeProgress("app-exit");
        };

        window.Opened += (_, _) =>
        {
            opened = true;
            MarkSmokeProgress("window-opened");

            Dispatcher.UIThread.Post(
                () =>
                {
                    loadedTurn = true;
                    MarkSmokeProgress("loaded-turn");

                    if (SmokeTestName == "todo")
                    {
                        sampleUpdated = ExerciseTodo(window);
                        if (sampleUpdated)
                        {
                            MarkSmokeProgress("todo-updated");
                        }

                        window.Close();
                    }
                    else
                    {
                        WaitForPackagePulse(window, success =>
                        {
                            sampleUpdated = success;
                            window.Close();
                        });
                    }
                },
                DispatcherPriority.Loaded);
        };

        window.Closed += (_, _) =>
        {
            closed = true;
            MarkSmokeProgress("window-closed");
            component.Dispose();
            lifetime.Shutdown(0);
        };

        var exitCode = lifetime.Start(args);
        if (exitCode != 0 ||
            !opened ||
            !loadedTurn ||
            !sampleUpdated ||
            !closed ||
            !exited)
        {
            Console.Error.WriteLine(
                "SMOKE: failed " +
                $"exitCode={exitCode} opened={opened} loadedTurn={loadedTurn} " +
                $"sample={SmokeTestName} sampleUpdated={sampleUpdated} " +
                $"closed={closed} exited={exited}");
            return 1;
        }

        return 0;
    }

    private static (Window Window, IDisposable Component) CreateSmokeTarget(string name)
    {
        IDisposable component;
        Control content;
        switch (name)
        {
            case "todo":
                var todo = new TodoComponent();
                component = todo;
                var todoRoots = todo.Mount();
                content = todoRoots.Count == 1 && todoRoots[0] is Control todoControl
                    ? todoControl
                    : throw new InvalidOperationException("Todo must mount exactly one Control root.");
                break;
            case "package-pulse":
                var packagePulse = new PackagePulseComponent();
                component = packagePulse;
                var packageRoots = packagePulse.Mount();
                content = packageRoots.Count == 1 && packageRoots[0] is Control packageControl
                    ? packageControl
                    : throw new InvalidOperationException("Package Pulse must mount exactly one Control root.");
                break;
            default:
                throw new ArgumentException($"Unknown smoke test '{name}'.", nameof(name));
        }

        return (new Window { Content = content }, component);
    }

    private static void WaitForPackagePulse(Window window, Action<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var phase = 0;
        var replacementQueued = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var query = Descendants(window).OfType<TextBox>().FirstOrDefault();
            var results = Descendants(window).OfType<StackPanel>()
                .FirstOrDefault(panel => panel.Classes.Contains("results"));
            var loading = Descendants(window).OfType<TextBlock>()
                .FirstOrDefault(text => text.Text == "Loading package index…");
            var empty = Descendants(window).OfType<TextBlock>()
                .FirstOrDefault(text => text.Text == "No packages match this query.");
            var failure = Descendants(window).OfType<TextBlock>()
                .FirstOrDefault(text => text.Text?.Contains("feed is unavailable", StringComparison.Ordinal) == true);
            if (query is null && phase < 3)
            {
                Finish(false, "shape");
                return;
            }

            switch (phase)
            {
                case 0 when loading is not null:
                    MarkSmokeProgress("package-pulse-initial-loading");
                    phase = 1;
                    break;
                case 1 when query is not null && results is not null && loading is null && PackageName(results) == "Lucent.UI":
                    MarkSmokeProgress("package-pulse-success");
                    SetQuery(query, "no-match");
                    phase = 2;
                    break;
                case 2 when query is not null && empty is not null:
                    MarkSmokeProgress("package-pulse-empty");
                    SetQuery(query, "fail");
                    phase = 3;
                    break;
                case 3 when failure is not null:
                    MarkSmokeProgress("package-pulse-failure");
                    var retry = Descendants(window).OfType<Button>().FirstOrDefault(button =>
                        string.Equals(button.Content?.ToString(), "Retry", StringComparison.Ordinal)) ??
                        failure?.GetVisualAncestors().OfType<Panel>()
                            .SelectMany(panel => panel.Children.OfType<Button>())
                            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Retry", StringComparison.Ordinal)) ??
                        (failure?.Parent as Panel)?.Children.OfType<Button>()
                            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Retry", StringComparison.Ordinal));
                    if (retry is null)
                    {
                        Finish(false, "missing-retry");
                        return;
                    }
                    retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    phase = 4;
                    break;
                case 4 when query is not null && results is not null && PackageName(results) == "Lucent.UI":
                    SetQuery(query, "avalonia");
                    phase = 5;
                    break;
                case 5 when query is not null && results is not null && PackageName(results) == "Lucent.UI":
                    MarkSmokeProgress("package-pulse-stale-results");
                    if (!replacementQueued)
                    {
                        replacementQueued = true;
                        SetQuery(query, "reactive");
                    }
                    phase = 6;
                    break;
                case 6 when results is not null && PackageName(results) == "Reactive.Core":
                    Finish(true, "latest-generation");
                    break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                Finish(false, $"timeout-phase-{phase}");
            }
        };
        timer.Start();

        void Finish(bool success, string marker)
        {
            timer.Stop();
            MarkSmokeProgress($"package-pulse-{marker}");
            completed(success);
        }

        static void SetQuery(TextBox query, string value)
        {
            query.Text = value;
            query.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        }

        static string? PackageName(StackPanel results) =>
            results.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(text => text.Classes.Contains("package-name"))?.Text;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var pending = new Stack<Control>([root]);
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            foreach (var child in current.GetVisualChildren().OfType<Control>()) pending.Push(child);
            if (current is ILogical logical)
                foreach (var child in logical.LogicalChildren.OfType<Control>()) pending.Push(child);
            if (current is Panel panel)
                foreach (var child in panel.Children) pending.Push(child);
            if (current is ContentControl content && content.Content is Control contentControl)
                pending.Push(contentControl);
            if (current is Decorator decorator && decorator.Child is Control decoratorChild)
                pending.Push(decoratorChild);
        }
    }

    private static bool ExerciseTodo(Window window)
    {
        if (window.Content is not Border { Child: StackPanel todoRoot } ||
            !Equals(window.Content is Border border ? border.Padding : default, new Thickness(32)) ||
            todoRoot.Children.Count != 8 ||
            todoRoot.Children[2] is not StackPanel inputRow ||
            inputRow.Children.Count != 2 ||
            inputRow.Children[0] is not TextBox
            {
                PlaceholderText: "What needs doing?",
            } draftInput ||
            inputRow.Children[1] is not Button addButton ||
            todoRoot.Children[3] is not CheckBox markAll ||
            todoRoot.Children[4] is not StackPanel todoList ||
            todoRoot.Children[5] is not TextBlock statusText ||
            todoRoot.Children[6] is not StackPanel filters ||
            todoRoot.Children[7] is not ProgressBar progress ||
            filters.Children.Count != 4 ||
            !Equals(addButton.Content, "Add task") ||
            todoList.Children.Count != 3 ||
            progress.Maximum != 3 ||
            progress.Value != 1)
        {
            return TodoFailure("initial-tree");
        }

        var firstRow = todoList.Children[0];
        var secondRow = todoList.Children[1];
        draftInput.Text = "Exercise generated TodoMVC";
        draftInput.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (todoList.Children.Count != 4 ||
            !ReferenceEquals(firstRow, todoList.Children[0]) ||
            !ReferenceEquals(secondRow, todoList.Children[1]) ||
            draftInput.Text != string.Empty ||
            statusText.Text != "3 item(s) left" ||
            progress.Maximum != 4 ||
            progress.Value != 1)
        {
            Console.Error.WriteLine(
                $"SMOKE: add-state rows={todoList.Children.Count} " +
                $"firstRetained={ReferenceEquals(firstRow, todoList.Children.ElementAtOrDefault(0))} " +
                $"secondRetained={ReferenceEquals(secondRow, todoList.Children.ElementAtOrDefault(1))} " +
                $"draft='{draftInput.Text}' status='{statusText.Text}' " +
                $"progress={progress.Value}/{progress.Maximum}");
            return TodoFailure("add");
        }

        if (!TryGetTodoRow(secondRow, out var secondToggle, out _, out _))
        {
            return TodoFailure("second-row-shape");
        }

        secondToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!ReferenceEquals(secondRow, todoList.Children[1]) ||
            statusText.Text != "2 item(s) left" ||
            progress.Value != 2)
        {
            return TodoFailure("toggle");
        }

        var thirdRow = todoList.Children[2];
        if (!TryGetTodoRow(thirdRow, out _, out var thirdTitle, out _))
        {
            return TodoFailure("third-row-shape");
        }

        thirdTitle.Text = "Edit a retained native row";
        thirdTitle.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        if (!ReferenceEquals(thirdRow, todoList.Children[2]) ||
            thirdTitle.Text != "Edit a retained native row")
        {
            return TodoFailure("edit");
        }

        ((Button)filters.Children[2]).RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));
        if (todoList.Children.Count != 2 ||
            !ReferenceEquals(firstRow, todoList.Children[0]) ||
            !ReferenceEquals(secondRow, todoList.Children[1]))
        {
            return TodoFailure("completed-filter");
        }

        ((Button)filters.Children[3]).RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));
        ((Button)filters.Children[0]).RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));
        if (todoList.Children.Count != 2 ||
            statusText.Text != "2 item(s) left")
        {
            return TodoFailure("clear-completed-filter");
        }

        var remainingRow = todoList.Children[1];
        if (!TryGetTodoRow(todoList.Children[0], out _, out _, out var deleteButton))
        {
            return TodoFailure("active-delete-shape");
        }

        deleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (todoList.Children.Count != 1 ||
            !ReferenceEquals(remainingRow, todoList.Children[0]) ||
            statusText.Text != "1 item(s) left")
        {
            return TodoFailure("delete");
        }

        markAll.IsChecked = true;
        markAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (statusText.Text != "0 item(s) left" || progress.Value != 1)
        {
            return TodoFailure("mark-all");
        }

        ((Button)filters.Children[3]).RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));
        var completed = todoList.Children.Count == 0 &&
            statusText.Text == "No tasks yet." &&
            progress.Maximum == 1 &&
            progress.Value == 0;
        return completed || TodoFailure("final-clear");
    }

    private static bool TryGetTodoRow(
        Control root,
        out CheckBox toggle,
        out TextBox title,
        out Button delete)
    {
        if (root is Border { Child: StackPanel row } &&
            row.Children.Count == 3 &&
            row.Children[0] is CheckBox rowToggle &&
            row.Children[1] is TextBox rowTitle &&
            row.Children[2] is Button rowDelete)
        {
            toggle = rowToggle;
            title = rowTitle;
            delete = rowDelete;
            return true;
        }

        toggle = null!;
        title = null!;
        delete = null!;
        return false;
    }

    private static bool TodoFailure(string checkpoint)
    {
        MarkSmokeProgress($"todo-failed-{checkpoint}");
        return false;
    }

    private static void MarkSmokeProgress(string marker)
    {
        Console.Error.WriteLine($"SMOKE: {marker}");
        Console.Error.Flush();
    }
}
