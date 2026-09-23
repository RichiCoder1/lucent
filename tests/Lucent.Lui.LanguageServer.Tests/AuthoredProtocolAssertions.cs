using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.LanguageServer.Tests;

internal readonly record struct AuthoredPosition(int Line, int Character);

internal readonly record struct AuthoredRange(AuthoredPosition Start, AuthoredPosition End);

internal readonly record struct ExpectedLocation(string Uri, AuthoredRange Range, string Excerpt);

internal readonly record struct ExpectedEdit(
    string Uri,
    AuthoredRange Range,
    string NewText,
    string Excerpt
);

internal sealed class AuthoredFixture
{
    private static readonly Regex marker = new(
        @"/\*<(?<close>/)?(?<name>[a-zA-Z0-9_.-]+)>\*/",
        RegexOptions.CultureInvariant
    );
    private readonly IReadOnlyDictionary<string, (int Start, int Length)> spans;

    private AuthoredFixture(
        string source,
        IReadOnlyDictionary<string, (int Start, int Length)> spans
    )
    {
        Source = source;
        this.spans = spans;
    }

    internal string Source { get; }

    internal static AuthoredFixture Parse(string markedSource)
    {
        ArgumentNullException.ThrowIfNull(markedSource);
        var source = new StringBuilder(markedSource.Length);
        var starts = new Dictionary<string, int>(StringComparer.Ordinal);
        var spans = new Dictionary<string, (int Start, int Length)>(StringComparer.Ordinal);
        var previous = 0;
        foreach (Match match in marker.Matches(markedSource))
        {
            source.Append(markedSource, previous, match.Index - previous);
            var name = match.Groups["name"].Value;
            if (!match.Groups["close"].Success)
            {
                if (starts.ContainsKey(name) || spans.ContainsKey(name))
                    throw new ArgumentException($"Authored marker '{name}' is duplicated.");
                starts.Add(name, source.Length);
            }
            else
            {
                if (!starts.Remove(name, out var start))
                    throw new ArgumentException($"Authored marker '{name}' has no opening marker.");
                spans.Add(name, (start, source.Length - start));
            }
            previous = match.Index + match.Length;
        }
        source.Append(markedSource, previous, markedSource.Length - previous);
        if (starts.Count != 0)
            throw new ArgumentException(
                "Unclosed authored markers: " + String.Join(", ", starts.Keys.Order())
            );
        return new AuthoredFixture(source.ToString(), spans);
    }

    internal AuthoredPosition Position(string name, int relativeOffset = 0)
    {
        var span = Span(name);
        if (relativeOffset < 0 || relativeOffset > span.Length)
            throw new ArgumentOutOfRangeException(nameof(relativeOffset));
        return Position(span.Start + relativeOffset);
    }

    internal ExpectedLocation Location(string uri, string name)
    {
        var span = Span(name);
        return new ExpectedLocation(uri, Range(span), Excerpt(span));
    }

    internal ExpectedEdit Edit(string uri, string name, string newText)
    {
        var span = Span(name);
        return new ExpectedEdit(uri, Range(span), newText, Excerpt(span));
    }

    private (int Start, int Length) Span(string name) =>
        spans.TryGetValue(name, out var span)
            ? span
            : throw new ArgumentException($"Authored marker '{name}' was not found.", nameof(name));

    private AuthoredRange Range((int Start, int Length) span) =>
        new(Position(span.Start), Position(span.Start + span.Length));

