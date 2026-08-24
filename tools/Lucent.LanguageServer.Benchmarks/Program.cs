using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Lucent.LanguageServer;

var outputPath = args.Length == 1 ? args[0] : throw new ArgumentException("Expected an output path.");
var repository = Directory.GetCurrentDirectory();
var manifestPath = Path.Combine(repository, "tools", "Lucent.LanguageServer.Benchmarks", "workload.json");
var manifest = JsonSerializer.Deserialize<Workload>(File.ReadAllText(manifestPath))
    ?? throw new InvalidOperationException("The benchmark workload manifest is invalid.");
ValidateManifest(manifest);

var fixturePath = Path.Combine(repository, "examples", "workbench", manifest.fixture);
if (!File.Exists(fixturePath)) fixturePath = Path.Combine(AppContext.BaseDirectory, manifest.fixture);
var fixture = File.ReadAllText(fixturePath);
var fixtureHash = Hash(fixture);
if (!string.Equals(manifest.fixtureHash, fixtureHash, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The benchmark fixture does not match its workload manifest hash.");
var position = Position(fixture, manifest.completionOffset);
if (position.line != manifest.completionLine || position.character != manifest.completionCharacter)
    throw new InvalidOperationException("The benchmark completion offset does not match its declared UTF-16 position.");
var identity = MeasureSourceIdentity(repository);
var uri = new Uri(fixturePath).AbsoluteUri;

await RunAsync(fixture, uri, edit: false, manifest.warmup, manifest); // untimed warm-up
var warm = await RunAsync(fixture, uri, edit: false, manifest.samples, manifest);
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
var postWarmManagedBytes = GC.GetTotalMemory(forceFullCollection: true);
var edited = await RunAsync(fixture, uri, edit: true, manifest.samples, manifest);
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
var retained = GC.GetTotalMemory(forceFullCollection: true);
var peak = Math.Max(warm.PeakManagedBytes, edited.PeakManagedBytes);
var generations = Math.Max(warm.MaxGenerations, edited.MaxGenerations);

var result = new
{
    runner = manifest.runner,
    sourceIdentity = new
    {
        head = identity.Head,
        worktreeSha256 = identity.WorktreeSha256,
        trackedDiffSha256 = identity.TrackedDiffSha256,
        trackedDiffBytes = identity.TrackedDiffBytes,
        relevantUntracked = identity.RelevantUntracked,
            algorithm = "SHA-256(HEAD UTF-8, NUL, SHA-256(git diff --binary HEAD -- measured paths), NUL, each sorted relevant untracked path UTF-8, NUL, SHA-256(file bytes), NUL). Measured paths are compiler, MSBuild, language-server, utility catalog/generator, compiler/LSP tests, build props/targets, and this benchmark; docs, plans, research, subagent artifacts, and other concurrent work are excluded.",
    },
    environment = new { os = RuntimeInformation.OSDescription, framework = RuntimeInformation.FrameworkDescription, processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), processorCount = Environment.ProcessorCount, configuration = "Release" },
    workload = new { manifest.samples, fixture = Path.GetFileName(fixturePath), fixtureHash, manifest.completionOffset, completionPosition = new { manifest.completionLine, manifest.completionCharacter }, manifest.documentVersionStart, manifest.editSchedule, manifest.requiredCompletionItems, manifest.completionItemSetFingerprint },
    measurement = new
    {
        timing = "Each sample writes its request(s) to an interactive in-process duplex stream, then waits for that exact JSON-RPC response before sending the next sample. Timings begin before didChange (for edit samples) or completion write (for warm samples), and end after the matching response frame is parsed.",
        allocations = "Server request-handler allocation uses process-wide GC.GetTotalAllocatedBytes before and after each handled request. This avoids invalid thread-local deltas across awaits and conservatively includes concurrent harness allocation during handler execution.",
    },
    warmCompletion = Summarize(warm),
    editCompletion = Summarize(edited),
    postWarmManagedBytes,
    peakLiveManagedBytes = peak,
    retainedManagedBytes = retained,
    maxActiveProjectGenerations = generations,
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
EnforceBudgets(warm, edited, postWarmManagedBytes, peak, retained, generations, manifest.budgets);

static async Task<Run> RunAsync(string source, string uri, bool edit, int samples, Workload manifest)
{
    using var input = new DuplexStream();
    using var output = new DuplexStream();
    var metrics = new List<LanguageServerRequestMetric>();
    var metricsGate = new object();
    long peakManagedBytes = GC.GetTotalMemory(forceFullCollection: false);
    var server = LanguageServer.RunAsync(input, output, metric =>
    {
        lock (metricsGate)
        {
            metrics.Add(metric);
            peakManagedBytes = Math.Max(peakManagedBytes, GC.GetTotalMemory(forceFullCollection: false));
        }
    });

    var requestId = 1;
    await SendAsync(input, new { jsonrpc = "2.0", id = requestId, method = "initialize", @params = new { rootUri = new Uri(Directory.GetCurrentDirectory()).AbsoluteUri, capabilities = new { } } });
    await ExpectResponseAsync(output, requestId++);
    await SendAsync(input, new { jsonrpc = "2.0", method = "initialized", @params = new { } });
    await SendAsync(input, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, languageId = "lucent", version = manifest.documentVersionStart, text = source } } });

    // Establish project state without timing the first parse/load.
    await MeasureCompletionAsync(input, output, requestId++, source, uri, manifest, edit: false, index: 0, metrics, metricsGate);
    lock (metricsGate) metrics.Clear();

    var measurements = new List<Sample>(samples);
    var responseIds = new HashSet<int>();
    for (var index = 0; index < samples; index++)
    {
        var sample = await MeasureCompletionAsync(input, output, requestId, source, uri, manifest, edit, index, metrics, metricsGate);
        if (!responseIds.Add(requestId))
            throw new InvalidOperationException("A completion response ID was scheduled twice.");
        requestId++;
        ValidateCompletion(sample.Response, manifest, requestId - 1);
        measurements.Add(sample);
    }

    await SendAsync(input, new { jsonrpc = "2.0", id = requestId, method = "shutdown", @params = (object?)null });
    await ExpectResponseAsync(output, requestId);
    await SendAsync(input, new { jsonrpc = "2.0", method = "exit", @params = (object?)null });
    input.CompleteWriting();
    var exit = await server;
    if (exit != 0) throw new InvalidOperationException($"Server exited {exit}.");
    if (measurements.Count != samples || responseIds.Count != samples)
        throw new InvalidOperationException("Completion response IDs do not exactly match the scheduled request IDs.");

    return new Run(
        measurements.Select(sample => sample.Elapsed.TotalMilliseconds).ToArray(),
        measurements.Select(sample => sample.AllocatedBytes).ToArray(),
        measurements.Max(sample => sample.GenerationCount),
        peakManagedBytes);
}

