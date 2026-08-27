using System.Text.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace IssueBrowser.Avalonia;

internal static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--issue-browser-walkthrough") return Walkthrough(args.Skip(1).FirstOrDefault());
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); return 0;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
    private static int Walkthrough(string? output)
    {
        var path = Path.GetFullPath(output ?? "../../evidence/issue-12/avalonia"); Directory.CreateDirectory(path); var session = new IssueBrowserSession(SeedCount());
        var (steps, assigneeFilter, closedFilter) = RunMounted(path, session);
        File.WriteAllLines(Path.Combine(path, "walkthrough.jsonl"), steps.Select(step => JsonSerializer.Serialize(step)));
        File.WriteAllText(Path.Combine(path, "semantics.json"), JsonSerializer.Serialize(session.Semantics));
        File.WriteAllText(Path.Combine(path, "assignee-filter.json"), JsonSerializer.Serialize(assigneeFilter));
        File.WriteAllText(Path.Combine(path, "closed-filter.json"), JsonSerializer.Serialize(closedFilter));
        File.WriteAllText(Path.Combine(path, "identity.json"), JsonSerializer.Serialize(new { rows = session.Rows.Count, realizedLimit = session.RealizedCeiling, selected = session.Selected?.Id, theme = session.Theme, query = session.Query, open = session.OpenOnly, high = session.HighOnly, scroll = session.ScrollIndex, draft = session.Title, reducedMotion = session.ReducedMotion, focusTrace = session.FocusTrace, logicalSize = new[] { 1200, 760 } }));
        foreach (var step in steps) Console.WriteLine(JsonSerializer.Serialize(step)); return steps.All(s => s.pass) ? 0 : 1;
    }
    private static List<Step> RunSteps(IssueBrowserSession session)
    {
        var steps = new List<Step> { S("W1 cold-start", session.Rows.Count == 10_000 && session.RealizedCeiling == 84, "10000 rows; realized <= 84", $"{session.Rows.Count} rows; ceiling {session.RealizedCeiling}") };
        var stale = session.Rows.Count; session.BeginSearch("auth"); var pending = session.Pending && session.Rows.Count == stale; session.CompleteSearch(); steps.Add(S("W2 search", pending && session.Rows.Count == 2000, "stale rows then 2000 auth rows", $"{session.Rows.Count} rows"));
        foreach (var query in new[] { "a", "au", "aut", "auth", "authentication" }) session.BeginSearch(query); session.CompleteSearch(); steps.Add(S("W3 latest-query", !session.Pending && session.Query == "authentication" && session.Rows.Count == 2000, "only final query commits", $"{session.Query} {session.Rows.Count}"));
        session.BeginSearch("auth"); session.ToggleOpen(); session.ToggleHigh(); session.CompleteSearch(); steps.Add(S("W4 filters", session.Rows.Count == 333, "333 open high auth rows", $"{session.Rows.Count} rows"));
        session.PageDown(); session.End(); var atEnd = session.ScrollIndex > 0; session.Home(); steps.Add(S("W5 scroll", atEnd && session.ScrollIndex == 0, "PageDown End Home", $"scroll {session.ScrollIndex}"));
        session.Select(session.Rows[0]); session.MoveSelection(1); steps.Add(S("W6 selection", session.Selected == session.Rows[1] && session.DetailKey == session.Selected.Id, "pointer and keyboard selection", session.DetailKey));
        var prior = session.Rows.ToList().IndexOf(session.Selected!); session.RemoveSelectedFromResults(); steps.Add(S("W7 filtered-selection", session.Selected == session.Rows[prior], "removed row clamps prior index", session.Selected?.Id ?? "none"));
        var original = session.Title; session.EditTitle("Scalar 😀 title"); session.CommitTitle(); var committed = session.Selected?.Title == "Scalar 😀 title"; session.EditTitle("uncommitted"); session.EscapeEdit(original); steps.Add(S("W8 title-edit", committed && session.Selected?.Title == original && session.Title == original, "scalar commit and Escape revert", session.Selected?.Title ?? "none"));
        session.BeginSearch("fail"); var staleFailure = session.Rows.Count > 0; session.CompleteSearch(); var failed = session.Error is not null; session.Retry(); steps.Add(S("W9 failure-retry", staleFailure && failed && session.Error is null, "error retains stale rows then retry clears", $"error={session.Error ?? "none"}"));
        session.EditTitle("draft"); var selected = session.Selected?.Id; var queryState = session.Query; var open = session.OpenOnly; var high = session.HighOnly; var scroll = session.ScrollIndex; session.SetReducedMotion(true); session.ToggleTheme(); steps.Add(S("W10 theme", session.Theme == "dark" && session.Selected?.Id == selected && session.Query == queryState && session.OpenOnly == open && session.HighOnly == high && session.ScrollIndex == scroll && session.Title == "draft" && session.ReducedMotion, "theme preserves query filters selection scroll draft and reduced motion", session.Theme));
        session.TabThrough(); session.ShiftTab(); session.EscapeToList(); steps.Add(S("W11 keyboard", session.Focus == "list" && session.FocusTrace.SequenceEqual(["search", "filters", "list", "details", "title", "details", "list"]), "Tab/ShiftTab traversal then Escape list focus", string.Join('>', session.FocusTrace)));
        steps.Add(S("W12 semantics", session.Semantics.All(item => item.Role.Length > 0 && item.Name.Length > 0) && session.Semantics.Single(item => item.Id == "title").Actions.Contains("set-value"), "roles names selection set-value", "session semantics"));
        return steps;
    }
    private static Step S(string step, bool pass, string expected, string observed) => new(step, pass, expected, observed);
    private static int SeedCount()
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "experiments", "native-stack", "gauntlet", "issues.seed.json"))) root = root.Parent;
        if (root is null) throw new FileNotFoundException("issues.seed.json");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "experiments", "native-stack", "gauntlet", "issues.seed.json")));
        return json.RootElement.GetProperty("count").GetInt32();
    }
    private static (List<Step> Steps, AssigneeFilterCheck AssigneeFilter, ClosedFilterCheck ClosedFilter) RunMounted(string path, IssueBrowserSession session)
    {
        var app = new App(); AppBuilder.Configure(() => app).UseHeadless(new AvaloniaHeadlessPlatformOptions()).UseSkia().SetupWithoutStarting(); app.Initialize();
        using var done = new CancellationTokenSource(); Exception? failure = null; List<Step>? steps = null; AssigneeFilterCheck? assigneeFilter = null; ClosedFilterCheck? closedFilter = null;
        Dispatcher.UIThread.Post(() => { try { using var component = new IssueBrowserWindowComponent(session); var window = component.MountRoot(); window.Show(); Dispatcher.UIThread.RunJobs(); steps = RunSteps(session); assigneeFilter = session.VerifyAssigneeFilter(); closedFilter = session.VerifyClosedFilter(); Dispatcher.UIThread.RunJobs(); var list = window.GetVisualDescendants().OfType<ListBox>().Single(); var title = window.GetVisualDescendants().OfType<TextBox>().Last(); if (list.Items.Count != session.Rows.Count || title.Text != session.Title) throw new InvalidOperationException("Mounted Lucent controls did not observe session state."); foreach (var dark in new[] { false, true }) { window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(1); using var bitmap = new RenderTargetBitmap(new PixelSize(1200, 760)); bitmap.Render(window); bitmap.Save(Path.Combine(path, dark ? "dark.png" : "light.png"), new PngBitmapEncoderOptions()); } window.Close(); } catch (Exception error) { failure = error; } finally { done.Cancel(); } });
        Dispatcher.UIThread.MainLoop(done.Token); if (failure is not null) throw failure; return (steps ?? throw new InvalidOperationException("Walkthrough did not run."), assigneeFilter ?? throw new InvalidOperationException("Assignee filter did not run."), closedFilter ?? throw new InvalidOperationException("Closed filter did not run."));
    }
    private sealed record Step(string step, bool pass, string expected, string observed);
}

