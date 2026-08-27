using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>Live UIA fragments over NativeUi's retained elements; no application semantic mirror.</summary>
internal interface IUiaSemanticSource { UiaSemanticNode[] UiaNodes { get; } bool UiaAction(string id, string action, string? value); }

internal sealed unsafe class UiaIssueProvider : IDisposable, IUiaProvider
{
    private const int S_OK = 0, E_FAIL = unchecked((int)0x80004005), UIA_E_ELEMENTNOTAVAILABLE = unchecked((int)0x80040201), UIA_E_INVALIDOPERATION = unchecked((int)0x80131509);
    private const int VT_I4 = 3, VT_BSTR = 8, VT_BOOL = 11, VT_UNKNOWN = 13;
    private static readonly UiaIssueWrappers Wrappers = new();
    private readonly IUiaSemanticSource _source;
    private readonly string? _id;
    private readonly nint _hwnd;
    private readonly UiaIssueProvider _root;
    private readonly Dictionary<string, UiaIssueProvider>? _children;
    private UiaSemanticNode[] _snapshot;
    private nint _unknown, _simple;
    private volatile bool _disposed;
    private int _maxCacheCount, _staleDisconnected, _focusEvents, _propertyEvents, _structureEvents, _rootPointCalls, _rootFocusCalls;

    public UiaIssueProvider(nint hwnd, IUiaSemanticSource source)
    {
        _hwnd = hwnd; _source = source; _root = this; _children = []; _snapshot = source.UiaNodes;
        _unknown = Wrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
        _simple = Query(_unknown, UiaIssueWrappers.Simple);
    }

    private UiaIssueProvider(UiaIssueProvider root, string id)
    {
        _hwnd = root._hwnd; _source = root._source; _root = root; _id = id; _snapshot = [];
        _unknown = Wrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
        _simple = Query(_unknown, UiaIssueWrappers.Simple);
    }

    internal bool IsRoot => _id is null;
    internal string? Role => Node?.Semantics.Role;
    public nint InterfacePointer => _simple;
    private UiaSemanticNode? Node => _id is null ? null : Nodes.FirstOrDefault(x => x.Id == _id);
    private UiaSemanticNode[] Nodes => _root._snapshot;
    internal int CacheCount { get { lock (_children!) return _children.Count; } }
    internal int MaxCacheCount => _maxCacheCount;
    internal int StaleDisconnected => _staleDisconnected;
    internal int FocusEvents => _focusEvents;
    internal int PropertyEvents => _propertyEvents;
    internal int StructureEvents => _structureEvents;
    internal int RootPointCalls => _rootPointCalls;
    internal int RootFocusCalls => _rootFocusCalls;

    private UiaSemanticNode? Related(int direction)
    {
        if (IsRoot) return direction is 3 or 4 ? (direction == 3 ? Nodes.FirstOrDefault(x => x.ParentId is null) : Nodes.LastOrDefault(x => x.ParentId is null)) : null;
        var node = Node;
        if (node is null) return null;
        if (direction == 0) return node.ParentId is null ? null : Nodes.FirstOrDefault(x => x.Id == node.ParentId);
        if (direction is 3 or 4) return direction == 3 ? Nodes.FirstOrDefault(x => x.ParentId == _id) : Nodes.LastOrDefault(x => x.ParentId == _id);
        var siblings = Nodes.Where(x => x.ParentId == node.ParentId).ToArray();
        var at = Array.FindIndex(siblings, x => x.Id == _id);
        return direction == 1 && at + 1 < siblings.Length ? siblings[at + 1] : direction == 2 && at > 0 ? siblings[at - 1] : null;
    }

    private UiaIssueProvider? Cached(UiaSemanticNode node)
    {
        lock (_root._children!)
        {
            if (_root._disposed) return null;
            if (_root._children.TryGetValue(node.Id, out var provider)) return provider;
            provider = new UiaIssueProvider(_root, node.Id);
            _root._children.Add(node.Id, provider);
            _root._maxCacheCount = Math.Max(_root._maxCacheCount, _root._children.Count);
            return provider;
        }
    }