static async Task<Sample> MeasureCompletionAsync(
    DuplexStream input,
    DuplexStream output,
    int requestId,
    string source,
    string uri,
    Workload manifest,
    bool edit,
    int index,
    List<LanguageServerRequestMetric> metrics,
    object metricsGate)
{
    int metricStart;
    lock (metricsGate) metricStart = metrics.Count;
    var started = Stopwatch.GetTimestamp();
    if (edit)
    {
        var text = source + (manifest.editSchedule == "alternate-newline" && index % 2 != 0 ? "\n\n" : "\n");
        await SendAsync(input, new { jsonrpc = "2.0", method = "textDocument/didChange", @params = new { textDocument = new { uri, version = manifest.documentVersionStart + index + 1 }, contentChanges = new[] { new { text } } } });
    }
    var position = new { line = manifest.completionLine, character = manifest.completionCharacter };
    await SendAsync(input, new { jsonrpc = "2.0", id = requestId, method = "textDocument/completion", @params = new { textDocument = new { uri }, position } });
    using var response = await ExpectResponseAsync(output, requestId);
    var elapsed = Stopwatch.GetElapsedTime(started);
    var expected = edit ? new[] { "textDocument/didChange", "textDocument/completion" } : new[] { "textDocument/completion" };
    var requestMetrics = await WaitForMetricsAsync(metrics, metricsGate, metricStart, expected);
    if (!requestMetrics.Select(metric => metric.Method).SequenceEqual(expected, StringComparer.Ordinal))
        throw new InvalidOperationException("A completion did not follow its scheduled document-version edit.");
    var completion = requestMetrics[^1];
    return new Sample(elapsed, requestMetrics.Sum(metric => metric.AllocatedBytes), completion.ProjectGenerationCount, response.RootElement.Clone());
}