    private AuthoredPosition Position(int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
            if (Source[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        return new AuthoredPosition(line, offset - lineStart);
    }

    private string Excerpt((int Start, int Length) span)
    {
        var lineStart = span.Start;
        while (lineStart > 0 && Source[lineStart - 1] is not ('\r' or '\n'))
            lineStart--;
        var lineEnd = span.Start + span.Length;
        while (lineEnd < Source.Length && Source[lineEnd] is not ('\r' or '\n'))
            lineEnd++;
        return Source[lineStart..lineEnd];
    }
}

internal static class AuthoredProtocolAssertions
{
    internal static void Locations(JsonElement actual, params ExpectedLocation[] expected)
    {
        var actualLocations = actual.ValueKind switch
        {
            JsonValueKind.Array => actual.EnumerateArray().Select(ReadLocation).ToArray(),
            JsonValueKind.Object => [ReadLocation(actual)],
            JsonValueKind.Null => [],
            _ => throw new AssertFailedException(
                "Expected an LSP location or location array, got " + actual.ValueKind + "."
            ),
        };
        var duplicate = actualLocations
            .GroupBy(item => (item.Uri, item.Range))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            Assert.Fail(
                "Duplicate actual location: " + Format(duplicate.Key.Uri, duplicate.Key.Range)
            );

        var expectedKeys = expected.Select(item => (item.Uri, item.Range)).ToHashSet();
        var actualKeys = actualLocations.Select(item => (item.Uri, item.Range)).ToHashSet();
        var missing = expectedKeys.Except(actualKeys).ToArray();
        var unexpected = actualKeys.Except(expectedKeys).ToArray();
        Assert.AreEqual(
            0,
            missing.Length + unexpected.Length,
            Failure(
                "locations",
                missing.Select(item => Format(item.Uri, item.Range)),
                unexpected.Select(item => Format(item.Uri, item.Range)),
                expected.Select(item => item.Excerpt)
            )
        );
    }

    internal static void WorkspaceEdits(JsonElement actual, params ExpectedEdit[] expected)
    {
        Assert.AreEqual(JsonValueKind.Object, actual.ValueKind, actual.GetRawText());
        var workspaceProperties = actual.EnumerateObject().ToArray();
        if (workspaceProperties.Any(property => property.Name == "documentChanges"))
            Assert.Fail("Unexpected workspace edit payload properties: documentChanges");
        var changeProperties = workspaceProperties
            .Where(property => property.Name == "changes")
            .ToArray();
        Assert.AreEqual(
            1,
            changeProperties.Length,
            "A workspace edit must contain exactly one 'changes' property. " + actual.GetRawText()
        );
        var changes = changeProperties[0].Value;
        var properties = changes.EnumerateObject().ToArray();
        var duplicateDocument = properties
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateDocument is not null)
            Assert.Fail("Duplicate actual document edit key: " + duplicateDocument.Key);

        var expectedDocuments = expected.Select(item => item.Uri).ToHashSet(StringComparer.Ordinal);
        var actualDocuments = properties
            .Select(item => item.Name)
            .ToHashSet(StringComparer.Ordinal);
        var missingDocuments = expectedDocuments.Except(actualDocuments).ToArray();
        var unexpectedDocuments = actualDocuments.Except(expectedDocuments).ToArray();
        if (missingDocuments.Length != 0 || unexpectedDocuments.Length != 0)
            Assert.Fail(
                Failure(
                    "workspace edit documents",
                    missingDocuments,
                    unexpectedDocuments,
                    expected.Select(item => item.Excerpt)
                )
            );

        var actualEdits = properties
            .SelectMany(property =>
                property.Value.EnumerateArray().Select(edit => ReadEdit(property.Name, edit))
            )
            .ToArray();
        var duplicateRange = actualEdits
            .GroupBy(item => (item.Uri, item.Range))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRange is not null)
            Assert.Fail(
                "Duplicate actual edit range: "
                    + Format(duplicateRange.Key.Uri, duplicateRange.Key.Range)
            );

        var expectedKeys = expected
            .Select(item => (item.Uri, item.Range, item.NewText))
            .ToHashSet();
        var actualKeys = actualEdits
            .Select(item => (item.Uri, item.Range, item.NewText))
            .ToHashSet();
        var missing = expectedKeys.Except(actualKeys).ToArray();
        var unexpected = actualKeys.Except(expectedKeys).ToArray();
        Assert.AreEqual(
            0,
            missing.Length + unexpected.Length,
            Failure(
                "workspace edits",
                missing.Select(item => Format(item.Uri, item.Range) + " => " + item.NewText),
                unexpected.Select(item => Format(item.Uri, item.Range) + " => " + item.NewText),
                expected.Select(item => item.Excerpt)
            )
        );
    }

    internal static void CompletionContains(JsonElement actual, string label, int? kind = null)
    {
        Assert.IsTrue(
            actual
                .GetProperty("items")
                .EnumerateArray()
                .Any(item =>
                    item.GetProperty("label").GetString() == label
                    && (kind is null || item.GetProperty("kind").GetInt32() == kind)
                ),
            $"Completion did not contain '{label}'"
                + (kind is null ? "." : $" with kind {kind}.")
                + Environment.NewLine
                + actual.GetRawText()
        );
    }

    private static (string Uri, AuthoredRange Range) ReadLocation(JsonElement location) =>
        (
            location.GetProperty("uri").GetString()
                ?? throw new AssertFailedException("LSP location URI was null."),
            ReadRange(location.GetProperty("range"))
        );

    private static (string Uri, AuthoredRange Range, string NewText) ReadEdit(
        string uri,
        JsonElement edit
    ) =>
        (
            uri,
            ReadRange(edit.GetProperty("range")),
            edit.GetProperty("newText").GetString()
                ?? throw new AssertFailedException("LSP edit replacement text was null.")
        );

    private static AuthoredRange ReadRange(JsonElement range) =>
        new(ReadPosition(range.GetProperty("start")), ReadPosition(range.GetProperty("end")));

    private static AuthoredPosition ReadPosition(JsonElement position) =>
        new(position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());

    private static string Format(string uri, AuthoredRange range) =>
        $"{uri} ({range.Start.Line},{range.Start.Character})-({range.End.Line},{range.End.Character})";

    private static string Failure(
        string subject,
        IEnumerable<string> missing,
        IEnumerable<string> unexpected,
        IEnumerable<string> excerpts
    ) =>
        $"Exact {subject} differed."
        + Environment.NewLine
        + "Missing: "
        + String.Join(" | ", missing.DefaultIfEmpty("<none>"))
        + Environment.NewLine
        + "Unexpected: "
        + String.Join(" | ", unexpected.DefaultIfEmpty("<none>"))
        + Environment.NewLine
        + "Authored: "
        + String.Join(" | ", excerpts.Distinct(StringComparer.Ordinal));
}
