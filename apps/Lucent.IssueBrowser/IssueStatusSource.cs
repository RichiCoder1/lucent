using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Core;

namespace Lucent.IssueBrowser;

public enum IssueDensity { Comfortable, Compact }

/// <summary>Application-owned save results; transport policy is deliberately not a Core concern.</summary>
public abstract record IssueStatusSaveOutcome
{
    public sealed record Saved : IssueStatusSaveOutcome;
    public sealed record Rejected(string Reason) : IssueStatusSaveOutcome;
    public sealed record TransientFailure(string Reason) : IssueStatusSaveOutcome;
}

public interface IIssueStatusSource
{
    Task<IssueStatusSaveOutcome> SaveAsync(int issueNumber, string status, CancellationToken cancellationToken);
}

/// <summary>Ordinary offline source: each deterministic branch keeps the reference app useful without test startup modes.</summary>
public sealed class FixtureIssueStatusSource : IIssueStatusSource
{
    private readonly HashSet<int> _transientFailures = [];

    public Task<IssueStatusSaveOutcome> SaveAsync(int issueNumber, string status, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IssueStatusSaveOutcome outcome = (issueNumber % 3) switch
        {
            0 => new IssueStatusSaveOutcome.Rejected("Fixture policy rejected this change."),
            1 when _transientFailures.Add(issueNumber) => new IssueStatusSaveOutcome.TransientFailure("Fixture source is temporarily unavailable."),
            _ => new IssueStatusSaveOutcome.Saved()
        };
        return Task.FromResult(outcome);
    }
}
