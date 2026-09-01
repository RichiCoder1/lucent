using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lucent.Core;
using Lucent.Platform.Windows;
using SDL3;

internal static unsafe partial class UiaLifecycleContracts
{
    private const uint WmGetObject = 0x003d,
        WmNcDestroy = 0x0082;
    private const nint UiaRootObjectId = -25;
    private static long _nextId;

    internal static void Run()
    {
        if (!SDL.Init(SDL.InitFlags.Video))
            throw new InvalidOperationException("SDL_Init(UIA lifecycle): " + SDL.GetError());
        try
        {
            DestroyContract();
            for (var pass = 0; pass < 3; pass++)
                RemoveContract(pass);
        }
        finally
        {
            SDL.Quit();
        }
    }

    private static void DestroyContract()
    {
        nint window = CreateWindow("Lucent UIA destroy");
        var hwnd = Hwnd(window);
        using var probe = new SubclassProbe(hwnd);
        using var composition = new Composition(new ReactiveGraph(), "uia-lifecycle");
        using var dispatcher = new WindowsUiaDispatcher();
        using var provider = new WindowsUiaProvider(hwnd, composition, dispatcher);
        using var listener = new WindowsUiaListener(hwnd, provider);
        var simple = AddRef(provider.InterfacePointer);
        var fragment = Query(simple, UiaWrappers.Fragment);
        var root = Query(simple, UiaWrappers.Root);
        try
        {
            _ = SendMessage(hwnd, WmGetObject, 0, UiaRootObjectId);
            Assert(
                listener.RootDeliveryCount == 1,
                "WM_GETOBJECT did not deliver the retained UIA root."
            );
            Assert(DestroyWindow(hwnd), "DestroyWindow(UIA lifecycle) failed.");
            window = 0;
            Assert(
                probe.DestroyCount == 1,
                "WM_NCDESTROY did not chain through the existing subclass."
            );
            listener.Dispose();
            Assert(!provider.IsAvailable, "UIA provider remained available after WM_NCDESTROY.");
            AssertUnavailable(simple, fragment, root);
            provider.Dispose();
            AssertUnavailable(simple, fragment, root);
        }
        finally
        {
            Release(fragment);
            Release(root);
            Release(simple);
            if (window != 0)
                SDL.DestroyWindow(window);
        }
    }

    private static void RemoveContract(int pass)
    {
        var window = CreateWindow("Lucent UIA remove " + pass);
        var hwnd = Hwnd(window);
        try
        {
            using var probe = new SubclassProbe(hwnd);
            using var composition = new Composition(new ReactiveGraph(), "uia-remove");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(hwnd, composition, dispatcher);
            using var listener = new WindowsUiaListener(hwnd, provider);
            listener.Dispose();
            _ = SendMessage(hwnd, WmGetObject, 0, UiaRootObjectId);
            Assert(
                listener.RootDeliveryCount == 0,
                "Removed UIA listener still handled WM_GETOBJECT."
            );
            Assert(DestroyWindow(hwnd), "DestroyWindow(UIA remove) failed.");
            window = 0;
            Assert(
                probe.DestroyCount == 1,
                "Removing UIA listener did not restore the subclass chain."
            );
        }
        finally
        {
            if (window != 0)
                SDL.DestroyWindow(window);
        }
    }

    private static nint CreateWindow(string title)
    {
        var window = SDL.CreateWindow(title, 1, 1, SDL.WindowFlags.Hidden);
        if (window == 0)
            throw new InvalidOperationException(
                "SDL_CreateWindow(UIA lifecycle): " + SDL.GetError()
            );
        return window;
    }

    private static nint Hwnd(nint window)
    {
        var hwnd = SDL.GetPointerProperty(
            SDL.GetWindowProperties(window),
            SDL.Props.WindowWin32HWNDPointer,
            0
        );
        if (hwnd == 0)
            throw new InvalidOperationException("SDL UIA lifecycle window did not expose an HWND.");
        return hwnd;
    }

