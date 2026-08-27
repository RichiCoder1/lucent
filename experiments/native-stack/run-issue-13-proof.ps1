$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$baseline = '3d0da02'
$frozenGauntletHash = '808268b07a0387c1ccb2eb95abc4685c3e0a2632d896279613b630c723f35a72'
$stack = $PSScriptRoot
$repo = Split-Path -Parent (Split-Path -Parent $stack)
$evidence = Join-Path $stack 'evidence/issue-13'
$native = Join-Path $stack 'NativeStackProbe/NativeStackProbe.csproj'
$avalonia = Join-Path $stack 'baseline/IssueBrowser.Avalonia/IssueBrowser.Avalonia.csproj'
$nativeFiles = @('experiments/native-stack/NativeStackProbe/IssueBrowser.cs', 'experiments/native-stack/NativeStackProbe/Program.cs')
$avaloniaFiles = @('experiments/native-stack/baseline/IssueBrowser.Avalonia/Program.cs', 'experiments/native-stack/baseline/IssueBrowser.Avalonia/IssueBrowser.lui', 'experiments/native-stack/baseline/IssueBrowser.Avalonia/StraightCSharpFilterBar.cs')
function Run([string]$command, [string[]]$arguments) { & $command @arguments; if ($LASTEXITCODE -ne 0) { throw "$command failed ($LASTEXITCODE): $($arguments -join ' ')" } }
function Lines([string]$path) { (@(Get-Content $path)).Count }
function BaselineLines([string]$path) { (@((& git show "$baseline`:$path"))).Count }
function Hash([string]$path) { ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($path))) -replace '-', '').ToLowerInvariant() }
function Changed([string]$path) {
  if ($path -eq 'experiments/native-stack/baseline/IssueBrowser.Avalonia/StraightCSharpFilterBar.cs') { return [ordered]@{ path = $path; before = 'absent'; after = "1-$(Lines (Join-Path $repo $path))" } }
  $diff = @(& git diff --unified=0 $baseline -- $path)
  return [ordered]@{ path = $path; hunks = @($diff | Where-Object { $_ -match '^@@' }) }
}
Run dotnet @('restore', $native, '--locked-mode')
Run dotnet @('restore', $avalonia, '--locked-mode')
if ((Hash (Join-Path $stack 'GAUNTLET.md')) -ne $frozenGauntletHash) { throw 'GAUNTLET.md changed from the frozen baseline' }
Run dotnet @('build', $native, '-c', 'Release', '--no-restore', '-p:TreatWarningsAsErrors=true')
Run dotnet @('publish', $native, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishAot=true', '--no-restore', '-p:TreatWarningsAsErrors=true')
Run dotnet @('build', $avalonia, '-c', 'Release', '--no-restore', '--no-dependencies', '-p:TreatWarningsAsErrors=true')
Remove-Item $evidence -Recurse -Force -ErrorAction Ignore
Run (Join-Path $stack 'NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe') @('--issue-browser-walkthrough', (Join-Path $evidence 'native'))
Run dotnet @('run', '--project', $avalonia, '-c', 'Release', '--no-build', '--', '--issue-browser-walkthrough', (Join-Path $evidence 'avalonia'))
$expected = @('W1 cold-start','W2 search','W3 latest-query','W4 filters','W5 scroll','W6 selection','W7 filtered-selection','W8 title-edit','W9 failure-retry','W10 theme','W11 keyboard','W12 semantics')
foreach ($implementation in 'native', 'avalonia') {
  $path = Join-Path $evidence $implementation
  $records = @(Get-Content (Join-Path $path 'walkthrough.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
  if ($records.Count -ne 12 -or ((@($records.step) -join "`n") -ne ($expected -join "`n")) -or @($records | Where-Object { $_.pass -ne $true }).Count) { throw "$implementation W1-W12 failed" }
  $assignee = Get-Content (Join-Path $path 'assignee-filter.json') -Raw | ConvertFrom-Json
  if (-not $assignee.pass -or $assignee.expected -ne 1000 -or $assignee.observed -ne 1000 -or $assignee.assignee -ne 'Ada') { throw "$implementation assignee filter is not exact" }
  $semantics = @(Get-Content (Join-Path $path 'semantics.json') -Raw | ConvertFrom-Json | ForEach-Object { $_ })
  if (@($semantics | Where-Object { -not $_.Role -or -not $_.Name }).Count -or -not @($semantics.Name).Contains('Ada')) { throw "$implementation semantics omit assignee" }
  foreach ($theme in 'light', 'dark') { $png = [IO.File]::ReadAllBytes((Join-Path $path "$theme.png")); if ($png.Length -lt 24 -or [BitConverter]::ToString($png[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A') { throw "$implementation $theme capture is invalid" }; $width = [BitConverter]::ToInt32([byte[]]@($png[19],$png[18],$png[17],$png[16]),0); $height = [BitConverter]::ToInt32([byte[]]@($png[23],$png[22],$png[21],$png[20]),0); if ($width -ne 1200 -or $height -ne 760) { throw "$implementation $theme capture is not 1200x760" } }
}
& git diff --check $baseline
if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed' }
$report = [ordered]@{
  issue = 13
  baselineCommit = $baseline
  gauntletSha256 = $frozenGauntletHash
  changedFilesAndLines = [ordered]@{ native = @($nativeFiles | ForEach-Object { Changed $_ }); avalonia = @($avaloniaFiles | ForEach-Object { Changed $_ }) }
  authoredLoc = [ordered]@{ native = [ordered]@{ before = (BaselineLines 'experiments/native-stack/NativeStackProbe/IssueBrowser.cs'); after = (Lines (Join-Path $repo 'experiments/native-stack/NativeStackProbe/IssueBrowser.cs')) }; avalonia = [ordered]@{ before = ((BaselineLines 'experiments/native-stack/baseline/IssueBrowser.Avalonia/Program.cs') + (BaselineLines 'experiments/native-stack/baseline/IssueBrowser.Avalonia/IssueBrowser.lui') + (BaselineLines 'experiments/native-stack/baseline/IssueBrowser.Avalonia/IssueBrowser.css')); after = ((Lines (Join-Path $repo 'experiments/native-stack/baseline/IssueBrowser.Avalonia/Program.cs')) + (Lines (Join-Path $repo 'experiments/native-stack/baseline/IssueBrowser.Avalonia/IssueBrowser.lui')) + (Lines (Join-Path $repo 'experiments/native-stack/baseline/IssueBrowser.Avalonia/IssueBrowser.css')) + (Lines (Join-Path $repo 'experiments/native-stack/baseline/IssueBrowser.Avalonia/StraightCSharpFilterBar.cs'))) } }
  imperativeUiSynchronizationCounts = [ordered]@{ native = [ordered]@{ before = 6; after = 7; basis = 'four/five semantic property assignments plus two manual list reconciliations' }; avalonia = [ordered]@{ before = 6; after = 6; basis = 'two notification helpers plus four manual collection reconciliations' } }
  filterBarSyntaxConfound = [ordered]@{ native = [ordered]@{ authoredLoc = 1; imperativeUiSynchronizationSites = 0 }; avaloniaLui = [ordered]@{ authoredLoc = 8; imperativeUiSynchronizationSites = 0 }; avaloniaStraightCSharp = [ordered]@{ authoredLoc = 33; imperativeUiSynchronizationSites = 7; compiledOnly = $true } }
  verification = [ordered]@{ w1ToW12 = 'pass'; assigneeFilter = 'Ada: 1000 exact rows on both implementations'; captures = 'light/dark PNG, 1200x760, signature checked'; nativeAot = 'pass'; diffCheck = 'pass' }
}
$report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'change-task.json') -Encoding UTF8
