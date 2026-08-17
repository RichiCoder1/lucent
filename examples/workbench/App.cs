using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using AvaloniaEdit;
using System.Threading;

namespace Lucent.Examples.Workbench;

internal sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        Resources["WorkbenchAccent"] = new SolidColorBrush(Colors.CornflowerBlue);
        Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
        {
            ["Lucent.Canvas"] = new SolidColorBrush(Color.Parse("#F2F0E9")), ["Lucent.Surface"] = new SolidColorBrush(Color.Parse("#FFFEFA")), ["Lucent.SurfaceRaised"] = new SolidColorBrush(Colors.White),
            ["Lucent.Text"] = new SolidColorBrush(Color.Parse("#101820")), ["Lucent.TextMuted"] = new SolidColorBrush(Color.Parse("#56636A")),
            ["Lucent.Border"] = new SolidColorBrush(Color.Parse("#C7CCC8")), ["Lucent.Accent"] = new SolidColorBrush(Color.Parse("#007A7B")),
            ["Lucent.AccentVivid"] = new SolidColorBrush(Color.Parse("#00A6A6")), ["Lucent.Signal"] = new SolidColorBrush(Color.Parse("#C23F45")), ["Lucent.Success"] = new SolidColorBrush(Color.Parse("#287A4B")), ["Lucent.Warning"] = new SolidColorBrush(Color.Parse("#8A6200")), ["Lucent.Danger"] = new SolidColorBrush(Color.Parse("#A52E34")),
            ["Lucent.Focus"] = new SolidColorBrush(Color.Parse("#007A7B")), ["Lucent.DurationFast"] = TimeSpan.FromMilliseconds(120), ["Lucent.DurationAlign"] = TimeSpan.FromMilliseconds(180),
            ["SystemControlFocusVisualPrimaryBrush"] = new SolidColorBrush(Color.Parse("#007A7B")), ["SystemControlFocusVisualSecondaryBrush"] = new SolidColorBrush(Color.Parse("#007A7B")), ["SystemControlFocusVisualMargin"] = new Thickness(2), ["SystemControlFocusVisualPrimaryThickness"] = new Thickness(2), ["SystemControlFocusVisualSecondaryThickness"] = new Thickness(0),
            ["Lucent.Space1"] = 4d, ["Lucent.Space2"] = 8d, ["Lucent.Space3"] = 12d, ["Lucent.Space4"] = 16d, ["Lucent.Space6"] = 24d, ["Lucent.Space8"] = 32d, ["Lucent.RadiusSm"] = new CornerRadius(2), ["Lucent.RadiusMd"] = new CornerRadius(4), ["Lucent.RadiusLg"] = new CornerRadius(8),
        };
        Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            ["Lucent.Canvas"] = new SolidColorBrush(Color.Parse("#101820")), ["Lucent.Surface"] = new SolidColorBrush(Color.Parse("#17232C")), ["Lucent.SurfaceRaised"] = new SolidColorBrush(Color.Parse("#1E2E38")),
            ["Lucent.Text"] = new SolidColorBrush(Color.Parse("#F2F0E9")), ["Lucent.TextMuted"] = new SolidColorBrush(Color.Parse("#AAB6B6")),
            ["Lucent.Border"] = new SolidColorBrush(Color.Parse("#32444D")), ["Lucent.Accent"] = new SolidColorBrush(Color.Parse("#39C6C4")),
            ["Lucent.AccentVivid"] = new SolidColorBrush(Color.Parse("#39C6C4")), ["Lucent.Signal"] = new SolidColorBrush(Color.Parse("#FF8A72")), ["Lucent.Success"] = new SolidColorBrush(Color.Parse("#59C987")), ["Lucent.Warning"] = new SolidColorBrush(Color.Parse("#F2C14E")), ["Lucent.Danger"] = new SolidColorBrush(Color.Parse("#FF6670")),
            ["Lucent.Focus"] = new SolidColorBrush(Color.Parse("#39C6C4")), ["Lucent.DurationFast"] = TimeSpan.FromMilliseconds(120), ["Lucent.DurationAlign"] = TimeSpan.FromMilliseconds(180),
            ["SystemControlFocusVisualPrimaryBrush"] = new SolidColorBrush(Color.Parse("#39C6C4")), ["SystemControlFocusVisualSecondaryBrush"] = new SolidColorBrush(Color.Parse("#39C6C4")), ["SystemControlFocusVisualMargin"] = new Thickness(2), ["SystemControlFocusVisualPrimaryThickness"] = new Thickness(2), ["SystemControlFocusVisualSecondaryThickness"] = new Thickness(0),
            ["Lucent.Space1"] = 4d, ["Lucent.Space2"] = 8d, ["Lucent.Space3"] = 12d, ["Lucent.Space4"] = 16d, ["Lucent.Space6"] = 24d, ["Lucent.Space8"] = 32d, ["Lucent.RadiusSm"] = new CornerRadius(2), ["Lucent.RadiusMd"] = new CornerRadius(4), ["Lucent.RadiusLg"] = new CornerRadius(8),
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var qualityProfile = TryQualityProfile(desktop.Args ?? [], out var requestedProfile)
                ? requestedProfile : null;
            Environment.SetEnvironmentVariable("LUCENT_QUALITY_CAPTURE", qualityProfile);
            var lifetimeToken = new CancellationTokenSource();
            void ReportUnhandled(Exception error) => Console.Error.WriteLine($"Workbench error: {error}");
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Lucent", "Workbench", "settings.json");
            var settingsRepository = new JsonFileSettingsRepository(settingsPath, ReportUnhandled);
            var settings = settingsRepository.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var saveCoordinator = new SettingsSaveCoordinator(settingsRepository, CancellationToken.None);
            var documentSession = new DocumentSession(new OpenDocument("Program.cs", "// Workbench document\n"), () => { });
            var problemLoader = new PlaceholderProblemLoader();
