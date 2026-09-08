using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>Exact UIAutomationCore.idl COM fragments over an atomically published semantic snapshot; only commands enter Core on the SDL owner thread.</summary>
internal sealed unsafe partial class WindowsUiaProvider : IDisposable
{
    internal const int Ok = 0,
        Fail = unchecked((int)0x80004005),
        NotAvailable = unchecked((int)0x80040201),
        ElementNotEnabled = unchecked((int)0x80040200),
        InvalidOperation = unchecked((int)0x80131509),
        InvalidArgument = unchecked((int)0x80070057),
        OutOfMemory = unchecked((int)0x8007000e);
    private const int VtI4 = 3,
        VtBstr = 8,
        VtBool = 11,
        VtUnknown = 13;
    private static readonly UiaWrappers Wrappers = new();
    private readonly nint _hwnd;
    private readonly string _applicationName;
    private readonly Composition _composition;
    private readonly WindowsUiaDispatcher _dispatcher;
    private readonly WindowsUiaProvider _root;
    private readonly NodeKey? _key;
    private readonly Dictionary<NodeKey, WindowsUiaProvider>? _cache;
    private Snapshot _snapshot = Snapshot.Empty;
    private nint _unknown,
        _simple;
    private int _disposed;

    internal WindowsUiaProvider(
        nint hwnd,
        Composition composition,
        WindowsUiaDispatcher dispatcher,
        string applicationName
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(hwnd, nameof(hwnd));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        _applicationName = applicationName;
        _hwnd = hwnd;
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _root = this;
        _cache = [];
        _unknown = Wrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
        _simple = Query(_unknown, UiaWrappers.Simple);
    }

    private WindowsUiaProvider(WindowsUiaProvider root, NodeKey key)
    {
        _hwnd = root._hwnd;
        _applicationName = root._applicationName;
        _composition = root._composition;
        _dispatcher = root._dispatcher;
        _root = root;
        _key = key;
        _unknown = Wrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
        _simple = Query(_unknown, UiaWrappers.Simple);
    }

    internal nint InterfacePointer => _simple;

    internal void RecordRootDelivery() => _dispatcher.RecordRootDelivery();

    internal void RecordListenerFailure() => _dispatcher.RecordListenerFailure();

    internal bool IsRoot => _key is null;
    internal SemanticRole? ProviderRole => CurrentNode()?.Role;
    internal SemanticAction ProviderActions => CurrentNode()?.Actions ?? SemanticAction.None;
    internal bool ProviderHasRange => CurrentNode()?.Range is not null;
    internal int CacheCount
    {
        get
        {
            var cache = _root._cache;
            if (cache is null)
                return 0;
            lock (cache)
                return cache.Count;
        }
    }
    internal int MaxCacheCount { get; private set; }
    internal int StaleCount { get; private set; }
    internal bool IsAvailable =>
        Volatile.Read(ref _disposed) == 0
        && Volatile.Read(ref _root._disposed) == 0
        && (IsRoot || CurrentNode() is not null);

    /// <summary>WndProc teardown only makes callbacks unavailable; owner disposal releases COM after it unwinds.</summary>
    internal void MarkUnavailable() => Interlocked.Exchange(ref _disposed, 1);

    // Counts detected semantic changes, even when no external UIA client is listening.
    internal long DetectedStructureChanges { get; private set; }
    internal long DetectedPropertyChanges { get; private set; }

