param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-10", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
if (-not $NoPublish) {
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$controls = & $exe --controls-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $controls.Ok -or -not $controls.ValueSemantics -or -not $controls.Disposal) { throw 'Controls proof failed.' }
$text = & $exe --text-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $text.Ok) { throw 'Text regression failed.' }
$structural = & $exe --structural-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $structural.Ok) { throw 'Structural regression failed.' }
$style = & $exe --style-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $style.Ok) { throw 'Style regression failed.' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$proof = [ordered]@{ ok = $true; nativeAot = $true; controls = $controls; regressions = [ordered]@{ text = $text; structural = $structural; style = $style }; limits = @('single-line scalar editing only', 'semantic Value is portable; native child UIA provider is deferred', 'no rich text, multiline, or undo stack') }
$proof | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 7 -Compress
