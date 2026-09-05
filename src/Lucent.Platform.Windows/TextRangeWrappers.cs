using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucent.Platform.Windows;

/// <summary>NativeAOT COM vtables for the Win32 ITextRangeProvider ABI.</summary>
internal sealed unsafe class TextRangeWrappers : ComWrappers
{
    private static readonly ComInterfaceEntry* EntriesPointer;

    static TextRangeWrappers()
    {
        GetIUnknownImpl(out var query, out var addRef, out var release);
        var range = Entry(
            WindowsUiaTextRange.Interface,
            query,
            addRef,
            release,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Clone,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint, int*, int>)
                    &Compare,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    int,
                    nint,
                    int,
                    int*,
                    int>)
                    &CompareEndpoints,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, int>)&Expand,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    int,
                    WindowsUiaProvider.RawVariant,
                    int,
                    nint*,
                    int>)
                    &FindAttribute,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    nint,
                    int,
                    int,
                    nint*,
                    int>)
                    &FindText,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    int,
                    WindowsUiaProvider.RawVariant*,
                    int>)
                    &Attribute,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Bounding,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Enclosing,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, nint*, int>)&Text,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, int, int*, int>)
                    &Move,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    int,
                    int,
                    int,
                    int*,
                    int>)
                    &MoveEndpoint,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, nint, int, int>)
                    &MoveEndpointByRange,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)&Select,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)
                    &UnsupportedSelection,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int>)
                    &UnsupportedSelection,
            (nint)
                (delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, int, int>)
                    &ScrollIntoView,
            (nint)(delegate* unmanaged[MemberFunction]<ComInterfaceDispatch*, nint*, int>)&Children
        );
        var peer = Entry(
            WindowsUiaTextRange.PeerInterface,
            query,
            addRef,
            release,
            (nint)
                (delegate* unmanaged[MemberFunction]<
                    ComInterfaceDispatch*,
                    long*,
                    long*,
                    int*,
                    int*,
                    int>)
                    &Describe
        );
        EntriesPointer = Entries(range, peer);
    }

    protected override ComInterfaceEntry* ComputeVtables(
        object obj,
        CreateComInterfaceFlags flags,
        out int count
    )
    {
        count = 2;
        return EntriesPointer;
    }

    protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) =>
        throw new NotSupportedException();

    protected override void ReleaseObjects(System.Collections.IEnumerable objects) { }

    private static WindowsUiaTextRange R(ComInterfaceDispatch* dispatch) =>
        ComInterfaceDispatch.GetInstance<WindowsUiaTextRange>(dispatch);

    private static int Guard(Func<int> action)
    {
        try
        {
            return action();
        }
        catch
        {
            return WindowsUiaProvider.Fail;
        }
    }

    private static ComInterfaceEntry Entry(Guid iid, params nint[] slots)
    {
        var table = (nint*)
            RuntimeHelpers.AllocateTypeAssociatedMemory(
                typeof(TextRangeWrappers),
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
                typeof(TextRangeWrappers),
                sizeof(ComInterfaceEntry) * entries.Length
            );
        for (var i = 0; i < entries.Length; i++)
            result[i] = entries[i];
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Clone(ComInterfaceDispatch* d, nint* value)
    {
        *value = 0;
        return Guard(() => R(d).Clone(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Compare(ComInterfaceDispatch* d, nint target, int* value)
    {
        *value = 0;
        return Guard(() => R(d).Compare(target, out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int CompareEndpoints(
        ComInterfaceDispatch* d,
        int endpoint,
        nint target,
        int targetEndpoint,
        int* value
    )
    {
        *value = 0;
        return Guard(() => R(d).CompareEndpoints(endpoint, target, targetEndpoint, out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Expand(ComInterfaceDispatch* d, int unit) => Guard(() => R(d).Expand(unit));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int FindAttribute(
        ComInterfaceDispatch* d,
        int attribute,
        WindowsUiaProvider.RawVariant value,
        int backward,
        nint* result
    )
    {
        *result = 0;
        return Guard(() => R(d).FindAttribute(attribute, value, backward, out *result));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int FindText(
        ComInterfaceDispatch* d,
        nint text,
        int backward,
        int ignoreCase,
        nint* result
    )
    {
        *result = 0;
        return Guard(() => R(d).FindText(text, backward, ignoreCase, out *result));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Attribute(
        ComInterfaceDispatch* d,
        int attribute,
        WindowsUiaProvider.RawVariant* value
    )
    {
        *value = default;
        return Guard(() => R(d).Attribute(attribute, value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Bounding(ComInterfaceDispatch* d, nint* value)
    {
        *value = 0;
        return Guard(() => R(d).BoundingRectangles(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Enclosing(ComInterfaceDispatch* d, nint* value)
    {
        *value = 0;
        return Guard(() => R(d).EnclosingElement(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Text(ComInterfaceDispatch* d, int maxLength, nint* value)
    {
        *value = 0;
        return Guard(() => R(d).Text(maxLength, out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Move(ComInterfaceDispatch* d, int unit, int count, int* moved)
    {
        *moved = 0;
        return Guard(() => R(d).Move(unit, count, out *moved));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int MoveEndpoint(
        ComInterfaceDispatch* d,
        int endpoint,
        int unit,
        int count,
        int* moved
    )
    {
        *moved = 0;
        return Guard(() => R(d).MoveEndpoint(endpoint, unit, count, out *moved));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int MoveEndpointByRange(
        ComInterfaceDispatch* d,
        int endpoint,
        nint target,
        int targetEndpoint
    ) => Guard(() => R(d).MoveEndpointByRange(endpoint, target, targetEndpoint));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Select(ComInterfaceDispatch* d) => Guard(() => R(d).Select());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int UnsupportedSelection(ComInterfaceDispatch* d) =>
        Guard(() => R(d).UnsupportedSelection());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int ScrollIntoView(ComInterfaceDispatch* d, int align) =>
        Guard(() => R(d).ScrollIntoView(align));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Children(ComInterfaceDispatch* d, nint* value)
    {
        *value = 0;
        return Guard(() => R(d).Children(out *value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Describe(
        ComInterfaceDispatch* d,
        long* epoch,
        long* element,
        int* start,
        int* end
    )
    {
        *epoch = *element = 0;
        *start = *end = 0;
        return Guard(() => R(d).Describe(out *epoch, out *element, out *start, out *end));
    }
}
