using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Automation;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new ArgumentException("Use <ready-json> <close-signal> <result-json>.");
            var ready = JsonSerializer.Deserialize<Ready>(File.ReadAllText(args[0])) ?? throw new InvalidOperationException("Missing host ready record.");
            if (!ready.Ok || ready.DynamicCodeSupported) throw new InvalidOperationException("Host was not a published NativeAOT executable.");
            var hwnd = (IntPtr)Convert.ToInt64(ready.Hwnd[2..], 16);
            var first = Read(hwnd);
            if (first.Name == "Lucent Native Issue Browser") return IssueBrowser(hwnd, args[1], args[2]);
            var second = Task.Run(() => Read(hwnd)).GetAwaiter().GetResult();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var third = Read(hwnd);
            var result = new Result(true, first, second, third,
                first == second && second == third && first.Name == "NativeStackProbe UIA root" &&
                first.AutomationId == "NativeStackProbe.Root");
            File.WriteAllText(args[2], JsonSerializer.Serialize(result));
            File.WriteAllText(args[1], "close");
            return result.Valid ? 0 : 1;
        }
        catch (Exception exception)
        {
            if (args.Length == 3) File.WriteAllText(args[2], JsonSerializer.Serialize(new Failure(false, exception.ToString())));
            return 1;
        }
    }

    private static Value Read(IntPtr hwnd)
    {
        var host = AutomationElement.FromHandle(hwnd) ?? throw new InvalidOperationException("UIA did not attach to the HWND.");
        var element = host;
        return new(element.Current.Name, element.Current.AutomationId, element.Current.ControlType.Id);
    }

    private static int IssueBrowser(IntPtr hwnd, string close, string resultPath)
    {
        var root = AutomationElement.FromHandle(hwnd) ?? throw new InvalidOperationException("UIA did not attach to issue browser HWND.");
        if (root.Current.AutomationId != "Lucent.NativeIssueBrowser.Root") throw new InvalidOperationException("UIA returned the HWND fallback instead of the issue-browser provider.");
        AutomationElement Find(string name, ControlType? type = null)
        {
            Condition condition = new PropertyCondition(AutomationElement.NameProperty, name);
            if (type is not null) condition = new AndCondition(condition, new PropertyCondition(AutomationElement.ControlTypeProperty, type));
            return root.FindFirst(TreeScope.Descendants, condition) ?? throw new InvalidOperationException($"Missing child {name}.");
        }
        var search = Find("Search issues", ControlType.Edit); var title = Find("Title", ControlType.Edit); var open = Find("Open", ControlType.Button); var list = Find("Issue list", ControlType.List);
        var selectionAvailable = (bool)list.GetCurrentPropertyValue(AutomationElement.IsSelectionPatternAvailableProperty);
        var initialItems = list.FindAll(TreeScope.Children, Condition.TrueCondition); var staleItem = initialItems.Count > 1 ? initialItems[1] : throw new InvalidOperationException("Missing initial list items.");
        var propertyEvents = 0; var focusEvents = 0; var structureEvents = 0;
        AutomationPropertyChangedEventHandler propertyHandler = (_, _) => System.Threading.Interlocked.Increment(ref propertyEvents);
        AutomationFocusChangedEventHandler focusHandler = (_, _) => System.Threading.Interlocked.Increment(ref focusEvents);
        StructureChangedEventHandler structureHandler = (_, _) => System.Threading.Interlocked.Increment(ref structureEvents);
        Automation.AddAutomationPropertyChangedEventHandler(root, TreeScope.Subtree, propertyHandler, ValuePattern.ValueProperty, SelectionItemPattern.IsSelectedProperty, AutomationElement.IsEnabledProperty);
        Automation.AddAutomationFocusChangedEventHandler(focusHandler);
        Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, structureHandler);
        try
        {
        var value = (ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern); value.SetValue("auth");
        System.Threading.Thread.Sleep(150);
        var staleRejected = false; try { _ = staleItem.Current.Name; } catch (ElementNotAvailableException) { staleRejected = true; }
        var searchValue = value.Current.Value == "auth";
        var titleValue = (ValuePattern)title.GetCurrentPattern(ValuePattern.Pattern); titleValue.SetValue("UIA child title");
        var titleSet = titleValue.Current.Value == "UIA child title";
        ((InvokePattern)open.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        System.Threading.Thread.Sleep(150);
        AutomationElement? item = null; SelectionItemPattern? selectionItem = null; var selectionItemAvailable = false;
        for (var attempt = 0; attempt != 5; attempt++)
        {
            try
            {
                item = list.FindFirst(TreeScope.Children, Condition.TrueCondition) ?? throw new InvalidOperationException("Missing list item.");
                selectionItemAvailable = (bool)item.GetCurrentPropertyValue(AutomationElement.IsSelectionItemPatternAvailableProperty);
                selectionItem = (SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern); selectionItem.Select(); break;
            }
            catch (ElementNotAvailableException) when (attempt != 4) { System.Threading.Thread.Sleep(30); }
        }
        if (item is null || selectionItem is null) throw new InvalidOperationException("Could not acquire a current list item.");
        item.SetFocus();
        selectionItem.AddToSelection();
        var removeRejected = false; try { selectionItem.RemoveFromSelection(); } catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException) { removeRejected = true; }
        var selected = selectionItem.Current.IsSelected && item.Current.HasKeyboardFocus && removeRejected && ((SelectionPattern)list.GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection().Length == 1;
        var enabled = open.Current.IsEnabled && title.Current.IsEnabled;
        var firstChild = TreeWalker.RawViewWalker.GetFirstChild(root);
        var lastChild = TreeWalker.RawViewWalker.GetLastChild(root);
        var navigation = firstChild is not null && lastChild is not null && TreeWalker.RawViewWalker.GetParent(firstChild) == root &&
            (firstChild == lastChild || TreeWalker.RawViewWalker.GetNextSibling(firstChild) is not null && TreeWalker.RawViewWalker.GetPreviousSibling(lastChild) is not null);
        foreach (var query in new[] { "Issue 1", "auth", "Issue 2", "auth" }) { value.SetValue(query); System.Threading.Thread.Sleep(100); _ = list.FindFirst(TreeScope.Children, Condition.TrueCondition); }
        System.Threading.Thread.Sleep(100);
        var broadQueries = BroadQuery(root);
        var events = propertyEvents > 0 && structureEvents > 0;
        var result = new IssueResult(true, searchValue, titleSet, selected, enabled, navigation, selectionAvailable && selectionItemAvailable, staleRejected, events, broadQueries, propertyEvents, focusEvents, structureEvents);
        File.WriteAllText(resultPath, JsonSerializer.Serialize(result)); File.WriteAllText(close, "close"); return result.Valid ? 0 : 1;
        }
        finally
        {
            Automation.RemoveAutomationPropertyChangedEventHandler(root, propertyHandler);
            Automation.RemoveAutomationFocusChangedEventHandler(focusHandler);
            Automation.RemoveStructureChangedEventHandler(root, structureHandler);
        }
    }

    private static int BroadQuery(AutomationElement root)
    {
        var queries = 0; var until = Environment.TickCount64 + 2_000;
        while (Environment.TickCount64 < until)
        {
            var elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement element in elements)
            {
                _ = element.Current.Name; _ = element.Current.AutomationId; _ = element.Current.ControlType;
                _ = element.Current.IsEnabled; _ = element.Current.HasKeyboardFocus; _ = element.Current.BoundingRectangle;
                foreach (var pattern in element.GetSupportedPatterns()) _ = element.GetCurrentPattern(pattern);
                _ = TreeWalker.RawViewWalker.GetParent(element);
                queries++;
            }
            var rectangle = root.Current.BoundingRectangle;
            if (!rectangle.IsEmpty) _ = AutomationElement.FromPoint(new System.Windows.Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2));
            _ = AutomationElement.FocusedElement;
        }
        return queries;
    }

    private sealed record Ready(bool Ok, string Hwnd, bool DynamicCodeSupported);
    private sealed record Value(string Name, string AutomationId, int ControlType);
    private sealed record Result(bool Ok, Value First, Value Second, Value Third, bool Valid);
    private sealed record Failure(bool Ok, string Error);
    private sealed record IssueResult(bool Ok, bool SearchValue, bool TitleValue, bool SelectionFocus, bool Enabled, bool Navigation, bool PatternAvailability, bool StaleRejected, bool Events, int BroadQueries, int PropertyEvents, int FocusEvents, int StructureEvents) { public bool Valid => SearchValue && TitleValue && SelectionFocus && Enabled && Navigation && PatternAvailability && StaleRejected && Events && BroadQueries > 0; }
}
