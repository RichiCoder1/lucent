param(
    [Parameter(Mandatory)] [Alias('PackagePath')] [string] $CandidateZipPath,
    [Parameter(Mandatory)] [Alias('EvidencePath')] [string] $CandidateEvidencePath,
    [string] $GateRecordPath,
    [ValidateRange(30, 600)] [int] $TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$prerequisite = 'DISM.exe /Online /Enable-Feature /FeatureName:Containers-DisposableClientVM /All /NoRestart'

function Stop-Sandbox([string] $Id) {
    if ($Id) { try { & $script:wsbPath StopSandbox --id $Id 2>$null | Out-Null } catch { } }
}

function Get-SandboxIds {
    if (-not $script:wsbPath) { return @() }
    try { @((& $script:wsbPath list --raw | ConvertFrom-Json).WindowsSandboxEnvironments.Id) }
    catch { @() }
}

function Get-Hash([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Properties($Value, [string[]] $Expected, [string] $Name) {
    if ($Value -isnot [System.Management.Automation.PSCustomObject]) { throw "$Name has the wrong type." }
    $actual = @($Value.PSObject.Properties.Name)
    if ($actual.Count -ne $Expected.Count) { throw "$Name has missing or undeclared fields." }
    foreach ($expectedName in $Expected) { if (@($actual | Where-Object { [string]::Equals($_, $expectedName, [StringComparison]::Ordinal) }).Count -ne 1) { throw "$Name has missing, undeclared, or wrongly-cased fields." } }
}

function Assert-True($Value, [string] $Name) {
    if ($Value -isnot [bool] -or -not $Value) { throw "$Name must be Boolean true." }
}

function Assert-Utc([string] $Value, [string] $Name) {
    $parsed = [DateTimeOffset]::MinValue
    if ($Value -isnot [string] -or -not [DateTimeOffset]::TryParseExact($Value, 'O', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed) -or $parsed.Offset -ne [TimeSpan]::Zero) { throw "$Name must be a round-trip UTC timestamp." }
}

function Copy-Atomic([string] $Source, [string] $Destination) {
    $Destination = [IO.Path]::GetFullPath($Destination)
    $parent = Split-Path -Parent $Destination
    New-Item -Path $parent -ItemType Directory -Force | Out-Null
    $temporary = Join-Path $parent ('.clean-machine-gate.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    Copy-Item -LiteralPath $Source -Destination $temporary -Force
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
}

function Validate-Candidate($Candidate, [string] $ZipPath) {
    Assert-Properties $Candidate @('acceptance', 'baseline', 'environment', 'gates', 'hashes', 'informational', 'issue', 'manualNotRun', 'mode', 'operations', 'packaging', 'pending', 'schema') 'candidate evidence'
    if ($Candidate.schema -isnot [long] -or $Candidate.schema -ne 2L -or $Candidate.issue -isnot [long] -or $Candidate.issue -ne 38L -or $Candidate.mode -cne 'Candidate' -or $Candidate.acceptance -cne 'clean-automated-candidate' -or $null -ne $Candidate.gates) { throw 'Candidate evidence must be schema 2, issue 38, clean-automated-candidate, and contain no gate records.' }
    Assert-Properties $Candidate.environment @('build', 'cpu', 'display', 'gpu', 'logicalProcessors', 'machine', 'os', 'powerPlan', 'ramBytes', 'recordedAtUtc', 'runtime', 'scaleAuthority', 'source') 'candidate.environment'
    Assert-Properties $Candidate.environment.source @('branch', 'commit', 'status', 'tree', 'untracked', 'workingTreeSha256') 'candidate.environment.source'
    if ($Candidate.environment.source.status.Count -ne 0 -or $Candidate.environment.source.commit -isnot [string] -or [string]::IsNullOrWhiteSpace($Candidate.environment.source.commit)) { throw 'Candidate evidence does not prove a clean source tree.' }
    Assert-Properties $Candidate.packaging @('copiedPublish', 'extractedChecksumsMatch', 'extractedPackageInventory', 'nativeAssetAndLicenseInventory', 'publishBytes', 'zip', 'zipBytes', 'zipSha256') 'candidate.packaging'
    if ($Candidate.packaging.zip -cne 'lucent-win-x64.zip' -or (Split-Path -Leaf $ZipPath) -cne $Candidate.packaging.zip) { throw 'The supplied package is not the exact candidate package named by evidence.' }
    if ($Candidate.packaging.zipSha256 -isnot [string] -or $Candidate.packaging.zipSha256 -notmatch '^[0-9a-f]{64}$' -or $Candidate.packaging.zipSha256 -cne (Get-Hash $ZipPath)) { throw 'Candidate evidence and supplied package SHA-256 differ.' }
    Assert-True $Candidate.packaging.copiedPublish 'candidate.packaging.copiedPublish'
    Assert-True $Candidate.packaging.extractedChecksumsMatch 'candidate.packaging.extractedChecksumsMatch'
    if ($Candidate.packaging.nativeAssetAndLicenseInventory -isnot [array] -or $Candidate.packaging.extractedPackageInventory -isnot [array] -or $Candidate.packaging.extractedPackageInventory.Count -eq 0 -or (ConvertTo-Json $Candidate.packaging.nativeAssetAndLicenseInventory -Compress) -cne (ConvertTo-Json $Candidate.packaging.extractedPackageInventory -Compress)) { throw 'Candidate package inventories are absent or differ.' }
}

function Get-Result([string] $OutputDirectory, [int64] $Deadline) {
    $path = Join-Path $OutputDirectory 'clean-machine-gate.json'
    $completionPath = Join-Path $OutputDirectory 'verification-complete.json'
    do {
        if ((Test-Path -LiteralPath $Path -PathType Leaf) -and (Test-Path -LiteralPath $completionPath -PathType Leaf)) {
            try {
                $completion = Get-Content -LiteralPath $completionPath -Raw | ConvertFrom-Json -DateKind String
                Assert-Properties $completion @('resultSha256','schema') 'sandbox completion marker'
                if ($completion.schema -isnot [long] -or $completion.schema -ne 1L -or $completion.resultSha256 -isnot [string] -or $completion.resultSha256 -cne (Get-Hash $Path)) { throw 'Sandbox completion marker does not bind the result.' }
                return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -DateKind String)
            } catch { }
        }
        Start-Sleep -Milliseconds 500
    } while ([Environment]::TickCount64 -lt $Deadline)
    throw "Sandbox did not produce clean-machine-gate.json within $TimeoutSeconds seconds."
}

function Validate-Result($Result, [string] $CandidateEvidenceSha256, [string] $PackageSha256, [string] $SourceCommit) {
    Assert-Properties $Result @('candidateEvidenceSha256', 'environment', 'inventoryPass', 'issue', 'launchPass', 'noDevelopmentSdk', 'packageSha256', 'recordedAtUtc', 'schema', 'sourceCommit') 'clean-machine gate record'
    if ($Result.schema -isnot [long] -or $Result.schema -ne 1L -or $Result.issue -isnot [long] -or $Result.issue -ne 38L) { throw 'Clean-machine gate record has the wrong schema or issue.' }
    foreach ($pair in @(@{ Actual = $Result.candidateEvidenceSha256; Expected = $CandidateEvidenceSha256; Name = 'candidateEvidenceSha256' }, @{ Actual = $Result.packageSha256; Expected = $PackageSha256; Name = 'packageSha256' }, @{ Actual = $Result.sourceCommit; Expected = $SourceCommit; Name = 'sourceCommit' })) {
        if ($pair.Actual -isnot [string] -or $pair.Actual -cne $pair.Expected) { throw "Clean-machine gate $($pair.Name) does not bind the candidate." }
    }
    if ($Result.environment -isnot [string] -or [string]::IsNullOrWhiteSpace($Result.environment)) { throw 'Clean-machine gate environment must be a non-empty string.' }
    Assert-True $Result.noDevelopmentSdk 'clean.noDevelopmentSdk'; Assert-True $Result.inventoryPass 'clean.inventoryPass'; Assert-True $Result.launchPass 'clean.launchPass'
    Assert-Utc $Result.recordedAtUtc 'clean.recordedAtUtc'
}

if ($env:OS -ne 'Windows_NT') { throw "Windows Sandbox is unavailable on this host. Enable it separately with: $prerequisite" }
$sandboxGui = Get-Command WindowsSandbox.exe -ErrorAction SilentlyContinue
if (-not $sandboxGui) { throw "Windows Sandbox is unavailable. Enable it separately with: $prerequisite" }

$zip = (Resolve-Path -LiteralPath $CandidateZipPath -ErrorAction Stop).Path
$evidence = (Resolve-Path -LiteralPath $CandidateEvidencePath -ErrorAction Stop).Path
if ((Split-Path -Leaf $zip) -cne 'lucent-win-x64.zip') { throw 'CandidateZipPath must name the exact lucent-win-x64.zip package.' }
$candidateEvidenceSha256 = Get-Hash $evidence
$packageSha256 = Get-Hash $zip
$candidate = Get-Content -LiteralPath $evidence -Raw | ConvertFrom-Json -DateKind String
Validate-Candidate $candidate $zip
$sourceCommit = $candidate.environment.source.commit
if (-not $GateRecordPath) { $GateRecordPath = Join-Path (Split-Path -Parent $evidence) 'clean-machine-gate.json' }

$root = Join-Path ([IO.Path]::GetTempPath()) ('lucent-m6-sandbox-' + [Guid]::NewGuid().ToString('N'))
$input = Join-Path $root 'input'; $output = Join-Path $root 'output'; $config = Join-Path $root 'Lucent-M6.wsb'
New-Item -Path $input, $output -ItemType Directory -Force | Out-Null
Copy-Item -LiteralPath $zip -Destination (Join-Path $input 'lucent-win-x64.zip')
Copy-Item -LiteralPath $evidence -Destination (Join-Path $input 'm6-evidence.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'M6-SandboxVerify.ps1') -Destination (Join-Path $input 'M6-SandboxVerify.ps1')
$hostFolder = [Security.SecurityElement]::Escape((Resolve-Path -LiteralPath $input).Path)
$outputFolder = [Security.SecurityElement]::Escape((Resolve-Path -LiteralPath $output).Path)
$wsbXml = @"
<Configuration>
  <Networking>Disable</Networking>
  <MappedFolders>
    <MappedFolder><HostFolder>$hostFolder</HostFolder><SandboxFolder>C:\M6\Input</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$outputFolder</HostFolder><SandboxFolder>C:\M6\Output</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:\M6\Input\M6-SandboxVerify.ps1</Command></LogonCommand>
</Configuration>
"@
[IO.File]::WriteAllText($config, $wsbXml, (New-Object Text.UTF8Encoding($false)))

$wsb = Get-Command wsb.exe, wsb -ErrorAction SilentlyContinue | Select-Object -First 1
$wsbPath = if ($wsb) { $wsb.Source } else { $null }
$sandboxId = $null
$completed = $false
try {
    $priorIds = @(Get-SandboxIds)
    Start-Process -FilePath $sandboxGui.Source -ArgumentList $config | Out-Null
    if ($wsbPath) {
        $idDeadline = [Environment]::TickCount64 + 30000
        do {
            Start-Sleep -Milliseconds 500
            $sandboxId = @(Get-SandboxIds | Where-Object { $_ -notin $priorIds } | Select-Object -First 1)[0]
        } while (-not $sandboxId -and [Environment]::TickCount64 -lt $idDeadline)
    }
    $result = Get-Result $output ([Environment]::TickCount64 + ($TimeoutSeconds * 1000))
    Validate-Result $result $candidateEvidenceSha256 $packageSha256 $sourceCommit
    Copy-Atomic (Join-Path $output 'clean-machine-gate.json') $GateRecordPath
    $completed = $true
    Write-Output "M6 clean-machine gate: $GateRecordPath"
}
finally {
    Stop-Sandbox $sandboxId
    if (-not $completed) { Write-Warning "Sandbox validation failed; temporary mapped folders were retained at $root." }
    elseif (-not $sandboxId) { Write-Warning "The sandbox ID was unavailable; the session may remain open and temporary mapped folders were retained at $root." }
    else { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}
