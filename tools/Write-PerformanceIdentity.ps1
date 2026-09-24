param(
    [Parameter(Mandatory)][string] $PublishDirectory,
    [Parameter(Mandatory)][string] $Executable,
    [string] $DotnetPath = 'dotnet'
)

# Called immediately after the Performance runner's Release/win-x64 NativeAOT publish.
# These are the caller's publish settings, not properties inferred from an arbitrary executable.
$ErrorActionPreference = 'Stop'
$directory = [IO.Path]::GetFullPath($PublishDirectory)
$app = Join-Path $directory $Executable
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw "Missing published binary: $app" }
$source = (& git rev-parse HEAD) -join ''
if ($LASTEXITCODE) { throw 'Could not identify the source revision.' }
$dirty = @(& git status --porcelain).Count -ne 0
if ($LASTEXITCODE) { throw 'Could not identify the source working state.' }
$sdk = (& $DotnetPath --version) -join ''
if ($LASTEXITCODE) { throw 'Could not identify the publishing SDK.' }
$identity = [ordered]@{
    schemaVersion = 1
    executable = $Executable
    appSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
    configuration = 'Release'
    execution = 'NativeAOT'
    targetFramework = 'net10.0-windows10.0.26100.0'
    runtimeIdentifier = 'win-x64'
    sdk = $sdk
    sourceRevision = $source
    sourceDirty = $dirty
    publishedUtc = [DateTime]::UtcNow.ToString('O')
    binaries = @(Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Extension -in '.dll', '.exe' } | Sort-Object Name | ForEach-Object {
        [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$app.benchmark.json" -Encoding utf8