#pragma warning disable LUC004A003 // event-owned modeless component is disposed by the native Closed handler below
            var component = new WorkbenchAppComponent(new AvaloniaWorkbenchDesktopHost(), lifetimeToken.Token,
                initialSettings: settings, saveCoordinator: saveCoordinator,
                errorReporter: ReportUnhandled, problemLoader: problemLoader, session: documentSession,
                __lucent_reportUnhandled: ReportUnhandled);
            var window = component.MountRoot();
            ApplyWindowIcon(window);
            AttachShutdown(window, lifetimeToken, component, documentSession, saveCoordinator, settingsRepository, ReportUnhandled);
            if (qualityProfile is not null)
            {
                SetQualitySize(window, qualityProfile);
                RequestedThemeVariant = qualityProfile.Contains("dark", StringComparison.OrdinalIgnoreCase) ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Opened += (_, _) => DispatcherTimer.RunOnce(() =>
                {
                    window.UpdateLayout();
                    SaveCapture(window, qualityProfile);
                    desktop.Shutdown(0);
                }, TimeSpan.FromMilliseconds(600), DispatcherPriority.Render);
            }
            desktop.MainWindow = window;
#pragma warning restore LUC004A003
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static int RunSmoke(string[] args)
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
        var documentSession = new DocumentSession(new OpenDocument("Program.cs", "// Workbench document\n"), () => { });
        var problemLoader = new PlaceholderProblemLoader();
#pragma warning disable LUC004A003 // event-owned smoke component is disposed by the native Closed handler below
        var component = new WorkbenchAppComponent(new AvaloniaWorkbenchDesktopHost(), lifetimeToken.Token,
            initialSettings: settings, saveCoordinator: saveCoordinator,
            errorReporter: ReportUnhandled, problemLoader: problemLoader, session: documentSession,
            __lucent_reportUnhandled: ReportUnhandled);
        var window = component.MountRoot();
        lifetime.MainWindow = window;
        var passed = false;
        AttachShutdown(window, lifetimeToken, component, documentSession, saveCoordinator, settingsRepository, ReportUnhandled,
            () => lifetime.Shutdown(passed ? 0 : 1));
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var buttons = Descendants(window).OfType<Button>().ToArray();
            var before = Descendants(window).OfType<TextBlock>().Any(text => text.Text == "3 problems");
            var openBinding = window.KeyBindings.First(binding =>
                binding.Gesture is KeyGesture gesture && gesture.Key == Key.O);
            var paletteBinding = window.KeyBindings.First(binding =>
                binding.Gesture is KeyGesture gesture && gesture.Key == Key.K);
            var toggleBinding = window.KeyBindings.First(binding =>
                binding.Gesture is KeyGesture gesture && gesture.Key == Key.T);
            var editor = Descendants(window).OfType<TextEditor>().First(textEditor =>
                textEditor.Text == "// Workbench document\n");
            var workspaceList = Descendants(window).OfType<ListBox>().First(list =>
                list.ItemsSource is IEnumerable<WorkspaceRow>);
            var problemsList = Descendants(window).OfType<ListBox>().First(list =>
                list.ItemsSource is IEnumerable<ProblemItem>);
            workspaceList.SelectedItem = workspaceList.Items.Cast<WorkspaceRow>().First(row => row.Node.Id == "readme");
            var workspaceSelected = workspaceList.SelectedItem is WorkspaceRow { Node.Id: "readme" };
            problemsList.SelectedIndex = 0;
            var problemSelected = problemsList.SelectedItem is ProblemItem;
            var dataVisible = workspaceList.Items.Count > 0 && problemsList.Items.Count == 3;
            window.UpdateLayout();
            var workspaceOpened = editor.Text == "// README.md\n" &&
                Descendants(window).OfType<TextBlock>().Any(text => text.Text == "README.md");
            editor.Focus();
            editor.AppendText("// smoke edit\n");
            var editorChanged = editor.Text.Contains("// smoke edit", StringComparison.Ordinal);
            paletteBinding.Command!.Execute(null);
            var quickOpenList = Descendants(window).OfType<ListBox>().First(list =>
                list.ItemsSource is IEnumerable<QuickOpenItem>);
            quickOpenList.SelectedIndex = 0;
            var quickOpenSelected = quickOpenList.SelectedItem is QuickOpenItem;
            var openMenu = Descendants(window).OfType<MenuItem>().First(item =>
                item.Header?.ToString() == "Open workspace");
            var sameCommand = ReferenceEquals(openBinding.Command, openMenu.Command);
            Descendants(window).OfType<Button>().First(button =>
                button.Content?.ToString() == "Close palette")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.KeyBindings.First(binding =>
                binding.Gesture is KeyGesture gesture && gesture.Key == Key.E).Command!.Execute(null);
            window.UpdateLayout();
            var focused = window.FocusManager?.GetFocusedElement();
            var focusRestored = focused is Visual visual &&
                (ReferenceEquals(visual, editor) || visual.GetVisualAncestors().Contains(editor));
            buttons.First(button => button.Content?.ToString() == "Increment edits")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            buttons.First(button => button.Content?.ToString() == "Switch document")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            toggleBinding.Command!.Execute(null);
            window.UpdateLayout();
            passed = before && Descendants(window).OfType<TextBlock>().Any(text => text.Text == "Edits: 1") &&
                Descendants(window).OfType<TextBlock>().Any(text => text.Text == "README.md") &&
                Descendants(window).OfType<TextBlock>().Any(text => text.Text == "Problems hidden" && text.IsVisible) &&
                sameCommand && focusRestored && workspaceSelected && problemSelected && quickOpenSelected &&
                editorChanged && workspaceOpened && dataVisible && quickOpenList.Items.Count > 0;
            window.Close();
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "lucent-workbench-main.txt"), $"|passed:{passed}");
        }, DispatcherPriority.Loaded);
