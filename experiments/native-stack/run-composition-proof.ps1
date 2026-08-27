param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-20", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
$work = Join-Path ([IO.Path]::GetTempPath()) ('native-stack-composition-' + [Guid]::NewGuid())
try {
    if (-not $NoPublish) {
        dotnet restore $project --locked-mode
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $composition = & $exe --composition-self-check | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $composition.Ok -or -not $composition.ControlPressed -or -not $composition.DumpsMatch -or -not $composition.Released -or -not $composition.NativePresented -or $composition.RasterDifferencePixels -ne 0) { throw 'Composition proof failed.' }
    $structural = & $exe --structural-self-check | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $structural.Ok) { throw 'Structural regression failed.' }
    $scene = & $exe --scene-self-check (Join-Path $work scene) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $scene.Ok) { throw 'Scene regression failed.' }
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    $proof = [ordered]@{ ok = $true; nativeAot = $true; composition = $composition; regressions = [ordered]@{ structural = $structural.Ok; scene = $scene.Ok }; limits = @('For refresh is explicit; reactive collection binding is issue #21', 'style variants and complete behaviors are issue #22', 'issue-browser rewrite is issue #23') }
    $proof | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
    $proof | ConvertTo-Json -Depth 7 -Compress
}
finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
