using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lucent.Core;

namespace Lucent.Platform.Windows;

internal sealed unsafe class UiaWrappers : ComWrappers
{
    internal static readonly Guid Simple = new("d6dd68d1-86fd-4332-8666-9abedea2d24c"), Fragment = new("f7063da8-8359-439c-9297-bbc5299a7d87"), Root = new("620ce2a5-ab8f-40a9-86cb-de3c75599b58"), Invoke = new("54fcb24b-e18e-47a2-b4d3-eccbe77599a2"), ValuePattern = new("c7935180-6fb3-4201-b174-7df73adbf64a"), SelectionPattern = new("fb8b03af-3bdf-48d4-bd36-1a65793be168"), SelectionItem = new("2acad808-b2d4-452d-a407-91ff1ad167b2"), Scroll = new("b38b8077-1fc3-42a5-8cae-d40c2215055a");
    private static readonly ComInterfaceEntry* RootEntries, PlainEntries, InvokeEntries, ValueEntries, ListEntries, ItemEntries, ScrollEntries;
    static UiaWrappers()
    {
        GetIUnknownImpl(out var query, out var addRef, out var release);
        var simple = Entry(Simple, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&Options, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, nint*, int>)&Pattern, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, WindowsUiaProvider.RawVariant*, int>)&Property, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Host);
        var fragment = Entry(Fragment, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, nint*, int>)&Navigate, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&RuntimeId, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)&Rectangle, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Null, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Focus, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&RootProvider);
        var root = Entry(Root, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double, double, nint*, int>)&Point, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&GetFocus);
        var invoke = Entry(Invoke, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&InvokeCall);
        var value = Entry(ValuePattern, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint, int>)&SetValue, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Value, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&False);
        var selection = Entry(SelectionPattern, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Selection, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&False, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&False);
        var item = Entry(SelectionItem, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Select, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Reject, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Reject, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&Selected, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Container);
        var scroll = Entry(Scroll, query, addRef, release, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, int, int>)&ScrollCall, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double, double, int>)&ScrollPercent, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)&HorizontalPercent, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)&VerticalPercent, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)&HorizontalView, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)&VerticalView, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&HorizontalScrollable, (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&VerticalScrollable);
        RootEntries = Entries(simple, fragment, root); PlainEntries = Entries(simple, fragment); InvokeEntries = Entries(simple, fragment, invoke); ValueEntries = Entries(simple, fragment, value); ListEntries = Entries(simple, fragment, selection); ItemEntries = Entries(simple, fragment, item); ScrollEntries = Entries(simple, fragment, scroll);
    }
    protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
    {
        var provider = (WindowsUiaProvider)obj;
        if (provider.IsRoot) { count = 3; return RootEntries; }
        var actions = provider.ProviderActions;
        if (actions.HasFlag(SemanticAction.Invoke)) { count = 3; return InvokeEntries; }
        if (actions.HasFlag(SemanticAction.SetValue)) { count = 3; return ValueEntries; }
        if (actions.HasFlag(SemanticAction.Select)) { count = 3; return ItemEntries; }
        if (actions.HasFlag(SemanticAction.Scroll)) { count = 3; return ScrollEntries; }
        if (provider.ProviderRole == SemanticRole.List) { count = 3; return ListEntries; }
        count = 2; return PlainEntries;
    }
    protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) => throw new NotSupportedException(); protected override void ReleaseObjects(System.Collections.IEnumerable objects) { }
    private static ComInterfaceEntry Entry(Guid iid, params nint[] slots) { var table = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(UiaWrappers), IntPtr.Size * slots.Length); for (var i = 0; i < slots.Length; i++) table[i] = slots[i]; return new() { IID = iid, Vtable = (nint)table }; }
    private static ComInterfaceEntry* Entries(params ComInterfaceEntry[] entries) { var result = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(UiaWrappers), sizeof(ComInterfaceEntry) * entries.Length); for (var i = 0; i < entries.Length; i++) result[i] = entries[i]; return result; }
    private static WindowsUiaProvider P(ComInterfaceDispatch* d) => ComInterfaceDispatch.GetInstance<WindowsUiaProvider>(d);
    private static int Guard(Func<int> action) { try { return action(); } catch { return unchecked((int)0x80004005); } }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Options(ComInterfaceDispatch* d, int* x) { *x = 0; return Guard(() => P(d).Options(out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Pattern(ComInterfaceDispatch* d, int i, nint* x) { *x = 0; return Guard(() => P(d).Pattern(i, out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Property(ComInterfaceDispatch* d, int i, WindowsUiaProvider.RawVariant* x) { *x = default; return Guard(() => P(d).Property(i, x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Host(ComInterfaceDispatch* d, nint* x) { *x = 0; return Guard(() => P(d).Host(out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Navigate(ComInterfaceDispatch* d, int i, nint* x) { *x = 0; return Guard(() => P(d).Navigate(i, out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int RuntimeId(ComInterfaceDispatch* d, nint* x) { *x = 0; return Guard(() => P(d).RuntimeId(out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Rectangle(ComInterfaceDispatch* d, double* x) { x[0] = x[1] = x[2] = x[3] = 0; return Guard(() => P(d).Rectangle(x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Focus(ComInterfaceDispatch* d) => Guard(() => P(d).Focus());
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int RootProvider(ComInterfaceDispatch* d, nint* x) { *x = 0; return Guard(() => P(d).Root(out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Point(ComInterfaceDispatch* d, double x, double y, nint* z) { *z = 0; return Guard(() => P(d).Point(x, y, out *z)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int GetFocus(ComInterfaceDispatch* d, nint* x) { *x = 0; return Guard(() => P(d).GetFocus(out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int InvokeCall(ComInterfaceDispatch* d) => Guard(() => P(d).Invoke());
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int SetValue(ComInterfaceDispatch* d, nint x) => Guard(() => P(d).SetValue(x));
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Value(ComInterfaceDispatch* d, nint* x) { *x = 0; return Guard(() => P(d).Value(out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Selection(ComInterfaceDispatch* d, nint* x) { *x = 0; return Guard(() => P(d).Selection(out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Select(ComInterfaceDispatch* d) => Guard(() => P(d).Select());
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Selected(ComInterfaceDispatch* d, int* x) { *x = 0; return Guard(() => P(d).Selected(out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Container(ComInterfaceDispatch* d, nint* x) { *x = 0; return Guard(() => P(d).Container(out *x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int ScrollCall(ComInterfaceDispatch* d, int x, int y) => Guard(() => P(d).Scroll(x, y));
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int ScrollPercent(ComInterfaceDispatch* d, double x, double y) => Guard(() => P(d).ScrollPercent(x, y));
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int HorizontalPercent(ComInterfaceDispatch* d, double* x) { *x = 0; return Guard(() => P(d).ScrollMetric(false, false, x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int VerticalPercent(ComInterfaceDispatch* d, double* x) { *x = 0; return Guard(() => P(d).ScrollMetric(true, false, x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int HorizontalView(ComInterfaceDispatch* d, double* x) { *x = 0; return Guard(() => P(d).ScrollMetric(false, true, x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int VerticalView(ComInterfaceDispatch* d, double* x) { *x = 0; return Guard(() => P(d).ScrollMetric(true, true, x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int HorizontalScrollable(ComInterfaceDispatch* d, int* x) { *x = 0; return Guard(() => P(d).Scrollable(false, x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int VerticalScrollable(ComInterfaceDispatch* d, int* x) { *x = 0; return Guard(() => P(d).Scrollable(true, x)); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Null(ComInterfaceDispatch* d, nint* x) { *x = 0; return Guard(() => P(d).IsAvailable ? WindowsUiaProvider.Ok : WindowsUiaProvider.NotAvailable); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int False(ComInterfaceDispatch* d, int* x) { *x = 0; return Guard(() => P(d).IsAvailable ? WindowsUiaProvider.Ok : WindowsUiaProvider.NotAvailable); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])] private static int Reject(ComInterfaceDispatch* d) => Guard(() => P(d).UnsupportedSelection());
}
