param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-23", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
$native = Join-Path $OutputDirectory native

if ($NoPublish) { throw '-NoPublish cannot produce final issue-23 evidence.' }

git -C "$PSScriptRoot/../.." diff --quiet -- experiments/native-stack
if ($LASTEXITCODE -ne 0) { throw 'Final issue-23 evidence requires a clean tracked experiment tree.' }
git -C "$PSScriptRoot/../.." diff --cached --quiet -- experiments/native-stack
if ($LASTEXITCODE -ne 0) { throw 'Final issue-23 evidence requires an empty experiment index.' }

dotnet restore $project --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Remove-Item -Recurse -Force $OutputDirectory -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $native | Out-Null
$walkthrough = @(& $exe --issue-browser-walkthrough $native | ForEach-Object { $_ | ConvertFrom-Json })
if ($LASTEXITCODE -ne 0 -or $walkthrough.Count -ne 12 -or @($walkthrough | Where-Object { -not $_.pass }).Count -ne 0) { throw 'W1-W12 walkthrough failed.' }
$identity = Get-Content (Join-Path $native identity.json) -Raw | ConvertFrom-Json
$assignee = Get-Content (Join-Path $native assignee-filter.json) -Raw | ConvertFrom-Json
if ($identity.Realized -gt 21 -or -not $assignee.Pass -or $assignee.Observed -ne 1000) { throw 'Virtualization or assignee gate failed.' }

$forbidden = 'BrowserRenderer|SKCanvas|SkiaSharp|StableElement|Scene\.Upsert|new\s+Bounds|\.SetItems\(|\bSync\s*\('
$matches = @(Select-String -Path "$PSScriptRoot/NativeStackProbe/IssueBrowser.cs" -Pattern $forbidden)
if ($matches.Count -ne 0) { throw "Forbidden issue-browser wiring: $($matches.Line -join '; ')" }

$composition = & $exe --composition-self-check | ConvertFrom-Json
$style = & $exe --style-self-check | ConvertFrom-Json
$controls = & $exe --controls-self-check | ConvertFrom-Json
$reactive = & $exe --reactive-self-check | ConvertFrom-Json
$virtualization = & $exe --virtualization-self-check | ConvertFrom-Json
$structural = & $exe --structural-self-check | ConvertFrom-Json
$sceneDirectory = Join-Path ([IO.Path]::GetTempPath()) ('native-stack-issue23-scene-' + [Guid]::NewGuid())
try { $scene = & $exe --scene-self-check $sceneDirectory | ConvertFrom-Json }
finally { Remove-Item -Recurse -Force $sceneDirectory -ErrorAction SilentlyContinue }
if ($LASTEXITCODE -ne 0 -or -not $composition.Ok -or -not $style.Ok -or -not $controls.Ok -or -not $reactive.Ok -or -not $virtualization.Ok -or -not $structural.Ok -or -not $scene.Ok) { throw 'Milestone 2A regression failed.' }

git -C "$PSScriptRoot/../.." diff --check
if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
$head = (git -C "$PSScriptRoot/../.." rev-parse HEAD).Trim()
function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($Path))) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
$artifacts = Get-ChildItem $native -File | Sort-Object Name | ForEach-Object { [ordered]@{ name = $_.Name; sha256 = Get-Sha256 $_.FullName; bytes = $_.Length } }
$inputs = @(git -C "$PSScriptRoot/../.." ls-files --cached --others --exclude-standard -- experiments/native-stack | Where-Object { $_ -notmatch '/(bin|obj|evidence)/' } | Sort-Object | ForEach-Object {
    $path = Join-Path "$PSScriptRoot/../.." $_
    if (Test-Path $path -PathType Leaf) { [ordered]@{ path = $_; sha256 = Get-Sha256 $path } }
})
$manifestText = ($inputs | ForEach-Object { "$($_.path) $($_.sha256)" }) -join "`n"
$sha = [Security.Cryptography.SHA256]::Create()
try { $manifestHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($manifestText))) -replace '-', '').ToLowerInvariant() }
finally { $sha.Dispose() }
$proof = [ordered]@{
    ok = $true
    issue = 23
    nativeAot = $true
    source = [ordered]@{ head = $head; trackedAndIndexClean = $true; inputManifestSha256 = $manifestHash; inputCount = $inputs.Count; inputs = $inputs }
    publish = [ordered]@{
        rid = 'win-x64'
        configuration = 'Release'
        nativeAot = $true
        trimmed = $true
        executable = [ordered]@{ path = $exe; sha256 = Get-Sha256 $exe; bytes = (Get-Item $exe).Length }
        project = [ordered]@{ path = 'experiments/native-stack/NativeStackProbe/NativeStackProbe.csproj'; sha256 = Get-Sha256 $project }
        lock = [ordered]@{ path = 'experiments/native-stack/NativeStackProbe/packages.lock.json'; sha256 = Get-Sha256 "$PSScriptRoot/NativeStackProbe/packages.lock.json" }
    }
    walkthrough = [ordered]@{ passed = 12; failed = 0; realized = $identity.Realized; limit = 21; assigneeResults = $assignee.Observed }
    forbiddenMatches = 0
    dumps = [ordered]@{ tree = (Test-Path (Join-Path $native tree.txt)); layout = (Test-Path (Join-Path $native layout.txt)); style = (Test-Path (Join-Path $native style.txt)); semantics = (Test-Path (Join-Path $native semantics.json)) }
    captures = [ordered]@{ light = '1200x760'; dark = '1200x760' }
    regressions = [ordered]@{ composition = $composition.Ok; style = $style.Ok; controls = $controls.Ok; reactive = $reactive.Ok; virtualization = $virtualization.Ok; structural = $structural.Ok; scene = $scene.Ok }
    artifacts = $artifacts
    decision = 'proceed'
}
$proof | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputDirectory proof.json) -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 7 -Compress
