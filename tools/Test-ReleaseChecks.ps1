#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Managed', 'Packages')][string] $Suite,
    [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')][string] $Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ReleaseSet.psm1') -Force
$root = Split-Path $PSScriptRoot -Parent
if ($env:NUGET_PACKAGES -and -not [IO.Path]::IsPathFullyQualified($env:NUGET_PACKAGES)) {
    $env:NUGET_PACKAGES = [IO.Path]::GetFullPath((Join-Path $root $env:NUGET_PACKAGES))
}
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'RichiCoder1/lucent') {
    throw 'Release check records are produced by the repository CI. Use Test-Repository locally.'
}
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $commit -cne $env:GITHUB_SHA) { throw 'CI checkout identity mismatch.' }
if (@(& git -C $root status --porcelain --untracked-files=no).Count -ne 0 -or $LASTEXITCODE) {
    throw 'Release checks require unchanged tracked source.'
}
if ($Suite -eq 'Packages' -and !$Version) { throw 'Package checks require an exact version.' }
$directory = Join-Path $root $(if ($Suite -eq 'Managed') { 'artifacts/release-managed' } else { "artifacts/packages/$Version" })
$checks = Join-Path $directory 'checks'
if (Test-Path -LiteralPath $checks) { throw 'Release check output already exists.' }
[IO.Directory]::CreateDirectory($checks) | Out-Null
$candidate = $null

function Invoke-RecordedCheck([string] $Kind, [string] $CommandId, [scriptblock] $Action) {
    $log = "checks/$Kind.log"
    $global:LASTEXITCODE = 0
    & $Action *>&1 | Tee-Object -FilePath (Join-Path $directory $log)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "$CommandId failed with exit code $exitCode." }
    $logs = @((Get-LuiArtifact $directory $log))
    if ($Kind -eq 'managed') {
        $resultsRoot = Join-Path $root 'artifacts/test/managed'
        $results = @(Get-ChildItem -LiteralPath $resultsRoot -Recurse -Filter '*.trx' -File)
        if ($results.Count -eq 0) { throw 'Managed verification produced no test reports.' }
        foreach ($result in $results) {
            $relative = 'reports/' + [IO.Path]::GetRelativePath($resultsRoot, $result.FullName).Replace('\', '/')
            $destination = Join-Path $directory $relative
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            Copy-Item -LiteralPath $result.FullName -Destination $destination
            $logs += Get-LuiArtifact $directory $relative
        }
    }
    $record = [ordered]@{
        schemaVersion = 1; kind = $Kind; commandId = $CommandId
        sourceCommit = $commit; runId = [long]$env:GITHUB_RUN_ID
        runAttempt = [int]$env:GITHUB_RUN_ATTEMPT; exitCode = $exitCode; logs = $logs
    }
    if ($Kind -ne 'managed') { $record['inputManifestSha256'] = $candidate.inputManifestSha256 }
    Write-LuiImmutableJson (Join-Path $checks "$Kind.json") $record
}