    private nint Provider(UiaSemanticNode? node, Guid iid) => node is null || Cached(node) is not { } provider ? 0 : Query(provider._unknown, iid);

    public int Options(out int value) { value = 0x0002 | 0x0010; return !IsRoot && Node is null ? UIA_E_ELEMENTNOTAVAILABLE : S_OK; }

    public int Property(int property, RawVariant* value)
    {
        *value = default;
        var node = Node;
        if (!IsRoot && node is null) return UIA_E_ELEMENTNOTAVAILABLE;
        if (node is null)
        {
            switch (property)
            {
                case 30003: I4(value, 50032); break;
                case 30005: Bstr(value, "Lucent Native Issue Browser"); break;
                case 30010: case 30016: case 30017: Bool(value, true); break;
                case 30011: Bstr(value, "Lucent.NativeIssueBrowser.Root"); break;
            }
            return S_OK;
        }

        var semantics = node.Semantics;
        switch (property)
        {
            case 30003: I4(value, ControlType(semantics.Role)); break;
            case 30005: Bstr(value, semantics.Name); break;
            case 30008: Bool(value, semantics.Focused); break;
            case 30009: Bool(value, semantics.Actions?.Length > 0); break;
            case 30010: Bool(value, semantics.Enabled); break;
            case 30011: Bstr(value, node.Id); break;
            case 30016: case 30017: Bool(value, true); break;
            case 30031: Bool(value, semantics.Actions?.Contains("press") == true); break;
            case 30036: Bool(value, semantics.Role == "listbox"); break;
            case 30037: Bool(value, semantics.Actions?.Contains("select") == true && semantics.Role == "option"); break;
            case 30043: Bool(value, semantics.Actions?.Contains("set-value") == true); break;
            case 30045: Bstr(value, semantics.Value ?? ""); break;
            case 30060: case 30061: Bool(value, false); break;
            case 30079: Bool(value, semantics.Selected); break;
        }
        return S_OK;
    }

    public int Pattern(int pattern, out nint result)
    {
        result = 0;
        var node = Node;
        if (!IsRoot && node is null) return UIA_E_ELEMENTNOTAVAILABLE;
        if (node is null) return S_OK;
        var iid = pattern switch
        {
            10000 when node.Semantics.Actions?.Contains("press") == true => UiaIssueWrappers.Invoke,
            10001 when node.Semantics.Role == "listbox" => UiaIssueWrappers.Selection,
            10002 when node.Semantics.Actions?.Contains("set-value") == true => UiaIssueWrappers.Value,
            10010 when node.Semantics.Role == "option" && node.Semantics.Actions?.Contains("select") == true => UiaIssueWrappers.SelectionItem,
            _ => Guid.Empty
        };
        if (iid != Guid.Empty) result = Query(_unknown, iid);
        return S_OK;
    }

    public int Host(out nint result)
    {
        if (!IsRoot) { result = 0; return Node is null ? UIA_E_ELEMENTNOTAVAILABLE : S_OK; }
        return Native.UiaHostProviderFromHwnd(_hwnd, out result);
    }

    public int Navigate(int direction, out nint result)
    {
        var node = Node;
        if (!IsRoot && node is null) { result = 0; return UIA_E_ELEMENTNOTAVAILABLE; }
        result = !IsRoot && direction == 0 && node is not null && node.ParentId is null
            ? Query(_root._unknown, UiaIssueWrappers.Fragment)
            : Provider(Related(direction), UiaIssueWrappers.Fragment);
        return S_OK;
    }

