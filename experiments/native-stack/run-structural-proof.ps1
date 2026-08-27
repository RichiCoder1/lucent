param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-8", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
$work = Join-Path ([IO.Path]::GetTempPath()) ("native-stack-structural-" + [Guid]::NewGuid())
try {
    if (-not $NoPublish) {
        dotnet restore $project --locked-mode
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $structural = & $exe --structural-self-check | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $structural.Ok -or -not $structural.EffectUnregistered -or -not $structural.SceneReleased -or -not $structural.SemanticReleased) { throw 'Structural self-check failed.' }
    $reactive = & $exe --reactive-self-check | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $reactive.Ok) { throw 'Reactive regression failed.' }
    $scene = & $exe --scene-self-check (Join-Path $work scene) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $scene.Ok) { throw 'Scene regression failed.' }
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    $proof = [ordered]@{ ok = $true; nativeAot = $true; structural = $structural; regressions = [ordered]@{ reactive = $reactive; scene = [ordered]@{ ok = $scene.Ok; dumpsMatch = $scene.DumpsMatch; rasterDifferencePixels = $scene.RasterDifferencePixels } }; limits = @('Show and keyed For own only their branches', 'pointer routing bubbles only', 'no general reconciliation or VDOM') }
    $proof | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
    $proof | ConvertTo-Json -Depth 7 -Compress
}
finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