    /// <summary>Called only at the Bootstrap safe point after Core projects a scene.</summary>
    internal void Refresh(RetainedScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!IsRoot || Volatile.Read(ref _disposed) != 0)
            return;
        var next = Flatten(_composition.SemanticSnapshot(), scene)
            .ToFrozenDictionary(node => node.Key);
        var prior = Volatile.Read(ref _snapshot);
        var snapshot = new Snapshot(next);
        if (!HasAcyclicTree(snapshot))
            throw new InvalidOperationException(
                "UIA fragment navigation does not match the retained semantic tree."
            );
        Volatile.Write(ref _snapshot, snapshot);
        var cache =
            _cache ?? throw new InvalidOperationException("Only the UIA root owns a cache.");
        WindowsUiaProvider[] stale;
        lock (cache)
        {
            var stalePairs = cache.Where(pair => !next.ContainsKey(pair.Key)).ToArray();
            stale = stalePairs.Select(pair => pair.Value).ToArray();
            foreach (var pair in stalePairs)
                cache.Remove(pair.Key);
            StaleCount += stale.Length;
        }
        foreach (var provider in stale)
            provider.Dispose();
        _dispatcher.RecordProvider(CacheCount, MaxCacheCount, StaleCount);
        foreach (var removed in prior.Nodes.Values.Where(node => !next.ContainsKey(node.Key)))
            RaiseStructure(
                removed.Parent is { } parent && next.TryGetValue(parent, out var source)
                    ? Provider(source) ?? this
                    : this,
                1,
                removed
            );
        foreach (var added in next.Values.Where(node => !prior.Nodes.ContainsKey(node.Key)))
            RaiseStructure(
                added.Parent is { } parent && next.TryGetValue(parent, out var source)
                    ? Provider(source) ?? this
                    : this,
                0,
                added
            );
        foreach (var node in next.Values)
        {
            if (!prior.Nodes.TryGetValue(node.Key, out var old))
                continue;
            if (old.Name != node.Name)
                RaiseProperty(node, 30005, old.Name, node.Name);
            if (old.Value != node.Value)
                RaiseProperty(node, 30045, old.Value ?? "", node.Value ?? "");
            if (old.Selected != node.Selected)
                RaiseProperty(node, 30079, old.Selected, node.Selected);
            if (old.Enabled != node.Enabled)
                RaiseProperty(node, 30010, old.Enabled, node.Enabled);
            if (old.Expanded != node.Expanded)
                RaiseProperty(
                    node,
                    30023,
                    ExpandCollapseStateValue(old.Expanded),
                    ExpandCollapseStateValue(node.Expanded)
                );
            if (old.Range?.Value != node.Range?.Value)
                RaiseProperty(
                    node,
                    30047,
                    old.Range?.Value ?? double.NaN,
                    node.Range?.Value ?? double.NaN
                );
            if (old.Range?.IsReadOnly != node.Range?.IsReadOnly)
                RaiseProperty(
                    node,
                    30048,
                    old.Range?.IsReadOnly ?? true,
                    node.Range?.IsReadOnly ?? true
                );
            if (old.Range?.Maximum != node.Range?.Maximum)
                RaiseProperty(
                    node,
                    30049,
                    old.Range?.Maximum ?? double.NaN,
                    node.Range?.Maximum ?? double.NaN
                );
            if (old.Range?.Minimum != node.Range?.Minimum)
                RaiseProperty(
                    node,
                    30050,
                    old.Range?.Minimum ?? double.NaN,
                    node.Range?.Minimum ?? double.NaN
                );
            if (old.Range?.LargeChange != node.Range?.LargeChange)
                RaiseProperty(
                    node,
                    30051,
                    old.Range?.LargeChange ?? double.NaN,
                    node.Range?.LargeChange ?? double.NaN
                );
            if (old.Range?.SmallChange != node.Range?.SmallChange)
                RaiseProperty(
                    node,
                    30052,
                    old.Range?.SmallChange ?? double.NaN,
                    node.Range?.SmallChange ?? double.NaN
                );
            if (old.Scroll != node.Scroll && node.Scroll is { } scroll)
                RaiseProperty(node, 30055, ScrollPercent(old.Scroll), ScrollPercent(scroll));
            if (
                node.Actions.HasFlag(SemanticAction.SelectText)
                && old.Text?.Text != node.Text?.Text
            )
                RaiseTextEvent(node, 20015);
            if (
                node.Actions.HasFlag(SemanticAction.SelectText)
                && (
                    old.Text?.Anchor != node.Text?.Anchor
                    || old.Text?.Caret != node.Text?.Caret
                    || old.Text?.AnchorAffinity != node.Text?.AnchorAffinity
                    || old.Text?.CaretAffinity != node.Text?.CaretAffinity
                )
            )
                RaiseTextEvent(node, 20014);
            if (!old.Focused && node.Focused)
                RaiseFocus(node);
        }
    }

    /// <summary>Owner-thread assertion: each retained node has one acyclic path from the synthetic root.</summary>
    private static bool HasAcyclicTree(Snapshot snapshot)
    {
        var visited = new HashSet<NodeKey>();
        var pending = new Stack<Node>(snapshot.Roots);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node.Key))
                return false;
            foreach (var child in snapshot.Children(node.Key))
                pending.Push(child);
        }
        return visited.Count == snapshot.Nodes.Count;
    }

    private IEnumerable<Node> Flatten(SemanticSnapshot? root, RetainedScene scene)
    {
        if (root is null)
            yield break;
        var bounds = scene.Boxes.ToDictionary(
            box => new NodeKey(box.Identity.CompositionEpoch, box.Identity.ElementId),
            box => box.Bounds
        );
        var input = scene.Input.ToDictionary(item => item.Identity);
        var textScenes = TextScenes(scene);
        var ordinal = 0L;
        foreach (var node in Flatten(root, null))
            yield return node;
        IEnumerable<Node> Flatten(SemanticSnapshot snapshot, NodeKey? parent)
        {
            var key = new NodeKey(snapshot.Identity.CompositionEpoch, snapshot.Identity.ElementId);
            var elementIdentity = new ElementIdentity(
                snapshot.Identity.CompositionEpoch,
                snapshot.Identity.ElementId
            );
            var scroll = snapshot.Actions.HasFlag(SemanticAction.Scroll)
                ? _composition.Input.GetSemanticScroll(elementIdentity)
                : null;
            var text = snapshot.Text;
            var textScene = text is null ? null : textScenes.GetValueOrDefault(elementIdentity);
            var textSnapshot = text is null
                ? null
                : new TextSnapshot(
                    text.Text,
                    text.Anchor,
                    text.Caret,
                    text.AnchorAffinity,
                    text.CaretAffinity,
                    text.IsReadOnly,
                    textScene?.Shape,
                    textScene?.Bounds ?? bounds.GetValueOrDefault(key),
                    textScene?.Clip
                        ?? PointBounds(elementIdentity, bounds.GetValueOrDefault(key), input)
                );
            yield return new(
                key,
                parent,
                ordinal++,
                snapshot.Identity,
                snapshot.Role,
                snapshot.Name,
                snapshot.Value,
                snapshot.Enabled,
                snapshot.Focused,
                snapshot.Selected,
                snapshot.Actions,
                bounds.GetValueOrDefault(key),
                PointBounds(new(key.Epoch, key.Element), bounds.GetValueOrDefault(key), input),
                scroll,
                textSnapshot,
                snapshot.Expanded,
                snapshot.Range
            );
            foreach (var child in snapshot.Children)
            foreach (var node in Flatten(child, key))
                yield return node;
        }
    }

    // Use structural input ancestry: semantic parents can omit a clipping presentation element.
    // Keep full layout bounds for UIA geometry; point lookup uses the clipped intersection only.
    private static LayoutRect PointBounds(
        ElementIdentity identity,
        LayoutRect bounds,
        Dictionary<ElementIdentity, RetainedInputElement> input
    )
    {
        if (!input.TryGetValue(identity, out var current))
            return default;
        while (current.Parent is { } parent)
        {
            if (!input.TryGetValue(parent, out current))
                return default;
            if (current.ChildClipBounds is not { } clip)
                continue;
            var x = Math.Max(bounds.X, clip.X);
            var y = Math.Max(bounds.Y, clip.Y);
            bounds = new(
                x,
                y,
                Math.Max(0, Math.Min(bounds.X + bounds.Width, clip.X + clip.Width) - x),
                Math.Max(0, Math.Min(bounds.Y + bounds.Height, clip.Y + clip.Height) - y)
            );
        }
        return bounds;
    }

    private bool Try<T>(string callback, Func<T> action, out T value) =>
        _dispatcher.TryInvoke(callback, action, out value);

    private Snapshot CurrentSnapshot() => Volatile.Read(ref _root._snapshot);

    private Node? CurrentNode()
    {
        _dispatcher.RecordRead("CurrentNode");
        return CurrentNode(CurrentSnapshot());
    }

    private Node? CurrentNode(Snapshot snapshot) =>
        Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _root._disposed) != 0 ? null
        : _key is { } key && snapshot.Nodes.TryGetValue(key, out var node) ? node
        : null;

    private WindowsUiaProvider? Provider(Node? node)
    {
        if (node is null || Volatile.Read(ref _root._disposed) != 0)
            return null;
        var cache = _root._cache!;
        lock (cache)
        {
            if (Volatile.Read(ref _root._disposed) != 0)
                return null;
            if (!CurrentSnapshot().Nodes.ContainsKey(node.Key))
                return null;
            if (cache.TryGetValue(node.Key, out var provider))
                return provider;
            provider = new WindowsUiaProvider(_root, node.Key);
            cache.Add(node.Key, provider);
            _root.MaxCacheCount = Math.Max(_root.MaxCacheCount, cache.Count);
            _dispatcher.RecordProvider(cache.Count, _root.MaxCacheCount, _root.StaleCount);
            return provider;
        }
    }

    private int Run(Func<Node, SemanticCommandResult> action)
    {
        _dispatcher.RecordAction("SemanticCommand");
        if (
            !Try(
                "SemanticCommand",
                () =>
                {
                    var node = NodeOwner();
                    return node is null ? SemanticCommandResult.Stale
                        : !node.Enabled ? SemanticCommandResult.Disabled
                        : action(node);
                },
                out SemanticCommandResult result
            )
        )
            return Fail;
        return result == SemanticCommandResult.Applied ? Ok
            : result == SemanticCommandResult.Stale ? NotAvailable
            : result == SemanticCommandResult.Disabled ? ElementNotEnabled
            : InvalidOperation;
    }

    private Node? NodeOwner() => CurrentNode(CurrentSnapshot());

    internal int Options(out int value)
    {
        value = 0;
        if (!IsAvailable)
            return NotAvailable;
        value = 0x0002 | 0x0010;
        return Ok;
    }

    internal int Pattern(int id, out nint value)
    {
        value = 0;
        if (!IsAvailable)
            return NotAvailable;
        var node = CurrentNode();
        if (node is null)
            return Ok;
        var iid = id switch
        {
            10000 when node.Actions.HasFlag(SemanticAction.Invoke) => UiaWrappers.Invoke,
            10001 when node.Role == SemanticRole.List => UiaWrappers.SelectionPattern,
            10002 when node.Actions.HasFlag(SemanticAction.SetValue) || node.Text is not null =>
                UiaWrappers.ValuePattern,
            10004 when node.Actions.HasFlag(SemanticAction.Scroll) => UiaWrappers.Scroll,
            10005 when node.Actions.HasFlag(SemanticAction.ExpandCollapse) =>
                UiaWrappers.ExpandCollapse,
            10003 when node.Range is not null => UiaWrappers.RangeValue,
            10010 when node.Actions.HasFlag(SemanticAction.Select) => UiaWrappers.SelectionItem,
            10014 when node.Text is not null && node.Actions.HasFlag(SemanticAction.SelectText) =>
                UiaWrappers.TextProvider,
            10024 when node.Text is not null && node.Actions.HasFlag(SemanticAction.SelectText) =>
                UiaWrappers.TextProvider2,
            _ => Guid.Empty,
        };
        if (iid != Guid.Empty)
            value = Query(_unknown, iid);
        return Ok;
    }

    internal int Property(int id, RawVariant* value)
    {
        *value = default;
        if (!IsAvailable)
            return NotAvailable;
        var node = CurrentNode();
        if (node is null)
        {
            if (id == 30003)
                I4(value, 50032);
            else if (id == 30005)
                Bstr(value, _applicationName);
            else if (id == 30010 || id == 30016 || id == 30017)
                Bool(value, true);
            else if (id == 30011)
                Bstr(value, "Lucent.Root");
            return Ok;
        }
        switch (id)
        {
            case 30003:
                I4(value, Type(node));
                break;
            case 30005:
                Bstr(value, node.Name);
                break;
            case 30008:
                Bool(value, node.Focused);
                break;
            case 30009:
                Bool(value, node.Actions != SemanticAction.None);
                break;
            case 30010:
                Bool(value, node.Enabled);
                break;
            case 30023:
                I4(value, ExpandCollapseStateValue(node.Expanded));
                break;
            case 30047 when node.Range is { } range:
                R8(value, range.Value);
                break;
            case 30048 when node.Range is { } range:
                Bool(value, range.IsReadOnly);
                break;
            case 30049 when node.Range is { } range:
                R8(value, range.Maximum);
                break;
            case 30050 when node.Range is { } range:
                R8(value, range.Minimum);
                break;
            case 30051 when node.Range is { } range:
                R8(value, range.LargeChange);
                break;
            case 30052 when node.Range is { } range:
                R8(value, range.SmallChange);
                break;
            case 30011:
                Bstr(value, "lucent." + node.Key.Epoch + "." + node.Key.Element);
                break;
            case 30016:
            case 30017:
                Bool(value, true);
                break;
            case 30031:
                Bool(value, node.Actions.HasFlag(SemanticAction.Invoke));
                break;
            case 30034:
                Bool(value, node.Actions.HasFlag(SemanticAction.Scroll));
                break;
            case 30036:
                Bool(value, node.Actions.HasFlag(SemanticAction.Select));
                break;
            case 30037:
                Bool(value, node.Role == SemanticRole.List);
                break;
            case 30040:
                Bool(
                    value,
                    node.Text is not null && node.Actions.HasFlag(SemanticAction.SelectText)
                );
                break;
            case 30043:
                Bool(value, node.Actions.HasFlag(SemanticAction.SetValue) || node.Text is not null);
                break;
            case 30119:
                Bool(
                    value,
                    node.Text is not null && node.Actions.HasFlag(SemanticAction.SelectText)
                );
                break;
            case 30045:
                Bstr(value, node.Value ?? "");
                break;
            case 30053:
                R8(value, -1);
                break;
            case 30054:
                R8(value, 100);
                break;
            case 30055:
                R8(value, ScrollPercent(node.Scroll));
                break;
            case 30056:
                R8(value, ScrollViewSize(node.Scroll));
                break;
            case 30057:
                Bool(value, false);
                break;
            case 30058:
                Bool(value, node.Scroll is { Maximum.Y: > 0 });
                break;
            case 30079:
                Bool(value, node.Selected);
                break;
        }
        return Ok;
    }

    internal int Host(out nint value)
    {
        value = 0;
        if (!IsAvailable)
            return NotAvailable;
        if (!IsRoot)
            return Ok;
        return UiaHostProviderFromHwnd(_hwnd, out value);
    }

    internal int Navigate(int direction, out nint value)
    {
        value = 0;
        if (!IsAvailable)
            return NotAvailable;
        _dispatcher.RecordRead("Navigate");
        var snapshot = CurrentSnapshot();
        var current = IsRoot ? null : CurrentNode(snapshot);
        if (!IsRoot && current is null)
            return NotAvailable;
        if (!IsRoot && direction == 0 && current!.Parent is null)
        {
            value = Query(_root._unknown, UiaWrappers.Fragment);
            return Ok;
        }
        var node = NavigateSnapshot(snapshot, direction, current);
        var provider = Provider(node);
        value = provider is null ? 0 : Query(provider._unknown, UiaWrappers.Fragment);
        return Ok;
    }

    private Node? NavigateSnapshot(Snapshot snapshot, int direction, Node? current)
    {
        if (IsRoot)
            return direction == 3 ? snapshot.Roots.FirstOrDefault()
                : direction == 4 ? snapshot.Roots.LastOrDefault()
                : null;
        if (current is null)
            return null;
        if (direction == 0)
            return current.Parent is { } parent && snapshot.Nodes.TryGetValue(parent, out var value)
                ? value
                : null;
        var children = snapshot.Children(current.Key);
        if (direction == 3)
            return children.FirstOrDefault();
        if (direction == 4)
            return children.LastOrDefault();
        var siblings = current.Parent is { } key ? snapshot.Children(key) : snapshot.Roots;
        var at = snapshot.SiblingPositions[current.Key];
        return direction == 1 && at + 1 < siblings.Length ? siblings[at + 1]
            : direction == 2 && at > 0 ? siblings[at - 1]
            : null;
    }

    internal int RuntimeId(out nint value)
    {
        value = 0;
        if (!IsAvailable)
            return NotAvailable;
        var node = CurrentNode();
        if (node is null)
            return Ok;
        value = SafeArrayCreateVector(VtI4, 0, 3);
        if (value == 0)
            return OutOfMemory;
        var i = 0;
        var data = 3;
        var hr = SafeArrayPutElement(value, &i, &data);
        i = 1;
        data = (int)node.Key.Epoch;
        if (hr >= 0)
            hr = SafeArrayPutElement(value, &i, &data);
        i = 2;
        data = (int)node.Key.Element;
        if (hr >= 0)
            hr = SafeArrayPutElement(value, &i, &data);
        if (hr < 0)
        {
            _ = SafeArrayDestroy(value);
            value = 0;
        }
        return hr;
    }

    internal int Rectangle(double* value)
    {
        if (!IsAvailable)
            return NotAvailable;
        var node = CurrentNode();
        GetClientRect(_hwnd, out var client);
        var point = new ScreenPoint();
        ClientToScreen(_hwnd, ref point);
        if (node is null)
        {
            value[0] = point.X;
            value[1] = point.Y;
            value[2] = client.Right - client.Left;
            value[3] = client.Bottom - client.Top;
            return Ok;
        }
        var scale = GetDpiForWindow(_hwnd) / 96d;
        value[0] = point.X + node.Bounds.X * scale;
        value[1] = point.Y + node.Bounds.Y * scale;
        value[2] = node.Bounds.Width * scale;
        value[3] = node.Bounds.Height * scale;
        return Ok;
    }

    internal int Focus() =>
        Run(node =>
            _composition.ExecuteSemanticCommand(node.Identity, new(SemanticCommandKind.Focus))
        );

    internal int Root(out nint value)
    {
        value = 0;
        if (!IsAvailable)
            return NotAvailable;
        value = Query(_root._unknown, UiaWrappers.Root);
        return value == 0 ? Fail : Ok;
    }

    internal int Point(double x, double y, out nint value)
    {
        _dispatcher.RecordRead("Point");
        value = 0;
        if (!IsAvailable)
            return NotAvailable;
        var point = new ScreenPoint();
        ClientToScreen(_hwnd, ref point);
        var scale = GetDpiForWindow(_hwnd) / 96d;
        var node = CurrentSnapshot()
            .Nodes.Values.Where(node =>
                x >= point.X + node.PointBounds.X * scale
                && y >= point.Y + node.PointBounds.Y * scale
                && x < point.X + (node.PointBounds.X + node.PointBounds.Width) * scale
                && y < point.Y + (node.PointBounds.Y + node.PointBounds.Height) * scale
            )
            .OrderBy(node => node.Ordinal)
            .LastOrDefault();
        var provider = Provider(node);
        value = provider is null ? 0 : Query(provider._unknown, UiaWrappers.Fragment);
        return Ok;
    }

    internal int GetFocus(out nint value)
    {
        _dispatcher.RecordRead("GetFocus");
        value = 0;
        if (!IsAvailable)
            return NotAvailable;
        var node = CurrentSnapshot().Nodes.Values.FirstOrDefault(node => node.Focused);
        var provider = Provider(node);
        value = provider is null ? 0 : Query(provider._unknown, UiaWrappers.Fragment);
        return Ok;
    }

    internal int Invoke() =>
        Run(node =>
            _composition.ExecuteSemanticCommand(node.Identity, new(SemanticCommandKind.Invoke))
        );

    internal int Expand() =>
        Run(node =>
            _composition.ExecuteSemanticCommand(node.Identity, new(SemanticCommandKind.Expand))
        );

    internal int Collapse() =>
        Run(node =>
            _composition.ExecuteSemanticCommand(node.Identity, new(SemanticCommandKind.Collapse))
        );

    internal int ExpandCollapseState(out int value)
    {
        var node = CurrentNode();
        value = ExpandCollapseStateValue(node?.Expanded);
        return node is null ? NotAvailable : Ok;
    }

    internal int SetRangeValue(double value) =>
        !double.IsFinite(value)
            ? InvalidArgument
            : Run(node =>
                node.Range is not { IsReadOnly: false } range
                || value < range.Minimum
                || value > range.Maximum
                    ? SemanticCommandResult.Rejected
                    : _composition.ExecuteSemanticCommand(
                        node.Identity,
                        new(SemanticCommandKind.SetRangeValue, NumericValue: value)
                    )
            );

    internal int RangeValue(out double value)
    {
        var node = CurrentNode();
        value = node?.Range?.Value ?? 0;
        return node?.Range is null ? NotAvailable : Ok;
    }

    internal int RangeReadOnly(out int value)
    {
        var node = CurrentNode();
        value = node?.Range?.IsReadOnly == true ? 1 : 0;
        return node?.Range is null ? NotAvailable : Ok;
    }

    internal int RangeMaximum(out double value)
    {
        var node = CurrentNode();
        value = node?.Range?.Maximum ?? 0;
        return node?.Range is null ? NotAvailable : Ok;
    }

    internal int RangeMinimum(out double value)
    {
        var node = CurrentNode();
        value = node?.Range?.Minimum ?? 0;
        return node?.Range is null ? NotAvailable : Ok;
    }

    internal int RangeLargeChange(out double value)
    {
        var node = CurrentNode();
        value = node?.Range?.LargeChange ?? 0;
        return node?.Range is null ? NotAvailable : Ok;
    }

    internal int RangeSmallChange(out double value)
    {
        var node = CurrentNode();
        value = node?.Range?.SmallChange ?? 0;
        return node?.Range is null ? NotAvailable : Ok;
    }

    internal int SetValue(nint text) =>
        Run(node =>
            _composition.ExecuteSemanticCommand(
                node.Identity,
                new(SemanticCommandKind.SetValue, Marshal.PtrToStringUni(text) ?? "")
            )
        );

    internal int Value(out nint value)
    {
        value = 0;
        var node = CurrentNode();
        if (node is null)
            return NotAvailable;
        value = SysAllocString(node.Value ?? "");
        return value == 0 ? OutOfMemory : Ok;
    }

    internal int ValueReadOnly(out int value)
    {
        var node = CurrentNode();
        value = node?.Text?.IsReadOnly == true ? 1 : 0;
        return node is null ? NotAvailable : Ok;
    }

    internal int Selection(out nint value)
    {
        _dispatcher.RecordRead("Selection");
        value = 0;
        var snapshot = CurrentSnapshot();
        var node = CurrentNode(snapshot);
        if (node is null)
            return NotAvailable;
        var selected = snapshot.Children(node.Key).Where(child => child.Selected).ToArray();
        value = SafeArrayCreateVector(VtUnknown, 0, (uint)selected.Length);
        if (value == 0)
            return OutOfMemory;
        for (var i = 0; i < selected.Length; i++)
        {
            var provider = Provider(selected[i]);
            var pointer = provider is null ? 0 : Query(provider._unknown, UiaWrappers.Simple);
            var at = i;
            var hr = SafeArrayPutElement(value, &at, (void*)pointer);
            Release(ref pointer);
            if (hr < 0)
            {
                _ = SafeArrayDestroy(value);
                value = 0;
                return hr;
            }
        }
        return Ok;
    }

    internal int Select() =>
        Run(node =>
            _composition.ExecuteSemanticCommand(node.Identity, new(SemanticCommandKind.Select))
        );

    internal int UnsupportedSelection()
    {
        var node = CurrentNode();
        return node is null ? NotAvailable
            : !node.Enabled ? ElementNotEnabled
            : InvalidOperation;
    }

    internal int Selected(out int value)
    {
        var node = CurrentNode();
        value = node?.Selected == true ? 1 : 0;
        return node is null ? NotAvailable : Ok;
    }

    internal int Container(out nint value)
    {
        _dispatcher.RecordRead("Container");
        value = 0;
        var snapshot = CurrentSnapshot();
        var node = CurrentNode(snapshot);
        if (node is null)
            return NotAvailable;
        while (node.Parent is { } parent && snapshot.Nodes.TryGetValue(parent, out node))
            if (node.Role == SemanticRole.List)
            {
                var provider = Provider(node);
                value = provider is null ? 0 : Query(provider._unknown, UiaWrappers.Simple);
                break;
            }
        return Ok;
    }

    internal int Scroll(int horizontal, int vertical)
    {
        if (horizontal != 2 || vertical is < 0 or > 4)
            return InvalidArgument;
        return Run(node =>
            node.Scroll is not { } scroll ? SemanticCommandResult.Rejected
            : vertical == 2 ? SemanticCommandResult.Applied
            : _composition.ExecuteSemanticCommand(
                node.Identity,
                new(
                    SemanticCommandKind.Scroll,
                    Vertical: vertical switch
                    {
                        0 => -scroll.Viewport.Height,
                        1 => -40,
                        3 => scroll.Viewport.Height,
                        4 => 40,
                        _ => 0,
                    }
                )
            )
        );
    }

    internal int ScrollPercent(double horizontal, double vertical)
    {
        if (
            horizontal != -1
            || !double.IsFinite(vertical)
            || (vertical != -1 && (vertical < 0 || vertical > 100))
        )
            return InvalidArgument;
        return Run(node =>
        {
            if (node.Scroll is not { } scroll)
                return SemanticCommandResult.Rejected;
            if (vertical == -1)
                return SemanticCommandResult.Applied;
            var target = (float)(scroll.Maximum.Y * vertical / 100d);
            if (target == scroll.Offset.Y)
                return SemanticCommandResult.Applied;
            return _composition.ExecuteSemanticCommand(
                node.Identity,
                new(SemanticCommandKind.Scroll, Vertical: target - scroll.Offset.Y)
            );
        });
    }

    internal int ScrollMetric(bool vertical, bool size, double* value)
    {
        var node = CurrentNode();
        if (node is null)
        {
            *value = 0;
            return NotAvailable;
        }
        *value = vertical
            ? size
                ? ScrollViewSize(node.Scroll)
                : ScrollPercent(node.Scroll)
            : size
                ? 100
                : -1;
        return Ok;
    }

    internal int Scrollable(bool vertical, int* value)
    {
        var node = CurrentNode();
        *value = vertical && node?.Scroll is { Maximum.Y: > 0 } ? 1 : 0;
        return node is null ? NotAvailable : Ok;
    }

    private void RaiseFocus(Node node)
    {
        if (UiaClientsAreListening() && Provider(node) is { } provider)
            _ = UiaRaiseAutomationEvent(provider._simple, 20005);
    }

    private void RaiseTextEvent(Node node, int eventId)
    {
        if (UiaClientsAreListening() && Provider(node) is { } provider)
            _ = UiaRaiseAutomationEvent(provider._simple, eventId);
    }

    private void RaiseStructure(WindowsUiaProvider provider, int change, Node changed)
    {
        DetectedStructureChanges++;
        if (!UiaClientsAreListening())
            return;
        var runtimeId = stackalloc int[]
        {
            3,
            checked((int)changed.Key.Epoch),
            checked((int)changed.Key.Element),
        };
        _ = UiaRaiseStructureChangedEvent(provider._simple, change, runtimeId, 3);
    }

    private void RaiseProperty(Node node, int id, object oldValue, object newValue)
    {
        DetectedPropertyChanges++;
        if (!UiaClientsAreListening() || Provider(node) is not { } provider)
            return;
        var oldVariant = Variant(oldValue);
        var newVariant = Variant(newValue);
        _ = UiaRaiseAutomationPropertyChangedEvent(provider._simple, id, oldVariant, newVariant);
        VariantClear(ref oldVariant);
        VariantClear(ref newVariant);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 && _unknown == 0)
            return;
        WindowsUiaProvider[] children = [];
        if (_cache is { } cache)
            lock (cache)
            {
                children = cache.Values.ToArray();
                cache.Clear();
            }
        foreach (var child in children)
            child.Dispose();
        if (_simple != 0)
            _ = UiaDisconnectProvider(_simple);
        Release(ref _simple);
        Release(ref _unknown);
    }

    private static int Type(Node node) =>
        node.Role switch
        {
            SemanticRole.Button => 50000,
            SemanticRole.TextField => 50004,
            SemanticRole.List => 50008,
            SemanticRole.ListItem => 50007,
            SemanticRole.Menu => 50009,
            SemanticRole.MenuItem => 50011,
            SemanticRole.Text => 50020,
            SemanticRole.Status => 50017,
            SemanticRole.Splitter => 50015,
            SemanticRole.Group when node.Actions.HasFlag(SemanticAction.Scroll) => 50033,
            _ => 50026,
        };

    private static double ScrollPercent(SemanticScrollState? scroll) =>
        scroll is not { Maximum.Y: > 0 } state ? -1 : state.Offset.Y * 100d / state.Maximum.Y;

    private static double ScrollViewSize(SemanticScrollState? scroll) =>
        scroll is not { Maximum.Y: > 0 } state
            ? 100
            : state.Viewport.Height * 100d / (state.Viewport.Height + state.Maximum.Y);

    internal static int ExpandCollapseStateValue(bool? expanded) =>
        expanded switch
        {
            false => 0, // ExpandCollapseState.Collapsed
            true => 1, // ExpandCollapseState.Expanded
            null => 3, // ExpandCollapseState.LeafNode
        };

    private static RawVariant Variant(object value)
    {
        var variant = new RawVariant();
        if (value is bool boolean)
            Bool(&variant, boolean);
        else if (value is int integer)
            I4(&variant, integer);
        else if (value is double number)
            R8(&variant, number);
        else
            Bstr(&variant, (string)value);
        return variant;
    }

    private static void I4(RawVariant* value, int data)
    {
        value->Type = VtI4;
        value->Value = data;
    }

    private static void Bool(RawVariant* value, bool data)
    {
        value->Type = VtBool;
        value->Value = data ? -1 : 0;
    }

    private static void R8(RawVariant* value, double data)
    {
        value->Type = 5;
        value->Double = data;
    }

    private static void Bstr(RawVariant* value, string data)
    {
        value->Type = VtBstr;
        value->Value = SysAllocString(data);
    }

    private static nint Query(nint pointer, Guid iid)
    {
        if (pointer == 0)
            return 0;
        nint result = 0;
        ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)pointer)[0])(
            pointer,
            &iid,
            &result
        );
        return result;
    }

    private static void Release(ref nint pointer)
    {
        if (pointer != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)pointer)[2])(pointer);
            pointer = 0;
        }
    }

    private readonly record struct NodeKey(long Epoch, long Element);

    private sealed record Node(
        NodeKey Key,
        NodeKey? Parent,
        long Ordinal,
        SemanticIdentity Identity,
        SemanticRole Role,
        string Name,
        string? Value,
        bool Enabled,
        bool Focused,
        bool Selected,
        SemanticAction Actions,
        LayoutRect Bounds,
        LayoutRect PointBounds,
        SemanticScrollState? Scroll,
        TextSnapshot? Text,
        bool? Expanded,
        SemanticRangeSnapshot? Range
    );

    // Immutable after construction: COM readers use one snapshot for every navigation step.
    private sealed class Snapshot
    {
        internal static readonly Snapshot Empty = new(
            new Dictionary<NodeKey, Node>().ToFrozenDictionary()
        );
        private readonly Dictionary<NodeKey, Node[]> _children;
        internal FrozenDictionary<NodeKey, Node> Nodes { get; }
        internal Node[] Roots { get; }
        internal Dictionary<NodeKey, int> SiblingPositions { get; } = [];

        internal Snapshot(FrozenDictionary<NodeKey, Node> nodes)
        {
            Nodes = nodes;
            var roots = new List<Node>();
            var children = new Dictionary<NodeKey, List<Node>>();
            foreach (var node in nodes.Values.OrderBy(node => node.Ordinal))
            {
                var siblings = roots;
                if (node.Parent is { } parent)
                {
                    if (!children.TryGetValue(parent, out siblings))
                        children.Add(parent, siblings = []);
                }
                SiblingPositions.Add(node.Key, siblings.Count);
                siblings.Add(node);
            }
            Roots = roots.ToArray();
            _children = children.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        }

        internal Node[] Children(NodeKey key) => _children.GetValueOrDefault(key) ?? [];
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct RawVariant
    {
        [FieldOffset(0)]
        internal ushort Type;

        [FieldOffset(8)]
        internal nint Value;

        [FieldOffset(8)]
        internal double Double;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [LibraryImport("UIAutomationCore.dll")]
    private static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);

    [LibraryImport("UIAutomationCore.dll")]
    internal static partial nint UiaReturnRawElementProvider(
        nint hwnd,
        nint wParam,
        nint lParam,
        nint provider
    );

    [LibraryImport("UIAutomationCore.dll")]
    private static partial int UiaDisconnectProvider(nint provider);

    [LibraryImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UiaClientsAreListening();

    [LibraryImport("UIAutomationCore.dll")]
    private static partial int UiaRaiseAutomationEvent(nint provider, int eventId);

    [LibraryImport("UIAutomationCore.dll")]
    private static partial int UiaRaiseStructureChangedEvent(
        nint provider,
        int type,
        int* runtimeId,
        int length
    );

    [LibraryImport("UIAutomationCore.dll")]
    private static partial int UiaRaiseAutomationPropertyChangedEvent(
        nint provider,
        int id,
        RawVariant oldValue,
        RawVariant newValue
    );

    [LibraryImport("oleaut32.dll")]
    private static partial nint SysAllocString([MarshalAs(UnmanagedType.LPWStr)] string value);

    [LibraryImport("oleaut32.dll")]
    private static partial int VariantClear(ref RawVariant value);

    [LibraryImport("oleaut32.dll")]
    private static partial nint SafeArrayCreateVector(ushort type, int lowerBound, uint count);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayPutElement(nint array, int* index, void* value);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayDestroy(nint array);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out Rect rectangle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hwnd, ref ScreenPoint point);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
