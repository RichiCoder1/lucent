using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Core;

namespace Lucent.IssueBrowser;

/// <summary>Frozen, local issue data used by the reference application and its deterministic transport contract.</summary>
public static class IssueFixture
{
    public const string Seed = "lucent-issue-browser-v1";
    public const string Sha256 = "9f4a8b3ce5df46f73a4a3c662345b4dcd479557f37cc7a599d5bb6472b8b4efa";
    public const int TotalCount = 10_000;
    public const int OpenCount = 6_667;
    public const int ClosedCount = 3_333;
    private static readonly BrowserIssue[] Seeds =
    [
        new(36, "Native IME composition must cancel cleanly", "open", "marta", "input, accessibility", "2025-01-16", "Keep preedit ownership inside the input adapter."),
        new(35, "Wire retained scene focus invalidation", "closed", "devin", "render", "2025-01-15", "Refresh focus paint without rebuilding input ownership."),
        new(34, "Add high contrast status tokens", "open", "marta", "accessibility", "2025-01-14", "Make loading and error status visible in all appearance modes."),
        new(33, "Validate NativeAOT publishing surface", "open", "joel", "build", "2025-01-13", "Keep the reference application trim-safe."),
        new(32, "Route pointer capture across scroll bounds", "closed", "devin", "input", "2025-01-12", "Release capture when a row departs the retained scene."),
        new(31, "Expose semantic snapshots for diagnostics", "open", "marta", "accessibility", "2025-01-11", "Diagnostics must explain the current semantic tree."),
        new(30, "Make async issue source stale-safe", "open", "joel", "reactive", "2025-01-10", "Only the latest response may update browser state."),
        new(29, "Implement deterministic issue fixture", "closed", "marta", "test", "2025-01-09", "Freeze realistic issue data for offline verification."),
        new(28, "Handle scale transition resource lifetime", "open", "devin", "render", "2025-01-08", "Dispose superseded backing resources after presentation."),
        new(27, "Clarify control composition ownership", "closed", "joel", "docs", "2025-01-07", "Document content and behavior ownership boundaries."),
        new(26, "Stabilize keyboard list selection", "open", "marta", "input", "2025-01-06", "Keep selection coherent through keyed list updates."),
        new(25, "Add CI artifact verification", "open", "joel", "build", "2025-01-05", "Verify the published native application contents.")
    ];
    public static IReadOnlyList<BrowserIssue> Issues { get; } = CreateIssues();

    public static string Json => "[" + string.Join(',', Issues.Select(issue =>
        $"{{\"number\":{issue.Number},\"title\":\"{issue.Title}\",\"state\":\"{issue.Status}\",\"assignee\":{{\"login\":\"{issue.Assignee}\"}},\"labels\":[{string.Join(',', issue.Labels.Split(", ", StringSplitOptions.None).Select(label => $"{{\"name\":\"{label}\"}}"))}],\"updated_at\":\"{issue.Updated}T00:00:00Z\",\"body\":\"{issue.Body}\"}}")) + "]";

    public static void AssertIntegrity()
    {
        var canonical = Seed + "\n" + string.Join('\n', Issues.Select(issue => string.Join('|', issue.Number, issue.Title, issue.Status, issue.Assignee, issue.Labels, issue.Updated, issue.Body)));
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        if (Issues.Count != TotalCount || Issues.Count(issue => issue.Status == "open") != OpenCount || Issues.Count(issue => issue.Status == "closed") != ClosedCount || actual != Sha256)
            throw new InvalidOperationException("The frozen issue fixture identity or counts changed.");
    }

    private static IReadOnlyList<BrowserIssue> CreateIssues()
    {
        var issues = new List<BrowserIssue>(TotalCount);
        var generated = 0;
        for (var number = TotalCount; number >= 37; number--)
        {
            var seed = Seeds[generated++ % Seeds.Length];
            issues.Add(seed with { Number = number, Title = seed.Title + " (issue #" + number + ")", Body = seed.Body + " Deterministic fixture issue #" + number + "." });
        }
        for (var number = 24; number >= 1; number--)
        {
            var seed = Seeds[generated++ % Seeds.Length];
            issues.Add(seed with { Number = number, Title = seed.Title + " (issue #" + number + ")", Body = seed.Body + " Deterministic fixture issue #" + number + "." });
        }
        issues.AddRange(Seeds);
        return issues.AsReadOnly();
    }
}
