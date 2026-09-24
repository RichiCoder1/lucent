using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lucent.Performance.Tests;

[TestClass]
public sealed class EvidenceTests
{
    [TestMethod]
    public void SimultaneousTimingAndStartupResourceBreachesAreBothReported()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create(inputMs: 20, startupProviders: 22);
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual(1, report.RootElement.GetProperty("schemaVersion").GetInt32());
        AssertMetric(report, "input.endToEnd.p95", "fail", 20);
        AssertMetric(report, "resources.lifetime.uiaProviders", "fail", 22);
        AssertMetric(report, "resources.input.uiaProviders", "pass", 15);
        Assert.AreEqual(1001, report.RootElement.GetProperty("frames").GetArrayLength());
        Assert.AreEqual(
            20,
            report.RootElement.GetProperty("frames")[1].GetProperty("projectionMs").GetDouble()
        );
        Assert.AreEqual(2, report.RootElement.GetProperty("resources").GetArrayLength());
    }

    [TestMethod]
    public void MissingShutdownIsIncompleteAndCannotPass()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create(includePost: false);
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        AssertMetric(report, "resources.post.uiaProviders", "incomplete", null);
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
    public void MalformedRowAndWrongPhaseRemainVisibleAlongsideIncompleteCorpus()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create(wrongPhase: true, malformed: true);
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        AssertMetric(report, "input.samples", "incomplete", null);
        Assert.IsTrue(
            report
                .RootElement.GetProperty("failures")
                .EnumerateArray()
                .Any(failure =>
                    failure
                        .GetProperty("message")
                        .GetString()!
                        .Contains("Line", StringComparison.Ordinal)
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
                        .Contains("wrong phase", StringComparison.Ordinal)
                )
        );
    }

    [TestMethod]
    public void PrimaryAndCleanupFailuresRetainTheirOrder()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create();
        evidence.Failures.Add(new("operation", "first failure"));
        evidence.Failures.Add(new("cleanup", "close failed"));
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        var failures = report.RootElement.GetProperty("failures");
        Assert.AreEqual("first failure", failures[0].GetProperty("message").GetString());
        Assert.AreEqual("close failed", failures[1].GetProperty("message").GetString());
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
    }

    [TestMethod]
    public void NoOpScrollAndEmptyEndpointAreRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VirtualizationEndpoint.Verify(["Issue 9999", "Issue 10000"], ["Issue 1", "Issue 2"], 2)
        );
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VirtualizationEndpoint.Verify(["Issue 9999", "Issue 10000"], [], 0)
        );
    }

    [TestMethod]
    public void ExtraFrameIsRetainedOutsideTheSelectedCorpus()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create(extraFrame: true);
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        AssertMetric(report, "input.samples", "pass", 500);
        Assert.AreEqual(1002, report.RootElement.GetProperty("frames").GetArrayLength());
        var input = report.RootElement.GetProperty("corpusDelimiters").GetProperty("input");
        Assert.AreEqual(502, input.GetProperty("lastFrame").GetInt32());
        Assert.AreEqual(502, input.GetProperty("sampleFrames")[499].GetInt32());
    }

    [TestMethod]
    public void MetadataNamesTheActualVerifierConfigurationAndJournalRejectsUnknownSchema()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create();
        fixture.AppendJournal("{\"schemaVersion\":999,\"phase\":\"input\"}");
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
#if DEBUG
        Assert.AreEqual(
            "Debug",
            report
                .RootElement.GetProperty("metadata")
                .GetProperty("verifierConfiguration")
                .GetString()
        );
#else
        Assert.AreEqual(
            "Release",
            report
                .RootElement.GetProperty("metadata")
                .GetProperty("verifierConfiguration")
                .GetString()
        );