    private static nint AddRef(nint pointer)
    {
        if (pointer == 0)
            throw new InvalidOperationException("UIA root COM pointer was null.");
        ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)pointer)[1])(pointer);
        return pointer;
    }

    private static nint Query(nint pointer, Guid iid)
    {
        nint result = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)pointer)[0])(
            pointer,
            &iid,
            &result
        );
        if (hr < 0 || result == 0)
            throw new InvalidOperationException($"UIA QueryInterface({iid}) failed: 0x{hr:X8}.");
        return result;
    }

    private static void AssertUnavailable(nint simple, nint fragment, nint root)
    {
        var options = -1;
        Assert(
            Simple(simple, 3, &options) == WindowsUiaProvider.NotAvailable && options == 0,
            "Disposed UIA options did not initialize its output."
        );
        nint pattern = 1;
        Assert(
            Simple(simple, 4, 10000, &pattern) == WindowsUiaProvider.NotAvailable && pattern == 0,
            "Disposed UIA pattern did not initialize its output."
        );
        WindowsUiaProvider.RawVariant variant = new() { Type = 0xffff, Value = 1 };
        Assert(
            Simple(simple, 5, 30005, &variant) == WindowsUiaProvider.NotAvailable
                && variant.Type == 0
                && variant.Value == 0,
            "Disposed UIA property did not initialize its VARIANT output."
        );
        nint host = 1;
        Assert(
            Simple(simple, 6, &host) == WindowsUiaProvider.NotAvailable && host == 0,
            "Disposed UIA host provider touched a dead HWND or left its output set."
        );

        nint value = 1;
        Assert(
            Fragment(fragment, 3, 3, &value) == WindowsUiaProvider.NotAvailable && value == 0,
            "Disposed UIA Navigate did not initialize its output."
        );
        Assert(
            Fragment(fragment, 4, &value) == WindowsUiaProvider.NotAvailable && value == 0,
            "Disposed UIA RuntimeId did not initialize its output."
        );
        var rectangle = stackalloc double[] { 1, 1, 1, 1 };
        Assert(
            Fragment(fragment, 5, rectangle) == WindowsUiaProvider.NotAvailable
                && rectangle[0] == 0
                && rectangle[1] == 0
                && rectangle[2] == 0
                && rectangle[3] == 0,
            "Disposed UIA bounding rectangle did not initialize its output."
        );
        Assert(
            Fragment(fragment, 6, &value) == WindowsUiaProvider.NotAvailable && value == 0,
            "Disposed UIA embedded roots did not initialize its output."
        );
        Assert(
            Fragment(fragment, 7) == WindowsUiaProvider.NotAvailable,
            "Disposed UIA SetFocus did not fail closed."
        );
        Assert(
            Fragment(fragment, 8, &value) == WindowsUiaProvider.NotAvailable && value == 0,
            "Disposed UIA fragment root did not initialize its output."
        );

        value = 1;
        Assert(
            Root(root, 3, 0, 0, &value) == WindowsUiaProvider.NotAvailable && value == 0,
            "Disposed UIA point lookup did not initialize its output."
        );
        Assert(
            Root(root, 4, &value) == WindowsUiaProvider.NotAvailable && value == 0,
            "Disposed UIA focus lookup did not initialize its output."
        );
    }

    private static int Simple(nint pointer, int slot, int* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int Simple(nint pointer, int slot, int id, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)(*(nint**)pointer)[slot])(
            pointer,
            id,
            value
        );

    private static int Simple(
        nint pointer,
        int slot,
        int id,
        WindowsUiaProvider.RawVariant* value
    ) =>
        (
            (delegate* unmanaged[Stdcall]<nint, int, WindowsUiaProvider.RawVariant*, int>)
                (*(nint**)pointer)[slot]
        )(pointer, id, value);

    private static int Simple(nint pointer, int slot, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int Fragment(nint pointer, int slot, int direction, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)(*(nint**)pointer)[slot])(
            pointer,
            direction,
            value
        );

    private static int Fragment(nint pointer, int slot, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int Fragment(nint pointer, int slot, double* value) =>
        ((delegate* unmanaged[Stdcall]<nint, double*, int>)(*(nint**)pointer)[slot])(
            pointer,
            value
        );

    private static int Fragment(nint pointer, int slot) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)pointer)[slot])(pointer);

    private static int Root(nint pointer, int slot, double x, double y, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, double, double, nint*, int>)(*(nint**)pointer)[slot])(
            pointer,
            x,
            y,
            value
        );

    private static int Root(nint pointer, int slot, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static void Release(nint pointer)
    {
        if (pointer != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)pointer)[2])(pointer);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed unsafe partial class SubclassProbe : IDisposable
    {
        private static readonly nint Procedure = (nint)
            (delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nint, nint>)&Callback;
        private readonly nint _window;
        private readonly nuint _id = checked((nuint)Interlocked.Increment(ref _nextId));
        private GCHandle _handle;
        private bool _removed,
            _destroyed;
        internal int DestroyCount { get; private set; }

        internal SubclassProbe(nint window)
        {
            _window = window;
            _handle = GCHandle.Alloc(this);
            if (!SetWindowSubclass(window, Procedure, _id, GCHandle.ToIntPtr(_handle)))
            {
                _handle.Free();
                throw new InvalidOperationException("SetWindowSubclass(UIA probe) failed.");
            }
        }

        public void Dispose()
        {
            if (!_removed && !_destroyed)
                _removed = RemoveWindowSubclass(_window, Procedure, _id);
            if (_handle.IsAllocated)
                _handle.Free();
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static nint Callback(
            nint window,
            uint message,
            nint wParam,
            nint lParam,
            nuint id,
            nint data
        )
        {
            var probe = GCHandle.FromIntPtr(data).Target as SubclassProbe;
            if (probe is not null && message == WmNcDestroy)
            {
                probe.DestroyCount++;
                probe._destroyed = true;
            }
            return DefSubclassProc(window, message, wParam, lParam);
        }

        [LibraryImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowSubclass(
            nint window,
            nint procedure,
            nuint id,
            nint data
        );

        [LibraryImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool RemoveWindowSubclass(nint window, nint procedure, nuint id);

        [LibraryImport("comctl32.dll")]
        private static partial nint DefSubclassProc(
            nint window,
            uint message,
            nint wParam,
            nint lParam
        );
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
}
