using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lucent.Performance.Tests;

[TestClass]
public sealed class CharacterizationTests
{
    [TestMethod]
    [DataRow(758, true)]
    [DataRow(762, true)]
    [DataRow(757, false)]
    [DataRow(763, false)]
    public void ResizeHeightMustStayWithinAuthoredResourceContract(int height, bool expected)
    {
        using var endpoint = JsonDocument.Parse(
            $$"""
            {
              "requestedClientWidthDip": 1100,
              "previousClientWidthDip": 1120,
              "actual": { "clientWidthDip": 1100, "clientHeightDip": {{height}} },
              "breakpoint": "wide",
              "splitterPresent": true,
              "detail": { "name": "ISSUE #10000" }
            }
            """
        );
        Assert.AreEqual(
            expected,
            CharacterizationEndpoints.Valid(
                "resize-same-breakpoint",
                0,
                endpoint.RootElement,
                null,
                default
            )
        );
    }

    [TestMethod]
    public void CompleteScenarioEvidenceSeparatesLegacyUiaDiagnosticFromCompletion()
    {
        using var fixture = new CharacterizationFixture();
        using var journal = fixture.Create();
        var evidence = new PerformanceEvidence(
            fixture.App,
            fixture.Raw,
            "issue-browser-characterization-v1"
        );
        evidence.CollectCharacterization(journal.RootElement);
        using var stream = new MemoryStream();
        evidence.Write(stream);
        using var report = JsonDocument.Parse(stream.ToArray());
        Assert.IsTrue(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual(
            "fail",
            report.RootElement.GetProperty("legacyUiaDiagnosticStatus").GetString()
        );
        Assert.AreEqual(5, report.RootElement.GetProperty("characterization").GetArrayLength());
        Assert.AreEqual(2501, report.RootElement.GetProperty("frames").GetArrayLength());
        Assert.AreEqual(2, report.RootElement.GetProperty("resources").GetArrayLength());
        Assert.AreEqual(
            5,
            report
                .RootElement.GetProperty("characterization")[0]
                .GetProperty("endToEndP95Ms")
                .GetDouble()
        );
    }

    [TestMethod]
    public void MissingPostObservationAndBadOperationBoundaryFailCompletion()
    {
        using var fixture = new CharacterizationFixture();
        using var journal = fixture.Create(includePost: false, invalidFirstFrame: true);
        var evidence = new PerformanceEvidence(
            fixture.App,
            fixture.Raw,
            "issue-browser-characterization-v1"
        );
        evidence.CollectCharacterization(journal.RootElement);
        using var stream = new MemoryStream();
        evidence.Write(stream);
        using var report = JsonDocument.Parse(stream.ToArray());
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.IsTrue(
            report
                .RootElement.GetProperty("failures")
                .EnumerateArray()
                .Any(failure =>
                    failure
                        .GetProperty("message")
                        .GetString()!
                        .Contains("incomplete operations", StringComparison.Ordinal)
                )
        );
        Assert.IsTrue(
            report
                .RootElement.GetProperty("failures")
                .EnumerateArray()
                .Any(failure =>
                    failure
                        .GetProperty("message")
                        .GetString()!
                        .Contains("Pre/post", StringComparison.Ordinal)
                )
        );
    }

    [TestMethod]
    public void RepeatedFocusProviderGrowthFailsBelowTheFiniteCeiling()
    {
        using var fixture = new CharacterizationFixture();
        using var journal = fixture.Create(growingProvider: true);
        var evidence = new PerformanceEvidence(
            fixture.App,
            fixture.Raw,
            "issue-browser-characterization-v1"
        );
        evidence.CollectCharacterization(journal.RootElement);
        using var stream = new MemoryStream();
        evidence.Write(stream);
        using var report = JsonDocument.Parse(stream.ToArray());
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        var metrics = report.RootElement.GetProperty("metrics").EnumerateArray().ToArray();
        Assert.AreEqual(
            "fail",
            metrics
                .Single(metric =>
                    metric.GetProperty("name").GetString()
                    == "characterization.focus.providerRepeatGrowth"
                )
                .GetProperty("status")
                .GetString()
        );
        Assert.AreEqual(
            "pass",
            metrics
                .Single(metric =>
                    metric.GetProperty("name").GetString() == "resources.lifetime.uiaProviders"
                )
                .GetProperty("status")
                .GetString()
        );
    }

    [TestMethod]
    [DataRow(false, true, 0)]
    [DataRow(true, false, 1)]
    public void ScrollReturnGrowthUsesSettledFrame(
        bool growingAtReturn,
        bool expectedPass,
        int expectedGrowth
    )
    {
        using var fixture = new CharacterizationFixture();
        using var journal = fixture.Create(
            scrollPairFrames: true,
            growingScrollReturn: growingAtReturn
        );
        var evidence = new PerformanceEvidence(
            fixture.App,
            fixture.Raw,
            "issue-browser-characterization-v1"
        );
        evidence.CollectCharacterization(journal.RootElement);
        using var stream = new MemoryStream();
        evidence.Write(stream);
        using var report = JsonDocument.Parse(stream.ToArray());
        Assert.AreEqual(expectedPass, report.RootElement.GetProperty("ok").GetBoolean());
        var metric = report
            .RootElement.GetProperty("metrics")
            .EnumerateArray()
            .Single(item =>
                item.GetProperty("name").GetString()
                == "characterization.scroll.providerReturnGrowth"
            );
        Assert.AreEqual((double)expectedGrowth, metric.GetProperty("actual").GetDouble());
        Assert.AreEqual(expectedPass ? "pass" : "fail", metric.GetProperty("status").GetString());
        var operations = journal.RootElement.GetProperty("scenarios")[1].GetProperty("operations");
        var frames = report.RootElement.GetProperty("frames");
        Assert.AreEqual(
            56,
            frames[operations[0].GetProperty("firstFrame").GetInt32() - 1]
                .GetProperty("uiaProviders")
                .GetInt32()
        );
        Assert.AreEqual(
            65,
            frames[operations[498].GetProperty("firstFrame").GetInt32() - 1]
                .GetProperty("uiaProviders")
                .GetInt32()
        );
    }

    [TestMethod]
    public void NoOpEndpointAndMissingLifecycleBoundaryAreRejected()
    {
        using var fixture = new CharacterizationFixture();
        using var journal = fixture.Create(badScrollEndpoint: true, missingCleanup: true);
        var evidence = new PerformanceEvidence(
            fixture.App,
            fixture.Raw,
            "issue-browser-characterization-v1"
        );
        evidence.CollectCharacterization(journal.RootElement);
        using var stream = new MemoryStream();
        evidence.Write(stream);
        using var report = JsonDocument.Parse(stream.ToArray());
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        var messages = report
            .RootElement.GetProperty("failures")
            .EnumerateArray()
            .Select(failure => failure.GetProperty("message").GetString())
            .ToArray();
        Assert.IsTrue(
            messages.Any(message => message!.Contains("Lifecycle", StringComparison.Ordinal))
        );
        Assert.IsTrue(
            messages.Any(message =>
                message!.Contains("list-scroll has incomplete", StringComparison.Ordinal)
            )
        );
    }

    [TestMethod]
    public void MissingAndInvalidOperationTimingsFailCompletion()
    {
        using var fixture = new CharacterizationFixture();
        using var journal = fixture.Create(invalidTimings: true);
        var evidence = new PerformanceEvidence(
            fixture.App,
            fixture.Raw,
            "issue-browser-characterization-v1"
        );
        evidence.CollectCharacterization(journal.RootElement);
        using var stream = new MemoryStream();
        evidence.Write(stream);
        using var report = JsonDocument.Parse(stream.ToArray());
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        var incomplete = report
            .RootElement.GetProperty("failures")
            .EnumerateArray()
            .Select(failure => failure.GetProperty("message").GetString())
            .Where(message =>
                message is not null
                && message.Contains("incomplete operations", StringComparison.Ordinal)
            )
            .ToArray();
        Assert.AreEqual(4, incomplete.Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingOrMalformedJournalKeepsAdjacentRawLogDiscoverable(bool malformed)
    {
        using var fixture = new CharacterizationFixture();
        File.WriteAllText(fixture.Raw, "lucent-performance-v1\n");
        var journalPath = Path.Combine(
            Path.GetDirectoryName(fixture.Raw)!,
            "characterization.json"
        );
        if (malformed)
            File.WriteAllText(journalPath, "{ incomplete");

        var evidence = CharacterizationEvidence.Evaluate(journalPath);
        using var stream = new MemoryStream();
        evidence.Write(stream);
        using var report = JsonDocument.Parse(stream.ToArray());
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual(fixture.Raw, report.RootElement.GetProperty("rawLog").GetString());
        Assert.AreEqual(
            journalPath,
            report
                .RootElement.GetProperty("metadata")
                .GetProperty("characterizationJournal")
                .GetString()
        );
        Assert.IsTrue(report.RootElement.GetProperty("failures").GetArrayLength() > 0);
    }

    private sealed class CharacterizationFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "lucent-characterization-test-" + Guid.NewGuid().ToString("N")
        );
        internal string App => Path.Combine(_directory, "issue.exe");
        internal string Raw => Path.Combine(_directory, "frames.log");

        internal CharacterizationFixture()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(App, [1, 2, 3]);
        }

        internal JsonDocument Create(
            bool includePost = true,
            bool invalidFirstFrame = false,
            bool growingProvider = false,
            bool badScrollEndpoint = false,
            bool missingCleanup = false,
            bool invalidTimings = false,
            bool scrollPairFrames = false,
            bool growingScrollReturn = false
        )
        {
            var names = new[]
            {
                "focus-traversal",
                "list-scroll",
                "selection-content",
                "resize-same-breakpoint",
                "resize-breakpoint-crossing",
            };
            var lines = new List<string>
            {
                "lucent-performance-v1",
                "resources|pre|0|0|0|0|100",
                FrameRow(1, "startup", 22),
            };
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("workload", "issue-browser-characterization-v1");
            writer.WriteString("app", App);
            writer.WriteString(
                "appSha256",
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(App)))
            );
            writer.WriteString("rawLog", Raw);
            writer.WriteNumber("processId", 1);
            writer.WriteStartObject("display");
            writer.WriteNumber("clientHeightDip", 760);
            writer.WriteNumber("windowDpi", 96);
            writer.WriteNumber("clientWidthPixels", 1120);
            writer.WriteNumber("clientHeightPixels", 760);
            writer.WriteEndObject();
            writer.WriteStartObject("warmup");
            writer.WriteStartObject("focusedEndpoint");
            writer.WriteString("runtimeId", "warm");
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteBoolean("complete", true);
            writer.WriteStartArray("failures");
            writer.WriteEndArray();
            writer.WriteStartArray("phases");
            WritePhase(writer, "launch.start", 0);
            WritePhase(writer, "ready", 1);
            WritePhase(writer, "warmup.start", 1);
            WritePhase(writer, "warmup.end", 1);
            var phaseFrame = 1;
            for (var scenarioIndex = 0; scenarioIndex < names.Length; scenarioIndex++)
            {
                WritePhase(writer, names[scenarioIndex] + ".start", phaseFrame);
                phaseFrame +=
                    names[scenarioIndex] == "list-scroll" && scrollPairFrames ? 1000 : 500;
                WritePhase(writer, names[scenarioIndex] + ".end", phaseFrame);
            }
            WritePhase(writer, "close.start", phaseFrame);
            WritePhase(writer, "close.end", phaseFrame);
            if (!missingCleanup)
                WritePhase(writer, "cleanup.end", phaseFrame);
            writer.WriteEndArray();
            writer.WriteStartArray("scenarios");
            var frame = 1;
            foreach (var name in names)
            {
                writer.WriteStartObject();
                writer.WriteString("name", name);
                writer.WriteNumber("expectedOperations", 500);
                writer.WriteNumber("firstFrame", frame + 1);
                writer.WriteNumber(
                    "lastFrame",
                    frame + (name == "list-scroll" && scrollPairFrames ? 1000 : 500)
                );
                writer.WriteBoolean("complete", true);
                writer.WriteStartArray("operations");
                for (var index = 0; index < 500; index++)
                {
                    frame++;
                    var firstFrame = frame;
                    lines.Add(
                        FrameRow(
                            frame,
                            name.StartsWith("resize", StringComparison.Ordinal)
                                ? "resize"
                                : "input",
                            growingProvider && name == names[0] && index == 499 ? 16
                                : scrollPairFrames && name == "list-scroll"
                                    ? index == 0 ? 56
                                        : 65
                                : 15
                        )
                    );
                    if (name == "list-scroll" && scrollPairFrames)
                    {
                        frame++;
                        lines.Add(
                            FrameRow(frame, "input", growingScrollReturn && index == 498 ? 36 : 35)
                        );
                    }
                    writer.WriteStartObject();
                    writer.WriteNumber("index", index);
                    writer.WriteString("status", "completed");
                    writer.WriteNumber(
                        "firstFrame",
                        invalidFirstFrame && name == names[0] && index == 0 ? 1 : firstFrame
                    );
                    writer.WriteNumber("lastFrame", frame);
                    writer.WriteNumber("attributedFrameCount", frame - firstFrame + 1);
                    if (invalidTimings && index == 0 && name == names[0])
                        writer.WriteNull("clientActionMs");
                    else
                        writer.WriteNumber("clientActionMs", 1);
                    if (!(invalidTimings && index == 0 && name == names[1]))
                        writer.WriteNumber(
                            "requestToFirstFrameMs",
                            invalidTimings && index == 0 && name == names[2] ? .5 : 2
                        );
                    writer.WriteNumber(
                        "settleDrainMs",
                        invalidTimings && index == 0 && name == names[3] ? -1 : 1
                    );
                    WriteEndpoint(writer, name, index, badScrollEndpoint);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
            if (includePost)
                lines.Add("resources|post|0|0|0|0|100");
            File.WriteAllLines(Raw, lines);
            return JsonDocument.Parse(stream.ToArray());
        }

        private static void WritePhase(Utf8JsonWriter writer, string name, int frames)
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteNumber("frames", frames);
            writer.WriteNumber("elapsedMs", frames);
            writer.WriteEndObject();
        }

