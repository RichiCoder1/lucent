param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-4", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
$work = Join-Path ([IO.Path]::GetTempPath()) ("native-stack-scene-" + [Guid]::NewGuid())
function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($Path)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
try {
    if (-not $NoPublish) {
        dotnet restore $project --locked-mode
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $first = Join-Path $work 'first'
    $second = Join-Path $work 'second'
    $one = & $exe --scene-self-check $first | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $one.ok) { throw 'First scene self-check failed.' }
    $two = & $exe --scene-self-check $second | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $two.ok) { throw 'Second scene self-check failed.' }
    $firstFiles = @(Get-ChildItem $first -File | Sort-Object Name)
    $secondFiles = @(Get-ChildItem $second -File | Sort-Object Name)
    $expected = @('elements.json', 'headless-input.png', 'layout.json', 'native-framebuffer.png', 'semantics.json', 'style.json')
    if ($firstFiles.Count -ne 6 -or $secondFiles.Count -ne 6 -or (Compare-Object $expected $firstFiles.Name) -or (Compare-Object $expected $secondFiles.Name) -or (Compare-Object $firstFiles.Name $secondFiles.Name)) { throw 'Unexpected or non-deterministic artifact filename set.' }
    $hashes = @($firstFiles | ForEach-Object {
        $other = Join-Path $second $_.Name
        $hash = Get-Sha256 $_.FullName
        if ($hash -ne (Get-Sha256 $other)) { throw "Non-deterministic artifact: $($_.Name)" }
        [ordered]@{ name = $_.Name; sha256 = $hash }
    })
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    Get-ChildItem $OutputDirectory -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item "$first/*" $OutputDirectory
    $one.ArtifactDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $proof = [ordered]@{ ok = $true; rasterTolerance = 0; deterministicRuns = 2; artifacts = $hashes; scene = $one }
    $proof | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline
    $proof | ConvertTo-Json -Depth 8 -Compress
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