    public int RuntimeId(out nint result)
    {
        result = 0;
        if (IsRoot) return S_OK;
        if (Node is null) return UIA_E_ELEMENTNOTAVAILABLE;
        if (!int.TryParse(_id!.AsSpan("native.".Length), out var stable)) return E_FAIL;
        result = Native.SafeArrayCreateVector(VT_I4, 0, 2);
        if (result == 0) return unchecked((int)0x8007000E);
        var index = 0; var value = 3;
        var hr = Native.SafeArrayPutElement(result, &index, &value);
        index = 1; value = stable;
        if (hr >= 0) hr = Native.SafeArrayPutElement(result, &index, &value);
        if (hr < 0) { Native.SafeArrayDestroy(result); result = 0; }
        return hr;
    }

    public int Rect(double* rectangle)
    {
        if (!IsRoot && Node is null) return UIA_E_ELEMENTNOTAVAILABLE;
        Native.GetClientRect(_hwnd, out var client);
        var bounds = Node?.Bounds ?? new Bounds(0, 0, client.Right - client.Left, client.Bottom - client.Top);
        var origin = new NativePoint();
        Native.ClientToScreen(_hwnd, ref origin);
        var scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        rectangle[0] = origin.X + bounds.X * scale;
        rectangle[1] = origin.Y + bounds.Y * scale;
        rectangle[2] = bounds.Width * scale;
        rectangle[3] = bounds.Height * scale;
        return S_OK;
    }

    public int Focus() => Node is null ? UIA_E_ELEMENTNOTAVAILABLE : _source.UiaAction(_id!, "focus", null) ? S_OK : E_FAIL;
    public int Root(out nint result) { if (!IsRoot && Node is null) { result = 0; return UIA_E_ELEMENTNOTAVAILABLE; } result = Query(_root._unknown, UiaIssueWrappers.Root); return S_OK; }

    public int Point(double x, double y, out nint result)
    {
        _root._rootPointCalls++;
        var origin = new NativePoint(); Native.ClientToScreen(_hwnd, ref origin);
        var scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        var clientX = (x - origin.X) / scale; var clientY = (y - origin.Y) / scale;
        var node = Nodes.LastOrDefault(n => clientX >= n.Bounds.X && clientY >= n.Bounds.Y && clientX < n.Bounds.X + n.Bounds.Width && clientY < n.Bounds.Y + n.Bounds.Height);
        result = Provider(node, UiaIssueWrappers.Fragment);
        return S_OK;
    }

    public int GetFocus(out nint result) { _root._rootFocusCalls++; result = Provider(Nodes.FirstOrDefault(n => n.Semantics.Focused), UiaIssueWrappers.Fragment); return S_OK; }
    public int Invoke() => Node is null ? UIA_E_ELEMENTNOTAVAILABLE : _source.UiaAction(_id!, "press", null) ? S_OK : E_FAIL;
    public int SetValue(nint value) => Node is null ? UIA_E_ELEMENTNOTAVAILABLE : _source.UiaAction(_id!, "set-value", Marshal.PtrToStringUni(value) ?? "") ? S_OK : E_FAIL;
    public int Value(out nint result) { var node = Node; if (node is null) { result = 0; return UIA_E_ELEMENTNOTAVAILABLE; } result = Native.SysAllocString(node.Semantics.Value ?? ""); return S_OK; }
    public int IsReadOnly(out int value) { value = 0; return Node is null ? UIA_E_ELEMENTNOTAVAILABLE : S_OK; }
    public int Select() => Node is null ? UIA_E_ELEMENTNOTAVAILABLE : _source.UiaAction(_id!, "select", null) ? S_OK : E_FAIL;
    public int AddToSelection() => Node is null ? UIA_E_ELEMENTNOTAVAILABLE : Node.Semantics.Selected ? S_OK : UIA_E_INVALIDOPERATION;
    public int RemoveFromSelection() => Node is null ? UIA_E_ELEMENTNOTAVAILABLE : UIA_E_INVALIDOPERATION;
    public int IsSelected(out int value) { var node = Node; value = node?.Semantics.Selected == true ? -1 : 0; return node is null ? UIA_E_ELEMENTNOTAVAILABLE : S_OK; }

