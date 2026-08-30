param(
    [string] $EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/m6'),
    [ValidateSet('PreCommit', 'Candidate', 'Final')] [string] $Mode = 'PreCommit',
    [string] $ManualGateRecord,
    [string] $CleanMachineGateRecord
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$baselinePath = Join-Path $root 'docs/M6-BASELINE.json'
$verifierProject = Join-Path $root 'tests/Lucent.M6.Verifier/Lucent.M6.Verifier.csproj'
$summaryPath = Join-Path $root 'docs/M6-AUTOMATED-SUMMARY.md'

function Assert-Exact($Actual, $Expected, [string] $Name) {
    if ($Expected -is [System.Management.Automation.PSCustomObject]) {
        if ($Actual -isnot [System.Management.Automation.PSCustomObject]) { throw "$Name has the wrong type." }
        $actualNames = @($Actual.PSObject.Properties.Name); $expectedNames = @($Expected.PSObject.Properties.Name)
        Assert-PropertyNames $actualNames $expectedNames $Name
        foreach ($property in $expectedNames) { Assert-Exact $Actual.$property $Expected.$property "$Name.$property" }
        return
    }
    if ($Expected -is [System.Array]) {
        if ($Actual -isnot [System.Array] -or $Actual.Count -ne $Expected.Count) { throw "$Name has the wrong array length." }
        for ($index = 0; $index -lt $Expected.Count; $index++) { Assert-Exact $Actual[$index] $Expected[$index] "$Name[$index]" }
        return
    }
    if ($null -eq $Actual -or $Actual.GetType() -ne $Expected.GetType()) { throw "$Name has the wrong scalar type." }
    if ($Expected -is [string]) {
        if (-not [string]::Equals($Actual, $Expected, [StringComparison]::Ordinal)) { throw "$Name changed from its frozen value." }
    }
    elseif (-not [object]::Equals($Actual, $Expected)) { throw "$Name changed from its frozen value." }
}

function Assert-PropertyNames([string[]] $Actual, [string[]] $Expected, [string] $Name) {
    if ($Actual.Count -ne $Expected.Count) { throw "$Name has missing or undeclared fields." }
    foreach ($expectedName in $Expected) {
        $matches = @($Actual | Where-Object { [string]::Equals($_, $expectedName, [StringComparison]::Ordinal) })
        if ($matches.Count -ne 1) { throw "$Name has missing, undeclared, or wrongly-cased fields." }
    }
}

function Assert-Properties($Value, [string[]] $Expected, [string] $Name) {
    if ($Value -isnot [System.Management.Automation.PSCustomObject]) { throw "$Name has the wrong type." }
    Assert-PropertyNames @($Value.PSObject.Properties.Name) $Expected $Name
}

function Assert-ExactString($Value, [string] $Expected, [string] $Name) {
    if ($Value -isnot [string] -or -not [string]::Equals($Value, $Expected, [StringComparison]::Ordinal)) { throw "$Name must be the exact expected string." }
}

function Assert-TrueBoolean($Value, [string] $Name) {
    if ($Value -isnot [bool] -or $Value -ne $true) { throw "$Name must be Boolean true." }
}

function Assert-UtcTimestamp($Value, [string] $Name) {
    if ($Value -isnot [string] -or -not $Value.EndsWith('Z', [StringComparison]::Ordinal)) { throw "$Name must be a round-trip UTC timestamp." }
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact($Value, 'O', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed) -or $parsed.Offset -ne [TimeSpan]::Zero) { throw "$Name must be a round-trip UTC timestamp." }
}

function Get-Hashes([string] $directory) {
    $prefix = (Resolve-Path $directory).Path.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    @((Get-ChildItem $directory -Force -File -Recurse | Sort-Object FullName | ForEach-Object { [ordered]@{ path = $_.FullName.Substring($prefix.Length).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }))
}

function Get-SourceIdentity {
    $status = @(& git -C $root status --porcelain=v1)
    $patch = (& git -C $root diff --no-ext-diff --binary HEAD | Out-String)
    $untracked = @(& git -C $root ls-files --others --exclude-standard | Sort-Object | ForEach-Object {
        $path = Join-Path $root $_
        [ordered]@{ path = $_; sha256 = if (Test-Path $path -PathType Leaf) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'non-file' } }
    })
    $material = (([ordered]@{ status = $status; patch = $patch; untracked = $untracked } | ConvertTo-Json -Depth 8 -Compress))
    [ordered]@{
        commit = (& git -C $root rev-parse HEAD).Trim(); tree = (& git -C $root rev-parse 'HEAD^{tree}').Trim(); branch = (& git -C $root branch --show-current).Trim()
        status = $status; workingTreeSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($material))).ToLowerInvariant(); untracked = $untracked
    }
}

