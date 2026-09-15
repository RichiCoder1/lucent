using Fixture;

var json = Application.Create();
if (json != "{\"Name\":\"Ada\"}")
    throw new InvalidOperationException($"Unexpected JSON: {json}");
if (PreparedEnvironment.Flavor != "outer-evaluated")
    throw new InvalidOperationException($"Missing evaluated option: {PreparedEnvironment.Flavor}");
if (!PreparedEnvironment.Defines.Contains("PREPARED_FIXTURE", StringComparison.Ordinal))
    throw new InvalidOperationException($"Missing define: {PreparedEnvironment.Defines}");
if (PreparedEnvironment.Runtime != "win-x64")
    throw new InvalidOperationException(
        $"Missing runtime identifier: {PreparedEnvironment.Runtime}"
    );
if (ReadArbitrary() != "alpha")
    throw new InvalidOperationException($"Missing arbitrary global property: {ReadArbitrary()}");
if (!PreparedEnvironment.Additional.Contains("Observed.input", StringComparison.Ordinal))
    throw new InvalidOperationException(
        $"Missing AdditionalFile: {PreparedEnvironment.Additional}"
    );
if (SupportMarker.Value != "project-reference")
    throw new InvalidOperationException("Project graph reference was not preserved.");
Console.WriteLine(json);

static string ReadArbitrary() => PreparedEnvironment.Arbitrary;
