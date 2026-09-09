using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lucent.Core;

namespace Lucent.Platform.Windows;

internal sealed unsafe class UiaWrappers : ComWrappers
{
    internal static readonly Guid Simple = new("d6dd68d1-86fd-4332-8666-9abedea2d24c"),
        Fragment = new("f7063da8-8359-439c-9297-bbc5299a7d87"),
        Root = new("620ce2a5-ab8f-40a9-86cb-de3c75599b58"),
        Invoke = new("54fcb24b-e18e-47a2-b4d3-eccbe77599a2"),
        ValuePattern = new("c7935180-6fb3-4201-b174-7df73adbf64a"),
        SelectionPattern = new("fb8b03af-3bdf-48d4-bd36-1a65793be168"),
        SelectionItem = new("2acad808-b2d4-452d-a407-91ff1ad167b2"),
        Scroll = new("b38b8077-1fc3-42a5-8cae-d40c2215055a"),
        ExpandCollapse = new("d847d3a5-cab0-4a98-8c32-ecb45c59ad24"),
        RangeValue = new("36dc7aef-33e6-4691-afe1-2be7274b3d33"),
        TogglePattern = new("56d00bd0-c4f4-433c-a836-1a52a57e0892"),
        TextProvider = new("3589c92c-63f3-4367-99bb-ada653b77cf2"),
        TextProvider2 = new("0dc5e6ed-3e16-4bf1-8f9a-a979878bc195");
    private static readonly ComInterfaceEntry* RootEntries,
        NodeEntries;

