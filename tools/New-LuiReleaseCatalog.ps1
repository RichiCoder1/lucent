#requires -Version 7.4
<#
.SYNOPSIS
Builds a one-release shipped catalog from a completed GitHub Actions artifact.
.DESCRIPTION
The script accepts only a run ID and attempt. It obtains run, workflow, and artifact
metadata from the fixed RichiCoder1/lucent GitHub API using GITHUB_TOKEN, downloads
the exact complete artifact, verifies its GitHub digest, and validates complete.json
and its local artifacts with ReleaseSet.psm1. It does not accept a local descriptor,
archive URL, digest, or user-authored acquisition receipt as publisher evidence.
The token and signed download URLs are never written to output or evidence files.
The output is a new one-entry catalog; existing catalog JSON is never imported or
merged as trusted data. Use EvidenceDirectory only to retain the verified artifact
and sanitized Actions metadata for a separate proof.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, [long]::MaxValue)][long] $RunId,
    [ValidateRange(1, [int]::MaxValue)][int] $RunAttempt,
    [string] $OutputPath,
    [string] $EvidenceDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:LuiCatalogRepository = 'RichiCoder1/lucent'
$script:LuiCatalogWorkflow = '.github/workflows/tests.yml'
$script:LuiCatalogApi = 'https://api.github.com'
Import-Module (Join-Path $PSScriptRoot 'ReleaseSet.psm1') -Force