    public int SelectionContainer(out nint result)
    {
        var node = Node;
        if (node is null) { result = 0; return UIA_E_ELEMENTNOTAVAILABLE; }
        while (node?.ParentId is { } parentId)
        {
            node = Nodes.FirstOrDefault(n => n.Id == parentId);
            if (node?.Semantics.Role == "listbox") { result = Provider(node, UiaIssueWrappers.Simple); return S_OK; }
        }
        result = 0; return S_OK;
    }

    public int GetSelection(out nint result)
    {
        if (Node is null) { result = 0; return UIA_E_ELEMENTNOTAVAILABLE; }
        var selected = Nodes.Where(n => n.ParentId == _id && n.Semantics.Selected).ToArray();
        result = Native.SafeArrayCreateVector(VT_UNKNOWN, 0, (uint)selected.Length);
        if (result == 0) return unchecked((int)0x8007000E);
        for (var index = 0; index < selected.Length; index++)
        {
            var provider = Provider(selected[index], UiaIssueWrappers.Simple);
            var at = index;
            var hr = Native.SafeArrayPutElement(result, &at, (void*)provider);
            Release(ref provider);
            if (hr < 0) { Native.SafeArrayDestroy(result); result = 0; return hr; }
        }
        return S_OK;
    }

    internal void Reconcile(UiaSemanticNode[] previous, UiaSemanticNode[] current)
    {
        if (!IsRoot || _disposed) return;
        _snapshot = current;
        var ids = current.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        UiaIssueProvider[] stale;
        lock (_children!)
        {
            stale = _children.Where(pair => !ids.Contains(pair.Key)).Select(pair => pair.Value).ToArray();
            foreach (var provider in stale) _children.Remove(provider._id!);
        }
        foreach (var provider in stale) { provider.Dispose(); _staleDisconnected++; }

        var oldById = previous.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var changedStructure = previous.Length != current.Length || previous.Any(old => !ids.Contains(old.Id));
        foreach (var node in current)
        {
            if (!oldById.TryGetValue(node.Id, out var old)) { changedStructure = true; continue; }
            if (old.Semantics.Value != node.Semantics.Value) RaiseProperty(node, 30045, old.Semantics.Value ?? "", node.Semantics.Value ?? "");
            if (old.Semantics.Selected != node.Semantics.Selected) RaiseProperty(node, 30079, old.Semantics.Selected, node.Semantics.Selected);
            if (old.Semantics.Enabled != node.Semantics.Enabled) RaiseProperty(node, 30010, old.Semantics.Enabled, node.Semantics.Enabled);
            if (!old.Semantics.Focused && node.Semantics.Focused) RaiseFocus(node);
        }
        if (changedStructure) RaiseStructure(current.FirstOrDefault(node => node.Semantics.Role == "listbox"));
    }

    internal void ActionCompleted(string id, string action)
    {
        if (action == "focus" && Nodes.FirstOrDefault(node => node.Id == id) is { } node) RaiseFocus(node);
    }

