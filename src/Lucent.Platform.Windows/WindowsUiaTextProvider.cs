using Lucent.Core;

namespace Lucent.Platform.Windows;

internal sealed unsafe partial class WindowsUiaProvider
{
    internal readonly record struct ScreenRectangle(
        double X,
        double Y,
        double Width,
        double Height
    );

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential
    )]
    internal struct UiaPoint
    {
        internal double X;
        internal double Y;
    }

    internal sealed record TextSnapshot(
        string Text,
        int Anchor,
        int Caret,
        TextAffinity AnchorAffinity,
        TextAffinity CaretAffinity,
        bool IsReadOnly,
        ShapedText? Shape,
        LayoutRect TextBounds,
        LayoutRect Clip
    )
    {
        internal IReadOnlyList<ParagraphLine> Lines => Shape?.Lines ?? Array.Empty<ParagraphLine>();
    }

    internal bool ProviderHasText =>
        CurrentNode() is { Text: not null } node && node.Actions.HasFlag(SemanticAction.SelectText);

    internal bool TryGetTextSnapshot(out TextSnapshot snapshot)
    {
        _dispatcher.RecordRead("TextSnapshot");
        var text = CurrentNode()?.Text;
        snapshot = text!;
        return text is not null;
    }

    internal void TextOwnerIdentity(out long epoch, out long element)
    {
        epoch = _key?.Epoch ?? 0;
        element = _key?.Element ?? 0;
    }

    internal int TextSelection(out nint value)
    {
        value = 0;
        if (!TryGetTextSnapshot(out var text))
            return NotAvailable;
        return TextRangeArray([new(this, text.Anchor, text.Caret)], out value);
    }

    internal int TextVisibleRanges(out nint value)
    {
        value = 0;
        if (!TryGetTextSnapshot(out var text))
            return NotAvailable;
        var (start, end) = VisibleRange(text);
        return TextRangeArray([new(this, start, end)], out value);
    }

    internal int TextRangeFromChild(nint child, out nint value)
    {
        value = 0;
        return TryGetTextSnapshot(out _) ? InvalidArgument : NotAvailable;
    }

    internal int TextRangeFromAnnotation(nint annotation, out nint value)
    {
        value = 0;
        return TryGetTextSnapshot(out _) ? InvalidArgument : NotAvailable;
    }

    internal int TextRangeFromPoint(double screenX, double screenY, out nint value)
    {
        value = 0;
        if (!double.IsFinite(screenX) || !double.IsFinite(screenY))
            return InvalidArgument;
        if (!TryGetTextSnapshot(out var text))
            return NotAvailable;
        var origin = new ScreenPoint();
        if (!ClientToScreen(_hwnd, ref origin))
            return Fail;
        var scale = GetDpiForWindow(_hwnd) / 96d;
        if (!(scale > 0))
            return Fail;
        var x = (float)((screenX - origin.X) / scale);
        var y = (float)((screenY - origin.Y) / scale);
        x = Math.Clamp(x, text.Clip.X, text.Clip.X + text.Clip.Width);
        y = Math.Clamp(y, text.Clip.Y, text.Clip.Y + text.Clip.Height);
        var offset = 0;
        if (text.Shape is not null)
            try
            {
                offset = text
                    .Shape.HitTest(text.Text, x - text.TextBounds.X, y - text.TextBounds.Y)
                    .Utf16Offset;
            }
            catch (ArgumentException)
            {
                offset = Math.Clamp(text.Caret, 0, text.Text.Length);
            }
        value = new WindowsUiaTextRange(this, offset, offset).CreateInterfacePointer();
        return value == 0 ? OutOfMemory : Ok;
    }

    internal int TextDocumentRange(out nint value)
    {
        value = 0;
        if (!TryGetTextSnapshot(out var text))
            return NotAvailable;
        value = new WindowsUiaTextRange(this, 0, text.Text.Length).CreateInterfacePointer();
        return value == 0 ? OutOfMemory : Ok;
    }

    internal int TextSupportedSelection(out int value)
    {
        value = 0;
        if (!TryGetTextSnapshot(out _))
            return NotAvailable;
        value = 1;
        return Ok;
    }

    internal int TextCaretRange(out int active, out nint value)
    {
        active = 0;
        value = 0;
        var node = CurrentNode();
        if (node?.Text is not { } text)
            return NotAvailable;
        active = node.Focused ? 1 : 0;
        value = new WindowsUiaTextRange(this, text.Caret, text.Caret).CreateInterfacePointer();
        return value == 0 ? OutOfMemory : Ok;
    }

    internal int TextEnclosingElement(out nint value)
    {
        value = 0;
        if (!TryGetTextSnapshot(out _))
            return NotAvailable;
        value = Query(_unknown, UiaWrappers.Simple);
        return value == 0 ? Fail : Ok;
    }

    internal IReadOnlyList<ScreenRectangle> TextBoundingRectangles(
        TextSnapshot text,
        int start,
        int end
    )
    {
        if (!IsAvailable || text.Shape is null)
            return Array.Empty<ScreenRectangle>();
        if (start == end)
            return Array.Empty<ScreenRectangle>();
        IReadOnlyList<LayoutRect> local;
        try
        {
            local = text.Shape.SelectionBounds(text.Text, start, end);
        }
        catch (ArgumentException)
        {
            return Array.Empty<ScreenRectangle>();
        }
        var origin = new ScreenPoint();
        if (!ClientToScreen(_hwnd, ref origin))
            return Array.Empty<ScreenRectangle>();
        var scale = GetDpiForWindow(_hwnd) / 96d;
        if (!(scale > 0))
            return Array.Empty<ScreenRectangle>();
        return local
            .Select(rectangle => new LayoutRect(
                text.TextBounds.X + rectangle.X,
                text.TextBounds.Y + rectangle.Y,
                rectangle.Width,
                rectangle.Height
            ))
            .Select(rectangle => Intersect(rectangle, text.Clip))
            .Where(rectangle => rectangle.Width > 0 && rectangle.Height > 0)
            .Select(rectangle => new ScreenRectangle(
                origin.X + rectangle.X * scale,
                origin.Y + rectangle.Y * scale,
                rectangle.Width * scale,
                rectangle.Height * scale
            ))
            .ToArray();
    }

    internal int SelectText(int start, int end) =>
        Run(node =>
            _composition.ExecuteSemanticCommand(
                node.Identity,
                new(SemanticCommandKind.SelectText, Anchor: start, Caret: end)
            )
        );

    internal int ScrollTextIntoView(int start, int end, bool alignToTop) =>
        Run(node =>
            _composition.ExecuteSemanticCommand(
                node.Identity,
                new(
                    SemanticCommandKind.ScrollTextIntoView,
                    Anchor: start,
                    Caret: end,
                    AlignToTop: alignToTop
                )
            )
        );

    private static int TextRangeArray(IReadOnlyList<WindowsUiaTextRange> ranges, out nint value)
    {
        value = SafeArrayCreateVector(VtUnknown, 0, checked((uint)ranges.Count));
        if (value == 0)
            return OutOfMemory;
        for (var i = 0; i < ranges.Count; i++)
        {
            var pointer = ranges[i].CreateInterfacePointer();
            if (pointer == 0)
            {
                _ = SafeArrayDestroy(value);
                value = 0;
                return OutOfMemory;
            }
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

    private static (int Start, int End) VisibleRange(TextSnapshot text)
    {
        if (text.Shape is null || text.Lines.Count == 0)
            return (0, text.Text.Length == 0 ? 0 : text.Text.Length);
        var visible = text
            .Lines.Where(line =>
                Intersect(
                    new(
                        text.TextBounds.X,
                        text.TextBounds.Y + line.Top,
                        Math.Max(line.Advance, 1),
                        Math.Max(0, line.Descent - line.Ascent + line.Leading)
                    ),
                    text.Clip
                )
                    is { Width: > 0, Height: > 0 }
            )
            .ToArray();
        if (visible.Length == 0)
            return (0, 0);
        var start = visible[0].Utf16Start;
        var last = visible[^1];
        var end = checked(last.Utf16Start + last.Utf16Length);
        var next = text.Lines.FirstOrDefault(line => line.Utf16Start > last.Utf16Start);
        if (last.HardBreak && next.Utf16Start > end)
            end = next.Utf16Start;
        return (start, Math.Min(end, text.Text.Length));
    }

    private static LayoutRect Intersect(LayoutRect left, LayoutRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        return new(
            x,
            y,
            Math.Max(0, Math.Min(left.X + left.Width, right.X + right.Width) - x),
            Math.Max(0, Math.Min(left.Y + left.Height, right.Y + right.Height) - y)
        );
    }

    private static Dictionary<ElementIdentity, TextSceneSnapshot> TextScenes(RetainedScene scene)
    {
        var result = new Dictionary<ElementIdentity, TextSceneSnapshot>();
        var viewport = new LayoutRect(0, 0, scene.Viewport.Width, scene.Viewport.Height);
        Add(scene.Nodes, viewport);
        return result;

        void Add(IEnumerable<SceneNode> nodes, LayoutRect clip)
        {
            foreach (var sceneNode in nodes)
            {
                if (sceneNode is TextSceneNode text)
                    result[text.Identity.Element] = new(text.Text, text.Bounds, clip);
                if (sceneNode is ClipSceneNode clipped)
                    Add(clipped.Children, Intersect(clip, clipped.Bounds));
                else if (sceneNode is OpacitySceneNode opacity)
                    Add(opacity.Children, clip);
            }
        }
    }

    private sealed record TextSceneSnapshot(ShapedText Shape, LayoutRect Bounds, LayoutRect Clip);
}
