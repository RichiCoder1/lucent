using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lucent.IssueBrowser;

/// <summary>Small GitHub REST shape adapter. Its caller supplies the transport, so production remains offline by default.</summary>
public sealed class GitHubIssueSource(HttpClient client)
{
    public async Task<IReadOnlyList<BrowserIssue>> LoadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/repos/RichiCoder1/lucent/issues?state=all&per_page=100"
        );
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
        request.Headers.UserAgent.ParseAdd("Lucent-IssueBrowser/0.1");
        using var response = await client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false)
        );
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("GitHub issues response must be an array.");
        return document
            .RootElement.EnumerateArray()
            .Where(issue => !issue.TryGetProperty("pull_request", out _))
            .Select(Read)
            .OrderByDescending(issue => issue.Number)
            .ToArray();
    }

    private static BrowserIssue Read(JsonElement issue)
    {
        var assignee =
            issue.TryGetProperty("assignee", out var owner)
            && owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty("login", out var login)
                ? login.GetString() ?? "unassigned"
                : "unassigned";
        var labels =
            issue.TryGetProperty("labels", out var labelsValue)
            && labelsValue.ValueKind == JsonValueKind.Array
                ? string.Join(
                    ", ",
                    labelsValue
                        .EnumerateArray()
                        .Select(label =>
                            label.TryGetProperty("name", out var name) ? name.GetString() : null
                        )
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                )
                : "none";
        return new(
            issue.GetProperty("number").GetInt32(),
            issue.GetProperty("title").GetString() ?? "Untitled",
            issue.GetProperty("state").GetString() ?? "open",
            assignee,
            labels,
            issue.GetProperty("updated_at").GetString() ?? "",
            issue.GetProperty("body").GetString() ?? ""
        );
    }
}
