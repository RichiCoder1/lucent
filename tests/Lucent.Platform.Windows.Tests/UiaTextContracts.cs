using System.Runtime.InteropServices;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void MultilineTextPatternContract()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA text) failed.");
        var window = CreateWindow("Lucent UIA text");
        try
        {
            var hwnd = Hwnd(window);
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "uia-text");
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var element = composition.Child(composition.Root, "editor");
            var source =
                "A😀\nCafe\u0301\nlast\n"
                + string.Join('\n', Enumerable.Range(4, 20).Select(line => $"line {line}"));
            using var session = new EditorSession(
                composition.Root.Scope,
                "uia-document",
                source,
                "uia-editor",
                multiline: true
            );
            session.SetSelection(1, 3);
            var state = Controls.TextArea(
                element,
                theme,
                "Notes",
                session: session,
                style: Style.Empty.Set(LayoutProperties.Width, 120).Set(LayoutProperties.Height, 64)
            );
            composition.Flush();
            using var renderer = new SkiaSceneRenderer();
            var scene = SceneLayout.Project(composition, new(160, 80, 1), renderer);
            Assert(composition.Input.SetScene(scene), "UIA text scene was rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(hwnd, composition, dispatcher, "UIA text");
            provider.Refresh(scene);

            var rootFragment = Query(provider.InterfacePointer, UiaWrappers.Fragment);
            nint editorFragment = 0;
            Assert(
                Fragment(rootFragment, 3, 3, &editorFragment) == WindowsUiaProvider.Ok
                    && editorFragment != 0,
                "UIA editor fragment was unavailable."
            );
            nint nested = 0;
            if (Fragment(editorFragment, 3, 3, &nested) == WindowsUiaProvider.Ok && nested != 0)
            {
                Release(editorFragment);
                editorFragment = nested;
            }
            var simple = Query(editorFragment, UiaWrappers.Simple);
            WindowsUiaProvider.RawVariant controlType = default;
            Assert(
                Simple(simple, 5, 30003, &controlType) == WindowsUiaProvider.Ok
                    && controlType.Type == 3
                    && controlType.Value == 50004,
                "Scrollable TextArea did not retain the UIA Edit control type."
            );
            nint textProvider = 0;
            Assert(
                Simple(simple, 4, 10014, &textProvider) == WindowsUiaProvider.Ok
                    && textProvider != 0,
                "TextArea did not expose TextPattern."
            );
            nint scrollProvider = 0;
            Assert(
                Simple(simple, 4, 10004, &scrollProvider) == WindowsUiaProvider.Ok
                    && scrollProvider != 0,
                "TextArea did not expose ScrollPattern."
            );
            var textProvider2 = Query(simple, UiaWrappers.TextProvider2);
            nint document = 0,
                selectionArray = 0,
                selection = 0,
                caret = 0,
                pointRange = 0;
            try
            {
                double verticalPercent = -2;
                Assert(
                    ScrollMetric(scrollProvider, 6, &verticalPercent) == WindowsUiaProvider.Ok
                        && verticalPercent >= 0,
                    "Scrollable TextArea did not expose its vertical percent."
                );
                Assert(
                    SetScrollPercent(scrollProvider, 4, -1, verticalPercent)
                        == WindowsUiaProvider.Ok,
                    "Setting the already-current vertical percent was not a successful no-op."
                );
                Assert(
                    SetScrollPercent(scrollProvider, 4, -1, 100) == WindowsUiaProvider.Ok
                        && state.ScrollState!.Offset.Y > 0,
                    "ScrollPattern did not dispatch through the TextArea viewport."
                );
                var supportedSelection = -1;
                Assert(
                    TextProviderSelection(textProvider, 8, &supportedSelection)
                        == WindowsUiaProvider.Ok
                        && supportedSelection == 1,
                    "TextPattern did not report single-selection support."
                );
                Assert(
                    TextProviderPoint(
                        textProvider,
                        6,
                        new WindowsUiaProvider.UiaPoint { X = 0, Y = 0 },
                        &pointRange
                    ) == WindowsUiaProvider.Ok
                        && pointRange != 0,
                    "RangeFromPoint's native UiaPoint ABI did not return a range."
                );
                Assert(
                    TextProvider(textProvider, 7, &document) == WindowsUiaProvider.Ok
                        && document != 0,
                    "DocumentRange was unavailable."
                );
                Assert(
                    ReadRange(document, -1) == source,
                    "DocumentRange did not return displayed plain text."
                );
                Assert(
                    ReadRange(document, 2) == "A",
                    "GetText split a surrogate-pair grapheme at maxLength."
                );

                Assert(
                    TextProvider(textProvider, 3, &selectionArray) == WindowsUiaProvider.Ok,
                    "GetSelection failed."
                );
                selection = ArrayElement(selectionArray, 0);
                Assert(
                    ReadRange(selection, -1) == "😀",
                    "GetSelection did not preserve UTF-16 grapheme boundaries."
                );

                var active = 1;
                Assert(
                    TextProvider2(textProvider2, 10, &active, &caret) == WindowsUiaProvider.Ok
                        && caret != 0,
                    "GetCaretRange failed."
                );
                Assert(active == 0, "GetCaretRange returned a non-BOOL activity value.");
                var comparison = 1;
                Assert(
                    Range(caret, 5, 0, caret, 1, &comparison) == WindowsUiaProvider.Ok
                        && comparison == 0,
                    "Caret range was not degenerate."
                );

                nint rectangles = 0;
                Assert(
                    Range(selection, 10, &rectangles) == WindowsUiaProvider.Ok,
                    "Selection bounding rectangles failed."
                );
                try
                {
                    Assert(
                        SafeArrayLength(rectangles) >= 4,
                        "Visible selection had no clipped line rectangle."
                    );
                }
                finally
                {
                    _ = SafeArrayDestroy(rectangles);
                }

                var moved = 0;
                Assert(
                    Range(document, 13, 0, 1, &moved) == WindowsUiaProvider.Ok && moved == 1,
                    "Character Move did not advance one grapheme."
                );
                Assert(
                    Range(document, 16) == WindowsUiaProvider.Ok && session.SelectedText == "😀",
                    "TextRange.Select did not dispatch the bounded semantic selection."
                );
                Assert(
                    Range(document, 17) == WindowsUiaProvider.InvalidOperation,
                    "Single-selection editor accepted AddToSelection."
                );

                nint clone = 0;
                Assert(
                    Range(document, 3, &clone) == WindowsUiaProvider.Ok && clone != 0,
                    "Text range clone failed."
                );
                try
                {
                    var equal = 0;
                    Assert(
                        RangeCompare(document, 4, clone, &equal) == WindowsUiaProvider.Ok
                            && equal == 1,
                        "Compare did not return Win32 BOOL TRUE for equal ranges."
                    );
                }
                finally
                {
                    Release(clone);
                }

                nint wholeDocument = 0;
                Assert(
                    TextProvider(textProvider, 7, &wholeDocument) == WindowsUiaProvider.Ok,
                    "Second DocumentRange was unavailable."
                );
                try
                {
                    moved = -1;
                    Assert(
                        Range(wholeDocument, 13, 6, 1, &moved) == WindowsUiaProvider.Ok
                            && moved == 0,
                        "Document-unit Move reported motion past its only span."
                    );
                    Assert(
                        ReadRange(wholeDocument, -1) == source,
                        "Document-unit boundary Move changed the range."
                    );
                    moved = 0;
                    Assert(
                        Range(wholeDocument, 13, 0, int.MaxValue, &moved) == WindowsUiaProvider.Ok,
                        "Character Move to the last span failed."
                    );
                    var last = ReadRange(wholeDocument, -1);
                    moved = -1;
                    Assert(
                        Range(wholeDocument, 13, 0, 1, &moved) == WindowsUiaProvider.Ok
                            && moved == 0,
                        "Last-character Move reported motion without changing spans."
                    );
                    Assert(
                        ReadRange(wholeDocument, -1) == last,
                        "Last-character boundary Move changed the range."
                    );
                }
                finally
                {
                    Release(wholeDocument);
                }

                state.ScrollState!.Offset = default;
                session.Text = "e\u0301😀";
                composition.Flush();
                var replacementScene = SceneLayout.Project(composition, new(160, 80, 1), renderer);
                Assert(
                    composition.Input.SetScene(replacementScene),
                    "Replacement UIA text scene was rejected."
                );
                provider.Refresh(replacementScene);
                Assert(
                    ReadRange(document, -1) == "e\u0301😀",
                    "Retained range endpoints were not normalized to replacement grapheme boundaries."
                );

                ConcurrentRangeReadsRemainCoherent(document);
                provider.MarkUnavailable();
                nint staleText = 1;
                Assert(
                    Range(document, 12, -1, &staleText) == WindowsUiaProvider.NotAvailable
                        && staleText == 0,
                    "Text range did not fail closed after its owner became unavailable."
                );
            }
            finally
            {
                Release(pointRange);
                Release(caret);
                Release(selection);
                if (selectionArray != 0)
                    _ = SafeArrayDestroy(selectionArray);
                Release(document);
                Release(scrollProvider);
                Release(textProvider2);
                Release(textProvider);
                Release(simple);
                Release(editorFragment);
                Release(rootFragment);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static void ConcurrentRangeReadsRemainCoherent(nint range)
    {
        Parallel.For(
            0,
            64,
            index =>
            {
                if ((index & 1) == 0)
                {
                    var moved = 0;
                    var hr = Range(range, 14, index % 4 < 2 ? 0 : 1, 0, index % 3 - 1, &moved);
                    Assert(hr == WindowsUiaProvider.Ok, "Concurrent endpoint move failed.");
                }
                else
                {
                    var text = ReadRange(range, -1);
                    Assert(
                        text.Length == 0
                            || !char.IsSurrogate(text[0])
                            || char.IsSurrogatePair(text, 0),
                        "Concurrent range read began inside a surrogate pair."
                    );
                    Assert(
                        text.Length == 0 || !char.IsHighSurrogate(text[^1]),
                        "Concurrent range read ended inside a surrogate pair."
                    );
                }
            }
        );
    }

    private static string ReadRange(nint range, int maxLength)
    {
        nint value = 0;
        var hr = Range(range, 12, maxLength, &value);
        Assert(hr == WindowsUiaProvider.Ok, $"ITextRangeProvider.GetText failed: 0x{hr:X8}.");
        try
        {
            return Marshal.PtrToStringBSTR(value);
        }
        finally
        {
            Marshal.FreeBSTR(value);
        }
    }

    private static nint ArrayElement(nint array, int index)
    {
        nint value = 0;
        Assert(
            SafeArrayGetElement(array, &index, &value) >= 0 && value != 0,
            "SAFEARRAY did not contain a text range."
        );
        return value;
    }

    private static int SafeArrayLength(nint array)
    {
        Assert(array != 0, "SAFEARRAY was null.");
        Assert(SafeArrayGetLBound(array, 1, out var lower) >= 0, "SAFEARRAY lower bound failed.");
        Assert(SafeArrayGetUBound(array, 1, out var upper) >= 0, "SAFEARRAY upper bound failed.");
        return upper < lower ? 0 : upper - lower + 1;
    }

    private static int SetScrollPercent(
        nint pointer,
        int slot,
        double horizontal,
        double vertical
    ) =>
        ((delegate* unmanaged[Stdcall]<nint, double, double, int>)(*(nint**)pointer)[slot])(
            pointer,
            horizontal,
            vertical
        );

    private static int ScrollMetric(nint pointer, int slot, double* value) =>
        ((delegate* unmanaged[Stdcall]<nint, double*, int>)(*(nint**)pointer)[slot])(
            pointer,
            value
        );

    private static int TextProvider(nint pointer, int slot, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int TextProviderSelection(nint pointer, int slot, int* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int TextProviderPoint(
        nint pointer,
        int slot,
        WindowsUiaProvider.UiaPoint point,
        nint* value
    ) =>
        (
            (delegate* unmanaged[Stdcall]<nint, WindowsUiaProvider.UiaPoint, nint*, int>)
                (*(nint**)pointer)[slot]
        )(pointer, point, value);

    private static int TextProvider2(nint pointer, int slot, int* active, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int*, nint*, int>)(*(nint**)pointer)[slot])(
            pointer,
            active,
            value
        );

    private static int Range(nint pointer, int slot) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)pointer)[slot])(pointer);

    private static int RangeCompare(nint pointer, int slot, nint target, int* value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int*, int>)(*(nint**)pointer)[slot])(
            pointer,
            target,
            value
        );

    private static int Range(nint pointer, int slot, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int Range(nint pointer, int slot, int argument, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)(*(nint**)pointer)[slot])(
            pointer,
            argument,
            value
        );

    private static int Range(nint pointer, int slot, int unit, int count, int* moved) =>
        ((delegate* unmanaged[Stdcall]<nint, int, int, int*, int>)(*(nint**)pointer)[slot])(
            pointer,
            unit,
            count,
            moved
        );

    private static int Range(
        nint pointer,
        int slot,
        int endpoint,
        int unit,
        int count,
        int* moved
    ) =>
        ((delegate* unmanaged[Stdcall]<nint, int, int, int, int*, int>)(*(nint**)pointer)[slot])(
            pointer,
            endpoint,
            unit,
            count,
            moved
        );

    private static int Range(
        nint pointer,
        int slot,
        int endpoint,
        nint target,
        int targetEndpoint,
        int* value
    ) =>
        ((delegate* unmanaged[Stdcall]<nint, int, nint, int, int*, int>)(*(nint**)pointer)[slot])(
            pointer,
            endpoint,
            target,
            targetEndpoint,
            value
        );

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetElement(nint array, int* index, void* value);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetLBound(nint array, uint dimension, out int lower);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetUBound(nint array, uint dimension, out int upper);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayDestroy(nint array);
}