static async Task<LanguageServerRequestMetric[]> WaitForMetricsAsync(
    List<LanguageServerRequestMetric> metrics,
    object gate,
    int start,
    IReadOnlyList<string> expected)
{
    for (var attempt = 0; attempt != 1000; attempt++)
    {
        lock (gate)
        {
            if (metrics.Count >= start + expected.Count)
            {
                var tail = metrics.TakeLast(expected.Count).ToArray();
                if (tail.Select(metric => metric.Method).SequenceEqual(expected, StringComparer.Ordinal))
                    return tail;
            }
        }
        await Task.Delay(1);
    }
    throw new InvalidOperationException("The server did not publish metrics for a completed request.");
}

static void ValidateCompletion(JsonElement root, Workload manifest, int requestId)
{
    if (root.TryGetProperty("error", out var error))
        throw new InvalidOperationException($"Completion {requestId} returned an error: {error.GetRawText()}");
    var labels = Labels(root);
    if (!manifest.requiredCompletionItems.All(labels.Contains))
        throw new InvalidOperationException($"Completion {requestId} omitted a required item.");
    if (!string.Equals(Fingerprint(labels), manifest.completionItemSetFingerprint, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Completion {requestId} item set does not match the workload fingerprint.");
}

static async Task SendAsync(DuplexStream stream, object message)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
    var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
    await stream.WriteAsync(header);
    await stream.WriteAsync(bytes);
    await stream.FlushAsync();
}

static async Task<JsonDocument> ExpectResponseAsync(DuplexStream stream, int expectedId)
{
    while (true)
    {
        var response = await ReadMessageAsync(stream);
        if (!response.RootElement.TryGetProperty("id", out var id))
        {
            response.Dispose();
            continue;
        }
        if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var actualId))
        {
            response.Dispose();
            throw new InvalidOperationException("JSON-RPC response ID was not an integer.");
        }
        if (actualId != expectedId)
        {
            response.Dispose();
            throw new InvalidOperationException($"Expected JSON-RPC response {expectedId}, received {actualId}.");
        }
        return response;
    }
}

static async Task<JsonDocument> ReadMessageAsync(DuplexStream stream)
{
    var header = new List<byte>();
    while (true)
    {
        var value = await ReadByteAsync(stream);
        if (value < 0) throw new EndOfStreamException("JSON-RPC response ended before a header.");
        header.Add((byte)value);
        if (header.Count >= 4 && header[^4..].SequenceEqual("\r\n\r\n"u8.ToArray())) break;
        if (header.Count > 64 * 1024) throw new InvalidDataException("JSON-RPC response header is too large.");
    }
    var headerText = Encoding.ASCII.GetString(header.ToArray());
    var contentLength = headerText.Split("\r\n")
        .Select(line => line.Split(':', 2))
        .Where(parts => parts.Length == 2 && string.Equals(parts[0], "Content-Length", StringComparison.OrdinalIgnoreCase))
        .Select(parts => int.TryParse(parts[1].Trim(), out var value) ? value : -1)
        .FirstOrDefault(-1);
    if (contentLength < 0) throw new InvalidDataException("JSON-RPC response has no content length.");
    var payload = new byte[contentLength];
    var offset = 0;
    while (offset < payload.Length)
    {
        var read = await stream.ReadAsync(payload.AsMemory(offset));
        if (read == 0) throw new EndOfStreamException("JSON-RPC response payload ended early.");
        offset += read;
    }
    return JsonDocument.Parse(payload);
}