$expectedBaseline = @'
{"schema":1,"frozenBeforeMeasurement":true,"samplesPerCorpus":500,"latencyMilliseconds":{"p95Maximum":16.7,"p99Maximum":33.3,"rendererP95Maximum":8.3},"idleSeconds":10,"virtualization":{"rows":10000,"visibleRows":2,"maximumRealizedRows":6},"managed":{"cycles":20,"liveGrowthBytesMaximum":16777216,"gcProcedure":"create/dispose one full-list warmup, then full compacting collect, wait for finalizers, full compacting collect before and after cycles"},"postGcResources":{"skiaSurfacesMaximum":1,"skiaTextBlobsMaximum":0,"sdlTexturesMaximum":1,"uiaProvidersMaximum":18,"nativeHandleGrowthMaximum":128,"rationale":"The presenter has one persistent surface/texture pair; paint blobs are scoped per draw; the existing UIA proof bounds the virtual provider cache at 18. The handle delta is deliberately conservative for stable process/window bookkeeping."},"warmup":"eight keyboard focus transitions and one resize before either corpus","vsync":"SDL_SetRenderVSync(renderer, 1)","clock":"host Stopwatch timestamp immediately before SDL input/window dispatch through the timestamp immediately after SDL_RenderPresent returns","tags":["lucent.operation=input","lucent.operation=resize","lucent.operation=startup","lucent.operation=other"]}
'@ | ConvertFrom-Json
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json
Assert-Exact $baseline $expectedBaseline 'M6 baseline'

if ($Mode -eq 'Candidate') {
    $candidateSource = Get-SourceIdentity
    if ($candidateSource.status.Count -ne 0) { throw 'Candidate M6 requires a clean source tree.' }
    & (Join-Path $PSScriptRoot 'Verify-M1.ps1'); if ($LASTEXITCODE) { exit $LASTEXITCODE }
    $afterCandidate = Get-SourceIdentity
    if ($afterCandidate.status.Count -ne 0) { throw 'Candidate M6 requires Verify-M1 to leave a clean source tree.' }
}