    internal bool ValidateRootAbi()
    {
        var root = Query(_unknown, UiaIssueWrappers.Root);
        if (root == 0) return false;
        var origin = new NativePoint(); Native.ClientToScreen(_hwnd, ref origin);
        nint child = 0; nint focused = 0;
        var pointHr = ((delegate* unmanaged[Stdcall]<nint, double, double, nint*, int>)(*(nint**)root)[3])(root, origin.X + 1, origin.Y + 1, &child);
        var focusHr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)root)[4])(root, &focused);
        Release(ref child); Release(ref focused); Release(ref root);
        return pointHr >= 0 && focusHr >= 0;
    }

    private void RaiseFocus(UiaSemanticNode node)
    {
        if (!Native.UiaClientsAreListening() || Cached(node) is not { } provider) return;
        if (Native.UiaRaiseAutomationEvent(provider._simple, 20005) >= 0) _focusEvents++;
    }

    private void RaiseStructure(UiaSemanticNode? node)
    {
        if (!Native.UiaClientsAreListening()) return;
        var provider = node is null ? this : Cached(node);
        if (provider is null) return;
        if (Native.UiaRaiseStructureChangedEvent(provider._simple, 2, null, 0) >= 0) _structureEvents++;
    }

    private void RaiseProperty(UiaSemanticNode node, int property, object oldValue, object newValue)
    {
        if (!Native.UiaClientsAreListening() || Cached(node) is not { } provider) return;
        var oldVariant = Variant(oldValue); var newVariant = Variant(newValue);
        if (Native.UiaRaiseAutomationPropertyChangedEvent(provider._simple, property, oldVariant, newVariant) >= 0) _propertyEvents++;
        Native.VariantClear(ref oldVariant); Native.VariantClear(ref newVariant);
    }

    private static RawVariant Variant(object value)
    {
        var result = new RawVariant();
        if (value is bool boolean) Bool(&result, boolean); else Bstr(&result, (string)value);
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_children is not null)
        {
            UiaIssueProvider[] children;
            lock (_children) children = _children.Values.ToArray();
            foreach (var child in children) child.Dispose();
        }
        if (_simple != 0) Native.UiaDisconnectProvider(_simple);
        Release(ref _simple); Release(ref _unknown);
    }

    private static int ControlType(string role) => role switch { "button" => 50000, "edit" => 50004, "listbox" => 50008, "option" => 50007, "text" => 50020, "group" => 50026, _ => 50033 };
    private static void I4(RawVariant* value, int data) { value->Type = VT_I4; value->Value = data; }
    private static void Bool(RawVariant* value, bool data) { value->Type = VT_BOOL; value->Value = data ? -1 : 0; }
    private static void Bstr(RawVariant* value, string data) { value->Type = VT_BSTR; value->Value = Native.SysAllocString(data); }
    private static nint Query(nint pointer, Guid iid) { if (pointer == 0) return 0; nint result = 0; ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)pointer)[0])(pointer, &iid, &result); return result; }
    private static void Release(ref nint pointer) { if (pointer != 0) { ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)pointer)[2])(pointer); pointer = 0; } }
}

internal sealed unsafe class UiaIssueWrappers : ComWrappers
{
    internal static readonly Guid Simple = new("d6dd68d1-86fd-4332-8666-9abedea2d24c"), Fragment = new("f7063da8-8359-439c-9297-bbc5299a7d87"), Root = new("620ce2a5-ab8f-40a9-86cb-de3c75599b58"), Invoke = new("54fcb24b-e18e-47a2-b4d3-eccbe77599a2"), Value = new("c7935180-6fb3-4201-b174-7df73adbf64a"), Selection = new("fb8b03af-3bdf-48d4-bd36-1a65793be168"), SelectionItem = new("2acad808-b2d4-452d-a407-91ff1ad167b2");
    private static readonly ComInterfaceEntry* RootEntries, PlainEntries, ButtonEntries, EditEntries, ListEntries, OptionEntries;