static async Task<int> ReadByteAsync(DuplexStream stream)
{
    var value = new byte[1];
    return await stream.ReadAsync(value) == 0 ? -1 : value[0];
}

static SourceIdentity MeasureSourceIdentity(string repository)
{
    var head = GitText(repository, "rev-parse", "HEAD");
    var paths = new[]
    {
        "build/Lucent.Compiler.props", "build/Lucent.Compiler.targets", "build/UtilityManifestGenerator",
        "src/Lucent.Compiler", "src/Lucent.Compiler.MSBuild", "src/Lucent.LanguageServer", "src/Lucent.Styles.Utilities",
        "tests/Lucent.Compiler.Tests", "tests/Lucent.Compiler.MSBuild.Tests", "tests/Lucent.LanguageServer.Tests",
        "tools/Lucent.LanguageServer.Benchmarks",
    };
    var trackedDiff = GitBytes(repository, ["diff", "--binary", "HEAD", "--", .. paths]);
    var untracked = GitText(repository, "ls-files", "--others", "--exclude-standard", "-z")
        .Split('\0', StringSplitOptions.RemoveEmptyEntries)
        .Where(IsMeasuredUntrackedPath)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => new SourceFileHash(path.Replace('\\', '/'), HashBytes(File.ReadAllBytes(Path.Combine(repository, path)))))
        .ToArray();
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    void Add(string value) { hash.AppendData(Encoding.UTF8.GetBytes(value)); hash.AppendData([0]); }
    Add(head);
    Add(HashBytes(trackedDiff));
    foreach (var file in untracked)
    {
        Add(file.Path);
        Add(file.Sha256);
    }
    return new SourceIdentity(head, Convert.ToHexString(hash.GetHashAndReset()), HashBytes(trackedDiff), trackedDiff.Length, untracked);
}

static bool IsMeasuredUntrackedPath(string path) =>
    path.StartsWith("build/UtilityManifestGenerator/", StringComparison.Ordinal) ||
    path.StartsWith("src/Lucent.Compiler/", StringComparison.Ordinal) ||
    path.StartsWith("src/Lucent.Compiler.MSBuild/", StringComparison.Ordinal) ||
    path.StartsWith("src/Lucent.LanguageServer/", StringComparison.Ordinal) ||
    path.StartsWith("src/Lucent.Styles.Utilities/", StringComparison.Ordinal) ||
    path.StartsWith("tests/Lucent.Compiler.Tests/", StringComparison.Ordinal) ||
    path.StartsWith("tests/Lucent.Compiler.MSBuild.Tests/", StringComparison.Ordinal) ||
    path.StartsWith("tests/Lucent.LanguageServer.Tests/", StringComparison.Ordinal) ||
    path.StartsWith("tools/Lucent.LanguageServer.Benchmarks/", StringComparison.Ordinal);

