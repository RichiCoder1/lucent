param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-9", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
if (-not $NoPublish) {
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$style = & $exe --style-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $style.Ok -or $style.IdleFrames -ne 0 -or -not $style.ClockDisposed -or -not $style.ReducedMidAnimation) { throw 'Style proof failed.' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$proof = [ordered]@{ ok = $true; nativeAot = $true; style = $style; limits = @('immutable typed styles only', 'base -> selected -> focus-visible -> hover -> pressed -> invalid -> disabled', 'paint-only opacity/transform transition', 'no CSS, selectors, inheritance, layout animation, or general animation framework') }
$proof | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 5 -Compress