#endif
        Assert.AreEqual(
            "unknown",
            report.RootElement.GetProperty("metadata").GetProperty("appConfiguration").GetString()
        );
        Assert.IsTrue(
            report
                .RootElement.GetProperty("failures")
                .EnumerateArray()
                .Any(failure =>
                    failure
                        .GetProperty("message")
                        .GetString()!
                        .Contains("Unsupported phase journal schema", StringComparison.Ordinal)
                )
        );
    }

    [TestMethod]
    public void CompleteEvidenceCanPassWithoutInventedObservations()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create();
        evidence.Managed = new(new(20, 1000, 2000, 1000, 0, 16L * 1024 * 1024), new(10_000, 2, 6));
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        Assert.IsTrue(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual(0, report.RootElement.GetProperty("failures").GetArrayLength());
    }

    [TestMethod]
    public void WarmupPeakIsAttributedWithoutContaminatingMeasuredCorpus()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create(warmupProviders: 22);
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        AssertMetric(report, "resources.lifetime.uiaProviders", "fail", 22);
        AssertMetric(report, "resources.warmup.uiaProviders", "fail", 22);
        AssertMetric(report, "resources.input.uiaProviders", "pass", 15);
    }

    [TestMethod]
    public void BadRowsHeaderAndIncompleteCorpusFailIndependently()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create(
            nonfinite: true,
            outOfOrder: true,
            unsupportedHeader: true,
            completeInput: false
        );
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        AssertMetric(report, "input.samples", "incomplete", 500);
        var messages = report
            .RootElement.GetProperty("failures")
            .EnumerateArray()
            .Select(failure => failure.GetProperty("message").GetString())
            .ToArray();
        Assert.IsTrue(
            messages.Any(message => message!.Contains("header", StringComparison.Ordinal))
        );
        Assert.IsTrue(messages.Any(message => message!.Contains("Line", StringComparison.Ordinal)));
        Assert.IsTrue(
            messages.Any(message => message!.Contains("out of order", StringComparison.Ordinal))
        );
    }

    [TestMethod]
    public void PostCloseNonzeroIsAnIndependentLifetimeFailure()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create(postProviders: 1);
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        AssertMetric(report, "resources.post.uiaProviders", "fail", 1);
        AssertMetric(report, "resources.lifetime.uiaProviders", "pass", 15);
    }

    [TestMethod]
    public void ManagedGrowthBreachRetainsTheActualObservation()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create();
        evidence.Managed = new(
            new(20, 1_000, 20_000_000, 20_000_000, 19_999_000, 16L * 1024 * 1024),
            new(10_000, 2, 6)
        );
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        AssertMetric(report, "managed.growth", "fail", 19_999_000);
        Assert.AreEqual(
            19_999_000,
            report.RootElement.GetProperty("managed").GetProperty("growthBytes").GetInt64()
        );
    }

    [TestMethod]
    public void MissingPersistedCleanupBoundaryCannotPass()
    {
        using var fixture = new EvidenceFixture();
        var evidence = fixture.Create();
        evidence.Managed = new(new(20, 1000, 2000, 1000, 0, 16L * 1024 * 1024), new(10_000, 2, 6));
        fixture.RemoveLastJournalLine();
        evidence.CollectAndEvaluate();
        using var report = Write(evidence);
        Assert.IsFalse(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.IsTrue(
            report
                .RootElement.GetProperty("failures")
                .EnumerateArray()
                .Any(failure =>
                    failure
                        .GetProperty("message")
                        .GetString()!
                        .Contains("truncated", StringComparison.Ordinal)
                )
        );
    }

    private static JsonDocument Write(PerformanceEvidence evidence)
    {
        using var stream = new MemoryStream();
        evidence.Write(stream);
        return JsonDocument.Parse(stream.ToArray());
    }

    private static void AssertMetric(
        JsonDocument report,
        string name,
        string status,
        double? actual
    )
    {
        var metric = report
            .RootElement.GetProperty("metrics")
            .EnumerateArray()
            .Single(metric => metric.GetProperty("name").GetString() == name);
        Assert.AreEqual(status, metric.GetProperty("status").GetString());
        if (actual is { } value)
            Assert.AreEqual(value, metric.GetProperty("actual").GetDouble());
        else
            Assert.AreEqual(JsonValueKind.Null, metric.GetProperty("actual").ValueKind);
    }

    private sealed class EvidenceFixture : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "lucent-evidence-test-" + Guid.NewGuid().ToString("N") + ".log"
        );

        internal void AppendJournal(string line) =>
            File.AppendAllText(_path + ".phases.jsonl", line + Environment.NewLine);

        internal void RemoveLastJournalLine()
        {
            var journal = _path + ".phases.jsonl";
            File.WriteAllLines(journal, File.ReadAllLines(journal)[..^1]);
        }

        internal PerformanceEvidence Create(
            double inputMs = 5,
            int startupProviders = 15,
            bool includePost = true,
            bool wrongPhase = false,
            bool malformed = false,
            bool extraFrame = false,
            int warmupProviders = 0,
            bool nonfinite = false,
            bool outOfOrder = false,
            bool unsupportedHeader = false,
            bool completeInput = true,
            int postProviders = 0
        )
        {
            var evidence = new PerformanceEvidence("missing-app.exe", _path) { IdleFrames = 0 };
            evidence.MarkPhase("launch", "start", 0);
            var lines = new List<string>
            {
                unsupportedHeader ? "lucent-performance-v999" : "lucent-performance-v1",
                "resources|pre|0|0|0|0|100",
            };
            lines.Add(Row("startup", 1, 5, startupProviders));
            var frameNumber = 1;
            evidence.MarkPhase("firstReady", "complete", 1);
            evidence.MarkPhase("warmup", "start", 1);
            if (warmupProviders > 0)
            {
                frameNumber++;
                lines.Add(Row("input", frameNumber, 5, warmupProviders));
            }
            evidence.MarkPhase("warmup", "complete", frameNumber);
            evidence.MarkPhase("managed", "start", frameNumber);
            evidence.MarkPhase("managed", "complete", frameNumber);
            evidence.BeginCorpus("input", frameNumber + 1);
            for (var index = 0; index < 500; index++)
            {
                if (extraFrame && index == 2)
                {
                    frameNumber++;
                    lines.Add(Row("other", frameNumber, 5, 15));
                }
                frameNumber++;
                var operation = wrongPhase && index == 2 ? "resize" : "input";
                var row = Row(operation, frameNumber, inputMs, 15);
                lines.Add(row);
                evidence.AddCorpusFrame("input", Frame.Parse(row), frameNumber);
            }
            if (completeInput)
                evidence.CompleteCorpus("input", frameNumber);
            evidence.BeginCorpus("resize", frameNumber + 1);
            for (var index = 0; index < 500; index++)
            {
                frameNumber++;
                var row = Row("resize", frameNumber, 5, 15);
                lines.Add(row);
                evidence.AddCorpusFrame("resize", Frame.Parse(row), frameNumber);
            }
            evidence.CompleteCorpus("resize", frameNumber);
            evidence.MarkPhase("idle", "start", frameNumber);
            evidence.MarkPhase("idle", "complete", frameNumber);
            evidence.MarkPhase("close", "start", frameNumber);
            evidence.MarkPhase("close", "complete", frameNumber);
            evidence.MarkPhase("cleanup", "start");
            evidence.MarkPhase("cleanup", "complete");
            if (malformed)
                lines.Insert(4, "frame|input|truncated");
            if (nonfinite)
                lines.Insert(4, "frame|input|99999|100000|NaN|0|0|0|0|0|0|0|0|100");
            if (outOfOrder)
                lines.Add("frame|input|1|2|1|0|0|0|0|0|0|0|0|100");
            if (includePost)
                lines.Add($"resources|post|0|0|0|{postProviders}|100");
            File.WriteAllLines(_path, lines);
            return evidence;
        }

        private static string Row(string operation, int sequence, double ms, int providers) =>
            string.Join(
                '|',
                "frame",
                operation,
                sequence * 100L,
                sequence * 100L + 50,
                ms.ToString("R", CultureInfo.InvariantCulture),
                ms.ToString("R", CultureInfo.InvariantCulture),
                "0.5",
                "0.2",
                "0.3",
                "1",
                "1",
                "1",
                providers.ToString(CultureInfo.InvariantCulture),
                "100"
            );

        public void Dispose()
        {
            if (File.Exists(_path))
                File.Delete(_path);
            if (File.Exists(_path + ".phases.jsonl"))
                File.Delete(_path + ".phases.jsonl");
        }
    }
}