static string GitText(string repository, params string[] arguments) => Encoding.UTF8.GetString(GitBytes(repository, arguments)).TrimEnd('\r', '\n');
static byte[] GitBytes(string repository, IReadOnlyList<string> arguments)
{
    using var process = new Process { StartInfo = new ProcessStartInfo("git", string.Join(' ', arguments)) { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
    process.Start();
    using var output = new MemoryStream();
    process.StandardOutput.BaseStream.CopyTo(output);
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    return output.ToArray();
}

static HashSet<string> Labels(JsonElement response) => response.GetProperty("result").EnumerateArray().Select(item => item.GetProperty("label").GetString() ?? string.Empty).ToHashSet(StringComparer.Ordinal);
static string Fingerprint(IEnumerable<string> labels) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", labels.OrderBy(label => label, StringComparer.Ordinal)))));
static string Hash(string value) => HashBytes(Encoding.UTF8.GetBytes(value));
static string HashBytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
static object Summarize(Run run) => new { count = run.Latencies.Length, medianMilliseconds = Percentile(run.Latencies, .50), p95Milliseconds = Percentile(run.Latencies, .95), p99Milliseconds = Percentile(run.Latencies, .99), medianAllocatedBytes = Percentile(run.Allocations.Select(value => (double)value).ToArray(), .50), p95AllocatedBytes = Percentile(run.Allocations.Select(value => (double)value).ToArray(), .95) };
static double Percentile(double[] values, double percentile) { Array.Sort(values); return values[(int)Math.Ceiling(percentile * values.Length) - 1]; }
static (int line, int character) Position(string text, int offset) { var line = text[..offset].Count(character => character == '\n'); var start = text.LastIndexOf('\n', Math.Max(0, offset - 1)); return (line, offset - (start + 1)); }
static void ValidateManifest(Workload m)
{
    if (m.runner != "1" || m.samples < 500 || m.warmup < 1 || m.completionOffset < 0 || m.completionLine < 0 || m.completionCharacter < 0 || m.documentVersionStart < 1 || m.requiredCompletionItems is not { Length: > 0 } || string.IsNullOrWhiteSpace(m.completionItemSetFingerprint) || m.series is null || !m.series.SequenceEqual(["warmCompletion", "editCompletion"], StringComparer.Ordinal)) throw new InvalidOperationException("The benchmark workload manifest is incomplete.");
}
static void EnforceBudgets(Run warm, Run edit, long postWarm, long peak, long retained, int generations, Budgets budgets)
{
    var w = Summarize(warm); var e = Summarize(edit); using var values = JsonDocument.Parse(JsonSerializer.Serialize(new { w, e })); double V(string group, string name) => values.RootElement.GetProperty(group).GetProperty(name).GetDouble();
    if (V("w", "p95Milliseconds") > budgets.warmP95Milliseconds || V("w", "p99Milliseconds") > budgets.warmP99Milliseconds || V("w", "medianAllocatedBytes") > budgets.warmMedianAllocatedBytes || V("w", "p95AllocatedBytes") > budgets.warmP95AllocatedBytes || V("e", "p95Milliseconds") > budgets.editP95Milliseconds || V("e", "medianAllocatedBytes") > budgets.editMedianAllocatedBytes || V("e", "p95AllocatedBytes") > budgets.editP95AllocatedBytes || generations > budgets.maxActiveProjectGenerations || peak - postWarm > budgets.peakAbovePostWarmBytes || retained - postWarm > Math.Max(postWarm / 10, budgets.retainedAbovePostWarmBytes)) throw new InvalidOperationException("The benchmark did not meet the Plan 008 performance contract.");
}

sealed class DuplexStream : Stream
{
    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
    private byte[]? _current;
    private int _offset;

    public void CompleteWriting() => _chunks.Writer.TryComplete();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_current is null || _offset == _current.Length)
        {
            if (!_chunks.Reader.TryRead(out _current))
            {
                try { _current = await _chunks.Reader.ReadAsync(cancellationToken); }
                catch (ChannelClosedException) { return 0; }
            }
            _offset = 0;
        }
        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _chunks.Writer.TryWrite(buffer.ToArray());
        return ValueTask.CompletedTask;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) CompleteWriting();
        base.Dispose(disposing);
    }
}

sealed record Sample(TimeSpan Elapsed, long AllocatedBytes, int GenerationCount, JsonElement Response);
sealed record Run(double[] Latencies, long[] Allocations, int MaxGenerations, long PeakManagedBytes);
sealed record Workload(string runner, int samples, string[] series, string fixture, string fixtureHash, int completionOffset, int completionLine, int completionCharacter, int documentVersionStart, string editSchedule, int warmup, string[] requiredCompletionItems, string completionItemSetFingerprint, Budgets budgets);
sealed record Budgets(double warmP95Milliseconds, double warmP99Milliseconds, double warmMedianAllocatedBytes, double warmP95AllocatedBytes, double editP95Milliseconds, double editMedianAllocatedBytes, double editP95AllocatedBytes, int maxActiveProjectGenerations, long peakAbovePostWarmBytes, long retainedAbovePostWarmBytes);
sealed record SourceFileHash(string Path, string Sha256);
sealed record SourceIdentity(string Head, string WorktreeSha256, string TrackedDiffSha256, int TrackedDiffBytes, IReadOnlyList<SourceFileHash> RelevantUntracked);