    static UiaWrappers()
    {
        GetIUnknownImpl(out var query, out var addRef, out var release);
        var simple = Entry(
            Simple,
            query,
            addRef,
            release,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&Options,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, nint*, int>)
                    &Pattern,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    int,
                    WindowsUiaProvider.RawVariant*,
                    int>)
                    &Property,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Host
        );
        var fragment = Entry(
            Fragment,
            query,
            addRef,
            release,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, nint*, int>)
                    &Navigate,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&RuntimeId,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &Rectangle,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Null,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Focus,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)
                    &RootProvider
        );
        var root = Entry(
            Root,
            query,
            addRef,
            release,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    double,
                    double,
                    nint*,
                    int>)
                    &Point,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&GetFocus
        );
        var invoke = Entry(
            Invoke,
            query,
            addRef,
            release,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&InvokeCall
        );
        var value = Entry(
            ValuePattern,
            query,
            addRef,
            release,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint, int>)&SetValue,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Value,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)
                    &ValueReadOnly
        );
        var selection = Entry(
            SelectionPattern,
            query,
            addRef,
            release,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Selection,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)
                    &CanSelectMultiple,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)
                    &IsSelectionRequired
        );
        var item = Entry(
            SelectionItem,
            query,
            addRef,
            release,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Select,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Reject,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Reject,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&Selected,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Container
        );
        var scroll = Entry(
            Scroll,
            query,
            addRef,
            release,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, int, int>)
                    &ScrollCall,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double, double, int>)
                    &ScrollPercent,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &HorizontalPercent,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &VerticalPercent,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &HorizontalView,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &VerticalView,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)
                    &HorizontalScrollable,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)
                    &VerticalScrollable
        );
        // IExpandCollapseProvider's authoritative Windows SDK vtable order is
        // Expand, Collapse, get_ExpandCollapseState (UIAutomationCore.h).
        var expand = Entry(
            ExpandCollapse,
            query,
            addRef,
            release,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Expand,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Collapse,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)
                    &ExpandCollapseState
        );
        var range = Entry(
            RangeValue,
            query,
            addRef,
            release,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double, int>)
                    &RangeSetValue,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &RangeValueGet,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)
                    &RangeReadOnly,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &RangeMaximum,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &RangeMinimum,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &RangeLargeChange,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, double*, int>)
                    &RangeSmallChange
        );
        // UIAutomationCore.h: IToggleProvider is Toggle, then get_ToggleState.
        var toggle = Entry(
            TogglePattern,
            query,
            addRef,
            release,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Toggle,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)&ToggleState
        );
        var text = Entry(
            TextProvider,
            query,
            addRef,
            release,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)
                    &TextSelection,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)
                    &VisibleRanges,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint, nint*, int>)
                    &RangeFromChild,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    WindowsUiaProvider.UiaPoint,
                    nint*,
                    int>)
                    &TextRangeFromPoint,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)
                    &DocumentRange,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)
                    &SupportedTextSelection
        );
        var text2 = Entry(
            TextProvider2,
            query,
            addRef,
            release,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)
                    &TextSelection,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)
                    &VisibleRanges,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint, nint*, int>)
                    &RangeFromChild,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    WindowsUiaProvider.UiaPoint,
                    nint*,
                    int>)
                    &TextRangeFromPoint,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)
                    &DocumentRange,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, int>)
                    &SupportedTextSelection,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint, nint*, int>)
                    &RangeFromAnnotation,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int*, nint*, int>)
                    &CaretRange
        );
        RootEntries = Entries(simple, fragment, root);
        // A stable CCW implements the transport interfaces. Pattern() advertises only
        // capabilities present in the current immutable semantic snapshot. Keeping the
        // transport stable supports compound controls and later optional capabilities
        // (for example progress changing from indeterminate to determinate) without
        // invalidating an accessibility client's retained provider identity.
        NodeEntries = Entries(
            simple,
            fragment,
            invoke,
            value,
            selection,
            item,
            scroll,
            expand,
            range,
            toggle,
            text,
            text2
        );
    }

    protected override ComInterfaceEntry* ComputeVtables(
        object obj,
        CreateComInterfaceFlags flags,
        out int count
    )
    {
        if (((WindowsUiaProvider)obj).IsRoot)
        {
            count = 3;
            return RootEntries;
        }
        count = 12;
        return NodeEntries;
    }

    protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) =>
        throw new NotSupportedException();

    protected override void ReleaseObjects(System.Collections.IEnumerable objects) { }

    private static ComInterfaceEntry Entry(Guid iid, params nint[] slots)
    {
        var table = (nint*)
            RuntimeHelpers.AllocateTypeAssociatedMemory(
                typeof(UiaWrappers),
                IntPtr.Size * slots.Length
            );
        for (var i = 0; i < slots.Length; i++)
            table[i] = slots[i];
        return new() { IID = iid, Vtable = (nint)table };
    }

    private static ComInterfaceEntry* Entries(params ComInterfaceEntry[] entries)
    {
        var result = (ComInterfaceEntry*)
            RuntimeHelpers.AllocateTypeAssociatedMemory(
                typeof(UiaWrappers),
                sizeof(ComInterfaceEntry) * entries.Length
            );
        for (var i = 0; i < entries.Length; i++)
            result[i] = entries[i];
        return result;
    }

    private static WindowsUiaProvider P(ComInterfaceDispatch* d) =>
        ComInterfaceDispatch.GetInstance<WindowsUiaProvider>(d);

    private static int Guard(Func<int> action)
    {
        try
        {
            return action();
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Options(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).Options(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Pattern(ComInterfaceDispatch* d, int i, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).Pattern(i, out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Property(ComInterfaceDispatch* d, int i, WindowsUiaProvider.RawVariant* x)
    {
        *x = default;
        return Guard(() => P(d).Property(i, x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Host(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).Host(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Navigate(ComInterfaceDispatch* d, int i, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).Navigate(i, out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RuntimeId(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).RuntimeId(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Rectangle(ComInterfaceDispatch* d, double* x)
    {
        x[0] = x[1] = x[2] = x[3] = 0;
        return Guard(() => P(d).Rectangle(x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Focus(ComInterfaceDispatch* d) => Guard(() => P(d).Focus());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RootProvider(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).Root(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Point(ComInterfaceDispatch* d, double x, double y, nint* z)
    {
        *z = 0;
        return Guard(() => P(d).Point(x, y, out *z));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetFocus(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).GetFocus(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int InvokeCall(ComInterfaceDispatch* d) => Guard(() => P(d).Invoke());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Toggle(ComInterfaceDispatch* d) => Guard(() => P(d).Toggle());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int ToggleState(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).ToggleState(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int CanSelectMultiple(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).CanSelectMultiple(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int IsSelectionRequired(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).IsSelectionRequired(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Expand(ComInterfaceDispatch* d) => Guard(() => P(d).Expand());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Collapse(ComInterfaceDispatch* d) => Guard(() => P(d).Collapse());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int ExpandCollapseState(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).ExpandCollapseState(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RangeSetValue(ComInterfaceDispatch* d, double value) =>
        Guard(() => P(d).SetRangeValue(value));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RangeValueGet(ComInterfaceDispatch* d, double* value)
    {
        *value = 0;
        return Guard(() => P(d).RangeValue(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RangeReadOnly(ComInterfaceDispatch* d, int* value)
    {
        *value = 0;
        return Guard(() => P(d).RangeReadOnly(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RangeMaximum(ComInterfaceDispatch* d, double* value)
    {
        *value = 0;
        return Guard(() => P(d).RangeMaximum(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RangeMinimum(ComInterfaceDispatch* d, double* value)
    {
        *value = 0;
        return Guard(() => P(d).RangeMinimum(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RangeLargeChange(ComInterfaceDispatch* d, double* value)
    {
        *value = 0;
        return Guard(() => P(d).RangeLargeChange(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RangeSmallChange(ComInterfaceDispatch* d, double* value)
    {
        *value = 0;
        return Guard(() => P(d).RangeSmallChange(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int SetValue(ComInterfaceDispatch* d, nint x) => Guard(() => P(d).SetValue(x));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Value(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).Value(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int ValueReadOnly(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).ValueReadOnly(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Selection(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).Selection(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Select(ComInterfaceDispatch* d) => Guard(() => P(d).Select());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Selected(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).Selected(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Container(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).Container(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int ScrollCall(ComInterfaceDispatch* d, int x, int y) =>
        Guard(() => P(d).Scroll(x, y));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int ScrollPercent(ComInterfaceDispatch* d, double x, double y) =>
        Guard(() => P(d).ScrollPercent(x, y));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int HorizontalPercent(ComInterfaceDispatch* d, double* x)
    {
        *x = 0;
        return Guard(() => P(d).ScrollMetric(false, false, x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int VerticalPercent(ComInterfaceDispatch* d, double* x)
    {
        *x = 0;
        return Guard(() => P(d).ScrollMetric(true, false, x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int HorizontalView(ComInterfaceDispatch* d, double* x)
    {
        *x = 0;
        return Guard(() => P(d).ScrollMetric(false, true, x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int VerticalView(ComInterfaceDispatch* d, double* x)
    {
        *x = 0;
        return Guard(() => P(d).ScrollMetric(true, true, x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int HorizontalScrollable(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).Scrollable(false, x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int VerticalScrollable(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).Scrollable(true, x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Null(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() =>
            P(d).IsAvailable ? WindowsUiaProvider.Ok : WindowsUiaProvider.NotAvailable
        );
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int False(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() =>
            P(d).IsAvailable ? WindowsUiaProvider.Ok : WindowsUiaProvider.NotAvailable
        );
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Reject(ComInterfaceDispatch* d) => Guard(() => P(d).UnsupportedSelection());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int TextSelection(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).TextSelection(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int VisibleRanges(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).TextVisibleRanges(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RangeFromChild(ComInterfaceDispatch* d, nint child, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).TextRangeFromChild(child, out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int TextRangeFromPoint(
        ComInterfaceDispatch* d,
        WindowsUiaProvider.UiaPoint point,
        nint* value
    )
    {
        *value = 0;
        return Guard(() => P(d).TextRangeFromPoint(point.X, point.Y, out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int DocumentRange(ComInterfaceDispatch* d, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).TextDocumentRange(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int SupportedTextSelection(ComInterfaceDispatch* d, int* x)
    {
        *x = 0;
        return Guard(() => P(d).TextSupportedSelection(out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RangeFromAnnotation(ComInterfaceDispatch* d, nint annotation, nint* x)
    {
        *x = 0;
        return Guard(() => P(d).TextRangeFromAnnotation(annotation, out *x));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int CaretRange(ComInterfaceDispatch* d, int* active, nint* x)
    {
        *active = 0;
        *x = 0;
        return Guard(() => P(d).TextCaretRange(out *active, out *x));
    }
}
