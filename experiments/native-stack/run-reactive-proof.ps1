param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-7", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
if (-not $NoPublish) {
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$reactive = & $exe --reactive-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $reactive.ok -or $reactive.unrelatedFrames -ne 0 -or -not $reactive.disposedAsyncCannotCommit -or -not $reactive.latestGenerationWins) { throw 'Reactive proof failed.' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$proof = [ordered]@{ ok = $true; nativeAot = $true; reactive = $reactive; limits = @('UI-thread graph only', '64 runtime reads per callback', 'manual UI-thread Drain for posted async commits', 'no compiler or .lui integration') }
$proof | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 5 -Compress
