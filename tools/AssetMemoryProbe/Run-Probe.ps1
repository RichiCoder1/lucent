[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')] [string] $Label = 'issue154',
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = [IO.Path]::GetFullPath((Join-Path $here '../..'))
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'artifacts/assets-146-memory/issue154'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$artifactPrefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $OutputDirectory.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must remain under repository artifacts: $OutputDirectory"
}

$project = Join-Path $here 'MemoryProbe.csproj'
$probeOutput = Join-Path $here 'bin/Release/net10.0'
$probe = Join-Path $probeOutput 'MemoryProbe.dll'
$fixtureInput = Join-Path $here 'fixtures'
$core = Join-Path $root 'src/Lucent.Core/bin/Release/net10.0/Lucent.Core.dll'
$renderer = Join-Path $root 'src/Lucent.Renderer.Skia/bin/Release/net10.0/Lucent.Renderer.Skia.dll'

foreach ($required in @($core, $renderer)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "A current Release framework binary is required before running the probe: $required"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

& $dotnet build $project -c Release --nologo
if ($LASTEXITCODE) {
    throw "Probe build failed: $LASTEXITCODE"
}

& $dotnet $probe generate
if ($LASTEXITCODE) {
    throw "Probe fixture generation failed: $LASTEXITCODE"
}

$cases = @(
    @{
        Name = 'progressive-rgb-444-32x23'
        File = 'progressive-rgb-444-32x23.jpg'
        Sha256 = '558567B52DD4BA6FBBC1CC92235D45B7E05E77A1B2AD6327C7939455BA930766'
    },
    @{
        Name = 'progressive-rgb-444-650x470'
        File = 'progressive-rgb-444-650x470.jpg'
        Sha256 = '8B331D7A4F00100333C767C1D76A1BA06B84A5194E99B08E0033A864293C431C'
    },
    @{
        Name = 'cmyk-600x397'
        File = 'cmyk-600x397.jpg'
        Sha256 = '7D19755D40A5DB1A59621740EDB04F0A6F76329448FF5B500B77B8F2D3854650'
    }
)

$results = @()
foreach ($case in $cases) {
    $path = Join-Path $fixtureInput $case.File
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Maintained fixture is missing: $path"
    }

    $actualSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actualSha256 -ne $case.Sha256) {
        throw "Fixture hash mismatch for $($case.Name): expected $($case.Sha256), got $actualSha256"
    }

    $json = & $dotnet $probe $path jpeg
    if ($LASTEXITCODE) {
        throw "Probe failed for $($case.Name): $LASTEXITCODE"
    }

    $result = ($json -join [Environment]::NewLine) | ConvertFrom-Json
    $result | Add-Member -NotePropertyName Fixture -NotePropertyValue $case.Name
    $result | Add-Member -NotePropertyName FixtureSha256 -NotePropertyValue $actualSha256
    $result | Add-Member -NotePropertyName RendererSha256 -NotePropertyValue (Get-FileHash -LiteralPath $renderer -Algorithm SHA256).Hash
    $result | Add-Member -NotePropertyName CoreSha256 -NotePropertyValue (Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash
    $results += $result
}

$summary = [ordered]@{
    Label = $Label
    TimestampUtc = [DateTime]::UtcNow.ToString('O')
    Runner = 'tools/AssetMemoryProbe/Run-Probe.ps1'
    Probe = [IO.Path]::GetRelativePath($root, $probe)
    PollIntervalMilliseconds = 1
    Warmup = 'Each MemoryProbe process decodes its generated 16x16 PNG before measuring the case.'
    Results = $results
    Notes = @(
        'Each case is a separate MemoryProbe process.',
        'Private bytes, working set and temporary reservations are sampled every 1 ms while loading.',
        'A sub-millisecond allocation peak can fall between samples; these measurements are evidence, not allocator bounds.',
        'The tool consumes already-built Release Core and Skia assemblies and does not build the shared solution.'
    )
}

$summaryPath = Join-Path $OutputDirectory ($Label + '-summary.json')
$jsonSummary = $summary | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($summaryPath, $jsonSummary, [Text.UTF8Encoding]::new($false))
$jsonSummary
