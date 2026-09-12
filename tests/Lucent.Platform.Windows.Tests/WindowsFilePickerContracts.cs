using Lucent.Core;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsFilePickerContracts
{
    [TestMethod]
    public void FilterAndOptionsSnapshotBeforeTheNativeDialogStarts()
    {
        var extensions = new[] { ".md", "txt", "MD", "tar.gz" };
        var filter = new FilePickerFilter("Documents", extensions);
        extensions[0] = "exe";
        Assert.AreEqual("*.md;*.txt;*.tar.gz", WindowsFilePickerNative.FilterPattern(filter));
        var filters = new[] { filter };
        var request = new WindowsFilePickerRequest(
            WindowsFilePickerKind.Open,
            null,
            null,
            filters,
            null,
            true
        );
        filters[0] = new("All", ["*"]);
        Assert.AreSame(filter, request.Filters[0]);
        Assert.IsTrue(request.Multiple);
        Assert.AreEqual("*.*", WindowsFilePickerNative.FilterPattern(filters[0]));
        Assert.Throws<ArgumentException>(() => new FilePickerFilter("Bad", ["txt;*.exe"]));
        Assert.Throws<ArgumentException>(() =>
            new WindowsFilePickerRequest(
                WindowsFilePickerKind.Save,
                null,
                "../name.txt",
                null,
                null,
                false
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new WindowsFilePickerRequest(
                WindowsFilePickerKind.Open,
                null,
                null,
                null,
                new("https://example.com"),
                false
            )
        );
    }

    [TestMethod]
    public void RequestsAreQueuedOutsideDispatchAndRejectReentrantProcessing()
    {
        using var owner = new Composition(new ReactiveGraph(), "picker");
        var calls = 0;
        var wakes = 0;
        WindowsFilePickerHost? host = null;
        ValueTask<FilePickerResult> second = default;
        host = new(
            owner,
            42,
            () => wakes++,
            (hwnd, request, canceled) =>
            {
                Assert.AreEqual((nint)42, hwnd);
                Assert.IsTrue(owner.IsInteractionSuspended);
                Assert.IsFalse(canceled());
                Assert.IsFalse(host!.ProcessOne());
                if (++calls == 1)
                    second = host.Enqueue(OpenRequest(), default);
                return new(FilePickerStatus.Canceled);
            }
        );
        using (host)
        {
            var first = host.Enqueue(OpenRequest(), default);
            Assert.IsFalse(first.IsCompleted);
            Assert.AreEqual(0, calls);
            Assert.IsTrue(host.ProcessOne());
            Assert.AreEqual(FilePickerStatus.Canceled, Completed(first).Status);
            Assert.IsFalse(second.IsCompleted);
            Assert.IsFalse(owner.IsInteractionSuspended);
            Assert.IsTrue(host.ProcessOne());
            Assert.AreEqual(FilePickerStatus.Canceled, Completed(second).Status);
            Assert.AreEqual(2, calls);
            Assert.IsTrue(wakes >= 2);
        }
    }

    [TestMethod]
    public void CancellationAndOwnerCloseWinWithoutAccessingFiles()
    {
        using var owner = new Composition(new ReactiveGraph(), "picker-close");
        using var cancel = new CancellationTokenSource();
        var calls = 0;
        WindowsFilePickerHost? host = null;
        host = new(
            owner,
            42,
            () => { },
            (_, _, canceled) =>
            {
                calls++;
                host!.DismissForCloseRequest();
                Assert.IsTrue(canceled());
                return new(
                    FilePickerStatus.Selected,
                    [new(new Uri("file:///C:/uncreated.txt"), "uncreated.txt")]
                );
            }
        );
        using (host)
        {
            var canceledBeforeShow = host.Enqueue(OpenRequest(), cancel.Token);
            cancel.Cancel();
            host.ProcessOne();
            Assert.AreEqual(FilePickerStatus.Canceled, Completed(canceledBeforeShow).Status);
            Assert.AreEqual(0, calls);
            var closeDuringShow = host.Enqueue(OpenRequest(), default);
            host.ProcessOne();
            Assert.AreEqual(FilePickerStatus.Canceled, Completed(closeDuringShow).Status);
            // A close veto permits new requests; the preceding close only canceled its generation.
            var afterVeto = host.Enqueue(OpenRequest(), default);
            Assert.IsFalse(afterVeto.IsCompleted);
            host.ProcessOne();
            Assert.AreEqual(2, calls);
            var pending = host.Enqueue(OpenRequest(), default);
            host.Dispose();
            Assert.AreEqual(FilePickerStatus.Canceled, Completed(pending).Status);
        }
    }

    [TestMethod]
    public void MissingHostAndDisposedOwnerHaveExplicitOrdinaryOutcomes()
    {
        using var owner = new Composition(new ReactiveGraph(), "picker-missing");
        var picker = new WindowsFilePicker(owner);
        Assert.AreEqual(FilePickerStatus.Unsupported, Completed(picker.OpenFilesAsync()).Status);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.AreEqual(
            FilePickerStatus.Canceled,
            Completed(picker.PickFolderAsync(cancellationToken: cancel.Token)).Status
        );
        owner.Dispose();
        Assert.AreEqual(FilePickerStatus.Canceled, Completed(picker.SaveFileAsync()).Status);
    }

    private static WindowsFilePickerRequest OpenRequest() =>
        new(WindowsFilePickerKind.Open, null, null, null, null, false);

    private static FilePickerResult Completed(ValueTask<FilePickerResult> result)
    {
        Assert.IsTrue(result.IsCompleted);
        return result.GetAwaiter().GetResult();
    }
}