        private static void WriteEndpoint(
            Utf8JsonWriter writer,
            string name,
            int index,
            bool badScrollEndpoint
        )
        {
            writer.WriteStartObject("endpoint");
            if (name == "focus-traversal")
            {
                writer.WriteString("runtimeId", "focus-" + index % 5);
                writer.WriteNumber("processId", 1);
                writer.WriteString("name", "Control");
                writer.WriteString("automationId", "control");
            }
            if (name == "list-scroll")
            {
                var percent =
                    badScrollEndpoint && index == 1 ? .4
                    : index < 250 ? (index + 1) * .4
                    : (499 - index) * .4;
                var first =
                    percent >= 100 ? 2 : Math.Max(2, 10_000 - (int)Math.Round(percent * 100));
                writer.WriteNumber("scrollPercent", percent);
                writer.WriteNumber("firstVisibleIssueNumber", first);
                writer.WriteNumber("lastVisibleIssueNumber", first - 1);
                writer.WriteStartArray("visibleIssueNumbers");
                writer.WriteNumberValue(first);
                writer.WriteNumberValue(first - 1);
                writer.WriteEndArray();
            }
            if (name == "selection-content")
            {
                var issue = index % 2 == 0 ? 9_999 : 10_000;
                writer.WriteNumber("selectedIssue", issue);
                writer.WriteStartObject("selectedRow");
                writer.WriteString("name", $"#{issue} issue");
                writer.WriteEndObject();
                writer.WriteStartObject("detail");
                writer.WriteString("name", $"ISSUE #{issue}");
                writer.WriteEndObject();
            }
            if (name.StartsWith("resize", StringComparison.Ordinal))
            {
                var crossing = name == "resize-breakpoint-crossing";
                var width = crossing
                    ? (index % 2 == 0 ? 760 : 1120)
                    : (index % 2 == 0 ? 1100 : 1120);
                var previous = crossing
                    ? (index % 2 == 0 ? 1120 : 760)
                    : (index % 2 == 0 ? 1120 : 1100);
                var wide = !crossing || index % 2 != 0;
                writer.WriteNumber("requestedClientWidthDip", width);
                writer.WriteNumber("previousClientWidthDip", previous);
                writer.WriteStartObject("actual");
                writer.WriteNumber("clientWidthDip", width);
                writer.WriteNumber("clientHeightDip", 760);
                writer.WriteEndObject();
                writer.WriteString("breakpoint", wide ? "wide" : "compact");
                writer.WriteBoolean("splitterPresent", wide);
                writer.WriteBoolean("backButtonPresent", !wide);
                writer.WriteStartObject("detail");
                writer.WriteString("name", "ISSUE #10000");
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        private static string FrameRow(int number, string operation, int providers) =>
            string.Join(
                '|',
                "frame",
                operation,
                number * 100L,
                number * 100L + 50,
                "5",
                "1",
                "1",
                "1",
                "2",
                "1",
                "1",
                "1",
                providers.ToString(CultureInfo.InvariantCulture),
                "100"
            );

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
