param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-5")

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
$work = Join-Path ([IO.Path]::GetTempPath()) ("native-stack-layout-" + [Guid]::NewGuid())
function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($Path)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
try {
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $first = Join-Path $work first; $second = Join-Path $work second
    $one = & $exe --layout-self-check $first | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $one.ok -or -not $one.flexChecks -or -not $one.shapeChecks -or -not $one.cultureChecks -or -not $one.glyphsRendered) { throw 'First layout self-check failed.' }
    $two = & $exe --layout-self-check $second | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $two.ok -or -not $two.flexChecks -or -not $two.shapeChecks -or -not $two.cultureChecks -or -not $two.glyphsRendered) { throw 'Second layout self-check failed.' }
    $firstFiles = @(Get-ChildItem $first -File | Sort-Object Name); $secondFiles = @(Get-ChildItem $second -File | Sort-Object Name)
    $expected = @('layout-text.json', 'layout-text.png')
    if ($firstFiles.Count -ne 2 -or $secondFiles.Count -ne 2 -or (Compare-Object $expected $firstFiles.Name) -or (Compare-Object $firstFiles.Name $secondFiles.Name)) { throw 'Unexpected or non-deterministic artifact filename set.' }
    $hashes = @($firstFiles | ForEach-Object { $hash = Get-Sha256 $_.FullName; if ($hash -ne (Get-Sha256 (Join-Path $second $_.Name))) { throw "Non-deterministic artifact: $($_.Name)" }; [ordered]@{ name = $_.Name; sha256 = $hash } })
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null; Get-ChildItem $OutputDirectory -File -ErrorAction SilentlyContinue | Remove-Item -Force; Copy-Item "$first/*" $OutputDirectory
    $proof = [ordered]@{ ok = $true; deterministicRuns = 2; artifacts = $hashes; layout = $one }; $proof | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline; $proof | ConvertTo-Json -Depth 8 -Compress
}
finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
