using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed class PerformanceEvidence(
    string app,
    string rawLog,
    string fixtureVersion = "issue-browser-compat-v1"
)
{
    internal const int SchemaVersion = 1;
    internal readonly List<EvidenceFailure> Failures = [];
    internal ManagedObservation? Managed;
    internal int? IdleFrames;
    internal double? LaunchToReadyMs;
    internal int? WindowDpi;
    internal int? ClientWidthPixels;
    internal int? ClientHeightPixels;
    internal string? CharacterizationJournalPath;
    private readonly Dictionary<string, CorpusEvidence> _corpora = new(StringComparer.Ordinal);
    private readonly List<Frame> _frames = [];
    private readonly List<Resource> _resources = [];
    private readonly List<EvidenceMetric> _metrics = [];
    private readonly List<PhaseBoundary> _phases = [];
    private PublishedIdentity? _identity;
    private bool _characterization;

    // 24 static nodes at the largest authored state plus 20 rows * 3 semantic nodes.
    // 20 rows = ceil(762 client DIPs / 49 compact-row DIPs) + four overscan rows.
    private const int AppUiaProviderLimit = 84;
    private readonly List<ScenarioObservation> _scenarios = [];
    private bool _collected;

    internal bool Passed =>
        _collected
        && Failures.Count == 0
        && _metrics.All(metric =>
            metric.Status == "pass"
            || (
                _characterization
                && metric.Name.StartsWith("resources.legacy18.", StringComparison.Ordinal)
            )
        );
    internal IReadOnlyList<EvidenceMetric> Metrics => _metrics;
    internal IReadOnlyList<Frame> Frames => _frames;

    internal void RecordFatal(string message)
    {
        _collected = true;
        Failures.Add(new("characterization", message));
    }

    internal void MarkPhase(
        string name,
        string status,
        int? frameNumber = null,
        IReadOnlyList<int>? sampleFrames = null
    )
    {
        var phase = new PhaseBoundary(name, status, frameNumber, DateTimeOffset.UtcNow);
        _phases.Add(phase);
        using var stream = new FileStream(
            rawLog + ".phases.jsonl",
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read
        );
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", SchemaVersion);
        writer.WriteString("phase", phase.Name);
        writer.WriteString("status", phase.Status);
        if (phase.FrameNumber is { } frame)
            writer.WriteNumber("frame", frame);
        else
            writer.WriteNull("frame");
        if (sampleFrames is not null)
        {
            writer.WriteStartArray("sampleFrames");
            foreach (var sample in sampleFrames)
                writer.WriteNumberValue(sample);
            writer.WriteEndArray();
        }
        writer.WriteString("utc", phase.Utc!.Value);
        writer.WriteEndObject();
        writer.Flush();
        stream.WriteByte((byte)'\n');
    }

    internal void BeginCorpus(string name, int firstFrame)
    {
        _corpora.Add(name, new(name, firstFrame));
        MarkPhase(name, "start", firstFrame - 1);
    }

    internal void AddCorpusFrame(string name, Frame frame, int frameNumber) =>
        _corpora[name].Add(frame, frameNumber);

    internal void CompleteCorpus(string name, int lastFrame)
    {
        _corpora[name].Complete(lastFrame);
        MarkPhase(
            name,
            "complete",
            lastFrame,
            _corpora[name].Samples.Select(sample => sample.Number).ToArray()
        );
    }

    internal void CollectAndEvaluate()
    {
        _collected = true;
        _identity = PublishedIdentity.Read(app);
        if (_identity.Status == "invalid")
            Failures.Add(new("identity", _identity.Error!));
        ValidateJournal();
        ReadTrace();
        foreach (var name in new[] { "input", "resize" })
        {
            _corpora.TryGetValue(name, out var corpus);
            CheckCorpus(name, corpus);
        }
        CheckMetric("idle.frames", IdleFrames, 0, "frames", "ten-second idle", "idle", null);
        CheckMetric(
            "managed.cycles",
            Managed?.Memory.Cycles,
            20,
            "cycles",
            "managed fixture",
            "managed",
            null,
            exact: true
        );
        CheckMetric(
            "virtualization.sourceRows",
            Managed?.Virtualization.SourceRows,
            10_000,
            "rows",
            "managed fixture",
            "managed",
            null,
            exact: true
        );
        CheckMetric(
            "managed.growth",
            Managed?.Memory.GrowthBytes,
            16L * 1024 * 1024,
            "bytes",
            "post-GC after 20 cycles",
            "managed",
            null
        );
        CheckMetric(
            "virtualization.visibleMaximum",
            Managed?.Virtualization.VisibleRowsMaximum,
            2,
            "rows",
            "20 managed cycles",
            "managed",
            null
        );
        CheckMetric(
            "virtualization.realizedMaximum",
            Managed?.Virtualization.RealizedMaximum,
            6,
            "rows",
            "20 managed cycles",
            "managed",
            null
        );
        CheckResources();
    }

    internal void CollectCharacterization(JsonElement journal)
    {
        _collected = true;
        _characterization = true;
        _identity = PublishedIdentity.Read(app);
        if (_identity.Status == "invalid")
            Failures.Add(new("identity", _identity.Error!));
        if (!journal.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1)
        {
            Failures.Add(new("characterization", "Unsupported characterization schema version."));
            return;
        }
        if (journal.GetProperty("workload").GetString() != "issue-browser-characterization-v1")
            Failures.Add(new("characterization", "Unsupported characterization workload."));
        if (
            !String.Equals(
                journal.GetProperty("appSha256").GetString(),
                Hash(app),
                StringComparison.OrdinalIgnoreCase
            )
        )
            Failures.Add(
                new("characterization", "Characterization app hash does not match the executable.")
            );
        if (!journal.GetProperty("complete").GetBoolean())
            Failures.Add(new("characterization", "Characterization did not complete."));
        if (
            !journal.TryGetProperty("display", out var display)
            || !display.TryGetProperty("clientHeightDip", out var height)
            || height.ValueKind != JsonValueKind.Number
            || !double.IsFinite(height.GetDouble())
            || height.GetDouble() <= 0
            || height.GetDouble() > 762
        )
            Failures.Add(
                new(
                    "characterization",
                    "Actual client height is missing or exceeds the 762-DIP app-v1 resource contract."
                )
            );
        if (
            journal.TryGetProperty("display", out display)
            && display.TryGetProperty("windowDpi", out var dpiElement)
            && display.TryGetProperty("clientWidthPixels", out var widthElement)
            && display.TryGetProperty("clientHeightPixels", out var heightPixelsElement)
            && dpiElement.ValueKind == JsonValueKind.Number
            && widthElement.ValueKind == JsonValueKind.Number
            && heightPixelsElement.ValueKind == JsonValueKind.Number
        )
        {
            WindowDpi = dpiElement.GetInt32();
            ClientWidthPixels = widthElement.GetInt32();
            ClientHeightPixels = heightPixelsElement.GetInt32();
        }
        else
            Failures.Add(
                new("characterization", "Actual DPI or client pixel geometry is missing.")
            );
        foreach (var failure in journal.GetProperty("failures").EnumerateArray())
            Failures.Add(
                new(
                    "driver",
                    failure.GetProperty("message").GetString() ?? "Unknown driver failure."
                )
            );
        foreach (var phase in journal.GetProperty("phases").EnumerateArray())
        {
            var label = phase.GetProperty("name").GetString()!;
            var split = label.LastIndexOf('.');
            var name = split < 0 ? label : label[..split];
            var status =
                split < 0 ? "complete"
                : label[(split + 1)..] == "end" ? "complete"
                : "start";
            _phases.Add(
                new(
                    name == "ready" ? "firstReady" : name,
                    status,
                    phase.GetProperty("frames").GetInt32(),
                    null
                )
            );
        }
        ValidateCharacterizationPhases(journal);
        var phaseRows = journal.GetProperty("phases").EnumerateArray().ToArray();
        var launch = phaseRows.FirstOrDefault(phase =>
            phase.GetProperty("name").GetString() == "launch.start"
        );
        var ready = phaseRows.FirstOrDefault(phase =>
            phase.GetProperty("name").GetString() == "ready"
        );
        if (
            launch.ValueKind == JsonValueKind.Object
            && ready.ValueKind == JsonValueKind.Object
            && launch.TryGetProperty("elapsedMs", out var launchElapsed)
            && ready.TryGetProperty("elapsedMs", out var readyElapsed)
            && launchElapsed.ValueKind == JsonValueKind.Number
            && readyElapsed.ValueKind == JsonValueKind.Number
            && double.IsFinite(readyElapsed.GetDouble() - launchElapsed.GetDouble())
            && readyElapsed.GetDouble() >= launchElapsed.GetDouble()
        )
            LaunchToReadyMs = readyElapsed.GetDouble() - launchElapsed.GetDouble();
        else
            Failures.Add(
                new("characterization", "Launch-to-ready phase elapsed time is incomplete.")
            );
        ReadTrace();
        var expectedNames = new[]
        {
            "focus-traversal",
            "list-scroll",
            "selection-content",
            "resize-same-breakpoint",
            "resize-breakpoint-crossing",
        };
        var scenarios = journal.GetProperty("scenarios").EnumerateArray().ToArray();
        if (scenarios.Length != expectedNames.Length)
            Failures.Add(
                new("characterization", $"Expected five scenarios, found {scenarios.Length}.")
            );
        foreach (var expected in expectedNames)
        {
            var matching = scenarios
                .Where(scenario => scenario.GetProperty("name").GetString() == expected)
                .ToArray();
            if (matching.Length != 1)
            {
                Failures.Add(new("characterization", $"Missing or duplicate scenario {expected}."));
                CheckMetric(
                    expected + ".operations",
                    null,
                    500,
                    "operations",
                    "characterization journal",
                    expected,
                    null,
                    exact: true
                );
                continue;
            }
            var scenario = matching[0];
            var operations = scenario.GetProperty("operations").EnumerateArray().ToArray();
            var complete =
                scenario.GetProperty("complete").GetBoolean()
                && scenario.GetProperty("expectedOperations").GetInt32() == 500
                && operations.Length == 500;
            var start = _phases
                .LastOrDefault(phase => phase.Name == expected && phase.Status == "start")
                .FrameNumber;
            var end = _phases
                .LastOrDefault(phase => phase.Name == expected && phase.Status == "complete")
                .FrameNumber;
            if (
                start is null
                || end is null
                || scenario.GetProperty("firstFrame").GetInt32() != start + 1
                || scenario.GetProperty("lastFrame").GetInt32() != end
            )
                complete = false;
            var selected = new List<Frame>(500);
            var settledResources = new List<Frame>(500);
            var priorLast = start ?? -1;
            JsonElement? priorEndpoint = null;
            for (var index = 0; index < operations.Length; index++)
            {
                var operation = operations[index];
                if (
                    operation.GetProperty("status").GetString() != "completed"
                    || operation.GetProperty("index").GetInt32() != index
                    || operation.GetProperty("endpoint").ValueKind
                        is JsonValueKind.Null
                            or JsonValueKind.Undefined
                )
                {
                    complete = false;
                    continue;
                }
                var endpoint = operation.GetProperty("endpoint");
                if (
                    !CharacterizationEndpoints.Valid(
                        expected,
                        index,
                        endpoint,
                        priorEndpoint,
                        journal
                    )
                )
                {
                    complete = false;
                    continue;
                }
                if (
                    !operation.TryGetProperty("clientActionMs", out var actionElement)
                    || actionElement.ValueKind != JsonValueKind.Number
                    || !operation.TryGetProperty(
                        "requestToFirstFrameMs",
                        out var firstFrameTimeElement
                    )
                    || firstFrameTimeElement.ValueKind != JsonValueKind.Number
                    || !operation.TryGetProperty("settleDrainMs", out var settleElement)
                    || settleElement.ValueKind != JsonValueKind.Number
                    || !double.IsFinite(actionElement.GetDouble())
                    || actionElement.GetDouble() < 0
                    || !double.IsFinite(firstFrameTimeElement.GetDouble())
                    || firstFrameTimeElement.GetDouble() < actionElement.GetDouble()
                    || !double.IsFinite(settleElement.GetDouble())
                    || settleElement.GetDouble() < 0
                )
                {
                    complete = false;
                    continue;
                }
                if (
                    !operation.TryGetProperty("firstFrame", out var firstElement)
                    || firstElement.ValueKind != JsonValueKind.Number
                    || !operation.TryGetProperty("lastFrame", out var lastElement)
                    || lastElement.ValueKind != JsonValueKind.Number
                )
                {
                    complete = false;
                    continue;
                }
                var first = firstElement.GetInt32();
                var last = lastElement.GetInt32();
                if (
                    start is null
                    || end is null
                    || first <= priorLast
                    || first <= start
                    || last < first
                    || last > end
                    || last > _frames.Count
                    || operation.GetProperty("attributedFrameCount").GetInt32() != last - first + 1
                )
                {
                    complete = false;
                    continue;
                }
                selected.Add(_frames[first - 1]);
                settledResources.Add(_frames[last - 1]);
                priorLast = last;
                priorEndpoint = endpoint;
            }
            CheckMetric(
                expected + ".operations",
                complete ? selected.Count : null,
                500,
                "operations",
                "attributed first frame and verified endpoint",
                expected,
                end,
                exact: true
            );
            if (!complete)
                Failures.Add(
                    new(
                        "characterization",
                        $"{expected} has incomplete operations, endpoints, timings, phase bounds or frame attribution."
                    )
                );
            if (expected == "focus-traversal")
            {
                double? growth = null;
                if (complete)
                {
                    var repeats = operations
                        .Select(
                            (operation, index) =>
                                (
                                    Identity: operation
                                        .GetProperty("endpoint")
                                        .GetProperty("runtimeId")
                                        .GetString()!,
                                    Providers: settledResources[index].UiaProviders
                                )
                        )
                        .GroupBy(sample => sample.Identity)
                        .ToArray();
                    if (repeats.Length > 0 && repeats.All(group => group.Count() >= 3))
                        growth = repeats.Max(group =>
                            group.Last().Providers - group.SkipLast(1).Last().Providers
                        );
                }
                CheckMetric(
                    "characterization.focus.providerRepeatGrowth",
                    growth,
                    0,
                    "providers",
                    "same focused identity, last attributed presentation for each visit after first lazy realization",
                    expected,
                    end
                );
            }
            if (expected == "list-scroll")
            {
                double? growth = null;
                if (
                    complete
                    && operations[0]
                        .GetProperty("endpoint")
                        .GetProperty("visibleIssueNumbers")
                        .EnumerateArray()
                        .Select(value => value.GetInt32())
                        .SequenceEqual(
                            operations[498]
                                .GetProperty("endpoint")
                                .GetProperty("visibleIssueNumbers")
                                .EnumerateArray()
                                .Select(value => value.GetInt32())
                        )
                )
                    growth = settledResources[498].UiaProviders - settledResources[0].UiaProviders;
                CheckMetric(
                    "characterization.scroll.providerReturnGrowth",
                    growth,
                    0,
                    "providers",
                    "same 0.4 percent scroll endpoint; last attributed presentation by settle, not a post-UIA query census",
                    expected,
                    complete ? operations[498].GetProperty("lastFrame").GetInt32() : end
                );
            }
            _scenarios.Add(
                new(
                    expected,
                    operations.Length,
                    selected.Count,
                    complete,
                    selected.Count == 0
                        ? null
                        : Percentile(selected.Select(frame => frame.EndToEnd).ToArray(), .95),
                    selected.Count == 0
                        ? null
                        : Percentile(selected.Select(frame => frame.Projection).ToArray(), .95),
                    selected.Count == 0
                        ? null
                        : Percentile(selected.Select(frame => frame.Raster).ToArray(), .95),
                    selected.Count == 0
                        ? null
                        : Percentile(selected.Select(frame => frame.Upload).ToArray(), .95),
                    selected.Count == 0
                        ? null
                        : Percentile(selected.Select(frame => frame.Present).ToArray(), .95)
                )
            );
        }
        CheckResources();
    }

    private void ValidateCharacterizationPhases(JsonElement journal)
    {
        var names = new[]
        {
            "launch.start",
            "ready",
            "warmup.start",
            "warmup.end",
            "focus-traversal.start",
            "focus-traversal.end",
            "list-scroll.start",
            "list-scroll.end",
            "selection-content.start",
            "selection-content.end",
            "resize-same-breakpoint.start",
            "resize-same-breakpoint.end",
            "resize-breakpoint-crossing.start",
            "resize-breakpoint-crossing.end",
            "close.start",
            "close.end",
            "cleanup.end",
        };
        var phases = journal.GetProperty("phases").EnumerateArray().ToArray();
        if (phases.Length != names.Length)
            Failures.Add(
                new(
                    "characterization",
                    $"Lifecycle phase journal has {phases.Length} boundaries, expected {names.Length}."
                )
            );
        var previousFrame = -1;
        var previousElapsed = -1d;
        for (var index = 0; index < names.Length; index++)
        {
            if (
                index >= phases.Length
                || phases[index].GetProperty("name").GetString() != names[index]
                || !phases[index].TryGetProperty("elapsedMs", out var elapsedElement)
                || elapsedElement.ValueKind != JsonValueKind.Number
            )
            {
                Failures.Add(
                    new(
                        "characterization",
                        "Lifecycle phase boundary is missing or out of order: " + names[index]
                    )
                );
                continue;
            }
            var frames = phases[index].GetProperty("frames").GetInt32();
            var elapsed = elapsedElement.GetDouble();
            if (frames < previousFrame || !double.IsFinite(elapsed) || elapsed < previousElapsed)
                Failures.Add(
                    new(
                        "characterization",
                        "Lifecycle frame/time boundaries moved backward at " + names[index]
                    )
                );
            previousFrame = frames;
            previousElapsed = elapsed;
        }
    }

    private static double Percentile(double[] values, double proportion)
    {
        Array.Sort(values);
        return values[(int)Math.Ceiling(values.Length * proportion) - 1];
    }

    private void ValidateJournal()
    {
        var journal = rawLog + ".phases.jsonl";
        if (!File.Exists(journal))
        {
            Failures.Add(new("journal", "Phase journal is missing."));
            return;
        }
        var required = new[]
        {
            "launch.start",
            "firstReady.complete",
            "warmup.start",
            "warmup.complete",
            "managed.start",
            "managed.complete",
            "input.start",
            "input.complete",
            "resize.start",
            "resize.complete",
            "idle.start",
            "idle.complete",
            "close.start",
            "close.complete",
            "cleanup.start",
            "cleanup.complete",
        };
        var actual = _phases.Select(phase => phase.Name + "." + phase.Status).ToArray();
        if (!actual.SequenceEqual(required))
            Failures.Add(
                new("journal", "Native lifecycle phase boundaries are missing or out of order.")
            );
        var lineNumber = 0;
        foreach (var line in File.ReadLines(journal))
        {
            lineNumber++;
            try
            {
                using var document = JsonDocument.Parse(line);
                if (
                    !document.RootElement.TryGetProperty("schemaVersion", out var version)
                    || version.GetInt32() != SchemaVersion
                )
                    throw new InvalidOperationException(
                        "Unsupported phase journal schema version."
                    );
                if (lineNumber > _phases.Count)
                    throw new InvalidOperationException("Phase journal has an extra boundary.");
                var phase = _phases[lineNumber - 1];
                var row = document.RootElement;
                if (
                    row.GetProperty("phase").GetString() != phase.Name
                    || row.GetProperty("status").GetString() != phase.Status
                    || (
                        row.GetProperty("frame").ValueKind == JsonValueKind.Null
                            ? null
                            : row.GetProperty("frame").GetInt32()
                    ) != phase.FrameNumber
                )
                    throw new InvalidOperationException(
                        "Persisted phase boundary disagrees with the collected observation."
                    );
                if (phase.Status == "complete" && _corpora.TryGetValue(phase.Name, out var corpus))
                {
                    if (
                        !row.TryGetProperty("sampleFrames", out var samples)
                        || !samples
                            .EnumerateArray()
                            .Select(value => value.GetInt32())
                            .SequenceEqual(corpus.Samples.Select(sample => sample.Number))
                    )
                        throw new InvalidOperationException(
                            "Persisted corpus sample frame indexes disagree with the collected observation."
                        );
                }
                else if (row.TryGetProperty("sampleFrames", out _))
                    throw new InvalidOperationException(
                        "Unexpected corpus sample frame indexes on a non-corpus boundary."
                    );
            }
            catch (Exception error)
                when (error
                        is JsonException
                            or InvalidOperationException
                            or FormatException
                            or KeyNotFoundException
                )
            {
                Failures.Add(new("journal", $"Line {lineNumber}: {error.Message}"));
            }
        }
        if (lineNumber != _phases.Count)
            Failures.Add(new("journal", "Phase journal is truncated or missing a boundary."));
    }

    private void ReadTrace()
    {
        if (!File.Exists(rawLog))
        {
            Failures.Add(new("trace", "Raw diagnostic log is missing."));
            return;
        }
        var lines = File.ReadAllLines(rawLog);
        if (lines.Length == 0 || lines[0] != "lucent-performance-v1")
            Failures.Add(new("trace", "Diagnostic header is missing or unsupported."));
        long previousRequest = -1;
        long previousPresented = -1;
        for (var index = 1; index < lines.Length; index++)
        {
            try
            {
                if (lines[index].StartsWith("frame|", StringComparison.Ordinal))
                {
                    var frame = Frame.Parse(lines[index]);
                    if (
                        frame.RequestTimestamp <= previousRequest
                        || frame.PresentedTimestamp <= previousPresented
                        || frame.PresentedTimestamp < frame.RequestTimestamp
                    )
                        throw new FormatException("Frame timestamps are out of order.");
                    previousRequest = frame.RequestTimestamp;
                    previousPresented = frame.PresentedTimestamp;
                    _frames.Add(frame);
                }
                else if (lines[index].StartsWith("resources|", StringComparison.Ordinal))
                    _resources.Add(Resource.Parse(lines[index]));
                else
                    throw new FormatException("Unknown diagnostic row.");
            }
            catch (Exception error)
                when (error is FormatException or OverflowException or InvalidOperationException)
            {
                Failures.Add(new("trace", $"Line {index + 1}: {error.Message}"));
            }
        }
        if (_resources.Count != 2 || _resources[0].Phase != "pre" || _resources[1].Phase != "post")
            Failures.Add(
                new("trace", "Pre/post resource observations are missing or out of order.")
            );
    }

    private void CheckCorpus(string name, CorpusEvidence? corpus)
    {
        var phase = name + " corpus";
        if (
            corpus is null
            || !corpus.Completed
            || corpus.Frames.Count != 500
            || corpus.LastFrame < corpus.FirstFrame
        )
        {
            Failures.Add(
                new(
                    "corpus",
                    $"{name} corpus is incomplete or its delimiter does not span 500 frames."
                )
            );
            CheckMetric(
                name + ".samples",
                corpus?.Frames.Count,
                500,
                "frames",
                phase,
                phase,
                corpus?.LastFrame,
                exact: true,
                forceIncomplete: true
            );
            return;
        }
        var selected = corpus.Samples.Select(sample => sample.Frame).ToArray();
        if (
            corpus.Samples.Any(sample =>
                sample.Number > _frames.Count
                || sample.Number < corpus.FirstFrame
                || sample.Number > corpus.LastFrame
                || _frames[sample.Number - 1] != sample.Frame
            ) || selected.Any(frame => frame.Operation != name)
        )
        {
            Failures.Add(
                new("corpus", $"{name} samples are missing, altered, or from the wrong phase.")
            );
            CheckMetric(
                name + ".samples",
                null,
                500,
                "frames",
                phase,
                phase,
                corpus.LastFrame,
                exact: true
            );
            return;
        }
        CheckMetric(
            name + ".samples",
            500,
            500,
            "frames",
            phase,
            phase,
            corpus.LastFrame,
            exact: true
        );
        foreach (
            var (field, percentile, limit) in new[]
            {
                ("endToEnd.p95", .95, 16.7),
                ("endToEnd.p99", .99, 33.3),
                ("raster.p95", .95, 8.3),
            }
        )
        {
            var values = selected
                .Select(frame =>
                    field.StartsWith("raster", StringComparison.Ordinal)
                        ? frame.Raster
                        : frame.EndToEnd
                )
                .Order()
                .ToArray();
            var actual = values[(int)Math.Ceiling(values.Length * percentile) - 1];
            var violating = selected.FirstOrDefault(frame =>
                (
                    field.StartsWith("raster", StringComparison.Ordinal)
                        ? frame.Raster
                        : frame.EndToEnd
                ) == actual
            );
            CheckMetric(
                name + "." + field,
                actual,
                limit,
                "ms",
                "application request to presenter completion; file I/O between requests",
                phase,
                violating == default ? null : _frames.IndexOf(violating) + 1
            );
        }
    }

    private void CheckResources()
    {
        if (_frames.Count == 0)
        {
            Failures.Add(new("resources", "No frame resource samples were recorded."));
            foreach (
                var name in new[]
                {
                    "surfaces",
                    "textures",
                    "textBlobs",
                    "uiaProviders",
                    "handleDelta",
                }
            )
                CheckMetric(
                    "resources.lifetime." + name,
                    null,
                    name switch
                    {
                        "surfaces" or "textures" => 1,
                        "textBlobs" => 256,
                        "uiaProviders" => _characterization ? AppUiaProviderLimit : 18,
                        _ => 128,
                    },
                    "count",
                    "all recorded frames",
                    "lifetime",
                    null
                );
            if (_characterization)
                CheckMetric(
                    "resources.legacy18.lifetime.uiaProviders",
                    null,
                    18,
                    "count",
                    "historical compatibility diagnostic",
                    "lifetime",
                    null
                );
            return;
        }
        var pre =
            _resources.Count > 0 && _resources[0].Phase == "pre" ? _resources[0] : (Resource?)null;
        var post =
            _resources.Count > 1 && _resources[1].Phase == "post" ? _resources[1] : (Resource?)null;
        CheckResourceSet("lifetime", _frames, pre);
        foreach (
            var group in _frames
                .Select((frame, index) => (Frame: frame, Phase: PhaseFor(index + 1)))
                .GroupBy(item => item.Phase)
        )
            CheckResourceSet(group.Key, group.Select(item => item.Frame).ToArray(), pre);
        foreach (var corpus in _corpora.Values)
            if (
                corpus.Completed
                && corpus.Frames.Count > 0
                && !_metrics.Any(metric => metric.Name == "resources." + corpus.Name + ".surfaces")
            )
                CheckResourceSet(corpus.Name, corpus.Frames, pre);
        foreach (
            var (name, actual) in new[]
            {
                ("surfaces", (double?)post?.Surfaces),
                ("textures", post?.Textures),
                ("textBlobs", post?.TextBlobs),
                ("uiaProviders", post?.UiaProviders),
            }
        )
            CheckMetric(
                "resources.post." + name,
                actual,
                0,
                "count",
                "post-close post-GC",
                "cleanup",
                null
            );
    }

    private void CheckResourceSet(string phase, IReadOnlyList<Frame> frames, Resource? pre)
    {
        var metrics = new (string Name, int Limit, Func<Frame, double> Value)[]
        {
            ("surfaces", 1, frame => frame.Surfaces),
            ("textures", 1, frame => frame.Textures),
            ("textBlobs", 256, frame => frame.TextBlobs),
            (
                "uiaProviders",
                _characterization ? AppUiaProviderLimit : 18,
                frame => frame.UiaProviders
            ),
        };
        foreach (var (name, limit, value) in metrics)
        {
            var peak = frames.MaxBy(value);
            var frameNumber = _frames.IndexOf(peak) + 1;
            CheckMetric(
                "resources." + phase + "." + name,
                value(peak),
                limit,
                "count",
                _characterization && name == "uiaProviders"
                    ? "sampled frame maximum; app-v1 ceiling 24 static + 3*(ceil(762/49)+4)=84, no stale overlap"
                    : "sampled frame maximum",
                phase == "lifetime" ? PhaseFor(frameNumber) : phase,
                frameNumber
            );
            if (_characterization && name == "uiaProviders")
                CheckMetric(
                    "resources.legacy18." + phase + ".uiaProviders",
                    value(peak),
                    18,
                    "count",
                    "historical Issue Browser compatibility limit; diagnostic pending migration disposition",
                    phase == "lifetime" ? PhaseFor(frameNumber) : phase,
                    frameNumber
                );
        }
        var handlePeak = frames.Max(frame => frame.Handles);
        if (phase == "lifetime")
            handlePeak = Math.Max(
                handlePeak,
                _resources.LastOrDefault(resource => resource.Phase == "post").Handles
            );
        var handleFrame = _frames.FindIndex(frame => frame.Handles == handlePeak) + 1;
        CheckMetric(
            "resources." + phase + ".handleDelta",
            pre is null ? null : (double)handlePeak - pre.Value.Handles,
            128,
            "handles",
            "sampled frame/pre/post peak minus pre-frame baseline",
            phase == "lifetime" ? PhaseFor(handleFrame) : phase,
            handleFrame == 0 ? null : handleFrame
        );
    }

    private string PhaseFor(int frameNumber)
    {
        foreach (
            var name in _phases
                .Select(phase => phase.Name)
                .Where(name => name is not ("launch" or "firstReady"))
                .Distinct()
                .Reverse()
        )
        {
            var start = _phases.LastOrDefault(phase =>
                phase.Name == name && phase.Status == "start"
            );
            var end = _phases.LastOrDefault(phase =>
                phase.Name == name && phase.Status == "complete"
            );
            if (
                start.FrameNumber is { } first
                && frameNumber > first
                && (end.FrameNumber is not { } last || frameNumber <= last)
            )
                return name;
        }
        var ready = _phases.LastOrDefault(phase =>
            phase.Name == "firstReady" && phase.Status == "complete"
        );
        return ready.FrameNumber is { } count && frameNumber <= count ? "startup" : "unclassified";
    }

    private void CheckMetric(
        string name,
        double? actual,
        double limit,
        string unit,
        string boundary,
        string phase,
        int? frame,
        bool exact = false,
        bool forceIncomplete = false
    )
    {
        var status =
            actual is null || forceIncomplete ? "incomplete"
            : (exact ? actual == limit : actual <= limit) ? "pass"
            : "fail";
        _metrics.Add(new(name, actual, limit, unit, boundary, status, phase, frame));
    }

    internal void Write(Stream output)
    {
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", SchemaVersion);
        writer.WriteBoolean("ok", Passed);
        if (_characterization)
        {
            var diagnosticStatus =
                _metrics.Any(metric =>
                    metric.Name.StartsWith("resources.legacy18.", StringComparison.Ordinal)
                    && metric.Status == "fail"
                )
                    ? "fail"
                : _metrics.Any(metric =>
                    metric.Name.StartsWith("resources.legacy18.", StringComparison.Ordinal)
                    && metric.Status == "incomplete"
                )
                    ? "incomplete"
                : "pass";
            writer.WriteString("legacyUiaDiagnosticStatus", diagnosticStatus);
        }
        writer.WriteString("rawLog", rawLog);
        writer.WriteStartObject("metadata");
        writer.WriteString("fixtureVersion", fixtureVersion);
        writer.WriteString("verifierConfiguration", Configuration());
        writer.WriteString("appConfiguration", _identity?.Configuration ?? "unknown");
        writer.WriteString("appIdentityStatus", _identity?.Status ?? "unknown");
        writer.WriteString("appExecution", _identity?.Execution ?? "unknown");
        writer.WriteString("appTargetFramework", _identity?.TargetFramework ?? "unknown");
        writer.WriteString("appRuntimeIdentifier", _identity?.RuntimeIdentifier ?? "unknown");
        writer.WriteString("publishingSdk", _identity?.Sdk ?? "unknown");
        writer.WriteString("appSourceRevision", _identity?.SourceRevision ?? "unknown");
        if (_identity?.SourceDirty is { } sourceDirty)
            writer.WriteBoolean("appSourceDirty", sourceDirty);
        else
            writer.WriteNull("appSourceDirty");
        writer.WriteString("appPublishedUtc", _identity?.PublishedUtc ?? "unknown");
        if (WindowDpi is { } dpi)
            writer.WriteNumber("windowDpi", dpi);
        else
            writer.WriteNull("windowDpi");
        if (ClientWidthPixels is { } width)
            writer.WriteNumber("clientWidthPixels", width);
        else
            writer.WriteNull("clientWidthPixels");
        if (ClientHeightPixels is { } height)
            writer.WriteNumber("clientHeightPixels", height);
        else
            writer.WriteNull("clientHeightPixels");
        writer.WriteString("framework", AppContext.TargetFrameworkName ?? "unknown");
        writer.WriteString("runtime", RuntimeInformation.FrameworkDescription);
        writer.WriteString("architecture", RuntimeInformation.ProcessArchitecture.ToString());
        writer.WriteString("execution", RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "AOT");
        writer.WriteString(
            "binaryInformationalVersion",
            Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? "unknown"
        );
        writer.WriteString("checkoutRevision", Git("rev-parse HEAD") ?? "unknown");
        writer.WriteString(
            "checkoutDirty",
            Git("status --porcelain") is { } dirty
                ? (dirty.Length == 0 ? "clean" : "dirty")
                : "unknown"
        );
        writer.WriteString("appSha256", Hash(app));
        writer.WriteString("verifierSha256", Hash(Environment.ProcessPath));
        writer.WriteString(
            "instrumentation",
            "per-frame file diagnostics; file I/O occurs between requests"
        );
        if (_characterization)
            writer.WriteNull("phaseJournal");
        else
            writer.WriteString("phaseJournal", rawLog + ".phases.jsonl");
        writer.WriteString("characterizationJournal", CharacterizationJournalPath ?? "unknown");
        writer.WriteString(
            "environment",
            Environment.GetEnvironmentVariable("LUCENT_PERFORMANCE_ENVIRONMENT") ?? "unknown"
        );
        writer.WriteString(
            "isolation",
            Environment.GetEnvironmentVariable("LUCENT_PERFORMANCE_ISOLATION") ?? "unknown"
        );
        writer.WriteEndObject();
        writer.WriteStartArray("failures");
        foreach (var failure in Failures)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", failure.Kind);
            writer.WriteString("message", failure.Message);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("metrics");
        foreach (var metric in _metrics)
        {
            writer.WriteStartObject();
            writer.WriteString("name", metric.Name);
            if (metric.Actual is { } actual)
                writer.WriteNumber("actual", actual);
            else
                writer.WriteNull("actual");
            writer.WriteNumber("limit", metric.Limit);
            writer.WriteString("unit", metric.Unit);
            writer.WriteString("boundary", metric.Boundary);
            writer.WriteString("status", metric.Status);
            writer.WriteString("phase", metric.Phase);
            if (metric.Frame is { } frame)
                writer.WriteNumber("frame", frame);
            else
                writer.WriteNull("frame");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("phases");
        foreach (var phase in _phases)
        {
            writer.WriteStartObject();
            writer.WriteString("name", phase.Name);
            writer.WriteString("status", phase.Status);
            if (phase.FrameNumber is { } frame)
                writer.WriteNumber("frame", frame);
            else
                writer.WriteNull("frame");
            if (phase.Utc is { } utc)
                writer.WriteString("utc", utc);
            else
                writer.WriteNull("utc");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartObject("corpusDelimiters");
        foreach (var corpus in _corpora.Values)
        {
            writer.WriteStartObject(corpus.Name);
            writer.WriteNumber("firstFrame", corpus.FirstFrame);
            if (corpus.Completed)
                writer.WriteNumber("lastFrame", corpus.LastFrame);
            else
                writer.WriteNull("lastFrame");
            writer.WriteNumber("samples", corpus.Frames.Count);
            writer.WriteBoolean("complete", corpus.Completed);
            writer.WriteStartArray("sampleFrames");
            foreach (var sample in corpus.Samples)
                writer.WriteNumberValue(sample.Number);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        if (IdleFrames is { } idle)
            writer.WriteNumber("idleFrames", idle);
        else
            writer.WriteNull("idleFrames");
        if (LaunchToReadyMs is { } readyMs)
            writer.WriteNumber("launchToReadyMs", readyMs);
        else
            writer.WriteNull("launchToReadyMs");
        writer.WriteStartArray("frames");
        foreach (var frame in _frames)
        {
            writer.WriteStartObject();
            writer.WriteString("operation", frame.Operation);
            writer.WriteNumber("requestTimestamp", frame.RequestTimestamp);
            writer.WriteNumber("presentedTimestamp", frame.PresentedTimestamp);
            writer.WriteNumber("endToEndMs", frame.EndToEnd);
            writer.WriteNumber("projectionMs", frame.Projection);
            writer.WriteNumber("rasterMs", frame.Raster);
            writer.WriteNumber("uploadMs", frame.Upload);
            writer.WriteNumber("presentMs", frame.Present);
            writer.WriteNumber("surfaces", frame.Surfaces);
            writer.WriteNumber("textures", frame.Textures);
            writer.WriteNumber("textBlobs", frame.TextBlobs);
            writer.WriteNumber("uiaProviders", frame.UiaProviders);
            writer.WriteNumber("handles", frame.Handles);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        if (_characterization)
        {
            writer.WriteStartArray("characterization");
            foreach (var scenario in _scenarios)
            {
                writer.WriteStartObject();
                writer.WriteString("name", scenario.Name);
                writer.WriteNumber("operations", scenario.Operations);
                writer.WriteNumber("attributedFrames", scenario.AttributedFrames);
                writer.WriteBoolean("complete", scenario.Complete);
                WriteNullable(writer, "endToEndP95Ms", scenario.EndToEndP95Ms);
                WriteNullable(writer, "projectionP95Ms", scenario.ProjectionP95Ms);
                WriteNullable(writer, "rasterP95Ms", scenario.RasterP95Ms);
                WriteNullable(writer, "uploadP95Ms", scenario.UploadP95Ms);
                WriteNullable(writer, "presentP95Ms", scenario.PresentP95Ms);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteStartArray("resources");
        foreach (var resource in _resources)
        {
            writer.WriteStartObject();
            writer.WriteString("phase", resource.Phase);
            writer.WriteNumber("surfaces", resource.Surfaces);
            writer.WriteNumber("textures", resource.Textures);
            writer.WriteNumber("textBlobs", resource.TextBlobs);
            writer.WriteNumber("uiaProviders", resource.UiaProviders);
            writer.WriteNumber("handles", resource.Handles);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        if (Managed is { } managed)
        {
            writer.WriteStartObject("managed");
            writer.WriteNumber("cycles", managed.Memory.Cycles);
            writer.WriteNumber("baselineBytes", managed.Memory.BaselineBytes);
            writer.WriteNumber("peakBytes", managed.Memory.PeakBytes);
            if (managed.Memory.PostGcBytes is { } postGcBytes)
                writer.WriteNumber("postGcBytes", postGcBytes);
            else
                writer.WriteNull("postGcBytes");
            if (managed.Memory.GrowthBytes is { } growthBytes)
                writer.WriteNumber("growthBytes", growthBytes);
            else
                writer.WriteNull("growthBytes");
            writer.WriteEndObject();
            writer.WriteStartObject("virtualization");
            writer.WriteNumber("sourceRows", managed.Virtualization.SourceRows);
            writer.WriteNumber("visibleRowsMaximum", managed.Virtualization.VisibleRowsMaximum);
            writer.WriteNumber("realizedMaximum", managed.Virtualization.RealizedMaximum);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("managed");
            writer.WriteNull("virtualization");
        }
        writer.WriteEndObject();
        writer.Flush();
    }

    private static string Configuration() =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static void WriteNullable(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } actual)
            writer.WriteNumber(name, actual);
        else
            writer.WriteNull(name);
    }

    private static string Hash(string? path)
    {
        try
        {
            return path is not null && File.Exists(path)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                : "unknown";
        }
        catch (IOException)
        {
            return "unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    private static string? Git(string argument)
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo("git", argument)
                {
                    WorkingDirectory = Environment.CurrentDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            return process.WaitForExit(2000) && process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class CorpusEvidence(string name, int firstFrame)
{
    internal string Name { get; } = name;
    internal int FirstFrame { get; } = firstFrame;
    internal int LastFrame { get; private set; }
    internal bool Completed { get; private set; }
    internal List<(int Number, Frame Frame)> Samples { get; } = [];
    internal IReadOnlyList<Frame> Frames => Samples.Select(sample => sample.Frame).ToArray();

    internal void Add(Frame frame, int frameNumber)
    {
        if (Samples.Count > 0 && frameNumber <= Samples[^1].Number)
            throw new InvalidOperationException(
                $"{Name} frame sequence is discontinuous at {frameNumber}."
            );
        Samples.Add((frameNumber, frame));
    }

    internal void Complete(int lastFrame)
    {
        LastFrame = lastFrame;
        Completed = true;
    }
}

internal readonly record struct EvidenceFailure(string Kind, string Message);

internal readonly record struct EvidenceMetric(
    string Name,
    double? Actual,
    double Limit,
    string Unit,
    string Boundary,
    string Status,
    string Phase,
    int? Frame
);

internal readonly record struct PhaseBoundary(
    string Name,
    string Status,
    int? FrameNumber,
    DateTimeOffset? Utc
);

internal readonly record struct ScenarioObservation(
    string Name,
    int Operations,
    int AttributedFrames,
    bool Complete,
    double? EndToEndP95Ms,
    double? ProjectionP95Ms,
    double? RasterP95Ms,
    double? UploadP95Ms,
    double? PresentP95Ms
);