Push-Location $root
try {
    if ($Suite -eq 'Managed') {
        Invoke-RecordedCheck managed repository-managed { ./tools/Test-Repository.ps1 -Suite Managed }
        if (@(& git -C $root status --porcelain --untracked-files=no).Count -ne 0 -or $LASTEXITCODE) {
            throw 'Release checks changed tracked source.'
        }
        return
    }

    ./tools/Pack-Packages.ps1 -Version $Version
    $serverStage = Join-Path $root "artifacts/release-server-$Version"
    if (Test-Path -LiteralPath $serverStage) { throw 'Server staging directory already exists.' }
    dotnet restore src/Lucent.Lui.LanguageServer/Lucent.Lui.LanguageServer.csproj --locked-mode
    if ($LASTEXITCODE) { throw 'Server restore failed.' }
    dotnet publish src/Lucent.Lui.LanguageServer/Lucent.Lui.LanguageServer.csproj -c Release --no-restore -o $serverStage
    if ($LASTEXITCODE) { throw 'Server publication failed.' }
    ./tools/Pack-LuiServer.ps1 -ServerDirectory $serverStage -OutputPath (Join-Path $directory 'server.zip')
    $helperStage = Join-Path $root "artifacts/release-cache-helper-$Version"
    if (Test-Path -LiteralPath $helperStage) { throw 'Cache helper staging directory already exists.' }
    dotnet restore src/Lucent.Tooling.Cache/Lucent.Tooling.Cache.csproj --locked-mode
    if ($LASTEXITCODE) { throw 'Cache helper restore failed.' }
    dotnet publish src/Lucent.Tooling.Cache/Lucent.Tooling.Cache.csproj -c Release --no-restore -p:UseAppHost=false -o $helperStage
    if ($LASTEXITCODE) { throw 'Cache helper publication failed.' }
    $doctorStage = Join-Path $root "artifacts/release-doctor-$Version"
    if (Test-Path -LiteralPath $doctorStage) { throw 'Doctor staging directory already exists.' }
    dotnet restore src/Lucent.Tools/Lucent.Tools.csproj --locked-mode
    if ($LASTEXITCODE) { throw 'Doctor restore failed.' }
    dotnet publish src/Lucent.Tools/Lucent.Tools.csproj -c Release --no-restore -p:UseAppHost=false -o $doctorStage
    if ($LASTEXITCODE) { throw 'Doctor publication failed.' }
    ./tools/Pack-LuiExtension.ps1 -ServerArchivePath (Join-Path $directory 'server.zip') -ServerDirectory $serverStage -CacheHelperDirectory $helperStage -DoctorDirectory $doctorStage -OutputPath (Join-Path $directory 'extension.vsix')
    $candidatePath = Join-Path $directory 'candidate.json'
    $candidate = New-LuiReleaseSet -Directory $directory -Version $Version -SourceCommit $commit -SourceState clean -ServerArchive server.zip -Vsix extension.vsix -OutputPath $candidatePath

    Invoke-RecordedCheck native repository-native { ./tools/Test-Repository.ps1 -Suite Native }
    Invoke-RecordedCheck packages package-consumers {
        ./tools/Test-AuthoringPackages.ps1 -Version $Version -Feed $directory
        ./tests/Probes/Navigation/AotHost/Test-Package.ps1 -Version $Version -Feed $directory
        ./tests/Probes/ContextNavigation/Hosted/Test-Package.ps1 -Version $Version -Feed $directory
        ./tools/Verify-LuiAssets.ps1 -Version $Version -Feed $directory
        ./tools/Test-HeadlessPackages.ps1 -Version $Version -Feed $directory
        ./tools/Test-Packages.ps1 -Version $Version -Feed $directory
        ./tools/Test-Templates.ps1 -DescriptorPath $candidatePath -ArtifactDirectory $directory
    }
    Invoke-RecordedCheck server published-server {
        # Execute the actual archive, not the source-tree language-server binary.
        $deployment = Join-Path $root "artifacts/release-server-check-$Version"
        if (Test-Path -LiteralPath $deployment) { throw 'Server proof directory already exists.' }
        Expand-Archive -LiteralPath (Join-Path $directory 'server.zip') -DestinationPath $deployment
        $previousServer = $env:LUCENT_LSP_SERVER_DLL
        try {
            $env:LUCENT_LSP_SERVER_DLL = Join-Path $deployment 'Lucent.Lui.LanguageServer.dll'
            ./tools/Test-Repository.ps1 -Project Lucent.Lui.LanguageServer.Tests -Filter 'FullyQualifiedName~ServerIdentityContracts|FullyQualifiedName~ProtocolLifecycleRejectsInvalidMessageOrder|FullyQualifiedName~ProtocolFormatsWithoutProjectAndRejectsUnavailableOrObsoleteSnapshots'
        }
        finally { $env:LUCENT_LSP_SERVER_DLL = $previousServer }
    }
    Invoke-RecordedCheck extension packaged-extension {
        node --test extensions/lucent-lui-vscode/extension.test.cjs extensions/lucent-lui-vscode/server-bundle.test.cjs extensions/lucent-lui-vscode/server-cache.test.cjs extensions/lucent-lui-vscode/server-acquisition.test.cjs extensions/lucent-lui-vscode/managed-tool.test.cjs extensions/lucent-lui-vscode/doctor-client.test.cjs extensions/lucent-lui-vscode/onboarding-ui.test.cjs extensions/lucent-lui-vscode/environment-ui.test.cjs
        if ($LASTEXITCODE) { throw 'Extension tests failed.' }
        ./tools/Test-LuiReleaseCatalog.ps1
        ./tools/Test-ReleaseSet.ps1 -RunFixtures -ServerArchivePath (Join-Path $directory 'server.zip') -VsixPath (Join-Path $directory 'extension.vsix')
        ./tools/Test-ReleaseSet.ps1 -DescriptorPath $candidatePath -ArtifactDirectory $directory
    }
    # All receipts must describe the same immutable bytes after the checks finish.
    Assert-LuiReleaseSet $candidate $directory
    if (@(& git -C $root status --porcelain --untracked-files=no).Count -ne 0 -or $LASTEXITCODE) {
        throw 'Release checks changed tracked source.'
    }
}
finally { Pop-Location }
