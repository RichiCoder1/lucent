namespace Lucent.IssueBrowser;

internal enum NavigationStorageStatus
{
    Ready,
    Missing,
    TooLarge,
    Unavailable,
    Saved,
    Cleared,
    Superseded,
    Stopped,
}

internal readonly record struct NavigationStorageRead(
    NavigationStorageStatus Status,
    ReadOnlyMemory<byte> Utf8 = default
);

// The seam controls only I/O timing. The store owns bounds, coalescing and commit order.
internal interface IIssueBrowserNavigationFile
{
    Task<NavigationStorageRead> ReadAsync(CancellationToken cancellationToken);
    Task<IPreparedNavigationFile> PrepareAsync(ReadOnlyMemory<byte> utf8);
    void Clear();
}

internal interface IPreparedNavigationFile : IDisposable
{
    void Commit();
}

/// <summary>Application-owned bounded storage; accepted writes outlive route ownership.</summary>
internal sealed class IssueBrowserNavigationStorage(IIssueBrowserNavigationFile file)
{
    private readonly object _gate = new();
    private long _generation;
    private PendingWrite? _pending;
    private Task? _worker;
    private bool _stopped;

    internal IssueBrowserNavigationStorage(string path)
        : this(new IssueBrowserNavigationFile(path)) { }

    internal Task<NavigationStorageRead> ReadAsync(CancellationToken cancellationToken = default) =>
        file.ReadAsync(cancellationToken);

    // Null is an explicit tombstone, not a failed capture. It fences old writes just like bytes.
    internal Task<NavigationStorageStatus> Submit(ReadOnlyMemory<byte>? utf8)
    {
        if (utf8 is { Length: > NavigationRestoration.MaximumPayloadBytes })
            return Task.FromResult(NavigationStorageStatus.TooLarge);
        lock (_gate)
        {
            if (_stopped)
                return Task.FromResult(NavigationStorageStatus.Stopped);
            var write = new PendingWrite(++_generation, utf8?.ToArray());
            _pending?.Completion.TrySetResult(NavigationStorageStatus.Superseded);
            _pending = write;
            _worker ??= Task.Run(ProcessAsync);
            return write.Completion.Task;
        }
    }

    internal Task DrainAsync()
    {
        lock (_gate)
            return _worker ?? Task.CompletedTask;
    }

    internal Task StopAsync()
    {
        lock (_gate)
        {
            _stopped = true;
            return _worker ?? Task.CompletedTask;
        }
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            PendingWrite write;
            lock (_gate)
            {
                if (_pending is null)
                {
                    _worker = null;
                    return;
                }
                write = _pending;
                _pending = null;
            }

            var status = NavigationStorageStatus.Unavailable;
            IPreparedNavigationFile? prepared = null;
            try
            {
                if (write.Utf8 is { } utf8)
                    prepared = await file.PrepareAsync(utf8).ConfigureAwait(false);
                lock (_gate)
                {
                    // Keep acceptance and the final rename/delete in one ordering boundary.
                    // The slow staging write is outside it and cannot publish stale bytes.
                    if (write.Generation != _generation)
                        status = NavigationStorageStatus.Superseded;
                    else if (prepared is not null)
                    {
                        prepared.Commit();
                        status = NavigationStorageStatus.Saved;
                    }
                    else
                    {
                        file.Clear();
                        status = NavigationStorageStatus.Cleared;
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Preserve the previous snapshot and permit a later independent submission.
            }
            finally
            {
                prepared?.Dispose();
                write.Completion.TrySetResult(status);
            }
        }
    }

    private sealed class PendingWrite(long generation, byte[]? utf8)
    {
        internal long Generation { get; } = generation;
        internal byte[]? Utf8 { get; } = utf8;
        internal TaskCompletionSource<NavigationStorageStatus> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal sealed class IssueBrowserNavigationFile(string path) : IIssueBrowserNavigationFile
{
    private readonly string _path = Path.GetFullPath(path);

    public async Task<NavigationStorageRead> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            const int maximum = NavigationRestoration.MaximumPayloadBytes;
            if (stream.Length > maximum)
                return new(NavigationStorageStatus.TooLarge);
            var buffer = new byte[maximum + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream
                    .ReadAsync(buffer.AsMemory(count), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;
                count += read;
            }
            return count > maximum
                ? new(NavigationStorageStatus.TooLarge)
                : new(NavigationStorageStatus.Ready, buffer.AsMemory(0, count));
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(NavigationStorageStatus.Missing);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(NavigationStorageStatus.Unavailable);
        }
    }

    public async Task<IPreparedNavigationFile> PrepareAsync(ReadOnlyMemory<byte> utf8)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            utf8.Length,
            NavigationRestoration.MaximumPayloadBytes
        );
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp"
        );
        var prepared = new PreparedFile(temporary, _path);
        try
        {
            await using (
                var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous
                )
            )
            {
                await stream.WriteAsync(utf8).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            return prepared;
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(_path);
        }
        catch (DirectoryNotFoundException)
        {
            // A tombstone must not create a directory just to clear an absent snapshot.
        }
    }

    private sealed class PreparedFile(string temporary, string destination)
        : IPreparedNavigationFile
    {
        public void Commit() => File.Move(temporary, destination, overwrite: true);

        public void Dispose()
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Cleanup is limited to this write's unique temporary file.
            }
        }
    }
}
