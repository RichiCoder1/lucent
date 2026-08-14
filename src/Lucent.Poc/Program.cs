using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Lucent.Poc;

internal static class Program
{
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
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        BuildAvaloniaApp().SetupWithLifetime(lifetime);

        var window = lifetime.MainWindow
            ?? throw new InvalidOperationException(
                "Smoke test expected App to create the main window during setup.");

        var opened = false;
        var loadedTurn = false;
        var todoUpdated = false;
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

                    todoUpdated = ExerciseTodo(window);
                    if (todoUpdated)
                    {
                        MarkSmokeProgress("todo-updated");
                    }

                    window.Close();
                },
                DispatcherPriority.Loaded);
        };

        window.Closed += (_, _) =>
        {
            closed = true;
            MarkSmokeProgress("window-closed");
            lifetime.Shutdown(0);
        };

        var exitCode = lifetime.Start(args);
        if (exitCode != 0 ||
            !opened ||
            !loadedTurn ||
            !todoUpdated ||
            !closed ||
            !exited)
        {
            Console.Error.WriteLine(
                "SMOKE: failed " +
                $"exitCode={exitCode} opened={opened} loadedTurn={loadedTurn} " +
                $"todoUpdated={todoUpdated} closed={closed} exited={exited}");
            return 1;
        }

        return 0;
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
