param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-11", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
if (-not $NoPublish) {
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$virtualization = & $exe --virtualization-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $virtualization.Ok -or -not $virtualization.BoundedRealizedCount -or -not $virtualization.ScopeInputFocusCaptureSceneSemanticsReleased) { throw 'Virtualization proof failed.' }
$structural = & $exe --structural-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $structural.Ok) { throw 'Structural regression failed.' }
$reactive = & $exe --reactive-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $reactive.Ok) { throw 'Reactive regression failed.' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$proof = [ordered]@{ ok = $true; nativeAot = $true; virtualization = $virtualization; regressions = [ordered]@{ structural = $structural; reactive = $reactive }; limits = @('fixed row height only; variable-height virtualization is excluded', 'two rows of overscan on each side', 'managed-memory result is coarse evidence; issue #15 owns the final budget') }
$proof | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 7 -Compress
