param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-21", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
if (-not $NoPublish) {
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$composition = & $exe --composition-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $composition.Ok -or -not $composition.ReactiveTextStable -or -not $composition.UnrelatedWriteIdle -or -not $composition.DisposedAsyncCannotUpdate -or -not $composition.KeyedStable) { throw 'Issue #21 composition proof failed.' }
$virtualization = & $exe --virtualization-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $virtualization.Ok -or -not $virtualization.TenThousandRows -or -not $virtualization.ComputedFiltering -or -not $virtualization.KeyedSelectionPreserved -or -not $virtualization.BoundedRealizedCount -or -not $virtualization.ScopeInputFocusCaptureSceneSemanticsReleased) { throw 'Issue #21 virtualization proof failed.' }
$reactive = & $exe --reactive-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $reactive.Ok -or $reactive.UnrelatedFrames -ne 0 -or -not $reactive.DisposedAsyncCannotCommit -or -not $reactive.LatestGenerationWins) { throw 'Issue #21 reactive regression failed.' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$proof = [ordered]@{ ok = $true; nativeAot = $true; composition = $composition; virtualization = $virtualization; reactive = $reactive; limits = @('fixed row height only; variable-height virtualization is excluded', 'style variants and complete behaviors are issue #22', 'issue-browser rewrite is issue #23') }
$proof | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 7 -Compress
