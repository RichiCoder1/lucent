using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Core;

namespace Lucent.IssueBrowser;

public sealed record BrowserIssue(
    int Number,
    string Title,
    string Status,
    string Assignee,
    string Labels,
    string Updated,
    string Body
);
