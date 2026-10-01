param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutputRoot = (Join-Path ([IO.Path]::GetTempPath()) ('lucent-preview-build-check-' + [Guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$feedDirectory = (Resolve-Path -LiteralPath $Feed).Path
foreach ($package in @('lucent.core', 'lucent.lui.sdk')) {
    if (-not (Test-Path -LiteralPath (Join-Path $feedDirectory "$package.$Version.nupkg") -PathType Leaf)) {
        throw "Preview build verification requires exact candidate $package $Version."
    }
}
if (-not [IO.Path]::IsPathFullyQualified($OutputRoot) -or (Test-Path -LiteralPath $OutputRoot)) {
    throw 'Use a new absolute preview build evidence directory.'
}
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$project = Join-Path $root 'tests/Lucent.Preview.Build.Tests/Lucent.Preview.Build.Tests.csproj'
$previousFeed = $env:LUCENT_PREVIEW_BUILD_FEED
$previousVersion = $env:LUCENT_PREVIEW_BUILD_VERSION
try {
    $env:LUCENT_PREVIEW_BUILD_FEED = $feedDirectory
    $env:LUCENT_PREVIEW_BUILD_VERSION = $Version
    & dotnet restore $project --locked-mode "-p:ArtifactsPath=$OutputRoot/build" *> (Join-Path $OutputRoot 'restore.log')
    if ($LASTEXITCODE) { throw 'Preview build test restore failed.' }
    & dotnet build $project -c Release --no-restore "-p:ArtifactsPath=$OutputRoot/build" -p:UseSharedCompilation=false -nodeReuse:false *> (Join-Path $OutputRoot 'build.log')
    if ($LASTEXITCODE) { throw 'Preview build test compilation failed.' }
    & dotnet test --project $project -c Release --no-build --no-restore "-p:ArtifactsPath=$OutputRoot/build" `
        --minimum-expected-tests 1 --report-trx --report-trx-filename results.trx --results-directory (Join-Path $OutputRoot 'results') *> (Join-Path $OutputRoot 'test.log')
    if ($LASTEXITCODE) { throw 'Preview SDK build contracts failed.' }
    Write-Output "Preview SDK build contracts passed. Evidence: $OutputRoot"
}
catch {
    throw "Preview SDK build verification failed. Evidence: $OutputRoot. $($_.Exception.Message)"
}
finally {
    $env:LUCENT_PREVIEW_BUILD_FEED = $previousFeed
    $env:LUCENT_PREVIEW_BUILD_VERSION = $previousVersion
}
