using System.Text.Json;

namespace Lucent.Examples.Workbench;

internal sealed class JsonFileSettingsRepository : ISettingsRepository, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly Action<Exception> _report;
    private readonly Action<string, string>? _commitForTests;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _reported;

    internal JsonFileSettingsRepository(string path, Action<Exception> reportRecoverableLoadError,
        Action<string, string>? commitForTests = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _report = reportRecoverableLoadError ?? throw new ArgumentNullException(nameof(reportRecoverableLoadError));
        _commitForTests = commitForTests;
    }

    public async Task<WorkbenchSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return WorkbenchSettings.Defaults;
            try
            {
                await using var stream = File.OpenRead(_path);
                return Clamp(await JsonSerializer.DeserializeAsync<WorkbenchSettings>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false) ?? WorkbenchSettings.Defaults);
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                if (Interlocked.Exchange(ref _reported, 1) == 0) _report(error);
                return WorkbenchSettings.Defaults;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(WorkbenchSettings value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(Clamp(value), JsonOptions);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (_commitForTests is not null) _commitForTests(temp, _path);
            else if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }
        finally
        {
            if (File.Exists(temp)) try { File.Delete(temp); } catch { }
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static WorkbenchSettings Clamp(WorkbenchSettings value) =>
        value with { SidebarWidth = Math.Clamp(value.SidebarWidth, 160, 640) };
}
