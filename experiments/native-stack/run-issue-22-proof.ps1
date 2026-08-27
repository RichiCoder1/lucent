param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-22", [switch]$NoPublish)

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
$style = & $exe --style-self-check | ConvertFrom-Json
$controls = & $exe --controls-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $composition.Ok -or -not $composition.ReactiveStyleFacets -or -not $style.Ok -or -not $style.FiniteSurface -or $style.IdleFrames -ne 0 -or -not $controls.Ok -or -not $controls.Disposal) { throw 'Issue #22 style and behavior proof failed.' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$proof = [ordered]@{ ok = $true; issue = 22; nativeAot = $true; composition = $composition; style = $style; controls = $controls; limits = @('finite typed records and ordered variants only', 'opacity/transform transitions only', 'no CSS, selectors, inheritance, property bags, arbitrary animation, or issue-browser rewrite') }
$proof | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 7 -Compress