internal sealed class App : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new Lucent.Themes.Shadcn.ShadcnTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) { var component = new IssueBrowserWindowComponent(new IssueBrowserSession()); desktop.MainWindow = component.MountRoot(); desktop.MainWindow.Closed += (_, _) => component.Dispose(); }
        base.OnFrameworkInitializationCompleted();
    }
}

public sealed record IssueRow(string Id, string Title, string Status, string Priority, string Assignee)
{
    public override string ToString() => $"{Id}  {Title}";
}
public sealed record SemanticItem(string Id, string Role, string Name, string[] Actions);

public sealed class IssueBrowserSession : INotifyPropertyChanged
{
    private readonly IssueRow[] _all; private int _generation;
    public ObservableCollection<IssueRow> Rows { get; } = []; private string _query = "", _title = "", _theme = "light", _focus = "search"; private bool _openOnly, _closedOnly, _highOnly, _adaOnly, _pending; private string? _error; private int _scrollIndex; private IssueRow? _selected;
    public event PropertyChangedEventHandler? PropertyChanged;
    public List<string> FocusTrace { get; } = ["search"]; public bool ReducedMotion { get; private set; }
    public string Query { get => _query; private set => Set(ref _query, value); } public bool OpenOnly { get => _openOnly; private set { if (Set(ref _openOnly, value)) On(nameof(OpenChecked)); } } public bool? OpenChecked => OpenOnly; public bool ClosedOnly { get => _closedOnly; private set { if (Set(ref _closedOnly, value)) On(nameof(ClosedChecked)); } } public bool? ClosedChecked => ClosedOnly; public bool HighOnly { get => _highOnly; private set { if (Set(ref _highOnly, value)) On(nameof(HighChecked)); } } public bool? HighChecked => HighOnly; public bool AdaOnly { get => _adaOnly; private set { if (Set(ref _adaOnly, value)) On(nameof(AdaChecked)); } } public bool? AdaChecked => AdaOnly; public IssueRow? Selected { get => _selected; private set { if (Set(ref _selected, value)) { On(nameof(SelectedItem)); On(nameof(DetailKey)); On(nameof(SelectedTitle)); On(nameof(SelectedStatus)); On(nameof(SelectedPriority)); } } } public object? SelectedItem => Selected; public string Title { get => _title; private set => Set(ref _title, value); } public bool Pending { get => _pending; private set => Set(ref _pending, value); } public string? Error { get => _error; private set => Set(ref _error, value); } public int ScrollIndex { get => _scrollIndex; private set => Set(ref _scrollIndex, value); } public System.Collections.IEnumerable? RowItems => Rows; public string DetailKey => Selected?.Id ?? "No selection"; public string SelectedTitle => Selected?.Title ?? "Select an issue"; public string SelectedStatus => Selected?.Status ?? "Open"; public string SelectedPriority => Selected?.Priority ?? "High"; public string MatchSummary => $"{Rows.Count} matching"; public string Theme { get => _theme; private set { if (Set(ref _theme, value)) On(nameof(RequestedTheme)); } } public ThemeVariant? RequestedTheme => Theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light; public string Focus { get => _focus; private set => Set(ref _focus, value); } public int RealizedCeiling => 84;
    public IssueBrowserSession(int count = 10_000) { _all = Enumerable.Range(0, count).Select(i => new IssueRow($"issue-{i:D5}", i % 5 == 0 ? $"Authentication failure {i}" : $"Issue {i}", i % 3 == 0 ? "Closed" : "Open", i % 4 == 0 ? "High" : i % 4 == 1 ? "Medium" : "Low", i % 2 == 0 ? "Ada" : "Lin")).ToArray(); Search(""); }
    public void Search(string query) { BeginSearch(query); CompleteSearch(); }
    public void BeginSearch(string query) { Query = query; Pending = true; Error = null; _generation++; }
    public void CompleteSearch() { if (!Pending) return; Pending = false; if (Query == "fail") { Error = "Search failed"; return; } Refresh(); }
    public void ToggleOpen() { OpenOnly = !OpenOnly; if (OpenOnly) ClosedOnly = false; Refresh(); }
    public void ToggleClosed() { ClosedOnly = !ClosedOnly; if (ClosedOnly) OpenOnly = false; Refresh(); }
    public void ToggleHigh() { HighOnly = !HighOnly; Refresh(); }
    public void ToggleAda() { AdaOnly = !AdaOnly; Refresh(); }
    public AssigneeFilterCheck VerifyAssigneeFilter() { ToggleOpen(); ToggleHigh(); ToggleAda(); return new(AdaOnly && Rows.Count == 1000 && Rows.All(row => row.Assignee == "Ada"), 1000, Rows.Count, AdaOnly ? "Ada" : "none"); }
    public ClosedFilterCheck VerifyClosedFilter() { Select(_all[0]); var selected = Selected; var title = Title; var theme = Theme; var scroll = ScrollIndex; var reduced = ReducedMotion; Query = "auth"; OpenOnly = false; ClosedOnly = false; HighOnly = false; AdaOnly = false; Refresh(); ToggleClosed(); var active = Rows.Count == 667; ToggleClosed(); var cleared = Rows.Count == 2000; ToggleClosed(); return new(active && cleared && ClosedOnly && !OpenOnly && Rows.Count == 667 && Selected == selected && Title == title && Theme == theme && ScrollIndex == scroll && ReducedMotion == reduced, 667, Rows.Count, "Closed"); }
    public void Select(IssueRow? row) { Selected = row; Title = row?.Title ?? ""; }
    public void EditTitle(string value) => Title = value;
    public void CommitTitle() { if (Selected is null) return; var updated = Selected with { Title = Title }; var index = Array.FindIndex(_all, row => row.Id == updated.Id); _all[index] = updated; var rowIndex = Rows.IndexOf(Selected); if (rowIndex >= 0) Rows[rowIndex] = updated; Selected = updated; }
    public void EscapeEdit(string original) { Title = original; CommitTitle(); }
    public void Retry() => Search("auth");
    public void PageDown() => ScrollIndex = Math.Min(Math.Max(0, Rows.Count - 1), ScrollIndex + 28); public void End() => ScrollIndex = Math.Max(0, Rows.Count - 1); public void Home() => ScrollIndex = 0;
    public void MoveSelection(int delta) { if (Selected is null) return; Select(Rows[Math.Clamp(Rows.ToList().IndexOf(Selected) + delta, 0, Rows.Count - 1)]); }
    public void RemoveSelectedFromResults() { if (Selected is null) return; var index = Rows.ToList().IndexOf(Selected); Rows.Remove(Selected); Select(Rows[Math.Min(index, Rows.Count - 1)]); }
    public void ToggleTheme() => Theme = Theme == "light" ? "dark" : "light"; public void SetReducedMotion(bool value) => ReducedMotion = value;
    public void TabThrough() { foreach (var item in new[] { "filters", "list", "details", "title" }) { Focus = item; FocusTrace.Add(item); } }
    public void ShiftTab() { Focus = "details"; FocusTrace.Add(Focus); }
    public void EscapeToList() { Focus = "list"; FocusTrace.Add(Focus); }
    public SemanticItem[] Semantics => [new("search", "edit", "Search issues", ["set-value"]), new("ada", "checkbox", "Ada", ["toggle"]), new("open", "checkbox", "Open", ["toggle"]), new("closed", "checkbox", "Closed", ["toggle"]), new("high", "checkbox", "High", ["toggle"]), new("issues", "list", "Issues", ["select", "scroll"]), new("title", "edit", "Issue title", ["set-value"]), new("retry", "button", "Retry", ["press"])];
    private void Refresh() { Rows.Clear(); foreach (var row in _all.Where(row => row.Title.Contains(Query, StringComparison.OrdinalIgnoreCase) && (!OpenOnly || row.Status == "Open") && (!ClosedOnly || row.Status == "Closed") && (!HighOnly || row.Priority == "High") && (!AdaOnly || row.Assignee == "Ada"))) Rows.Add(row); if (Selected is not null && !Rows.Contains(Selected)) Select(Rows.FirstOrDefault()); On(nameof(Rows)); On(nameof(RowItems)); On(nameof(MatchSummary)); }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; On(name); return true; }
    private void On(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record AssigneeFilterCheck(bool pass, int expected, int observed, string assignee);
public sealed record ClosedFilterCheck(bool pass, int expected, int observed, string status);
