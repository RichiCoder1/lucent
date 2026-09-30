#requires -Version 7.4
<#
.SYNOPSIS
Finalizes a release set from this CI run's authenticated, verified input artifacts.
.DESCRIPTION
Run only in a gated CI job after authenticated downloads with digest verification.
The artifact IDs/digests come from prior upload steps, not downloaded JSON. Records
are emitted immediately after the workflow's fixed successful command batches.
This script checks those records and their logs; it neither runs nor invents checks.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $CandidatePath,
    [Parameter(Mandatory)][string] $ArtifactDirectory,
    [Parameter(Mandatory)][string] $ManagedArtifactDirectory,
    [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long] $InputArtifactId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string] $InputArtifactDigest,
    [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long] $ManagedArtifactId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string] $ManagedArtifactDigest,
    [Parameter(Mandatory)][string] $OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseSet.psm1') -Force

function Read-Check([string] $Kind, [string] $CommandId, [string] $Directory) {
    $path = Resolve-LuiArtifactPath $Directory "checks/$Kind.json"
    $record = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    $keys = @('schemaVersion', 'kind', 'commandId', 'sourceCommit', 'runId', 'runAttempt', 'exitCode', 'logs')
    if ($Kind -ne 'managed') { $keys += 'inputManifestSha256' }
    if (@($record.Keys).Count -ne $keys.Count -or @($record.Keys | Where-Object { $_ -cnotin $keys }).Count) { throw "Invalid check record fields: $Kind" }
    if ($record['schemaVersion'] -ne 1 -or $record['kind'] -cne $Kind -or $record['commandId'] -cne $CommandId -or $record['exitCode'] -isnot [long] -or $record['exitCode'] -ne 0) { throw "The fixed CI command did not succeed: $Kind" }
    if ($record['sourceCommit'] -cne $candidate.releaseSet.sourceCommit -or $record['runId'] -cne $runId -or $record['runAttempt'] -cne $runAttempt) { throw "Check belongs to another source or CI run: $Kind" }
    if ($Kind -ne 'managed' -and $record['inputManifestSha256'] -cne $candidate.inputManifestSha256) { throw "Check belongs to different release inputs: $Kind" }
    if ($record['logs'] -isnot [Collections.IList] -or $record['logs'].Count -eq 0) { throw "Check has no captured output: $Kind" }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($log in $record['logs']) {
        if (!$seen.Add([string]$log['fileName'])) { throw "Duplicate check output: $Kind" }
        $actual = Get-LuiArtifact $Directory $log['fileName']
        if ((ConvertTo-LuiCanonicalJson $actual) -cne (ConvertTo-LuiCanonicalJson $log)) { throw "Changed check output: $Kind/$($log['fileName'])" }
    }
    return $record
}

