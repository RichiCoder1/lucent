$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$stack = $PSScriptRoot; $repo = Split-Path -Parent (Split-Path -Parent $stack); $evidence = Join-Path $stack 'evidence/issue-12'
function Run([string]$command, [string[]]$arguments) { & $command @arguments; if ($LASTEXITCODE -ne 0) { throw "$command failed ($LASTEXITCODE): $($arguments -join ' ')" } }
function Hash([string]$path) { ([System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($path))) -replace '-','').ToLowerInvariant() }
Run dotnet @('restore', "$stack/NativeStackProbe/NativeStackProbe.csproj", '--locked-mode')
Run dotnet @('restore', "$stack/baseline/IssueBrowser.Avalonia/IssueBrowser.Avalonia.csproj", '--locked-mode')
Run dotnet @('build', "$stack/NativeStackProbe/NativeStackProbe.csproj", '-c', 'Release', '--no-restore', '/warnaserror')
Run dotnet @('publish', "$stack/NativeStackProbe/NativeStackProbe.csproj", '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishAot=true', '--no-restore', '/warnaserror')
$nativeExe = "$stack/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
Run $nativeExe @('--issue-browser-walkthrough', "$evidence/native")
Run dotnet @('build', "$stack/baseline/IssueBrowser.Avalonia/IssueBrowser.Avalonia.csproj", '-c', 'Release', '--no-restore', '/warnaserror')
Run dotnet @('run', '--project', "$stack/baseline/IssueBrowser.Avalonia/IssueBrowser.Avalonia.csproj", '-c', 'Release', '--no-build', '--', '--issue-browser-walkthrough', "$evidence/avalonia")
$expected = @('W1 cold-start','W2 search','W3 latest-query','W4 filters','W5 scroll','W6 selection','W7 filtered-selection','W8 title-edit','W9 failure-retry','W10 theme','W11 keyboard','W12 semantics')
foreach ($implementation in 'native', 'avalonia') {
  $records = @(Get-Content "$evidence/$implementation/walkthrough.jsonl" | ForEach-Object { $_ | ConvertFrom-Json })
  if ($records.Count -ne 12 -or ((@($records.step) -join "`n") -ne ($expected -join "`n")) -or @($records | Where-Object { $_.pass -ne $true }).Count) { throw "$implementation walkthrough is not the exact passing W1-W12 sequence" }
  $semantics = Get-Content "$evidence/$implementation/semantics.json" -Raw | ConvertFrom-Json
  if (@($semantics | Where-Object { -not $_.Role -or -not $_.Name }).Count -or -not @($semantics.Id).Contains('title')) { throw "$implementation semantic dump is incomplete" }
  foreach ($theme in 'light', 'dark') { $png = [IO.File]::ReadAllBytes("$evidence/$implementation/$theme.png"); if ($png.Length -lt 24 -or [BitConverter]::ToString($png[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A') { throw "$implementation $theme capture is invalid" }; $w = [BitConverter]::ToInt32([byte[]]@($png[19],$png[18],$png[17],$png[16]),0); $h = [BitConverter]::ToInt32([byte[]]@($png[23],$png[22],$png[21],$png[20]),0); if ($w -ne 1200 -or $h -ne 760) { throw "$implementation $theme capture is not 1200x760" } }
  $identity = Get-Content "$evidence/$implementation/identity.json" -Raw | ConvertFrom-Json
  if ($identity.rows -ne 333 -or $identity.theme -ne 'dark' -or $identity.query -ne 'auth' -or -not $identity.reducedMotion -or $identity.draft -ne 'draft') { throw "$implementation final walkthrough state is incomplete" }
}
$files = Get-ChildItem "$evidence" -File -Recurse | Where-Object { $_.Name -ne 'proof.json' } | ForEach-Object { [ordered]@{ path = $_.FullName.Substring($repo.Length + 1).Replace('\','/'); sha256 = Hash $_.FullName } }
$inputs = @(
  "$stack/GAUNTLET.md", "$stack/gauntlet/issues.seed.json", "$stack/run-issue-12-proof.ps1", "$stack/Directory.Packages.props",
  "$stack/NativeStackProbe/IssueBrowser.cs", "$stack/NativeStackProbe/Program.cs", "$stack/NativeStackProbe/NativeStackProbe.csproj", "$stack/NativeStackProbe/packages.lock.json",
  "$stack/baseline/IssueBrowser.Avalonia/Program.cs", "$stack/baseline/IssueBrowser.Avalonia/IssueBrowser.lui", "$stack/baseline/IssueBrowser.Avalonia/IssueBrowser.css", "$stack/baseline/IssueBrowser.Avalonia/IssueBrowser.Avalonia.csproj", "$stack/baseline/IssueBrowser.Avalonia/packages.lock.json"
) | ForEach-Object { [ordered]@{ path = $_.Substring($repo.Length + 1).Replace('\','/'); sha256 = Hash $_ } }
[ordered]@{ issue = 12; result = 'pass'; gauntletSha256 = Hash "$stack/GAUNTLET.md"; inputs = @($inputs); machine = @{ os = [Environment]::OSVersion.VersionString; framework = [Environment]::Version.ToString(); dotnet = (& dotnet --version) }; artifacts = @($files) } | ConvertTo-Json -Depth 6 | Set-Content "$evidence/proof.json" -Encoding UTF8
