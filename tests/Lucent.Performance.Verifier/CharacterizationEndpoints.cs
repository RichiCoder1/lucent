using System.Text.Json;

internal static class CharacterizationEndpoints
{
    internal static bool Valid(
        string scenario,
        int index,
        JsonElement endpoint,
        JsonElement? previous,
        JsonElement journal
    )
    {
        try
        {
            return scenario switch
            {
                "focus-traversal" => Focus(endpoint, previous, journal),
                "list-scroll" => Scroll(index, endpoint, previous),
                "selection-content" => Selection(index, endpoint),
                "resize-same-breakpoint" => Resize(index, endpoint, crossing: false),
                "resize-breakpoint-crossing" => Resize(index, endpoint, crossing: true),
                _ => false,
            };
        }
        catch (Exception error)
            when (error is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static bool Focus(JsonElement endpoint, JsonElement? previous, JsonElement journal)
    {
        var identity = endpoint.GetProperty("runtimeId").GetString();
        var prior =
            previous?.GetProperty("runtimeId").GetString()
            ?? journal
                .GetProperty("warmup")
                .GetProperty("focusedEndpoint")
                .GetProperty("runtimeId")
                .GetString();
        return !String.IsNullOrWhiteSpace(identity)
            && identity != prior
            && endpoint.GetProperty("processId").GetInt32()
                == journal.GetProperty("processId").GetInt32()
            && (
                !String.IsNullOrWhiteSpace(endpoint.GetProperty("name").GetString())
                || !String.IsNullOrWhiteSpace(endpoint.GetProperty("automationId").GetString())
            );
    }

    private static bool Scroll(int index, JsonElement endpoint, JsonElement? previous)
    {
        var expected = index < 250 ? (index + 1) * .4 : (499 - index) * .4;
        var percent = endpoint.GetProperty("scrollPercent").GetDouble();
        if (
            Math.Abs(percent - expected) > .01
            || previous is { } prior
                && Math.Abs(percent - prior.GetProperty("scrollPercent").GetDouble()) < .01
        )
            return false;
        var visible = endpoint
            .GetProperty("visibleIssueNumbers")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();
        if (
            visible.Length == 0
            || endpoint.GetProperty("firstVisibleIssueNumber").GetInt32() != visible[0]
            || endpoint.GetProperty("lastVisibleIssueNumber").GetInt32() != visible[^1]
        )
            return false;
        if (
            visible.Distinct().Count() != visible.Length
            || visible.Any(number => number < 1 || number > 10_000)
        )
            return false;
        if (
            previous is { } before
            && visible.SequenceEqual(
                before
                    .GetProperty("visibleIssueNumbers")
                    .EnumerateArray()
                    .Select(value => value.GetInt32())
            )
        )
            return false;
        return index switch
        {
            249 => visible.Length >= 2 && visible[^2] == 2 && visible[^1] == 1,
            499 => visible.Length >= 2 && visible[0] == 10_000 && visible[1] == 9_999,
            _ => true,
        };
    }

    private static bool Selection(int index, JsonElement endpoint)
    {
        var issue = index % 2 == 0 ? 9_999 : 10_000;
        return endpoint.GetProperty("selectedIssue").GetInt32() == issue
            && endpoint.GetProperty("detail").GetProperty("name").GetString() == $"ISSUE #{issue}"
            && (endpoint.GetProperty("selectedRow").GetProperty("name").GetString() ?? "").Contains(
                $"#{issue}",
                StringComparison.Ordinal
            );
    }

    private static bool Resize(int index, JsonElement endpoint, bool crossing)
    {
        var width = crossing ? (index % 2 == 0 ? 760 : 1120) : (index % 2 == 0 ? 1100 : 1120);
        var actual = endpoint.GetProperty("actual").GetProperty("clientWidthDip").GetDouble();
        var height = endpoint.GetProperty("actual").GetProperty("clientHeightDip").GetDouble();
        var wide = !crossing || index % 2 != 0;
        if (
            endpoint.GetProperty("requestedClientWidthDip").GetInt32() != width
            || Math.Abs(actual - width) > 2
            || Math.Abs(height - 760) > 2
            || Math.Abs(actual - endpoint.GetProperty("previousClientWidthDip").GetDouble()) < 1
        )
            return false;
        return endpoint.GetProperty("breakpoint").GetString() == (wide ? "wide" : "compact")
            && endpoint.GetProperty("splitterPresent").GetBoolean() == wide
            && (!crossing || endpoint.GetProperty("backButtonPresent").GetBoolean() != wide)
            && endpoint.GetProperty("detail").GetProperty("name").GetString() == "ISSUE #10000";
    }
}