function Get-LuiCatalogProperty($Object, [string] $Name) {
    if ($null -eq $Object) { return $null }
    if ($Object -is [Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Assert-LuiActionsRun($Run, $Workflow, [long] $ExpectedRunId, [int] $ExpectedAttempt) {
    $repository = Get-LuiCatalogProperty $Run 'repository'
    $headRepository = Get-LuiCatalogProperty $Run 'head_repository'
    $repositoryName = Get-LuiCatalogProperty $repository 'full_name'
    $headRepositoryName = Get-LuiCatalogProperty $headRepository 'full_name'
    $workflowPath = Get-LuiCatalogProperty $Workflow 'path'
    $headSha = Get-LuiCatalogProperty $Run 'head_sha'
    if ((Get-LuiCatalogProperty $Run 'id') -ne $ExpectedRunId -or
        (Get-LuiCatalogProperty $Run 'run_attempt') -ne $ExpectedAttempt -or
        (Get-LuiCatalogProperty $Run 'status') -cne 'completed' -or
        (Get-LuiCatalogProperty $Run 'conclusion') -cne 'success' -or
        (Get-LuiCatalogProperty $Run 'event') -cnotin @('push', 'workflow_dispatch') -or
        (Get-LuiCatalogProperty $Run 'head_branch') -cne 'main' -or
        $repositoryName -cne $script:LuiCatalogRepository -or
        $headRepositoryName -cne $script:LuiCatalogRepository -or
        [string]::IsNullOrWhiteSpace([string]$headSha) -or
        [string]$headSha -cnotmatch '^[0-9a-f]{40}$' -or
        (Get-LuiCatalogProperty $Workflow 'id') -ne (Get-LuiCatalogProperty $Run 'workflow_id') -or
        $workflowPath -cne $script:LuiCatalogWorkflow) {
        throw 'GitHub Actions run is not a successful main-branch run of the fixed Lucent workflow.'
    }
    $repositoryId = Get-LuiCatalogProperty $repository 'id'
    if ($repositoryId -le 0 -or
        (Get-LuiCatalogProperty $headRepository 'id') -ne $repositoryId -or
        (Get-LuiCatalogProperty $Run 'run_number') -le 0) {
        throw 'GitHub Actions run is not owned by the fixed repository.'
    }
    return [ordered]@{
        repository = $script:LuiCatalogRepository
        workflow = $script:LuiCatalogWorkflow
        runId = [long](Get-LuiCatalogProperty $Run 'id')
        runAttempt = [int](Get-LuiCatalogProperty $Run 'run_attempt')
        runNumber = [long](Get-LuiCatalogProperty $Run 'run_number')
        headSha = [string]$headSha
        repositoryId = [long]$repositoryId
    }
}

function Assert-LuiActionsArtifact($Artifact, $Run, [string] $ExpectedName) {
    $workflowRun = Get-LuiCatalogProperty $Artifact 'workflow_run'
    $digest = [string](Get-LuiCatalogProperty $Artifact 'digest')
    $artifactId = Get-LuiCatalogProperty $Artifact 'id'
    $artifactSize = Get-LuiCatalogProperty $Artifact 'size_in_bytes'
    $runId = Get-LuiCatalogProperty $Run 'id'
    $repository = Get-LuiCatalogProperty $Run 'repository'
    $repositoryId = Get-LuiCatalogProperty $repository 'id'
    if ($artifactId -le 0 -or
        (Get-LuiCatalogProperty $Artifact 'name') -cne $ExpectedName -or
        (Get-LuiCatalogProperty $Artifact 'expired') -ne $false -or
        $artifactSize -le 0 -or $artifactSize -gt 512MB -or
        $digest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
        (Get-LuiCatalogProperty $workflowRun 'id') -ne $runId -or
        (Get-LuiCatalogProperty $workflowRun 'head_sha') -cne (Get-LuiCatalogProperty $Run 'head_sha') -or
        (Get-LuiCatalogProperty $workflowRun 'repository_id') -ne $repositoryId -or
        (Get-LuiCatalogProperty $workflowRun 'head_repository_id') -ne $repositoryId) {
        throw "GitHub Actions artifact is not the expected unexpired artifact for this run: $ExpectedName"
    }
    return [ordered]@{
        id = [long]$artifactId
        name = [string](Get-LuiCatalogProperty $Artifact 'name')
        digest = $digest
        bytes = [long]$artifactSize
    }
}

function Assert-LuiCompleteDescriptor($Descriptor, $RunIdentity, $PackageArtifact, $ManagedArtifact, [string] $ArtifactDirectory) {
    if ($Descriptor['status'] -cne 'complete') { throw 'Only a complete release descriptor can enter the catalog.' }
    if ($Descriptor['releaseSet']['sourceState'] -cne 'clean' -or
        $Descriptor['releaseSet']['sourceCommit'] -cne $RunIdentity.headSha) {
        throw 'Catalog release must have a clean source tree at the authenticated run head.'
    }
    $expectedVersion = "0.3.0-dev.$($RunIdentity.runNumber).1"
    if ($Descriptor['releaseSet']['version'] -cne $expectedVersion) {
        throw 'Release version does not match the authenticated workflow run number.'
    }
    $provenance = $Descriptor['provenance']
    if ($provenance['repository'] -cne $RunIdentity.repository -or
        $provenance['workflow'] -cne $RunIdentity.workflow -or
        $provenance['sourceCommit'] -cne $RunIdentity.headSha -or
        $provenance['runId'] -ne $RunIdentity.runId -or
        $provenance['runAttempt'] -ne $RunIdentity.runAttempt -or
        $provenance['artifactId'] -ne $PackageArtifact.id -or
        $provenance['artifactDigest'] -cne $PackageArtifact.digest.Substring(7)) {
        throw 'Complete descriptor provenance differs from authenticated Actions metadata.'
    }
    $managedEvidence = @($Descriptor['evidence'] | Where-Object { $_['kind'] -ceq 'managed' })
    if ($managedEvidence.Count -ne 1) { throw 'Complete descriptor must have exactly one managed evidence receipt.' }
    $managedPath = Resolve-LuiArtifactPath $ArtifactDirectory $managedEvidence[0]['artifact']['fileName']
    $managedReceipt = Get-Content -LiteralPath $managedPath -Raw | ConvertFrom-Json -AsHashtable
    if ($managedReceipt['managedArtifact']['id'] -ne $ManagedArtifact.id -or
        $managedReceipt['managedArtifact']['digest'] -cne $ManagedArtifact.digest.Substring(7)) {
        throw 'Managed verification receipt differs from authenticated Actions artifact metadata.'
    }
    $sdkPackages = @($Descriptor['packages'] | Where-Object { $_['id'] -ceq 'Lucent.Lui.Sdk' })
    if ($sdkPackages.Count -ne 1 -or
        $sdkPackages[0]['version'] -cne $Descriptor['releaseSet']['version'] -or
        $sdkPackages[0]['repositoryCommit'] -cne $RunIdentity.headSha -or
        $sdkPackages[0]['artifact']['sha256'] -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Complete descriptor does not contain exactly one matching Lucent.Lui.Sdk package.'
    }
    return $sdkPackages[0]
}

function New-LuiCatalogEntry($Descriptor, $RunIdentity, $CompleteArtifact, $PackageArtifact, $ManagedArtifact, [string] $ArtifactDirectory) {
    # This is the existing release-set validator, not a second descriptor validator.
    Assert-LuiReleaseSet $Descriptor $ArtifactDirectory
    $sdkPackage = Assert-LuiCompleteDescriptor $Descriptor $RunIdentity $PackageArtifact $ManagedArtifact $ArtifactDirectory
    $serverPath = Resolve-LuiArtifactPath $ArtifactDirectory $Descriptor['server']['artifact']['fileName']
    $serverBundle = Get-LuiServerBundle $serverPath
    $descriptorPath = Resolve-LuiArtifactPath $ArtifactDirectory 'complete.json'
    $descriptorSha256 = (Get-FileHash -LiteralPath $descriptorPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $serverIdentity = $Descriptor['server']['identity']
    return [ordered]@{
        releaseVersion = [string]$Descriptor['releaseSet']['version']
        sdk = [ordered]@{
            id = 'Lucent.Lui.Sdk'
            version = [string]$sdkPackage['version']
            packageSha256 = [string]$sdkPackage['artifact']['sha256']
        }
        server = [ordered]@{
            artifact = [ordered]@{
                bytes = [long]$Descriptor['server']['artifact']['bytes']
                sha256 = [string]$Descriptor['server']['artifact']['sha256']
            }
            identity = $serverIdentity
            filesSha256 = [string]$serverBundle.filesSha256
        }
        anchor = [ordered]@{
            kind = 'bundled-catalog'
            sourceCommit = [string]$Descriptor['releaseSet']['sourceCommit']
            descriptorSha256 = $descriptorSha256
            githubActions = [ordered]@{
                repository = $RunIdentity.repository
                workflow = $RunIdentity.workflow
                runId = [long]$RunIdentity.runId
                runAttempt = [int]$RunIdentity.runAttempt
                headSha = [string]$RunIdentity.headSha
                artifact = [ordered]@{
                    id = [long]$CompleteArtifact.id
                    name = [string]$CompleteArtifact.name
                    digest = [string]$CompleteArtifact.digest
                }
            }
        }
    }
}

function Invoke-LuiGitHubGet([string] $Path, [string] $Token) {
    $headers = @{
        Authorization = "Bearer $Token"
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent' = 'Lucent-ReleaseCatalog/1.0'
    }
    try {
        return Invoke-RestMethod -Method Get -Uri ($script:LuiCatalogApi + $Path) -Headers $headers -TimeoutSec 60
    }
    catch {
        throw "Authenticated GitHub Actions metadata request failed: $Path"
    }
}

function Get-LuiActionsRunArtifacts($Run, [string] $Token) {
    $artifacts = [Collections.Generic.List[object]]::new()
    $page = 1
    $totalCount = $null
    do {
        $response = Invoke-LuiGitHubGet "/repos/$script:LuiCatalogRepository/actions/runs/$($Run.id)/artifacts?per_page=100&page=$page" $Token
        if ($null -eq $totalCount) { $totalCount = [int]$response.total_count }
        if ([int]$response.total_count -ne $totalCount) { throw 'GitHub Actions artifact listing changed while being read.' }
        foreach ($artifact in @($response.artifacts)) { $artifacts.Add($artifact) }
        $page++
        if ($page -gt 100 -and $artifacts.Count -lt $totalCount) { throw 'GitHub Actions artifact listing exceeded its page limit.' }
    } while ($artifacts.Count -lt $totalCount)
    if ($artifacts.Count -ne $totalCount) { throw 'GitHub Actions artifact listing was incomplete.' }
    return ,$artifacts.ToArray()
}

function Get-LuiNamedActionsArtifact($Artifacts, $Run, [string] $Name, [string] $Token) {
    $matches = @($Artifacts | Where-Object { $_.name -ceq $Name })
    if ($matches.Count -ne 1) { throw "Expected one GitHub Actions artifact named $Name." }
    $listed = Assert-LuiActionsArtifact $matches[0] $Run $Name
    $detail = Invoke-LuiGitHubGet "/repos/$script:LuiCatalogRepository/actions/artifacts/$($listed.id)" $Token
    $verified = Assert-LuiActionsArtifact $detail $Run $Name
    if ($verified.id -ne $listed.id -or $verified.digest -cne $listed.digest -or $verified.bytes -ne $listed.bytes) {
        throw "GitHub Actions artifact detail changed while being read: $Name"
    }
    return $verified
}

function Save-LuiActionsArtifact(
    [long] $ArtifactId,
    [string] $Token,
    [string] $DestinationPath,
    [Net.Http.HttpMessageHandler] $MessageHandler = $null,
    [TimeSpan] $TransferTimeout = ([TimeSpan]::FromMinutes(5))
) {
    if ($TransferTimeout -le [TimeSpan]::Zero) { throw 'Artifact transfer deadline must be positive.' }
    $handler = $MessageHandler
    if ($null -eq $handler) {
        $handler = [Net.Http.HttpClientHandler]::new()
        $handler.AllowAutoRedirect = $false
    }
    $uri = [Uri]::new("$script:LuiCatalogApi/repos/$script:LuiCatalogRepository/actions/artifacts/$ArtifactId/zip")
    $authorized = $true
    $redirectCount = 0
    $deadline = [Threading.CancellationTokenSource]::new($TransferTimeout)
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $createdDestination = $false
    try {
        while ($true) {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $uri)
            $request.Headers.UserAgent.ParseAdd('Lucent-ReleaseCatalog/1.0')
            if ($authorized) {
                $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token)
                $request.Headers.Accept.ParseAdd('application/vnd.github+json')
            }
            try {
                $response = $client.SendAsync(
                    $request,
                    [Net.Http.HttpCompletionOption]::ResponseHeadersRead,
                    $deadline.Token
                ).GetAwaiter().GetResult()
            }
            catch {
                $request.Dispose()
                throw
            }
            if ([int]$response.StatusCode -in @(301, 302, 303, 307, 308)) {
                $location = $response.Headers.Location
                $response.Dispose()
                $request.Dispose()
                $redirectCount++
                if ($redirectCount -gt 3 -or $null -eq $location) { throw 'GitHub artifact download returned an invalid redirect.' }
                if (!$location.IsAbsoluteUri) { $location = [Uri]::new($uri, $location) }
                if ($location.Scheme -cne 'https' -or $location.UserInfo) { throw 'GitHub artifact download redirected outside HTTPS.' }
                $uri = $location
                $authorized = $false
                continue
            }
            if ($response.StatusCode -ne [Net.HttpStatusCode]::OK) {
                $response.Dispose()
                $request.Dispose()
                throw 'GitHub artifact download failed.'
            }
            $contentLength = $response.Content.Headers.ContentLength
            if (($null -ne $contentLength -and $contentLength -le 0) -or
                ($null -ne $contentLength -and $contentLength -gt 512MB)) {
                $response.Dispose()
                $request.Dispose()
                throw 'GitHub artifact exceeds the supported encoded size.'
            }
            $input = $null
            $output = $null
            try {
                $input = $response.Content.ReadAsStreamAsync($deadline.Token).GetAwaiter().GetResult()
                $output = [IO.FileStream]::new($DestinationPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                $createdDestination = $true
                [byte[]]$buffer = [byte[]]::new(81920)
                $readBuffer = [Memory[byte]]::new($buffer, 0, $buffer.Length)
                [long]$total = 0
                while (($read = $input.ReadAsync($readBuffer, $deadline.Token).GetAwaiter().GetResult()) -gt 0) {
                    $total += $read
                    if ($total -gt 512MB) { throw 'GitHub artifact exceeds the supported encoded size.' }
                    $writeBuffer = [ReadOnlyMemory[byte]]::new($buffer, 0, $read)
                    $output.WriteAsync($writeBuffer, $deadline.Token).GetAwaiter().GetResult()
                }
                if ($total -le 0) { throw 'GitHub artifact download was empty.' }
            }
            finally {
                if ($null -ne $output) { $output.Dispose() }
                if ($null -ne $input) { $input.Dispose() }
                $response.Dispose()
                $request.Dispose()
            }
            return
        }
    }
    catch {
        if ($createdDestination -and (Test-Path -LiteralPath $DestinationPath)) {
            Remove-Item -LiteralPath $DestinationPath -Force
        }
        if ($deadline.IsCancellationRequested) { throw 'GitHub artifact download exceeded its total transfer deadline.' }
        if ($_.Exception.Message -match 'supported encoded size|invalid redirect|outside HTTPS|empty|total transfer deadline') { throw }
        throw 'Authenticated GitHub artifact download failed.'
    }
    finally {
        $client.Dispose()
        $deadline.Dispose()
        $handler.Dispose()
    }
}

function Expand-LuiVerifiedArtifact([string] $ArchivePath, [string] $DestinationDirectory) {
    $archive = Open-LuiArchive $ArchivePath
    try {
        [long]$expandedTotal = 0
        foreach ($name in $archive.Entries.Keys) {
            $parts = $name.Split('/')
            $current = $DestinationDirectory
            for ($index = 0; $index -lt $parts.Length - 1; $index++) {
                $current = Join-Path $current $parts[$index]
                [IO.Directory]::CreateDirectory($current) | Out-Null
            }
            Assert-LuiRelativePath $name
            $root = [IO.Path]::GetFullPath($DestinationDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
            $target = [IO.Path]::GetFullPath((Join-Path $root ($name.Replace('/', [IO.Path]::DirectorySeparatorChar))))
            if (!$target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Verified artifact entry escaped its extraction directory.'
            }
            for ($parent = [IO.Path]::GetDirectoryName($target); $parent -ne $root; $parent = [IO.Path]::GetDirectoryName($parent)) {
                if (([IO.File]::GetAttributes($parent) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw 'Verified artifact extraction path contains a link.'
                }
            }
            $source = $archive.Entries[$name].Open()
            $destination = [IO.FileStream]::new($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                [long]$entryBytes = 0
                $expectedBytes = [long]$archive.Entries[$name].Length
                [byte[]]$buffer = [byte[]]::new(81920)
                while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $entryBytes += $read
                    $expandedTotal += $read
                    if ($entryBytes -gt $expectedBytes -or $expandedTotal -gt 1GB) {
                        throw 'Verified artifact expanded beyond its declared limits.'
                    }
                    $destination.Write($buffer, 0, $read)
                }
                if ($entryBytes -ne $expectedBytes) { throw 'Verified artifact entry length differs from its archive metadata.' }
            }
            finally { $destination.Dispose(); $source.Dispose() }
        }
    }
    finally { $archive.Zip.Dispose() }
}

function Save-LuiCatalogEvidence([string] $EvidenceDirectory, [string] $ArchivePath, [string] $ArtifactDirectory, $RunIdentity, $Run, $CompleteArtifact, $PackageArtifact, $ManagedArtifact, [string] $DescriptorSha256) {
    if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) { return }
    $destination = [IO.Path]::GetFullPath($EvidenceDirectory)
    if ([IO.Directory]::Exists($destination) -or [IO.File]::Exists($destination)) {
        throw 'Evidence directory must be a new path.'
    }
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    Copy-Item -LiteralPath $ArchivePath -Destination (Join-Path $destination 'complete-release.zip')
    $contents = Join-Path $destination 'contents'
    [IO.Directory]::CreateDirectory($contents) | Out-Null
    Get-ChildItem -LiteralPath $ArtifactDirectory -Force | Copy-Item -Destination $contents -Recurse
    $metadata = [ordered]@{
        schemaVersion = 1
        repository = $RunIdentity.repository
        workflow = $RunIdentity.workflow
        run = [ordered]@{
            id = [long]$RunIdentity.runId
            attempt = [int]$RunIdentity.runAttempt
            number = [long]$RunIdentity.runNumber
            headSha = [string]$RunIdentity.headSha
            event = [string]$Run.event
            conclusion = [string]$Run.conclusion
        }
        artifacts = [ordered]@{
            complete = $CompleteArtifact
            packages = $PackageArtifact
            managed = $ManagedArtifact
        }
        descriptorSha256 = $DescriptorSha256
    }
    Write-LuiImmutableJson (Join-Path $destination 'acquisition.json') $metadata
}

function Invoke-LuiReleaseCatalogGeneration([long] $Id, [int] $Attempt, [string] $Destination, [string] $EvidencePath) {
    if ($Id -le 0 -or $Attempt -le 0 -or [string]::IsNullOrWhiteSpace($Destination)) {
        throw 'Specify a positive Actions run ID, attempt, and a new output path.'
    }
    if (Test-Path -LiteralPath $Destination) { throw 'Catalog output must be a new immutable path.' }
    $token = $env:GITHUB_TOKEN
    if ([string]::IsNullOrWhiteSpace($token)) { throw 'GITHUB_TOKEN is required for authenticated Actions artifact acquisition.' }
    $run = Invoke-LuiGitHubGet "/repos/$script:LuiCatalogRepository/actions/runs/$Id/attempts/$Attempt" $token
    $workflowId = Get-LuiCatalogProperty $run 'workflow_id'
    if ($workflowId -le 0) { throw 'Authenticated run omitted its workflow ID.' }
    $workflow = Invoke-LuiGitHubGet "/repos/$script:LuiCatalogRepository/actions/workflows/$workflowId" $token
    $runIdentity = Assert-LuiActionsRun $run $workflow $Id $Attempt
    $artifacts = Get-LuiActionsRunArtifacts $run $token
    $completeName = "complete-release-$Id-$Attempt"
    $packageName = "verified-packages-$Id-$Attempt"
    $managedName = "verified-managed-$Id-$Attempt"
    $completeArtifact = Get-LuiNamedActionsArtifact $artifacts $run $completeName $token
    $packageArtifact = Get-LuiNamedActionsArtifact $artifacts $run $packageName $token
    $managedArtifact = Get-LuiNamedActionsArtifact $artifacts $run $managedName $token

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('lucent-lui-catalog-' + [Guid]::NewGuid().ToString('N'))
    $fullTempRoot = [IO.Path]::GetFullPath($tempRoot)
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (!$fullTempRoot.StartsWith($tempParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Temporary artifact directory escaped the system temporary directory.'
    }
    [IO.Directory]::CreateDirectory($tempRoot) | Out-Null
    $archivePath = Join-Path $tempRoot 'complete-release.zip'
    $artifactDirectory = Join-Path $tempRoot 'contents'
    [IO.Directory]::CreateDirectory($artifactDirectory) | Out-Null
    try {
        Save-LuiActionsArtifact $completeArtifact.id $token $archivePath
        $actualDigest = 'sha256:' + (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualDigest -cne $completeArtifact.digest) { throw 'Downloaded complete artifact digest differs from GitHub metadata.' }
        Expand-LuiVerifiedArtifact $archivePath $artifactDirectory
        $descriptorPath = Resolve-LuiArtifactPath $artifactDirectory 'complete.json'
        $descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json -AsHashtable
        $entry = New-LuiCatalogEntry $descriptor $runIdentity $completeArtifact $packageArtifact $managedArtifact $artifactDirectory
        $descriptorSha256 = (Get-FileHash -LiteralPath $descriptorPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Save-LuiCatalogEvidence $EvidencePath $archivePath $artifactDirectory $runIdentity $run $completeArtifact $packageArtifact $managedArtifact $descriptorSha256
        $catalog = [ordered]@{ schemaVersion = 1; releases = @($entry) }
        Write-LuiImmutableJson $Destination $catalog
        Write-Output "Verified release catalog written to $Destination ($($entry.releaseVersion), run $Id attempt $Attempt)."
    }
    finally {
        if ([IO.Directory]::Exists($fullTempRoot)) {
            $attributes = [IO.File]::GetAttributes($fullTempRoot)
            if (!$fullTempRoot.StartsWith($tempParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                ($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Temporary artifact cleanup target failed its containment check.'
            }
            [IO.Directory]::Delete($fullTempRoot, $true)
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-LuiReleaseCatalogGeneration $RunId $RunAttempt $OutputPath $EvidenceDirectory
}