    static UiaIssueWrappers()
    {
        GetIUnknownImpl(out var query, out var addRef, out var release);
        var simple = Entry(Simple, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&Options, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, nint*, int>)&Pattern, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, RawVariant*, int>)&Property, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Host);
        var fragment = Entry(Fragment, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, nint*, int>)&Navigate, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&RuntimeId, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)&Rect, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&NullArray, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Focus, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&RootProvider);
        var root = Entry(Root, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double, double, nint*, int>)&Point, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&GetFocus);
        var invoke = Entry(Invoke, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&InvokeCall);
        var value = Entry(Value, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint, int>)&SetValue, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&GetValue, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&ReadOnly);
        var selection = Entry(Selection, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&GetSelection, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&False, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&False);
        var selectionItem = Entry(SelectionItem, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Select, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Add, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Remove, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&Selected, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Container);
        RootEntries = Entries(simple, fragment, root); PlainEntries = Entries(simple, fragment); ButtonEntries = Entries(simple, fragment, invoke); EditEntries = Entries(simple, fragment, value); ListEntries = Entries(simple, fragment, selection); OptionEntries = Entries(simple, fragment, selectionItem);
    }

    private static ComInterfaceEntry Entry(Guid iid, params nint[] slots)
    {
        var vtable = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(UiaIssueWrappers), IntPtr.Size * slots.Length);
        for (var index = 0; index < slots.Length; index++) vtable[index] = slots[index];
        return new() { IID = iid, Vtable = (nint)vtable };
    }

    private static ComInterfaceEntry* Entries(params ComInterfaceEntry[] entries)
    {
        var result = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(UiaIssueWrappers), sizeof(ComInterfaceEntry) * entries.Length);
        for (var index = 0; index < entries.Length; index++) result[index] = entries[index];
        return result;
    }

    protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
    {
        if (obj is not UiaIssueProvider provider) { count = 0; return null; }
        if (provider.IsRoot) { count = 3; return RootEntries; }
        count = provider.Role is "button" or "edit" or "listbox" or "option" ? 3 : 2;
        return provider.Role switch { "button" => ButtonEntries, "edit" => EditEntries, "listbox" => ListEntries, "option" => OptionEntries, _ => PlainEntries };
    }

    protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) => throw new NotSupportedException();
    protected override void ReleaseObjects(System.Collections.IEnumerable objects) { }
    private static UiaIssueProvider Provider(ComInterfaceDispatch* dispatch) => ComInterfaceDispatch.GetInstance<UiaIssueProvider>(dispatch);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Options(ComInterfaceDispatch* d, int* x) => Provider(d).Options(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Pattern(ComInterfaceDispatch* d, int i, nint* x) => Provider(d).Pattern(i, out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Property(ComInterfaceDispatch* d, int i, RawVariant* x) => Provider(d).Property(i, x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Host(ComInterfaceDispatch* d, nint* x) => Provider(d).Host(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Navigate(ComInterfaceDispatch* d, int i, nint* x) => Provider(d).Navigate(i, out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int RuntimeId(ComInterfaceDispatch* d, nint* x) => Provider(d).RuntimeId(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Rect(ComInterfaceDispatch* d, double* x) => Provider(d).Rect(x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Focus(ComInterfaceDispatch* d) => Provider(d).Focus();
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int RootProvider(ComInterfaceDispatch* d, nint* x) => Provider(d).Root(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Point(ComInterfaceDispatch* d, double x, double y, nint* z) => Provider(d).Point(x, y, out *z);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int GetFocus(ComInterfaceDispatch* d, nint* x) => Provider(d).GetFocus(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int InvokeCall(ComInterfaceDispatch* d) => Provider(d).Invoke();
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int SetValue(ComInterfaceDispatch* d, nint x) => Provider(d).SetValue(x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int GetValue(ComInterfaceDispatch* d, nint* x) => Provider(d).Value(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int ReadOnly(ComInterfaceDispatch* d, int* x) => Provider(d).IsReadOnly(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int GetSelection(ComInterfaceDispatch* d, nint* x) => Provider(d).GetSelection(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Select(ComInterfaceDispatch* d) => Provider(d).Select();
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Add(ComInterfaceDispatch* d) => Provider(d).AddToSelection();
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Remove(ComInterfaceDispatch* d) => Provider(d).RemoveFromSelection();
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Selected(ComInterfaceDispatch* d, int* x) => Provider(d).IsSelected(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Container(ComInterfaceDispatch* d, nint* x) => Provider(d).SelectionContainer(out *x);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int NullArray(ComInterfaceDispatch* d, nint* x) { *x = 0; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int False(ComInterfaceDispatch* d, int* x) { *x = 0; return 0; }
}
