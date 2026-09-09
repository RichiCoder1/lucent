using System.Runtime.InteropServices;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed partial class WindowsWindowIconContracts
{
    [TestMethod]
    public void SdlArgbPixelsUnpremultiplyAndReorderPartialAlpha()
    {
        // Skia's source is premultiplied RGBA. SDL's Windows path asserts ARGB8888,
        // whose little-endian bytes are straight BGRA before CreateIconFromResource.
        var converted = WindowsWindowIcon.ToSdlArgb8888([64, 32, 16, 128]);

        CollectionAssert.AreEqual(new byte[] { 32, 64, 128, 128 }, converted);
    }

    [TestMethod]
    public void SdlArgbPixelsClearTransparentRgbAndRejectIncompletePixels()
    {
        CollectionAssert.AreEqual(
            new byte[] { 0, 0, 0, 0 },
            WindowsWindowIcon.ToSdlArgb8888([255, 128, 1, 0])
        );
        Assert.Throws<ArgumentException>(() => WindowsWindowIcon.ToSdlArgb8888([1, 2, 3]));
    }

    [TestMethod]
    public void InstalledHwndIconRetainsPartialAlphaThroughNativeDraw()
    {
        Assert.IsTrue(SDL.Init(SDL.InitFlags.Video), SDL.GetError());
        nint window = 0;
        WindowsWindowIcon? icon = null;
        try
        {
            window = SDL.CreateWindow("Lucent icon alpha", 32, 32, SDL.WindowFlags.Hidden);
            Assert.AreNotEqual(0, window, SDL.GetError());
            var pixels = new byte[32 * 32 * 4];
            var pixel = (16 * 32 + 16) * 4;
            pixels[pixel] = 64;
            pixels[pixel + 1] = 32;
            pixels[pixel + 2] = 16;
            pixels[pixel + 3] = 128;
            icon = WindowsWindowIcon.Install(window, [new ArtworkRendition(32, 32, pixels)]);

            var hwnd = SDL.GetPointerProperty(
                SDL.GetWindowProperties(window),
                SDL.Props.WindowWin32HWNDPointer,
                0
            );
            Assert.AreNotEqual(0, hwnd);
            var hIcon = SendMessage(hwnd, WmGetIcon, IconBig, 0);
            Assert.AreNotEqual(0, hIcon, "SDL did not install an HWND icon.");

            var dc = CreateCompatibleDC(0);
            Assert.AreNotEqual(0, dc);
            nint bitmap = 0;
            nint bits = 0;
            nint previous = 0;
            try
            {
                var info = new BitmapInfo
                {
                    Header = new BitmapInfoHeader
                    {
                        Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                        Width = 32,
                        Height = -32,
                        Planes = 1,
                        BitCount = 32,
                        Compression = 0,
                    },
                };
                bitmap = CreateDibSection(dc, ref info, 0, out bits, 0, 0);
                Assert.AreNotEqual(0, bitmap);
                Assert.AreNotEqual(0, bits);
                previous = SelectObject(dc, bitmap);
                Assert.AreNotEqual(0, previous);
                Assert.IsTrue(DrawIconEx(dc, 0, 0, hIcon, 32, 32, 0, 0, DrawIconNormal));

                var rendered = new byte[32 * 32 * 4];
                Marshal.Copy(bits, rendered, 0, rendered.Length);
                var renderedPixel = (16 * 32 + 16) * 4;
                // DrawIconEx writes the premultiplied BGRA result into the top-down DIB.
                Assert.IsTrue(rendered[renderedPixel + 3] > 0);
                Assert.IsTrue(rendered[renderedPixel + 3] < 255);
                Assert.AreEqual(16, rendered[renderedPixel]);
                Assert.AreEqual(32, rendered[renderedPixel + 1]);
                Assert.AreEqual(64, rendered[renderedPixel + 2]);
            }
            finally
            {
                if (previous != 0)
                    SelectObject(dc, previous);
                if (bitmap != 0)
                    DeleteObject(bitmap);
                DeleteDC(dc);
            }
        }
        finally
        {
            icon?.Dispose();
            if (window != 0)
                SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    [TestMethod]
    public void InstalledIconWithoutThirtyTwoPixelRenditionSupportsReplacementAndDisposal()
    {
        Assert.IsTrue(SDL.Init(SDL.InitFlags.Video), SDL.GetError());
        nint window = 0;
        WindowsWindowIcon? icon = null;
        try
        {
            window = SDL.CreateWindow("Lucent icon renditions", 32, 32, SDL.WindowFlags.Hidden);
            Assert.AreNotEqual(0, window, SDL.GetError());

            icon = WindowsWindowIcon.Install(
                window,
                [CreateOpaqueRendition(16), CreateOpaqueRendition(48)]
            );
            AssertInstalledHwndIcon(window);

            icon.Dispose();
            icon = WindowsWindowIcon.Install(
                window,
                [CreateOpaqueRendition(16), CreateOpaqueRendition(48)]
            );
            AssertInstalledHwndIcon(window);
        }
        finally
        {
            icon?.Dispose();
            if (window != 0)
                SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    [TestMethod]
    public void IconPreparationCompletionDefersCancellationSourceDisposalUntilWorkerEnds()
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var cancellation = new CancellationTokenSource();

        WindowsBootstrap.ObserveIconPreparationCompletion(completion.Task, cancellation);
        cancellation.Cancel();
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsFalse(IsDisposed(cancellation));

        completion.SetResult(null);
        SpinWait.SpinUntil(() => IsDisposed(cancellation), TimeSpan.FromSeconds(1));
        Assert.IsTrue(IsDisposed(cancellation));
    }

    private static void AssertInstalledHwndIcon(nint window)
    {
        var hwnd = SDL.GetPointerProperty(
            SDL.GetWindowProperties(window),
            SDL.Props.WindowWin32HWNDPointer,
            0
        );
        Assert.AreNotEqual(0, hwnd);
        Assert.AreNotEqual(
            0,
            SendMessage(hwnd, WmGetIcon, IconBig, 0),
            "SDL did not install an HWND icon."
        );
    }

    private static ArtworkRendition CreateOpaqueRendition(int size)
    {
        var pixels = new byte[checked(size * size * 4)];
        for (var index = 3; index < pixels.Length; index += 4)
            pixels[index] = byte.MaxValue;
        return new ArtworkRendition(size, size, pixels);
    }

    private static bool IsDisposed(CancellationTokenSource cancellation)
    {
        try
        {
            _ = cancellation.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private const uint WmGetIcon = 0x007F;
    private const nint IconBig = 1;
    private const uint DrawIconNormal = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color;
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawIconEx(
        nint dc,
        int x,
        int y,
        nint icon,
        int width,
        int height,
        uint step,
        nint brush,
        uint flags
    );

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
    private static partial nint CreateDibSection(
        nint dc,
        ref BitmapInfo info,
        uint usage,
        out nint bits,
        nint section,
        uint offset
    );

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint objectHandle);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint objectHandle);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}
