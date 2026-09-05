using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>A bounded plain-text UIA range whose reads use only the owning provider's published snapshot.</summary>
internal sealed unsafe partial class WindowsUiaTextRange
{
    internal const int StartEndpoint = 0,
        EndEndpoint = 1;
    internal static readonly Guid Interface = new("5347ad7b-c355-46f8-aff5-909033582f63"),
        PeerInterface = new("8e7038d7-3b28-46de-a43d-61c88647a8e1");
    private static readonly TextRangeWrappers Wrappers = new();
    private readonly WindowsUiaProvider _owner;
    private long _endpoints;

    internal WindowsUiaTextRange(WindowsUiaProvider owner, int start, int end)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _endpoints = Pack(Math.Min(start, end), Math.Max(start, end));
    }

    internal nint CreateInterfacePointer()
    {
        var unknown = Wrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
        try
        {
            return Query(unknown, Interface);
        }
        finally
        {
            Release(unknown);
        }
    }

    private bool TrySnapshot(
        out WindowsUiaProvider.TextSnapshot snapshot,
        out int start,
        out int end
    )
    {
        start = end = 0;
        if (!_owner.TryGetTextSnapshot(out snapshot))
            return false;
        while (true)
        {
            var state = Volatile.Read(ref _endpoints);
            (start, end) = Unpack(state);
            var degenerate = start == end;
            start = Normalize(snapshot.Text, Math.Clamp(start, 0, snapshot.Text.Length), false);
            end = degenerate
                ? start
                : Normalize(snapshot.Text, Math.Clamp(end, start, snapshot.Text.Length), true);
            var normalized = Pack(start, end);
            if (Interlocked.CompareExchange(ref _endpoints, normalized, state) == state)
                return true;
        }
    }

    private bool TrySnapshot(out WindowsUiaProvider.TextSnapshot snapshot) =>
        TrySnapshot(out snapshot, out _, out _);

    internal int Clone(out nint value)
    {
        value = 0;
        if (!TrySnapshot(out _, out var start, out var end))
            return WindowsUiaProvider.NotAvailable;
        value = new WindowsUiaTextRange(_owner, start, end).CreateInterfacePointer();
        return value == 0 ? WindowsUiaProvider.OutOfMemory : WindowsUiaProvider.Ok;
    }

    internal int Describe(out long epoch, out long element, out int start, out int end)
    {
        epoch = element = 0;
        start = end = 0;
        if (!TrySnapshot(out _, out start, out end))
            return WindowsUiaProvider.NotAvailable;
        _owner.TextOwnerIdentity(out epoch, out element);
        return WindowsUiaProvider.Ok;
    }

    internal int Compare(nint target, out int value)
    {
        value = 0;
        if (!TrySnapshot(out _, out var ownStart, out var ownEnd))
            return WindowsUiaProvider.NotAvailable;
        var hr = Peer(target, out var epoch, out var element, out var start, out var end);
        if (hr < 0)
            return WindowsUiaProvider.InvalidArgument;
        _owner.TextOwnerIdentity(out var ownEpoch, out var ownElement);
        value =
            epoch == ownEpoch && element == ownElement && start == ownStart && end == ownEnd
                ? 1
                : 0;
        return WindowsUiaProvider.Ok;
    }

    internal int CompareEndpoints(int endpoint, nint target, int targetEndpoint, out int value)
    {
        value = 0;
        if (!ValidEndpoint(endpoint) || !ValidEndpoint(targetEndpoint))
            return WindowsUiaProvider.InvalidArgument;
        if (!TrySnapshot(out _, out var ownStart, out var ownEnd))
            return WindowsUiaProvider.NotAvailable;
        var hr = Peer(target, out var epoch, out var element, out var start, out var end);
        if (hr < 0)
            return WindowsUiaProvider.InvalidArgument;
        _owner.TextOwnerIdentity(out var ownEpoch, out var ownElement);
        if (epoch != ownEpoch || element != ownElement)
            return WindowsUiaProvider.InvalidArgument;
        value =
            (endpoint == StartEndpoint ? ownStart : ownEnd)
            - (targetEndpoint == StartEndpoint ? start : end);
        return WindowsUiaProvider.Ok;
    }

    internal int Expand(int unit)
    {
        if (!TrySnapshot(out var snapshot, out var start, out _))
            return WindowsUiaProvider.NotAvailable;
        if (!ValidUnit(unit))
            return WindowsUiaProvider.InvalidArgument;
        var expanded = UnitAt(snapshot, unit, start);
        SetEndpoints(expanded.Start, expanded.End);
        return WindowsUiaProvider.Ok;
    }

    internal int FindAttribute(
        int attribute,
        WindowsUiaProvider.RawVariant value,
        int backward,
        out nint result
    )
    {
        result = 0;
        if (!TrySnapshot(out var snapshot, out var start, out var end))
            return WindowsUiaProvider.NotAvailable;
        bool? expected = attribute switch
        {
            40015 => snapshot.IsReadOnly,
            _ => null,
        };
        if (expected is null || value.Type != 11 || (value.Value != 0) != expected.Value)
            return WindowsUiaProvider.Ok;
        result = new WindowsUiaTextRange(_owner, start, end).CreateInterfacePointer();
        return result == 0 ? WindowsUiaProvider.OutOfMemory : WindowsUiaProvider.Ok;
    }

    internal int FindText(nint text, int backward, int ignoreCase, out nint result)
    {
        result = 0;
        if (!TrySnapshot(out var snapshot, out var start, out var end))
            return WindowsUiaProvider.NotAvailable;
        if (text == 0)
            return WindowsUiaProvider.InvalidArgument;
        var needle = Marshal.PtrToStringBSTR(text) ?? "";
        if (needle.Length == 0)
            return WindowsUiaProvider.InvalidArgument;
        var comparison =
            ignoreCase != 0
                ? StringComparison.CurrentCultureIgnoreCase
                : StringComparison.CurrentCulture;
        var haystack = snapshot.Text.Substring(start, end - start);
        var relative =
            backward != 0
                ? haystack.LastIndexOf(needle, comparison)
                : haystack.IndexOf(needle, comparison);
        if (relative < 0)
            return WindowsUiaProvider.Ok;
        var found = start + relative;
        result = new WindowsUiaTextRange(
            _owner,
            found,
            found + needle.Length
        ).CreateInterfacePointer();
        return result == 0 ? WindowsUiaProvider.OutOfMemory : WindowsUiaProvider.Ok;
    }

    internal int Attribute(int attribute, WindowsUiaProvider.RawVariant* value)
    {
        *value = default;
        if (!TrySnapshot(out var snapshot))
            return WindowsUiaProvider.NotAvailable;
        if (attribute == 40015)
            SetBool(value, snapshot.IsReadOnly);
        else
            return ReservedNotSupported(value);
        return WindowsUiaProvider.Ok;
    }

    internal int BoundingRectangles(out nint value)
    {
        value = 0;
        if (!TrySnapshot(out var snapshot, out var start, out var end))
            return WindowsUiaProvider.NotAvailable;
        var rectangles = _owner.TextBoundingRectangles(snapshot, start, end);
        value = SafeArrayCreateVector(5, 0, checked((uint)(rectangles.Count * 4)));
        if (value == 0)
            return WindowsUiaProvider.OutOfMemory;
        var index = 0;
        foreach (var rectangle in rectangles)
        foreach (
            var coordinate in new[] { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height }
        )
        {
            var data = coordinate;
            var at = index++;
            var hr = SafeArrayPutElement(value, &at, &data);
            if (hr < 0)
            {
                _ = SafeArrayDestroy(value);
                value = 0;
                return hr;
            }
        }
        return WindowsUiaProvider.Ok;
    }

    internal int EnclosingElement(out nint value) => _owner.TextEnclosingElement(out value);

    internal int Text(int maxLength, out nint value)
    {
        value = 0;
        if (!TrySnapshot(out var snapshot, out var start, out var end))
            return WindowsUiaProvider.NotAvailable;
        if (maxLength < -1)
            return WindowsUiaProvider.InvalidArgument;
        var requestedEnd =
            maxLength < 0 ? end : checked((int)Math.Min(end, (long)start + maxLength));
        var safeEnd = maxLength < 0 ? requestedEnd : Normalize(snapshot.Text, requestedEnd, false);
        var length = safeEnd - start;
        value = SysAllocString(snapshot.Text.Substring(start, length));
        return value == 0 ? WindowsUiaProvider.OutOfMemory : WindowsUiaProvider.Ok;
    }

    internal int Move(int unit, int count, out int moved)
    {
        moved = 0;
        if (!TrySnapshot(out var snapshot, out var start, out var end))
            return WindowsUiaProvider.NotAvailable;
        if (!ValidUnit(unit))
            return WindowsUiaProvider.InvalidArgument;
        if (count == 0)
            return WindowsUiaProvider.Ok;
        var degenerate = start == end;
        var at = degenerate ? start : UnitAt(snapshot, unit, start).Start;
        var target = degenerate
            ? MoveBoundary(snapshot, unit, at, count, out moved)
            : MoveRangeStart(snapshot, unit, at, count, out moved);
        if (degenerate)
            SetEndpoints(target, target);
        else
        {
            var movedRange = UnitAt(snapshot, unit, target);
            SetEndpoints(movedRange.Start, movedRange.End);
        }
        return WindowsUiaProvider.Ok;
    }

    internal int MoveEndpoint(int endpoint, int unit, int count, out int moved)
    {
        moved = 0;
        if (!ValidEndpoint(endpoint) || !ValidUnit(unit))
            return WindowsUiaProvider.InvalidArgument;
        if (!TrySnapshot(out var snapshot, out var start, out var end))
            return WindowsUiaProvider.NotAvailable;
        if (count == 0)
            return WindowsUiaProvider.Ok;
        var target = MoveBoundary(
            snapshot,
            unit,
            endpoint == StartEndpoint ? start : end,
            count,
            out moved
        );
        SetEndpoint(endpoint, target);
        return WindowsUiaProvider.Ok;
    }

    internal int MoveEndpointByRange(int endpoint, nint target, int targetEndpoint)
    {
        if (!ValidEndpoint(endpoint) || !ValidEndpoint(targetEndpoint))
            return WindowsUiaProvider.InvalidArgument;
        if (!TrySnapshot(out _))
            return WindowsUiaProvider.NotAvailable;
        var hr = Peer(target, out var epoch, out var element, out var start, out var end);
        if (hr < 0)
            return WindowsUiaProvider.InvalidArgument;
        _owner.TextOwnerIdentity(out var ownEpoch, out var ownElement);
        if (epoch != ownEpoch || element != ownElement)
            return WindowsUiaProvider.InvalidArgument;
        SetEndpoint(endpoint, targetEndpoint == StartEndpoint ? start : end);
        return WindowsUiaProvider.Ok;
    }

    internal int Select()
    {
        if (!TrySnapshot(out _, out var start, out var end))
            return WindowsUiaProvider.NotAvailable;
        return _owner.SelectText(start, end);
    }

    internal int UnsupportedSelection() =>
        TrySnapshot(out _) ? WindowsUiaProvider.InvalidOperation : WindowsUiaProvider.NotAvailable;

    internal int ScrollIntoView(int alignToTop)
    {
        if (!TrySnapshot(out _, out var start, out var end))
            return WindowsUiaProvider.NotAvailable;
        return _owner.ScrollTextIntoView(start, end, alignToTop != 0);
    }

    internal int Children(out nint value)
    {
        value = 0;
        if (!TrySnapshot(out _))
            return WindowsUiaProvider.NotAvailable;
        value = SafeArrayCreateVector(13, 0, 0);
        return value == 0 ? WindowsUiaProvider.OutOfMemory : WindowsUiaProvider.Ok;
    }

    private void SetEndpoint(int endpoint, int value)
    {
        while (true)
        {
            var state = Volatile.Read(ref _endpoints);
            var (start, end) = Unpack(state);
            if (endpoint == StartEndpoint)
            {
                start = value;
                if (start > end)
                    end = start;
            }
            else
            {
                end = value;
                if (end < start)
                    start = end;
            }
            if (Interlocked.CompareExchange(ref _endpoints, Pack(start, end), state) == state)
                return;
        }
    }

    private void SetEndpoints(int start, int end) =>
        Interlocked.Exchange(ref _endpoints, Pack(start, end));

    private static long Pack(int start, int end) => ((long)(uint)start << 32) | (uint)end;

    private static (int Start, int End) Unpack(long value) =>
        (unchecked((int)(uint)(value >> 32)), unchecked((int)(uint)value));

    private static (int Start, int End) UnitAt(
        WindowsUiaProvider.TextSnapshot snapshot,
        int unit,
        int offset
    )
    {
        var boundaries = Boundaries(snapshot, Promote(unit));
        var index = Array.BinarySearch(boundaries, offset);
        if (index < 0)
            index = Math.Max(0, ~index - 1);
        if (index == boundaries.Length - 1)
            index = Math.Max(0, index - 1);
        return (boundaries[index], boundaries[Math.Min(index + 1, boundaries.Length - 1)]);
    }

    private static int MoveBoundary(
        WindowsUiaProvider.TextSnapshot snapshot,
        int unit,
        int offset,
        int count,
        out int moved
    )
    {
        var boundaries = Boundaries(snapshot, Promote(unit));
        var index = Array.BinarySearch(boundaries, offset);
        if (index < 0)
            index = count < 0 ? Math.Max(0, ~index - 1) : Math.Min(boundaries.Length - 1, ~index);
        var target = Math.Clamp((long)index + count, 0, boundaries.Length - 1);
        moved = checked((int)target - index);
        return boundaries[target];
    }

    private static int MoveRangeStart(
        WindowsUiaProvider.TextSnapshot snapshot,
        int unit,
        int offset,
        int count,
        out int moved
    )
    {
        var boundaries = Boundaries(snapshot, Promote(unit));
        var lastSpan = Math.Max(0, boundaries.Length - 2);
        var index = Array.BinarySearch(boundaries, offset);
        if (index < 0)
            index = count < 0 ? Math.Max(0, ~index - 1) : Math.Min(lastSpan, ~index);
        index = Math.Min(index, lastSpan);
        var target = Math.Clamp((long)index + count, 0, lastSpan);
        moved = checked((int)target - index);
        return boundaries[target];
    }

    private static int[] Boundaries(WindowsUiaProvider.TextSnapshot snapshot, int unit)
    {
        var text = snapshot.Text;
        if (unit == 0)
            return StringInfo
                .ParseCombiningCharacters(text)
                .Append(text.Length)
                .Distinct()
                .Order()
                .ToArray();
        if (unit == 2)
        {
            var graphemes = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
            var result = new List<int> { 0 };
            bool? wasWord = null;
            for (var i = 0; i + 1 < graphemes.Length; i++)
            {
                var at = graphemes[i];
                var rune = Rune.GetRuneAt(text, at);
                var word = Rune.IsLetterOrDigit(rune) || rune.Value == '_';
                if (wasWord is not null && word != wasWord)
                    result.Add(at);
                wasWord = word;
            }
            result.Add(text.Length);
            return result.Distinct().Order().ToArray();
        }
        if (unit == 3)
            return snapshot
                .Lines.Select(line => line.Utf16Start)
                .Append(text.Length)
                .Distinct()
                .Order()
                .ToArray();
        return [0, text.Length];
    }

    private static int Promote(int unit) =>
        unit switch
        {
            0 => 0,
            1 or 2 => 2,
            3 => 3,
            _ => 6,
        };

    private static bool ValidUnit(int unit) => unit is >= 0 and <= 6;

    private static bool ValidEndpoint(int endpoint) => endpoint is StartEndpoint or EndEndpoint;

    private static int Normalize(string text, int offset, bool forward)
    {
        var boundaries = StringInfo
            .ParseCombiningCharacters(text)
            .Append(text.Length)
            .Distinct()
            .Order()
            .ToArray();
        var index = Array.BinarySearch(boundaries, offset);
        if (index >= 0)
            return boundaries[index];
        index = ~index;
        return forward
            ? boundaries[Math.Min(index, boundaries.Length - 1)]
            : boundaries[Math.Max(0, index - 1)];
    }

    private static int Peer(
        nint target,
        out long epoch,
        out long element,
        out int start,
        out int end
    )
    {
        epoch = element = 0;
        start = end = 0;
        if (target == 0)
            return WindowsUiaProvider.InvalidArgument;
        var peer = Query(target, PeerInterface);
        if (peer == 0)
            return WindowsUiaProvider.InvalidArgument;
        try
        {
            long peerEpoch = 0,
                peerElement = 0;
            int peerStart = 0,
                peerEnd = 0;
            var hr = (
                (delegate* unmanaged[Stdcall]<nint, long*, long*, int*, int*, int>)
                    (*(nint**)peer)[3]
            )(peer, &peerEpoch, &peerElement, &peerStart, &peerEnd);
            epoch = peerEpoch;
            element = peerElement;
            start = peerStart;
            end = peerEnd;
            return hr;
        }
        finally
        {
            Release(peer);
        }
    }

    private static nint Query(nint pointer, Guid iid)
    {
        nint result = 0;
        if (pointer != 0)
            _ = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)pointer)[0])(
                pointer,
                &iid,
                &result
            );
        return result;
    }

    private static void Release(nint pointer)
    {
        if (pointer != 0)
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)pointer)[2])(pointer);
    }

    private static void SetBool(WindowsUiaProvider.RawVariant* value, bool data)
    {
        value->Type = 11;
        value->Value = data ? -1 : 0;
    }

    private static int ReservedNotSupported(WindowsUiaProvider.RawVariant* value)
    {
        var hr = UiaGetReservedNotSupportedValue(out var unknown);
        if (hr >= 0)
        {
            value->Type = 13;
            value->Value = unknown;
        }
        return hr;
    }

    [LibraryImport("oleaut32.dll")]
    private static partial nint SysAllocString([MarshalAs(UnmanagedType.LPWStr)] string value);

    [LibraryImport("oleaut32.dll")]
    private static partial nint SafeArrayCreateVector(ushort type, int lowerBound, uint count);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayPutElement(nint array, int* index, void* value);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayDestroy(nint array);

    [LibraryImport("UIAutomationCore.dll")]
    private static partial int UiaGetReservedNotSupportedValue(out nint value);
}
