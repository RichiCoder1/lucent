using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace Lucent.Examples.Workbench;

internal static class WorkbenchSmoke
{
    public static int Run(string[] args)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);
        var lifetimeToken = new CancellationTokenSource();
        void ReportUnhandled(Exception error) => Console.Error.WriteLine($"Workbench error: {error}");
        var settingsPath = Path.Combine(Path.GetTempPath(), "lucent-workbench-settings.json");
        File.Delete(settingsPath);
        var settingsRepository = new JsonFileSettingsRepository(settingsPath, ReportUnhandled);
        var settings = settingsRepository.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        var saveCoordinator = new SettingsSaveCoordinator(settingsRepository, CancellationToken.None);
        var workspace = new WorkspaceService();
        var documentSession = App.CreateSession(workspace);
        var problemLoader = new ProjectProblemLoader(workspace);
#pragma warning disable LUC004A003
        var component = new WorkbenchAppComponent(new AvaloniaWorkbenchDesktopHost(), lifetimeToken.Token,
            initialSettings: settings, saveCoordinator: saveCoordinator,
            errorReporter: ReportUnhandled, problemLoader: problemLoader, session: documentSession, workspace: workspace,
            __lucent_reportUnhandled: ReportUnhandled);
        var window = component.MountRoot();
        lifetime.MainWindow = window;
        var passed = false;
        App.AttachShutdown(window, lifetimeToken, component, documentSession, saveCoordinator, settingsRepository, ReportUnhandled,
            () => lifetime.Shutdown(passed ? 0 : 1));
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var before = Descendants(window).OfType<TextBlock>().Any(text => text.Text == "0 problems");
            var openBinding = window.KeyBindings.First(binding => binding.Gesture is KeyGesture { Key: Key.O });
            var paletteBinding = window.KeyBindings.First(binding => binding.Gesture is KeyGesture { Key: Key.K });
            var toggleBinding = window.KeyBindings.First(binding => binding.Gesture is KeyGesture { Key: Key.T });
            var editor = Descendants(window).OfType<TextEditor>().First();
            var workspaceList = Descendants(window).OfType<ListBox>().First(list => list.ItemsSource is IEnumerable<WorkspaceRow>);
            var problemsList = Descendants(window).OfType<ListBox>().First(list => list.ItemsSource is IEnumerable<ProblemItem>);
            workspaceList.SelectedItem = workspaceList.Items.Cast<WorkspaceRow>().First(row => row.Node.Id == "readme");
            var workspaceSelected = workspaceList.SelectedItem is WorkspaceRow { Node.Id: "readme" };
            var problemSelected = problemsList.Items.Count == 0;
            var dataVisible = workspaceList.Items.Count > 0 && problemsList.Items.Count == 0;
            window.UpdateLayout();
            var workspaceOpened = editor.Text == "// README.md\n" &&
                Descendants(window).OfType<TextBlock>().Any(text => text.Text == "README.md");
            editor.Focus();
            editor.AppendText("// smoke edit\n");
            var editorChanged = editor.Text.Contains("// smoke edit", StringComparison.Ordinal);
            paletteBinding.Command!.Execute(null);
            var quickOpenList = Descendants(window).OfType<ListBox>().First(list => list.ItemsSource is IEnumerable<QuickOpenItem>);
            quickOpenList.SelectedIndex = 0;
            var quickOpenSelected = quickOpenList.SelectedItem is QuickOpenItem;
            var openMenu = Descendants(window).OfType<MenuItem>().First(item => item.Header?.ToString() == "Open workspace");
            var sameCommand = ReferenceEquals(openBinding.Command, openMenu.Command);
            Descendants(window).OfType<Button>().First(button => button.Content?.ToString() == "Close palette")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.KeyBindings.First(binding => binding.Gesture is KeyGesture { Key: Key.E }).Command!.Execute(null);
            window.UpdateLayout();
            var focused = window.FocusManager?.GetFocusedElement();
            var focusRestored = focused is Visual visual &&
                (ReferenceEquals(visual, editor) || visual.GetVisualAncestors().Contains(editor));
            toggleBinding.Command!.Execute(null);
            window.UpdateLayout();
            passed = before && Descendants(window).OfType<TextBlock>().Any(text => text.Text == "README.md") &&
                Descendants(window).OfType<TextBlock>().Any(text => text.Text == "Problems hidden" && text.IsVisible) &&
                sameCommand && focusRestored && workspaceSelected && problemSelected && quickOpenSelected &&
                editorChanged && workspaceOpened && dataVisible && quickOpenList.Items.Count > 0;
            window.Close();
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "lucent-workbench-main.txt"), $"|passed:{passed}");
        }, DispatcherPriority.Loaded);
#pragma warning restore LUC004A003
        return lifetime.Start([]);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child))
                yield return nested;
    }
}