if ($Mode -eq 'Final') {
    $source = Get-SourceIdentity
    if ($source.status.Count -ne 0) { throw 'Final M6 requires a clean source tree.' }

    $evidencePath = Join-Path $EvidenceDirectory 'm6-evidence.json'; $zip = Join-Path $EvidenceDirectory 'lucent-win-x64.zip'
    if (-not (Test-Path $evidencePath -PathType Leaf) -or -not (Test-Path $zip -PathType Leaf)) { throw 'Final M6 requires existing candidate evidence and package.' }
    $candidateEvidenceSha256 = (Get-FileHash $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $candidate = Get-Content $evidencePath -Raw | ConvertFrom-Json
    Assert-Properties $candidate @('acceptance','baseline','environment','gates','hashes','informational','issue','manualNotRun','mode','operations','packaging','pending','schema') 'M6 candidate evidence'
    if ($candidate.schema -isnot [long] -or $candidate.schema -ne 2L -or $candidate.issue -isnot [long] -or $candidate.issue -ne 38L) { throw 'M6 candidate evidence has the wrong schema or issue type/value.' }
    Assert-ExactString $candidate.mode 'Candidate' 'candidate.mode'; Assert-ExactString $candidate.acceptance 'clean-automated-candidate' 'candidate.acceptance'
    if ($null -ne $candidate.gates) { throw 'Candidate M6 evidence must not contain gate records.' }
    $expectedPending = @('manual visual review', 'manual Accessibility Insights/Narrator walkthrough', 'manual real Japanese IME smoke', 'clean-machine NativeAOT package run')
    Assert-Exact $candidate.pending $expectedPending 'candidate.pending'; Assert-Exact $candidate.manualNotRun $expectedPending 'candidate.manualNotRun'
    Assert-Exact $candidate.baseline $baseline 'candidate.baseline'
    Assert-Properties $candidate.hashes @('baselineSha256','verifierSha256') 'candidate.hashes'
    Assert-ExactString $candidate.hashes.baselineSha256 (Get-FileHash $baselinePath -Algorithm SHA256).Hash.ToLowerInvariant() 'candidate.hashes.baselineSha256'
    Assert-ExactString $candidate.hashes.verifierSha256 (Get-FileHash (Join-Path $root 'tests/Lucent.M6.Verifier/Program.cs') -Algorithm SHA256).Hash.ToLowerInvariant() 'candidate.hashes.verifierSha256'
    Assert-Properties $candidate.environment @('build','cpu','display','gpu','logicalProcessors','machine','os','powerPlan','ramBytes','recordedAtUtc','runtime','scaleAuthority','source') 'candidate.environment'
    Assert-Properties $candidate.environment.source @('branch','commit','status','tree','untracked','workingTreeSha256') 'candidate.environment.source'
    if ($candidate.environment.source.status.Count -ne 0) { throw 'Candidate evidence source tree is not clean.' }
    Assert-ExactString $candidate.environment.source.commit $source.commit 'candidate.source.commit'; Assert-ExactString $candidate.environment.source.tree $source.tree 'candidate.source.tree'
    Assert-Properties $candidate.operations @('corpusDelimiters','idleFrames','input','managed','ok','resize','resources','virtualization') 'candidate.operations'
    Assert-TrueBoolean $candidate.operations.ok 'candidate.operations.ok'
    foreach ($corpus in @('input','resize')) {
        Assert-Properties $candidate.operations.$corpus @('p95Ms','p99Ms','rendererP95Ms','samples') "candidate.operations.$corpus"
        Assert-Properties $candidate.operations.corpusDelimiters.$corpus @('firstFrame','lastFrame','samples') "candidate.operations.corpusDelimiters.$corpus"
        if ($candidate.operations.corpusDelimiters.$corpus.samples -ne 500 -or $candidate.operations.corpusDelimiters.$corpus.firstFrame -le 0 -or $candidate.operations.corpusDelimiters.$corpus.lastFrame -lt $candidate.operations.corpusDelimiters.$corpus.firstFrame) { throw "Candidate $corpus corpus delimiters are invalid." }
    }
    if ($candidate.operations.input.samples -ne 500 -or $candidate.operations.resize.samples -ne 500 -or $candidate.operations.input.p95Ms -gt 16.7 -or $candidate.operations.resize.p95Ms -gt 16.7 -or $candidate.operations.input.p99Ms -gt 33.3 -or $candidate.operations.resize.p99Ms -gt 33.3 -or $candidate.operations.input.rendererP95Ms -gt 8.3 -or $candidate.operations.resize.rendererP95Ms -gt 8.3 -or $candidate.operations.idleFrames -ne 0) { throw 'Candidate operation evidence does not satisfy frozen latency/idle bounds.' }
    Assert-Properties $candidate.operations.virtualization @('realizedMaximum','sourceRows','visibleRowsMaximum') 'candidate.operations.virtualization'
    Assert-Properties $candidate.operations.managed @('baselineBytes','cycles','growthBytes','limitBytes','peakBytes','postGcBytes') 'candidate.operations.managed'
    if ($candidate.operations.virtualization.sourceRows -ne 10000 -or $candidate.operations.virtualization.realizedMaximum -gt 6 -or $candidate.operations.managed.cycles -ne 20 -or $candidate.operations.managed.growthBytes -gt 16777216) { throw 'Candidate operation evidence does not satisfy frozen virtualization/memory bounds.' }
    $resources = $candidate.operations.resources
    Assert-Properties $resources @('handles','lifecycle','surfaceMaximum','textBlobMaximum','textureMaximum','uiaProviderMaximum') 'candidate.operations.resources'
    if ($resources.surfaceMaximum -gt 1 -or $resources.textureMaximum -gt 1 -or $resources.textBlobMaximum -gt 0 -or $resources.uiaProviderMaximum -gt 18) { throw 'Candidate resource evidence exceeds frozen bounds.' }
    Assert-Properties $resources.handles @('baseline','final','finalDelta','peak','peakDelta') 'candidate.operations.resources.handles'
    if ($resources.handles.peakDelta -ne ($resources.handles.peak - $resources.handles.baseline) -or $resources.handles.finalDelta -ne ($resources.handles.final - $resources.handles.baseline) -or $resources.handles.peakDelta -gt 128 -or $resources.handles.finalDelta -gt 128) { throw 'Candidate handle evidence is inconsistent or exceeds frozen bounds.' }
    Assert-Properties $resources.lifecycle @('peak','post','pre') 'candidate.operations.resources.lifecycle'
    foreach ($phase in @('pre','post')) { Assert-Properties $resources.lifecycle.$phase @('handles','surfaces','textBlobs','textures','uiaProviders') "candidate.operations.resources.lifecycle.$phase" }
    Assert-Properties $resources.lifecycle.peak @('surfaces','textBlobs','textures','uiaProviders') 'candidate.operations.resources.lifecycle.peak'
    if ($resources.lifecycle.post.surfaces -ne 0 -or $resources.lifecycle.post.textures -ne 0 -or $resources.lifecycle.post.textBlobs -ne 0 -or $resources.lifecycle.post.uiaProviders -ne 0) { throw 'Candidate lifecycle resources did not return after disposal/GC.' }
    Assert-Properties $candidate.informational @('coldLaunchMilliseconds','publishBytes','testedWindow','workingSetBytes','zipBytes') 'candidate.informational'
    foreach ($value in @($candidate.informational.coldLaunchMilliseconds,$candidate.informational.publishBytes,$candidate.informational.workingSetBytes,$candidate.informational.zipBytes)) { if ($null -eq $value -or $value -le 0) { throw 'Candidate informational evidence is incomplete.' } }
    Assert-Properties $candidate.informational.testedWindow @('clientPixels','dpi','hwnd','monitor','monitorBounds','scale') 'candidate.informational.testedWindow'
    if ($candidate.informational.testedWindow.hwnd -isnot [string] -or [string]::IsNullOrWhiteSpace($candidate.informational.testedWindow.hwnd) -or $candidate.informational.testedWindow.monitor -isnot [string] -or [string]::IsNullOrWhiteSpace($candidate.informational.testedWindow.monitor) -or $candidate.informational.testedWindow.dpi -le 0 -or $candidate.informational.testedWindow.scale -le 0 -or $candidate.informational.testedWindow.clientPixels.Count -ne 2 -or $candidate.informational.testedWindow.monitorBounds.Count -ne 4) { throw 'Candidate tested-window evidence is incomplete.' }
    Assert-Properties $candidate.packaging @('copiedPublish','extractedChecksumsMatch','extractedPackageInventory','nativeAssetAndLicenseInventory','publishBytes','zip','zipBytes','zipSha256') 'candidate.packaging'
    Assert-TrueBoolean $candidate.packaging.copiedPublish 'candidate.packaging.copiedPublish'; Assert-TrueBoolean $candidate.packaging.extractedChecksumsMatch 'candidate.packaging.extractedChecksumsMatch'
    Assert-ExactString $candidate.packaging.zip 'lucent-win-x64.zip' 'candidate.packaging.zip'
    $packageSha256 = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-ExactString $candidate.packaging.zipSha256 $packageSha256 'candidate.packaging.zipSha256'
    foreach ($inventory in @(@{ value = $candidate.packaging.nativeAssetAndLicenseInventory; name = 'candidate.packaging.nativeAssetAndLicenseInventory' }, @{ value = $candidate.packaging.extractedPackageInventory; name = 'candidate.packaging.extractedPackageInventory' })) {
        if ($inventory.value -isnot [array] -or $inventory.value.Count -eq 0) { throw "$($inventory.name) must be a non-empty recursive inventory." }
        foreach ($entry in $inventory.value) {
            Assert-Properties $entry @('bytes','path','sha256') "$($inventory.name) entry"
            if ($entry.bytes -isnot [long] -or $entry.bytes -lt 0) { throw "$($inventory.name) entry.bytes must be a non-negative integer." }
            if ($entry.path -isnot [string] -or [string]::IsNullOrWhiteSpace($entry.path) -or $entry.sha256 -isnot [string]) { throw "$($inventory.name) has malformed recursive evidence." }
        }
    }
    if (-not [string]::Equals((ConvertTo-Json $candidate.packaging.nativeAssetAndLicenseInventory -Compress), (ConvertTo-Json $candidate.packaging.extractedPackageInventory -Compress), [StringComparison]::Ordinal)) { throw 'Candidate publish and extracted package inventories differ.' }
    $extract = Join-Path ([IO.Path]::GetTempPath()) ('lucent-m6-final-' + [Guid]::NewGuid().ToString('N')); New-Item $extract -ItemType Directory -Force | Out-Null
    try {
        Expand-Archive $zip -DestinationPath $extract
        $actualInventory = Get-Hashes $extract
        if (-not [string]::Equals((ConvertTo-Json $actualInventory -Compress), (ConvertTo-Json $candidate.packaging.extractedPackageInventory -Compress), [StringComparison]::Ordinal)) { throw 'Candidate package recursive checksum inventory is not reproducible.' }
    } finally { Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue }

    if (-not (Test-Path $ManualGateRecord -PathType Leaf) -or -not (Test-Path $CleanMachineGateRecord -PathType Leaf)) { throw 'Final M6 requires manual and clean-machine gate records.' }
    $manual = Get-Content $ManualGateRecord -Raw | ConvertFrom-Json; $clean = Get-Content $CleanMachineGateRecord -Raw | ConvertFrom-Json
    Assert-Properties $manual @('accessibility','candidateEvidenceSha256','ime','issue','packageSha256','recordedAtUtc','schema','sourceCommit','visual') 'Manual gate record'
    Assert-Properties $clean @('candidateEvidenceSha256','environment','inventoryPass','issue','launchPass','noDevelopmentSdk','packageSha256','recordedAtUtc','schema','sourceCommit') 'Clean-machine gate record'
    if ($manual.schema -isnot [long] -or $manual.schema -ne 1L -or $manual.issue -isnot [long] -or $manual.issue -ne 38L) { throw 'Manual gate record has the wrong schema or issue type/value.' }
    if ($clean.schema -isnot [long] -or $clean.schema -ne 1L -or $clean.issue -isnot [long] -or $clean.issue -ne 38L) { throw 'Clean-machine gate record has the wrong schema or issue type/value.' }
    Assert-ExactString $manual.sourceCommit $candidate.environment.source.commit 'manual.sourceCommit'; Assert-ExactString $manual.packageSha256 $packageSha256 'manual.packageSha256'; Assert-ExactString $manual.candidateEvidenceSha256 $candidateEvidenceSha256 'manual.candidateEvidenceSha256'
    foreach ($gate in @('visual','accessibility','ime')) { Assert-Properties $manual.$gate @('notes','pass') "manual.$gate"; Assert-TrueBoolean $manual.$gate.pass "manual.$gate.pass"; if ($manual.$gate.notes -isnot [string] -or [string]::IsNullOrWhiteSpace($manual.$gate.notes)) { throw "manual.$gate.notes must be a non-empty string." } }
    Assert-ExactString $clean.sourceCommit $candidate.environment.source.commit 'clean.sourceCommit'; Assert-ExactString $clean.packageSha256 $packageSha256 'clean.packageSha256'; Assert-ExactString $clean.candidateEvidenceSha256 $candidateEvidenceSha256 'clean.candidateEvidenceSha256'
    Assert-TrueBoolean $clean.launchPass 'clean.launchPass'; Assert-TrueBoolean $clean.inventoryPass 'clean.inventoryPass'; Assert-TrueBoolean $clean.noDevelopmentSdk 'clean.noDevelopmentSdk'
    if ($clean.environment -isnot [string] -or [string]::IsNullOrWhiteSpace($clean.environment)) { throw 'clean.environment must be a non-empty string.' }
    Assert-UtcTimestamp $manual.recordedAtUtc 'manual.recordedAtUtc'; Assert-UtcTimestamp $clean.recordedAtUtc 'clean.recordedAtUtc'
    [ordered]@{
        schema = 2; issue = 38; mode = 'Final'; acceptance = 'final-gates-recorded'; baseline = $candidate.baseline; hashes = $candidate.hashes; environment = $candidate.environment; operations = $candidate.operations; informational = $candidate.informational
        packaging = $candidate.packaging; gates = [ordered]@{ manual = $manual; manualSha256 = (Get-FileHash $ManualGateRecord -Algorithm SHA256).Hash.ToLowerInvariant(); cleanMachine = $clean; cleanMachineSha256 = (Get-FileHash $CleanMachineGateRecord -Algorithm SHA256).Hash.ToLowerInvariant() }; pending = @(); manualNotRun = @()
    } | ConvertTo-Json -Depth 10 -Compress | Set-Content $evidencePath
    @"
# M6 final summary

Mode: Final. Acceptance: final-gates-recorded. Evidence: ``artifacts/m6/m6-evidence.json``.

Candidate automated evidence and the exact package SHA-256 were verified without rebuilding, repackaging, or remeasuring; manual and clean-machine records are attached to the candidate.
"@ | Set-Content $summaryPath
    Write-Output "M6 final evidence: $evidencePath"
    exit 0
}

New-Item $EvidenceDirectory -ItemType Directory -Force | Out-Null
$evidencePath = Join-Path $EvidenceDirectory 'm6-evidence.json'
$source = Get-SourceIdentity
$computer = Get-CimInstance Win32_ComputerSystem; $os = Get-CimInstance Win32_OperatingSystem; $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$gpu = @(Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name })
$display = @(Get-CimInstance Win32_DesktopMonitor | ForEach-Object { [ordered]@{ name = $_.Name; width = $_.ScreenWidth; height = $_.ScreenHeight } })
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class M6WindowInfo {
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct MONITORINFOEX { public int cbSize; public int l,t,r,b; public int wl,wt,wr,wb; public int flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string device; }
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left,top,right,bottom; }
}
'@
function Get-WindowObservation([IntPtr] $Hwnd) {
    $monitor = [M6WindowInfo]::MonitorFromWindow($Hwnd, 2); $info = [M6WindowInfo+MONITORINFOEX]::new(); $info.cbSize = [Runtime.InteropServices.Marshal]::SizeOf([type][M6WindowInfo+MONITORINFOEX]); $rect = [M6WindowInfo+RECT]::new()
    if ($monitor -eq [IntPtr]::Zero -or -not [M6WindowInfo]::GetMonitorInfo($monitor, [ref]$info) -or -not [M6WindowInfo]::GetClientRect($Hwnd, [ref]$rect)) { throw 'Could not record tested HWND monitor/DPI.' }
    $dpi = [M6WindowInfo]::GetDpiForWindow($Hwnd); if ($dpi -eq 0) { throw 'GetDpiForWindow failed for tested HWND.' }
    [ordered]@{ hwnd = ('0x{0:X}' -f $Hwnd.ToInt64()); monitor = $info.device; monitorBounds = @($info.l, $info.t, $info.r, $info.b); dpi = $dpi; scale = $dpi / 96.0; clientPixels = @(($rect.right - $rect.left), ($rect.bottom - $rect.top)) }
}
$environment = [ordered]@{
    recordedAtUtc = [DateTime]::UtcNow.ToString('O'); machine = $env:COMPUTERNAME; cpu = $cpu.Name; logicalProcessors = $computer.NumberOfLogicalProcessors; ramBytes = [int64]$computer.TotalPhysicalMemory
    gpu = $gpu; display = $display; scaleAuthority = 'GetDpiForWindow / 96'; powerPlan = (powercfg /getactivescheme | Out-String).Trim(); os = "$($os.Caption) $($os.Version) build $($os.BuildNumber)"
    runtime = (& $dotnet --info | Out-String).Trim(); build = 'Release win-x64 self-contained NativeAOT warning-as-error'; source = $source
}
& $dotnet restore (Join-Path $root 'Lucent.slnx') --locked-mode; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet build (Join-Path $root 'Lucent.slnx') --no-restore -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
$appProject = Join-Path $root 'apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj'
& $dotnet publish $appProject -c Release -r win-x64 --self-contained true --no-restore -p:PublishAot=true -p:PublishTrimmed=true -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
$publish = Join-Path $root 'apps/Lucent.IssueBrowser/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish'
& (Join-Path $PSScriptRoot 'Verify-M0Assets.ps1') $publish; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'Verify-M0Assets.ps1') $publish -Negative; if ($LASTEXITCODE) { exit $LASTEXITCODE }
$copy = Join-Path $EvidenceDirectory 'publish-copy'; $zip = Join-Path $EvidenceDirectory 'lucent-win-x64.zip'; $unzipped = Join-Path $EvidenceDirectory 'publish-unzipped'
Remove-Item $copy, $zip, $unzipped -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $publish $copy -Recurse -Force; Compress-Archive -Path (Join-Path $copy '*') -DestinationPath $zip; Expand-Archive $zip -DestinationPath $unzipped
$copyHashes = Get-Hashes $copy; $zipHashes = Get-Hashes $unzipped
if (-not [string]::Equals(($copyHashes | ConvertTo-Json -Compress), ($zipHashes | ConvertTo-Json -Compress), [StringComparison]::Ordinal)) { throw 'Zip extraction checksum inventory differs from publish copy.' }
$zipSha256 = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$exe = Join-Path $unzipped 'Lucent.IssueBrowser.exe'
$launchWatch = [Diagnostics.Stopwatch]::StartNew(); $launch = Start-Process $exe -WorkingDirectory $unzipped -PassThru
try {
    do { Start-Sleep -Milliseconds 25; $launch.Refresh() } until ($launch.MainWindowHandle -ne 0 -or $launch.HasExited -or $launchWatch.ElapsedMilliseconds -ge 15000)
    if ($launch.HasExited -or $launch.MainWindowHandle -eq 0) { throw 'Copied/extracted publish did not expose an HWND.' }
$publishBytes = [long](($copyHashes | ForEach-Object { $_['bytes'] } | Measure-Object -Sum).Sum)
$informational = [ordered]@{ coldLaunchMilliseconds = $launchWatch.Elapsed.TotalMilliseconds; workingSetBytes = $launch.WorkingSet64; publishBytes = $publishBytes; zipBytes = (Get-Item $zip).Length; testedWindow = Get-WindowObservation $launch.MainWindowHandle }
    $null = $launch.CloseMainWindow(); if (-not $launch.WaitForExit(10000) -or $launch.ExitCode -ne 0) { throw 'Copied/extracted publish did not close normally.' }
} finally { if (-not $launch.HasExited) { $launch.Kill(); $launch.WaitForExit() }; $launch.Dispose() }
& $dotnet publish $verifierProject -c Release -r win-x64 --self-contained true --no-restore -p:PublishAot=true -p:PublishTrimmed=true -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
$verifierExe = Join-Path $root 'tests/Lucent.M6.Verifier/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/Lucent.M6.Verifier.exe'
if (-not (Test-Path $verifierExe -PathType Leaf)) { throw 'NativeAOT M6 verifier publish is missing.' }
$operationText = & $verifierExe --app $exe
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$operations = ($operationText | Select-Object -Last 1) | ConvertFrom-Json
$hashes = [ordered]@{ baselineSha256 = (Get-FileHash $baselinePath -Algorithm SHA256).Hash.ToLowerInvariant(); verifierSha256 = (Get-FileHash (Join-Path $root 'tests/Lucent.M6.Verifier/Program.cs') -Algorithm SHA256).Hash.ToLowerInvariant() }
$source = Get-SourceIdentity
$environment.source = $source
if ($Mode -eq 'Candidate') {
    if ($source.status.Count -ne 0 -or -not [string]::Equals($source.commit, $candidateSource.commit, [StringComparison]::Ordinal) -or -not [string]::Equals($source.tree, $candidateSource.tree, [StringComparison]::Ordinal) -or -not [string]::Equals($source.workingTreeSha256, $candidateSource.workingTreeSha256, [StringComparison]::Ordinal)) { throw 'Candidate M6 source changed during verification.' }
}
$pending = @('manual visual review', 'manual Accessibility Insights/Narrator walkthrough', 'manual real Japanese IME smoke', 'clean-machine NativeAOT package run')
$acceptance = if ($Mode -eq 'Candidate') { 'clean-automated-candidate' } else { 'precommit-automated-only' }
[ordered]@{
    schema = 2; issue = 38; mode = $Mode; acceptance = $acceptance; baseline = $baseline; hashes = $hashes; environment = $environment; operations = $operations; informational = $informational
    packaging = [ordered]@{ copiedPublish = $true; zip = (Split-Path $zip -Leaf); publishBytes = $informational.publishBytes; zipBytes = $informational.zipBytes; zipSha256 = $zipSha256; nativeAssetAndLicenseInventory = $copyHashes; extractedPackageInventory = $zipHashes; extractedChecksumsMatch = $true }
    gates = $null; pending = $pending; manualNotRun = $pending
} | ConvertTo-Json -Depth 10 -Compress | Set-Content $evidencePath
if ($Mode -eq 'PreCommit') {
@"
# M6 automated pre-commit summary

Mode: PreCommit. Acceptance: precommit-automated-only. Evidence: ``artifacts/m6/m6-evidence.json``.

Automated subset records NativeAOT latency, virtualization, GC, resource, inventory, and packaging observations. It is not final acceptance while the source is dirty or manual visual/accessibility/IME and clean-machine records are pending.
"@ | Set-Content $summaryPath
}
Write-Output "M6 evidence: $evidencePath"