#pragma warning restore LUC004A003
        return lifetime.Start(Array.Empty<string>());
    }

    private static void ApplyWindowIcon(Window window)
    {
        using var stream = AssetLoader.Open(new Uri(
            $"avares://{typeof(App).Assembly.GetName().Name}/Assets/lucent-icon-32.png"));
        window.Icon = new WindowIcon(stream);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private static bool TryQualityProfile(string[] args, out string profile)
    {
        var index = Array.IndexOf(args, "--quality-capture");
        profile = index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
        return profile is "workbench-light-shell" or "workbench-dark-palette";
    }

    private static void SaveCapture(Window window, string profile)
    {
        var path = Path.Combine("docs", "quality", "007-example-ux", "captures", profile + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)window.Bounds.Width), Math.Max(1, (int)window.Bounds.Height)));
        bitmap.Render(window);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }

    private static void SetQualitySize(Window window, string profile)
    {
        if (profile.Contains("light-shell", StringComparison.OrdinalIgnoreCase))
        {
            window.Width = 1280;
            window.Height = 800;
        }
        else
        {
            window.Width = 960;
            window.Height = 680;
        }
    }

    private static void AttachShutdown(
        Window window,
        CancellationTokenSource lifetime,
        WorkbenchAppComponent component,
        DocumentSession documentSession,
        SettingsSaveCoordinator saveCoordinator,
        JsonFileSettingsRepository settingsRepository,
        Action<Exception> reportUnhandled,
        Action? afterClosed = null)
    {
        var shuttingDown = false;
        var componentFaultReported = false;
        var saveFaultReported = false;
        var repositoryDisposed = false;

        void ReportComponentFault(Exception error)
        {
            if (!componentFaultReported)
            {
                componentFaultReported = true;
                reportUnhandled(error);
            }
        }

        void ReportSaveFault(Exception error)
        {
            if (!saveFaultReported)
            {
                saveFaultReported = true;
                reportUnhandled(error);
            }
        }

        void DisposeRepository()
        {
            if (repositoryDisposed) return;
            repositoryDisposed = true;
            settingsRepository.Dispose();
        }

        window.Closing += (_, args) =>
        {
            if (shuttingDown) return;
            args.Cancel = true;
            shuttingDown = true;
            window.IsEnabled = false;
            lifetime.Cancel();
            documentSession.Dispose();
            try { component.Dispose(); } catch (Exception error) { ReportComponentFault(error); }
            _ = FinishShutdownAsync();
        };
        window.Closed += (_, _) =>
        {
            saveCoordinator.Dispose();
            if (repositoryDisposed) DisposeRepository();
            lifetime.Dispose();
            afterClosed?.Invoke();
        };

        async Task FinishShutdownAsync()
        {
            var tail = saveCoordinator.Tail;
            try
            {
                await tail.WaitAsync(TimeSpan.FromSeconds(2));
                DisposeRepository();
            }
            catch (TimeoutException)
            {
                _ = tail.ContinueWith(completed =>
                {
                    if (completed.IsFaulted && completed.Exception is { } error)
                        ReportSaveFault(error);
                    DisposeRepository();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { DisposeRepository(); }
            catch (Exception error)
            {
                ReportSaveFault(error);
                DisposeRepository();
            }
            window.Close();
        }
    }
}
