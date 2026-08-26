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
using Lucent.Examples;
using System.Threading;

namespace Lucent.Examples.Workbench;

internal sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new Lucent.Themes.Shadcn.ShadcnTheme());
        Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var qualityProfile = ExampleQualityCapture.Profile(
                desktop.Args ?? [], "workbench-light-shell", "workbench-dark-palette");
            var lifetimeToken = new CancellationTokenSource();
            void ReportUnhandled(Exception error) => Console.Error.WriteLine($"Workbench error: {error}");
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Lucent", "Workbench", "settings.json");
            var settingsRepository = new JsonFileSettingsRepository(settingsPath, ReportUnhandled);
            var settings = settingsRepository.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (qualityProfile is not null) settings = settings with { SidebarWidth = 220 };
            RequestedThemeVariant = settings.Theme switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
            if (qualityProfile is not null)
                RequestedThemeVariant = qualityProfile.Contains("dark", StringComparison.OrdinalIgnoreCase)
                    ? ThemeVariant.Dark
                    : ThemeVariant.Light;
            var saveCoordinator = new SettingsSaveCoordinator(settingsRepository, CancellationToken.None);
            var workspace = new WorkspaceService();
            RestoreWorkspaceAsync(workspace, settings, CancellationToken.None, ReportUnhandled).GetAwaiter().GetResult();
            var documentSession = CreateSession(workspace);
            var problemLoader = new ProjectProblemLoader(workspace);
#pragma warning disable LUC004A003 // event-owned modeless component is disposed by the native Closed handler below
            var component = new WorkbenchAppComponent(new AvaloniaWorkbenchDesktopHost(), lifetimeToken.Token,
                initialSettings: settings, saveCoordinator: saveCoordinator,
                errorReporter: ReportUnhandled, problemLoader: problemLoader, session: documentSession, workspace: workspace,
                initialShowPalette: qualityProfile == "workbench-dark-palette",
                __lucent_reportUnhandled: ReportUnhandled);
            var window = component.MountRoot();
            ExampleQualityCapture.ApplyIcon<App>(window);
            AttachShutdown(window, lifetimeToken, component, documentSession, saveCoordinator, settingsRepository, ReportUnhandled);
            if (qualityProfile is not null)
            {
                var size = qualityProfile.Contains("light-shell", StringComparison.OrdinalIgnoreCase)
                    ? new PixelSize(1280, 800)
                    : new PixelSize(960, 680);
                ExampleQualityCapture.Configure(this, desktop, window, qualityProfile, size,
                    TimeSpan.FromMilliseconds(600), candidate =>
                    {
                        var tree = ExampleQualityCapture.Descendants(candidate).OfType<ListBox>()
                            .FirstOrDefault(list => list.ItemsSource is IEnumerable<WorkspaceRow>);
                        if (tree is not null && tree.Items.Count > 0) tree.SelectedIndex = 0;
                    });
            }
            desktop.MainWindow = window;
#pragma warning restore LUC004A003
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static DocumentSession CreateSession(WorkspaceService workspace)
    {
        var document = new OpenDocument("Program.cs", "// Open a Lucent workspace.\n");
        if (workspace.QuickOpenItems.FirstOrDefault() is { } item)
            document = new OpenDocument(item.Path, workspace.ReadAsync(item.Path, CancellationToken.None).GetAwaiter().GetResult());
        return new DocumentSession(document, () => { });
    }

    internal static async Task RestoreWorkspaceAsync(
        WorkspaceService workspace,
        WorkbenchSettings settings,
        CancellationToken cancellationToken,
        Action<Exception>? errorReporter = null)
    {
        if (string.IsNullOrWhiteSpace(settings.RecentWorkspace) || !Directory.Exists(settings.RecentWorkspace))
            return;
        try
        {
            await workspace.OpenAsync(settings.RecentWorkspace, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errorReporter?.Invoke(exception);
            workspace.Close();
        }
    }

    internal static void AttachShutdown(
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