function Assert-ManagedResults($Record) {
    $reports = @($Record['logs'] | Where-Object { $_['fileName'].EndsWith('.trx', [StringComparison]::OrdinalIgnoreCase) })
    if (!$reports.Count) { throw 'Managed evidence omitted its TRX reports.' }
    foreach ($report in $reports) {
        $path = Resolve-LuiArtifactPath $ManagedArtifactDirectory $report['fileName']
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $reader = [Xml.XmlReader]::Create($path, $settings)
        try {
            $document = [Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
            $summary = $document.SelectSingleNode('/*[local-name()="TestRun"]/*[local-name()="ResultSummary"]')
            $counters = $summary.SelectSingleNode('*[local-name()="Counters"]')
            $results = @($document.SelectNodes('/*[local-name()="TestRun"]/*[local-name()="Results"]/*[local-name()="UnitTestResult"]'))
            $passed = @($results | Where-Object { $_.GetAttribute('outcome') -ceq 'Passed' })
            if ($summary.GetAttribute('outcome') -cne 'Completed' -or [long]$counters.GetAttribute('passed') -le 0 -or [long]$counters.GetAttribute('failed') -ne 0 -or !$passed.Count -or @($results | Where-Object { $_.GetAttribute('outcome') -cnotin @('Passed', 'NotExecuted') }).Count) { throw "Managed report did not pass: $($report['fileName'])" }
            if ([long]$counters.GetAttribute('total') -ne $results.Count -or [long]$counters.GetAttribute('passed') -ne $passed.Count) { throw "Managed report counters disagree with results: $($report['fileName'])" }
        }
        finally { $reader.Dispose() }
    }
}

if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'RichiCoder1/lucent' -or $env:GITHUB_REF -cne 'refs/heads/main' -or $env:GITHUB_WORKFLOW_REF -cne 'RichiCoder1/lucent/.github/workflows/tests.yml@refs/heads/main' -or $env:GITHUB_EVENT_NAME -cnotin @('push', 'workflow_dispatch')) { throw 'Release completion requires the gated main-branch GitHub workflow.' }
if ($env:GITHUB_RUN_ID -notmatch '^[1-9][0-9]*$' -or $env:GITHUB_RUN_ATTEMPT -notmatch '^[1-9][0-9]*$') { throw 'CI run identity is missing.' }
$runId = [long]$env:GITHUB_RUN_ID
$runAttempt = [long]$env:GITHUB_RUN_ATTEMPT
$candidate = Get-Content -LiteralPath $CandidatePath -Raw | ConvertFrom-Json -AsHashtable
Assert-LuiReleaseSet $candidate $ArtifactDirectory
if ($candidate.status -cne 'candidate' -or $candidate.releaseSet.sourceState -cne 'clean' -or $candidate.releaseSet.sourceCommit -cne $env:GITHUB_SHA) { throw 'Completion requires this CI commit and a clean candidate.' }
if (Test-Path -LiteralPath $OutputPath) { throw 'Immutable complete descriptor already exists.' }
$commandIds = [ordered]@{ managed = 'repository-managed'; native = 'repository-native'; packages = 'package-consumers'; server = 'published-server'; extension = 'packaged-extension' }
$provenance = @{ repository = 'RichiCoder1/lucent'; sourceCommit = $candidate.releaseSet.sourceCommit; workflow = '.github/workflows/tests.yml'; runId = $runId; runAttempt = $runAttempt; artifactId = $InputArtifactId; artifactDigest = $InputArtifactDigest }
$receipts = [ordered]@{}
foreach ($kind in $commandIds.Keys) {
    $directory = if ($kind -eq 'managed') { $ManagedArtifactDirectory } else { $ArtifactDirectory }
    $record = Read-Check $kind $commandIds[$kind] $directory
    if ($kind -eq 'managed') { Assert-ManagedResults $record }
    $receipt = @{ kind = $kind; sourceCommit = $candidate.releaseSet.sourceCommit; inputManifestSha256 = $candidate.inputManifestSha256; status = 'passed'; provenance = $provenance; execution = $record }
    if ($kind -eq 'managed') { $receipt['managedArtifact'] = @{ id = $ManagedArtifactId; digest = $ManagedArtifactDigest } }
    $name = "completion/$kind.json"
    if (Test-Path -LiteralPath (Join-Path $ArtifactDirectory $name)) { throw "Immutable completion receipt already exists: $name" }
    $receipts[$name] = $receipt
}
# All commands, logs, reports and destination collisions are checked before writes.
$evidence = @()
foreach ($name in $receipts.Keys) {
    $receipt = $receipts[$name]
    Write-LuiImmutableJson (Join-Path $ArtifactDirectory $name) $receipt
    $evidence += @{ kind = $receipt.kind; sourceCommit = $receipt.sourceCommit; inputManifestSha256 = $receipt.inputManifestSha256; status = 'passed'; artifact = Get-LuiArtifact $ArtifactDirectory $name }
}
$null = New-LuiReleaseSet -Directory $ArtifactDirectory -Version $candidate.releaseSet.version -SourceCommit $candidate.releaseSet.sourceCommit -SourceState clean -ServerArchive $candidate.server.artifact.fileName -Vsix $candidate.extension.artifact.fileName -OutputPath $OutputPath -Completion @{ evidence = $evidence; provenance = $provenance }
Write-Output "Complete CI release descriptor: $OutputPath"
