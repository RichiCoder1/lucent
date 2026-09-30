using System.Text.Json;
using System.Text.Json.Nodes;
using Lucent.Tools;

namespace Lucent.Tools.Tests;

[TestClass]
public sealed class FeedDoctorClientContracts
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task FeedCliPassesExplicitIdentityOverTheSeparateProbeWithoutRunningStaticOrTrustedChecks()
    {
        var probe = new Probe(request => new(0, Serialize(Observed(request))));
        var output = new StringWriter();
        Assert.AreEqual(
            0,
            await Program.RunAsync(
                [
                    "doctor",
                    "--feed",
                    "Lucent.Core",
                    "--version",
                    "0.3.0-dev.101.1",
                    "--workspace",
                    Path.GetTempPath(),
                    "--json",
                ],
                output,
                new StringWriter(),
                feedProbe: probe
            )
        );
        Assert.AreEqual("Lucent.Core", probe.Request!.PackageId);
        Assert.AreEqual("0.3.0-dev.101.1", probe.Request.Version);
        Assert.IsFalse(probe.Request.Online);
        using var report = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("nuget-feed-doctor", report.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual(
            probe.Request.Generation,
            report.RootElement.GetProperty("generation").GetString()
        );
        Assert.AreEqual(
            "notChecked",
            report.RootElement.GetProperty("restoreReadiness").GetString()
        );
        Assert.IsFalse(output.ToString().Contains(Path.GetTempPath(), StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FeedCliExcludesNativeTrustedAndPartialModesBeforeCallingTheAdapter()
    {
        foreach (
            var extras in new[]
            {
                new[] { "--native-prerequisites" },
                new[] { "--trusted-project", "private-project", "--server", "private-server" },
                new[] { "--online", "--online" },
            }
        )
        {
            var probe = new Probe(request => new(0, Serialize(Observed(request))));
            var output = new StringWriter();
            Assert.AreEqual(
                1,
                await Program.RunAsync(
                    ["doctor", "--feed", "Lucent.Core", "--version", "1.0.0", "--json", .. extras],
                    output,
                    new StringWriter(),
                    feedProbe: probe
                )
            );
            Assert.IsNull(probe.Request);
            Assert.DoesNotContain("private-project", output.ToString());
        }
    }

    [TestMethod]
    public async Task CurrentGenerationAndExactPrivacySchemaAreRequiredBeforeForwardingAnyObservation()
    {
        var request = Request();
        foreach (
            var mutate in new Action<JsonObject>[]
            {
                value => value["generation"] = "older-generation",
                value => value["rawUrl"] = "https://private.invalid/SECRET",
                value => value["reason"] = "SECRET C:\\private",
                value => value["restoreReadiness"] = "available",
                value =>
                    value["sources"] = new JsonArray(
                        new JsonObject
                        {
                            ["source"] = 1,
                            ["selection"] = "eligible",
                            ["kind"] = "http",
                            ["reachability"] = "reachable",
                            ["authentication"] = "notExercised",
                            ["release"] = "available",
                            ["reason"] = "none",
                        }
                    ),
            }
        )
        {
            var value = JsonNode.Parse(Serialize(Observed(request)))!.AsObject();
            mutate(value);
            var report = await FeedDoctorClient.RunAsync(
                request,
                new Probe(_ => new(0, value.ToJsonString()))
            );
            Assert.AreEqual("unavailable", report.Status);
            Assert.AreEqual("invocation-failed", report.Reason);
            Assert.IsEmpty(report.Sources);
            Assert.DoesNotContain("SECRET", Serialize(report));
        }
        var wrongExit = await FeedDoctorClient.RunAsync(
            request,
            new Probe(_ => new(1, Serialize(Observed(request))))
        );
        Assert.AreEqual("unavailable", wrongExit.Status);
    }

    [TestMethod]
    public async Task OnlineNegativeObservationIsNotAClientFailureAndNeverClaimsPrivateAvailability()
    {
        var request = Request() with { Online = true };
        var negative = Observed(request) with
        {
            Sources =
            [
                new(1, "eligible", "http", "reachable", "authRequired", "unknown", "http-status"),
            ],
        };
        var report = await FeedDoctorClient.RunAsync(
            request,
            new Probe(_ => new(0, Serialize(negative)))
        );
        Assert.AreEqual("observed", report.Status);
        Assert.AreEqual("authRequired", report.Sources.Single().Authentication);
        Assert.AreEqual("notChecked", report.PrivateAvailability);
        Assert.AreEqual("notChecked", report.ConfiguredAuthentication);
    }

    [TestMethod]
    public async Task InvalidInputsNeverStartTheAdapterAndCancelledLateSuccessIsDiscarded()
    {
        var request = Request();
        var probe = new Probe(value => new(0, Serialize(Observed(value))));
        var invalid = await FeedDoctorClient.RunAsync(
            request with
            {
                Version = "[1.0,2.0)",
            },
            probe
        );
        Assert.AreEqual("invalid-request", invalid.Status);
        Assert.IsNull(probe.Request);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            FeedDoctorClient.RunAsync(
                request,
                new Probe(value =>
                {
                    cancel.Cancel();
                    return new(0, Serialize(Observed(value)));
                }),
                cancel.Token
            )
        );
        using var unconfirmedCancel = new CancellationTokenSource();
        var unconfirmed = await FeedDoctorClient.RunAsync(
            request,
            new Probe(_ =>
            {
                unconfirmedCancel.Cancel();
                return new(1, "", false);
            }),
            unconfirmedCancel.Token
        );
        Assert.AreEqual("unavailable", unconfirmed.Status);
        Assert.AreEqual("invocation-failed", unconfirmed.Reason);
        Assert.IsTrue(Path.IsPathFullyQualified(FeedDoctorClient.PayloadPath));
        Assert.AreEqual(
            Path.Combine(AppContext.BaseDirectory, "nuget", "Lucent.Tools.NuGet.dll"),
            FeedDoctorClient.PayloadPath
        );
    }

    private static FeedDoctorRequest Request() =>
        new(Path.GetTempPath(), "Lucent.Core", "1.0.0", false, "current-generation");

    private static FeedDoctorReport Observed(FeedDoctorRequest request) =>
        new(
            1,
            "nuget-feed-doctor",
            request.Online ? "online-observation" : "effective-configuration",
            request.Generation,
            "observed",
            [],
            "none"
        );

    private static string Serialize(FeedDoctorReport value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private sealed class Probe(Func<FeedDoctorRequest, FeedDoctorProcessResult> run)
        : IFeedDoctorProbe
    {
        public FeedDoctorRequest? Request { get; private set; }

        public Task<FeedDoctorProcessResult> RunAsync(
            FeedDoctorRequest request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            return Task.FromResult(run(request));
        }
    }
}
