#requires -Version 7.4
<#
.SYNOPSIS
Validates a descriptor against its local artifacts, or runs isolated rejection fixtures.
.DESCRIPTION
This is an offline integrity/compatibility check. A complete descriptor and its receipts
must arrive through an independently authenticated CI artifact; JSON and hashes alone
do not authenticate their publisher. This command never acquires or executes artifacts.
#>
[CmdletBinding(DefaultParameterSetName = 'Validate')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Validate')][string] $DescriptorPath,
    [Parameter(Mandatory, ParameterSetName = 'Validate')][string] $ArtifactDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Fixtures')][switch] $RunFixtures,
    [Parameter(Mandatory, ParameterSetName = 'Fixtures')][string] $ServerArchivePath,
    [Parameter(Mandatory, ParameterSetName = 'Fixtures')][string] $VsixPath,
    [Parameter(ParameterSetName = 'Fixtures')][string] $OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseSet.psm1') -Force
if (!$RunFixtures) {
    $descriptor = Get-Content -LiteralPath $DescriptorPath -Raw | ConvertFrom-Json -AsHashtable
    Assert-LuiReleaseSet $descriptor $ArtifactDirectory
    Write-Output "Verified $($descriptor.status) descriptor against local bytes: $DescriptorPath"
    return
}

function Assert-True([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}
function Copy-Json($Value) { ConvertTo-LuiCanonicalJson $Value | ConvertFrom-Json -AsHashtable }
function Json-Bytes($Value) { return ,([Text.Encoding]::UTF8.GetBytes((ConvertTo-LuiCanonicalJson $Value))) }
function Add-ZipEntry($Zip, [string] $Name, [byte[]] $Bytes, [int] $Attributes = 0) {
    $entry = $Zip.CreateEntry($Name)
    $entry.ExternalAttributes = $Attributes
    $stream = $entry.Open()
    try { $stream.Write($Bytes, 0, $Bytes.Length) } finally { $stream.Dispose() }
}
function Write-Zip([string] $Path, $Entries) {
    $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try { foreach ($entry in $Entries) { Add-ZipEntry $zip $entry.name $entry.bytes $entry.attributes } }
    finally { $zip.Dispose() }
}
function Entry([string] $Name, [byte[]] $Bytes, [int] $Attributes = 0) {
    return @{ name = $Name; bytes = $Bytes; attributes = $Attributes }
}
function Replace-Entries([string] $Path, [hashtable] $Changes, [bool] $UpdateInventory = $false) {
    $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Update)
    try {
        foreach ($name in $Changes.Keys) {
            $old = $zip.GetEntry($name)
            if ($old) { $old.Delete() }
            if ($null -ne $Changes[$name]) { Add-ZipEntry $zip $name $Changes[$name] }
        }
        if ($UpdateInventory) {
            $zip.GetEntry('lucent-server-files.json').Delete()
            $files = @()
            foreach ($entry in $zip.Entries) {
                $stream = $entry.Open()
                try {
                    $length = $stream.Length
                    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
                }
                finally { $stream.Dispose() }
                $files += @{ fileName = $entry.FullName; bytes = $length; sha256 = $hash }
            }
            Add-ZipEntry $zip 'lucent-server-files.json' (Json-Bytes @{ schemaVersion = 1; files = $files })
        }
    }
    finally { $zip.Dispose() }
}
function Expect-Rejected([string] $Name, [scriptblock] $Action, [string] $Pattern = '.') {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw "$Name rejected for the wrong reason: $($_.Exception.Message)" }
        $script:results.Add(@{ name = $Name; outcome = 'rejected'; message = $_.Exception.Message })
        Write-Output "PASS rejection: $Name"
        return
    }
    throw "Fixture was incorrectly accepted: $Name"
}
function Check-DescriptorMutation([string] $Name, [scriptblock] $Mutate, [string] $Pattern = '.') {
    $changed = Copy-Json $candidate
    & $Mutate $changed
    Expect-Rejected $Name { Assert-LuiReleaseSet $changed $fixture } $Pattern
}
function Check-ServerMutation([string] $Name, [hashtable] $Changes, [bool] $Inventory, [string] $Pattern) {
    $path = Join-Path $rejections "$Name.zip"
    Copy-Item -LiteralPath (Join-Path $fixture 'server.zip') -Destination $path
    Replace-Entries $path $Changes $Inventory
    Expect-Rejected $Name { Get-LuiServerArchive $path } $Pattern
}
function Package-Bytes([string] $Id, [string] $PackageVersion = $version, [string] $Commit = $source, [string] $DependencyVersion = $version) {
    $xml = "<package><metadata><id>$Id</id><version>$PackageVersion</version><repository type=`"git`" commit=`"$Commit`"/><dependencies><dependency id=`"Lucent.Core`" version=`"$DependencyVersion`"/></dependencies></metadata></package>"
    # NuGet emits a UTF-8 preamble; use actual producer encoding in these fixtures.
    return ,([Text.Encoding]::UTF8.GetPreamble() + [Text.Encoding]::UTF8.GetBytes($xml))
}

if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "../artifacts/release-set-fixtures/$([Guid]::NewGuid().ToString('N'))" }
$fixture = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $fixture) { throw 'Fixture output must be a new directory.' }
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$rejections = Join-Path $fixture 'rejections'
[IO.Directory]::CreateDirectory($rejections) | Out-Null
Set-Content -LiteralPath (Join-Path $fixture 'FIXTURES.txt') -Value 'Synthetic packages and completion receipts for validator tests only. These are not product release evidence.'
$results = [Collections.Generic.List[object]]::new()
$protected = @('global.json', 'Directory.Packages.props', 'eng/Packages.props', 'nuget.config', 'extensions/lucent-lui-vscode/package.json') |
    ForEach-Object { Join-Path $PSScriptRoot "../$_" } | Where-Object { Test-Path -LiteralPath $_ }
$before = @($protected | Get-FileHash)
Copy-Item -LiteralPath $ServerArchivePath -Destination (Join-Path $fixture 'server.zip')
Copy-Item -LiteralPath $VsixPath -Destination (Join-Path $fixture 'extension.vsix')
$server = Get-LuiServerArchive (Join-Path $fixture 'server.zip')
$source = $server.sourceCommit
$version = '0.3.0-dev.fixture.1'
$archive = Open-LuiArchive (Join-Path $fixture 'server.zip')
try { $compiler = Read-LuiArchiveBytes $archive 'Lucent.Lui.Compiler.dll' }
finally { $archive.Zip.Dispose() }
$ids = Get-Content (Join-Path $PSScriptRoot 'package-set.json') -Raw | ConvertFrom-Json
foreach ($id in $ids) {
    $entries = @(Entry "$id.nuspec" (Package-Bytes $id))
    if ($id -eq 'Lucent.Lui.Sdk') {
        $entries += Entry 'analyzers/dotnet/cs/Lucent.Lui.Compiler.dll' $compiler
        $entries += Entry 'tools/net10.0/Lucent.Lui.Compiler.dll' $compiler
    }
    Write-Zip (Join-Path $fixture "$id.$version.nupkg") $entries
}
$candidatePath = Join-Path $fixture 'candidate.fixture.json'
$candidate = New-LuiReleaseSet -Directory $fixture -Version $version -SourceCommit $source -SourceState clean -ServerArchive server.zip -Vsix extension.vsix -OutputPath $candidatePath
Assert-True ($candidate.status -ceq 'candidate' -and $candidate.evidence.Count -eq 0 -and $null -eq $candidate.provenance) 'Candidate falsely claimed completion.'
Assert-True (@($candidate.packages | Where-Object id -eq 'Lucent.Lui.Sdk').Count -eq 1) 'SDK was not recorded exactly once.'
Assert-True ($candidate.server.identity.compiler.sha256 -ceq [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($compiler)).ToLowerInvariant()) 'Recorded compiler hash differs from the actual PE bytes.'
$candidateHash = (Get-FileHash -LiteralPath $candidatePath).Hash
$results.Add(@{ name = 'actual-server-and-vsix-with-synthetic-package-metadata'; outcome = 'passed' })
Expect-Rejected 'immutable-descriptor-output' {
    New-LuiReleaseSet -Directory $fixture -Version $version -SourceCommit $source -SourceState clean -ServerArchive server.zip -Vsix extension.vsix -OutputPath $candidatePath
} 'Immutable output'
Assert-True ((Get-FileHash -LiteralPath $candidatePath).Hash -ceq $candidateHash) 'An existing descriptor was modified.'

Check-DescriptorMutation 'unknown-schema' { param($d) $d.schemaVersion = 99 }
Check-DescriptorMutation 'unknown-field' { param($d) $d['unrecognized'] = $true }
Check-DescriptorMutation 'unsupported-protocol' { param($d) $d.protocol.major = 99 } 'protocol policy'
Check-DescriptorMutation 'unsupported-language' { param($d) $d.language.featureLevel = 'unknown' } 'language policy'
Check-DescriptorMutation 'unsupported-rid' { param($d) $d.targets = @('linux-x64') }
Check-DescriptorMutation 'unsupported-sdk' { param($d) $d.sdk.version = '99.0.100' } 'SDK selection'
Check-DescriptorMutation 'wrong-artifact-length' { param($d) $d.server.artifact.bytes++ } 'archive bytes'
Check-DescriptorMutation 'wrong-artifact-hash' { param($d) $d.extension.artifact.sha256 = '0' * 64 } 'VSIX bytes'
Check-DescriptorMutation 'artifact-path-escape' { param($d) $d.server.artifact.fileName = '../server.zip' } 'Unsafe artifact path'
Check-DescriptorMutation 'mixed-source-identity' { param($d) $d.releaseSet.sourceCommit = '0' * 40 } 'Mixed server/package'
Check-DescriptorMutation 'stale-input-hash' { param($d) $d.inputManifestSha256 = '0' * 64 } 'input identity'
Check-DescriptorMutation 'completion-without-receipts' { param($d) $d.status = 'complete' }

$corePath = Join-Path $fixture "Lucent.Core.$version.nupkg"
$coreBytes = [IO.File]::ReadAllBytes($corePath)
try {
    Move-Item -LiteralPath $corePath -Destination (Join-Path $rejections 'missing-core.nupkg')
    $output = Join-Path $fixture 'must-not-exist.json'
    Expect-Rejected 'missing-package-no-partial-output' {
        New-LuiReleaseSet -Directory $fixture -Version $version -SourceCommit $source -SourceState clean -ServerArchive server.zip -Vsix extension.vsix -OutputPath $output
    } 'Missing package: Lucent.Core'
    Assert-True (!(Test-Path -LiteralPath $output)) 'A rejected set produced partial output.'
}
finally { [IO.File]::WriteAllBytes($corePath, $coreBytes) }
foreach ($case in @(
    @{ name = 'nuspec-id'; bytes = Package-Bytes 'Lucent.Other' },
    @{ name = 'nuspec-version'; bytes = Package-Bytes 'Lucent.Core' '0.3.0-dev.wrong' },
    @{ name = 'nuspec-source'; bytes = Package-Bytes 'Lucent.Core' $version ('0' * 40) },
    @{ name = 'dependency-version'; bytes = Package-Bytes 'Lucent.Core' $version $source '0.3.0-dev.wrong' }
)) {
    try {
        Replace-Entries $corePath @{ 'Lucent.Core.nuspec' = $case.bytes }
        Expect-Rejected $case.name { Get-LuiPackageInventory $fixture $version $source $server.compiler.sha256 } 'Package identity|Mixed Lucent'
    }
    finally { [IO.File]::WriteAllBytes($corePath, $coreBytes) }
}
$extra = Join-Path $fixture "Lucent.Extra.$version.nupkg"
try {
    [IO.File]::WriteAllBytes($extra, $coreBytes)
    Expect-Rejected 'extra-package' { Get-LuiPackageInventory $fixture $version $source $server.compiler.sha256 } 'Unexpected package'
}
finally { Remove-Item -LiteralPath $extra }
$sdkPath = Join-Path $fixture "Lucent.Lui.Sdk.$version.nupkg"
$sdkBytes = [IO.File]::ReadAllBytes($sdkPath)
try {
    Replace-Entries $sdkPath @{ 'tools/net10.0/Lucent.Lui.Compiler.dll' = [Text.Encoding]::UTF8.GetBytes('another compiler') }
    Expect-Rejected 'second-sdk-compiler-disagrees' { Get-LuiPackageInventory $fixture $version $source $server.compiler.sha256 } 'SDK/server compiler mismatch'
}
finally { [IO.File]::WriteAllBytes($sdkPath, $sdkBytes) }

foreach ($case in @(
    @{ name = 'archive-traversal'; entries = @(Entry '../outside' ([byte[]]@(1))) },
    @{ name = 'archive-rooted'; entries = @(Entry '/outside' ([byte[]]@(1))) },
    @{ name = 'archive-drive'; entries = @(Entry 'C:/outside' ([byte[]]@(1))) },
    @{ name = 'archive-backslash'; entries = @(Entry 'a\b' ([byte[]]@(1))) },
    @{ name = 'archive-case-collision'; entries = @((Entry 'A' ([byte[]]@(1))), (Entry 'a' ([byte[]]@(2)))) },
    @{ name = 'archive-symlink'; entries = @(Entry 'link' ([byte[]]@(1)) (0xa000 -shl 16)) },
    @{ name = 'archive-parent-file'; entries = @((Entry 'a' ([byte[]]@(1))), (Entry 'a/b' ([byte[]]@(2)))) }
)) {
    $path = Join-Path $rejections ($case.name + '.zip')
    Write-Zip $path $case.entries
    Expect-Rejected $case.name { $opened = Open-LuiArchive $path; $opened.Zip.Dispose() } 'Unsafe|Duplicate|Unsupported|parent directory'
}
Check-ServerMutation 'server-missing-notice' @{ 'notices/Roslyn-LICENSE.txt' = $null } $true 'Missing server deployment file'
Check-ServerMutation 'server-missing-runtime-dependency' @{ 'Microsoft.CodeAnalysis.dll' = $null } $true 'Missing server dependency'
Check-ServerMutation 'server-inventory-mismatch' @{ 'LICENSE' = [byte[]]@(1, 2) } $false 'inventory mismatch'
$forged = Copy-Json $server
$forged.server.informationalVersion = '99.0.0+' + $source
Check-ServerMutation 'server-false-self-description' @{ 'lucent-server.json' = Json-Bytes $forged } $true 'assembly identity mismatch'

$vsixArchive = Open-LuiArchive (Join-Path $fixture 'extension.vsix')
try { $manifest = Read-LuiArchiveJson $vsixArchive 'extension/package.json' }
finally { $vsixArchive.Zip.Dispose() }
foreach ($case in @(
    @{ name = 'vsix-protocol'; mutate = { param($m) $m.lucentRelease.protocol.minimum.major = 99 }; pattern = 'protocol compatibility' },
    @{ name = 'vsix-publisher'; mutate = { param($m) $m.publisher = 'someone-else' }; pattern = 'VSIX identity mismatch' },
    @{ name = 'vsix-version'; mutate = { param($m) $m.version = '99.0.0' }; pattern = 'VSIX identity mismatch' },
    @{ name = 'vsix-unsafe-main'; mutate = { param($m) $m.main = './../outside.js' }; pattern = 'Unsafe artifact path' }
)) {
    $changed = Copy-Json $manifest
    & $case.mutate $changed
    $path = Join-Path $rejections ($case.name + '.vsix')
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/package.json' = Json-Bytes $changed }
    Expect-Rejected $case.name { Get-LuiVsix $path } $case.pattern
}
$path = Join-Path $rejections 'vsix-hidden-server.vsix'
Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
Replace-Entries $path @{ 'extension/server/Lucent.Lui.LanguageServer.dll' = [byte[]]@(1) }
Expect-Rejected 'vsix-hidden-server' { Get-LuiVsix $path } $(if ($manifest.lucentRelease.serverDelivery -ceq 'bundled') { 'inventory mismatch' } else { 'undeclared bundled server' })
if ($manifest.lucentRelease.serverDelivery -ceq 'bundled') {
    $changed = Copy-Json $manifest
    $changed.lucentRelease.serverDelivery = 'external-path'
    $path = Join-Path $rejections 'vsix-false-external-claim.vsix'
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/package.json' = Json-Bytes $changed }
    Expect-Rejected 'vsix-false-external-claim' { Get-LuiVsix $path } 'undeclared bundled server'
    $path = Join-Path $rejections 'vsix-tampered-bundled-server.vsix'
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/server/Lucent.Lui.LanguageServer.dll' = [byte[]]@(1) }
    Expect-Rejected 'vsix-tampered-bundled-server' { Get-LuiVsix $path } 'inventory mismatch'
    $path = Join-Path $rejections 'vsix-missing-bundled-notice.vsix'
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/server/notices/Roslyn-LICENSE.txt' = $null }
    Expect-Rejected 'vsix-missing-bundled-notice' { Get-LuiVsix $path } 'Missing archive entry'
}
else {
    $changed = Copy-Json $manifest
    $changed.lucentRelease.serverDelivery = 'bundled'
    $path = Join-Path $rejections 'vsix-false-bundled-claim.vsix'
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/package.json' = Json-Bytes $changed }
    Expect-Rejected 'vsix-false-bundled-claim' { Get-LuiVsix $path } 'extension language compatibility|extension/server/lucent-server.json|Missing archive entry'
}
foreach ($declared in @('extension/extension.js', 'extension/language-configuration.json', 'extension/syntaxes/lui.tmLanguage.json')) {
    $path = Join-Path $rejections ('vsix-missing-' + [IO.Path]::GetFileName($declared) + '.vsix')
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ $declared = $null }
    Expect-Rejected "vsix-missing-$declared" { Get-LuiVsix $path } 'Missing declared VSIX entry'
}
$path = Join-Path $rejections 'vsix-invalid-grammar.vsix'
Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
Replace-Entries $path @{ 'extension/syntaxes/lui.tmLanguage.json' = [Text.Encoding]::UTF8.GetBytes('invalid JSON') }
Expect-Rejected 'vsix-invalid-grammar' { Get-LuiVsix $path } 'JSON'

if ($manifest['lucentCacheHelper']) {
    foreach ($case in @(
        @{ name = 'vsix-missing-cache-helper'; entry = 'extension/tooling-cache/Lucent.Tooling.Cache.dll'; bytes = $null; pattern = 'cache helper payload' },
        @{ name = 'vsix-tampered-cache-helper'; entry = 'extension/tooling-cache/Lucent.Tooling.Cache.dll'; bytes = [byte[]]@(1); pattern = 'Cache helper bytes' },
        @{ name = 'vsix-extra-cache-helper-file'; entry = 'extension/tooling-cache/extra.dll'; bytes = [byte[]]@(1); pattern = 'cache helper payload' },
        @{ name = 'vsix-missing-cache-module'; entry = 'extension/server-cache.js'; bytes = $null; pattern = 'Missing declared VSIX entry' },
        @{ name = 'vsix-missing-acquisition-module'; entry = 'extension/server-acquisition.js'; bytes = $null; pattern = 'Missing declared VSIX entry' },
        @{ name = 'vsix-missing-project-requirements'; entry = 'extension/project-requirements.js'; bytes = $null; pattern = 'Missing declared VSIX entry' }
    )) {
        $path = Join-Path $rejections ($case.name + '.vsix')
        Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
        Replace-Entries $path @{ $case.entry = $case.bytes }
        Expect-Rejected $case.name { Get-LuiVsix $path } $case.pattern
    }
    $changed = Copy-Json $manifest
    $changed.Remove('lucentCacheHelper')
    $path = Join-Path $rejections 'vsix-no-helper-manifest.vsix'
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/package.json' = Json-Bytes $changed }
    Expect-Rejected 'vsix-no-helper-manifest' { Get-LuiVsix $path } 'no bundled helper'
}
if ($manifest['lucentNuGetDoctor']) {
    $changed = Copy-Json $manifest
    $changed.lucentNuGetDoctor.files = @($changed.lucentNuGetDoctor.files | Where-Object fileName -cne 'notices/NuGet-LICENSE.txt')
    $path = Join-Path $rejections 'vsix-missing-required-nuget-notice.vsix'
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/package.json' = Json-Bytes $changed; 'extension/nuget-doctor/notices/NuGet-LICENSE.txt' = $null }
    Expect-Rejected 'vsix-missing-required-nuget-notice' { Get-LuiVsix $path } 'Required NuGet doctor notice'
    $changed = Copy-Json $manifest
    $staleBytes = [Text.Encoding]::UTF8.GetBytes('obsolete local build output')
    $changed.lucentNuGetDoctor.files += @{ fileName = 'obsolete.dll'; bytes = $staleBytes.Length; sha256 = Get-LuiBytesHash $staleBytes }
    $path = Join-Path $rejections 'vsix-inventoried-stale-nuget-output.vsix'
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/package.json' = Json-Bytes $changed; 'extension/nuget-doctor/obsolete.dll' = $staleBytes }
    Expect-Rejected 'vsix-inventoried-stale-nuget-output' { Get-LuiVsix $path } 'NuGet doctor payload declared runtime and notice closure'
    foreach ($case in @(
        @{ name = 'vsix-tampered-nuget-dependency'; entry = 'extension/nuget-doctor/NuGet.Protocol.dll'; bytes = [byte[]]@(1); pattern = 'NuGet doctor bytes' },
        @{ name = 'vsix-extra-nuget-dependency'; entry = 'extension/nuget-doctor/extra.dll'; bytes = [byte[]]@(1); pattern = 'NuGet doctor payload' }
    )) {
        $path = Join-Path $rejections ($case.name + '.vsix')
        Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
        Replace-Entries $path @{ $case.entry = $case.bytes }
        Expect-Rejected $case.name { Get-LuiVsix $path } $case.pattern
    }
    $changed = Copy-Json $manifest
    $changed.Remove('lucentNuGetDoctor')
    $path = Join-Path $rejections 'vsix-no-nuget-doctor-manifest.vsix'
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/package.json' = Json-Bytes $changed }
    Expect-Rejected 'vsix-no-nuget-doctor-manifest' { Get-LuiVsix $path } 'no bundled NuGet doctor'
}
if ($manifest['lucentDoctor']) {
    foreach ($case in @(
        @{ name = 'vsix-missing-doctor'; entry = 'extension/doctor/Lucent.Tools.dll'; bytes = $null; pattern = 'doctor payload' },
        @{ name = 'vsix-tampered-doctor'; entry = 'extension/doctor/Lucent.Tools.dll'; bytes = [byte[]]@(1); pattern = 'Doctor bytes' },
        @{ name = 'vsix-extra-doctor-file'; entry = 'extension/doctor/extra.dll'; bytes = [byte[]]@(1); pattern = 'doctor payload' },
        @{ name = 'vsix-missing-doctor-client'; entry = 'extension/doctor-client.js'; bytes = $null; pattern = 'Missing declared VSIX entry' },
        @{ name = 'vsix-missing-doctor-setup'; entry = 'extension/onboarding/setup.md'; bytes = $null; pattern = 'Missing declared VSIX entry' }
    )) {
        $path = Join-Path $rejections ($case.name + '.vsix')
        Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
        Replace-Entries $path @{ $case.entry = $case.bytes }
        Expect-Rejected $case.name { Get-LuiVsix $path } $case.pattern
    }
    $changed = Copy-Json $manifest
    $changed.Remove('lucentDoctor')
    $path = Join-Path $rejections 'vsix-no-doctor-manifest.vsix'
    Copy-Item -LiteralPath (Join-Path $fixture 'extension.vsix') -Destination $path
    Replace-Entries $path @{ 'extension/package.json' = Json-Bytes $changed }
    Expect-Rejected 'vsix-no-doctor-manifest' { Get-LuiVsix $path } 'no bundled doctor'
}

# These five receipts are deliberately synthetic: they prove validator boundaries,
# never that managed/native/product checks actually ran or GitHub authenticated them.
$provenance = @{ repository = 'RichiCoder1/lucent'; sourceCommit = $source; workflow = '.github/workflows/tests.yml'; runId = 1; runAttempt = 1; artifactId = 1; artifactDigest = '1' * 64 }
$evidence = @()
foreach ($kind in @('managed', 'native', 'packages', 'server', 'extension')) {
    $receipt = @{ kind = $kind; status = 'passed'; sourceCommit = $source; inputManifestSha256 = $candidate.inputManifestSha256; provenance = $provenance }
    $name = "$kind.fixture-receipt.json"
    [IO.File]::WriteAllBytes((Join-Path $fixture $name), (Json-Bytes $receipt))
    $evidence += @{ kind = $kind; status = 'passed'; sourceCommit = $source; inputManifestSha256 = $candidate.inputManifestSha256; artifact = Get-LuiArtifact $fixture $name }
}
$completion = @{ provenance = $provenance; evidence = $evidence }
$complete = New-LuiReleaseSet -Directory $fixture -Version $version -SourceCommit $source -SourceState clean -ServerArchive server.zip -Vsix extension.vsix -OutputPath (Join-Path $fixture 'complete.fixture.json') -Completion $completion
Assert-True ($complete.status -ceq 'complete' -and $complete.evidence.Count -eq 5) 'Complete fixture did not retain all evidence.'
Assert-True ((Get-FileHash -LiteralPath $candidatePath).Hash -ceq $candidateHash) 'Completing modified the immutable candidate.'
$results.Add(@{ name = 'synthetic-complete-receipts'; outcome = 'passed' })
foreach ($case in @(
    @{ name = 'dirty-complete'; mutate = { param($d) $d.releaseSet.sourceState = 'dirty-development' }; pattern = 'input identity|Only a clean' },
    @{ name = 'stale-evidence'; mutate = { param($d) $d.evidence[0].inputManifestSha256 = '0' * 64 }; pattern = 'different release inputs' },
    @{ name = 'wrong-evidence-source'; mutate = { param($d) $d.evidence[0].sourceCommit = '0' * 40 }; pattern = 'different release inputs' },
    @{ name = 'missing-native-evidence'; mutate = { param($d) $d.evidence = @($d.evidence | Where-Object kind -ne 'native') }; pattern = '.' },
    @{ name = 'duplicate-evidence'; mutate = { param($d) $d.evidence[0] = $d.evidence[1] }; pattern = 'evidence kinds' },
    @{ name = 'wrong-ci-source'; mutate = { param($d) $d.provenance.sourceCommit = '0' * 40 }; pattern = 'different source commit' },
    @{ name = 'changed-ci-artifact'; mutate = { param($d) $d.provenance.artifactId = 2 }; pattern = 'evidence provenance' }
)) {
    $changed = Copy-Json $complete
    & $case.mutate $changed
    Expect-Rejected $case.name { Assert-LuiReleaseSet $changed $fixture } $case.pattern
}
$nativePath = Join-Path $fixture 'native.fixture-receipt.json'
$nativeBytes = [IO.File]::ReadAllBytes($nativePath)
try {
    $receipt = [Text.Encoding]::UTF8.GetString($nativeBytes) | ConvertFrom-Json -AsHashtable
    $receipt.status = 'skipped'
    [IO.File]::WriteAllBytes($nativePath, (Json-Bytes $receipt))
    $changed = Copy-Json $complete
    ($changed.evidence | Where-Object kind -eq 'native').artifact = Get-LuiArtifact $fixture 'native.fixture-receipt.json'
    Expect-Rejected 'receipt-does-not-prove-native-pass' { Assert-LuiReleaseSet $changed $fixture } 'evidence status'
}
finally { [IO.File]::WriteAllBytes($nativePath, $nativeBytes) }

# Exercise the actual finalizer with explicitly synthetic CI records/context.
$managedDirectory = Join-Path $fixture 'managed-artifact'
[IO.Directory]::CreateDirectory((Join-Path $fixture 'checks')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $managedDirectory 'checks')) | Out-Null
$ciNames = @('GITHUB_ACTIONS', 'GITHUB_REPOSITORY', 'GITHUB_REF', 'GITHUB_EVENT_NAME', 'GITHUB_WORKFLOW_REF', 'GITHUB_SHA', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT')
$oldCi = @{}
foreach ($name in $ciNames) { $oldCi[$name] = [Environment]::GetEnvironmentVariable($name) }
$finalPath = Join-Path $fixture 'ci-complete.fixture.json'
$finalArguments = @{ CandidatePath = $candidatePath; ArtifactDirectory = $fixture; ManagedArtifactDirectory = $managedDirectory; InputArtifactId = 1; InputArtifactDigest = '1' * 64; ManagedArtifactId = 2; ManagedArtifactDigest = '2' * 64; OutputPath = $finalPath }
$finalizer = Join-Path $PSScriptRoot 'Complete-ReleaseSet.ps1'
try {
    $env:GITHUB_ACTIONS = 'false'
    Expect-Rejected 'finalizer-outside-ci' { & $finalizer @finalArguments } 'gated main-branch'
    $env:GITHUB_ACTIONS = 'true'
    $env:GITHUB_REPOSITORY = 'RichiCoder1/lucent'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_WORKFLOW_REF = 'RichiCoder1/lucent/.github/workflows/tests.yml@refs/heads/main'
    $env:GITHUB_EVENT_NAME = 'push'
    $env:GITHUB_SHA = $source
    $env:GITHUB_RUN_ID = '1'
    $env:GITHUB_RUN_ATTEMPT = '1'
    $idsByKind = @{ native = 'repository-native'; packages = 'package-consumers'; server = 'published-server'; extension = 'packaged-extension'; managed = 'repository-managed' }
    $records = @{}
    $trx = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult outcome="Passed" /></Results><ResultSummary outcome="Completed"><Counters total="1" passed="1" failed="0" /></ResultSummary></TestRun>'
    [IO.File]::WriteAllText((Join-Path $managedDirectory 'managed.trx'), $trx)
    foreach ($kind in $idsByKind.Keys) {
        $directory = if ($kind -eq 'managed') { $managedDirectory } else { $fixture }
        [IO.File]::WriteAllText((Join-Path $directory "$kind.log"), 'Synthetic successful command output, not product evidence.')
        $record = @{ schemaVersion = 1; kind = $kind; commandId = $idsByKind[$kind]; sourceCommit = $source; runId = 1; runAttempt = 1; exitCode = 0; logs = @(Get-LuiArtifact $directory "$kind.log") }
        if ($kind -eq 'managed') { $record.logs += Get-LuiArtifact $directory 'managed.trx' }
        else { $record['inputManifestSha256'] = $candidate.inputManifestSha256 }
        $path = Join-Path $directory "checks/$kind.json"
        $records[$kind] = @{ path = $path; bytes = Json-Bytes $record }
        [IO.File]::WriteAllBytes($path, $records[$kind].bytes)
    }
    foreach ($case in @(
        @{ name = 'finalizer-failed-command'; kind = 'native'; mutate = { param($r) $r.exitCode = 1 }; pattern = 'fixed CI command' },
        @{ name = 'finalizer-pass-boolean'; kind = 'native'; mutate = { param($r) $r.exitCode = $true }; pattern = 'fixed CI command' },
        @{ name = 'finalizer-arbitrary-command'; kind = 'server'; mutate = { param($r) $r.commandId = 'identity-only' }; pattern = 'fixed CI command' },
        @{ name = 'finalizer-stale-input'; kind = 'packages'; mutate = { param($r) $r.inputManifestSha256 = '0' * 64 }; pattern = 'different release inputs' },
        @{ name = 'finalizer-other-run'; kind = 'extension'; mutate = { param($r) $r.runAttempt = 2 }; pattern = 'another source or CI run' },
        @{ name = 'finalizer-other-managed-source'; kind = 'managed'; mutate = { param($r) $r.sourceCommit = '0' * 40 }; pattern = 'another source or CI run' },
        @{ name = 'finalizer-missing-trx'; kind = 'managed'; mutate = { param($r) $r.logs = @($r.logs | Where-Object { !$_.fileName.EndsWith('.trx') }) }; pattern = 'omitted its TRX' },
        @{ name = 'finalizer-changed-log'; kind = 'extension'; mutate = { param($r) $r.logs[0].sha256 = '0' * 64 }; pattern = 'Changed check output' }
    )) {
        $record = [Text.Encoding]::UTF8.GetString($records[$case.kind].bytes) | ConvertFrom-Json -AsHashtable
        & $case.mutate $record
        try {
            [IO.File]::WriteAllBytes($records[$case.kind].path, (Json-Bytes $record))
            Expect-Rejected $case.name { & $finalizer @finalArguments } $case.pattern
            Assert-True (!(Test-Path -LiteralPath $finalPath) -and !(Test-Path -LiteralPath (Join-Path $fixture 'completion'))) 'Finalizer wrote partial output for rejected evidence.'
        }
        finally { [IO.File]::WriteAllBytes($records[$case.kind].path, $records[$case.kind].bytes) }
    }
    try {
        [IO.File]::WriteAllText((Join-Path $managedDirectory 'managed.trx'), $trx.Replace('outcome="Passed"', 'outcome="Failed"'))
        $record = [Text.Encoding]::UTF8.GetString($records.managed.bytes) | ConvertFrom-Json -AsHashtable
        $record.logs[1] = Get-LuiArtifact $managedDirectory 'managed.trx'
        [IO.File]::WriteAllBytes($records.managed.path, (Json-Bytes $record))
        Expect-Rejected 'finalizer-failed-trx-with-fresh-hash' { & $finalizer @finalArguments } 'Managed report did not pass'
    }
    finally {
        [IO.File]::WriteAllText((Join-Path $managedDirectory 'managed.trx'), $trx)
        [IO.File]::WriteAllBytes($records.managed.path, $records.managed.bytes)
    }
    & $finalizer @finalArguments
    $final = Get-Content -LiteralPath $finalPath -Raw | ConvertFrom-Json -AsHashtable
    Assert-True ($final.status -ceq 'complete' -and $final.provenance.artifactId -eq 1) 'Finalizer did not retain the uploaded input artifact identity.'
    $managedReceipt = Get-Content -LiteralPath (Join-Path $fixture 'completion/managed.json') -Raw | ConvertFrom-Json -AsHashtable
    Assert-True ($managedReceipt.managedArtifact.id -eq 2 -and $managedReceipt.execution.commandId -ceq 'repository-managed') 'Finalizer lost independent managed-job provenance.'
    $results.Add(@{ name = 'synthetic-ci-finalizer'; outcome = 'passed' })
    Expect-Rejected 'finalizer-immutable-complete' { & $finalizer @finalArguments } 'Immutable complete descriptor'
}
finally {
    foreach ($name in $ciNames) {
        if ($null -eq $oldCi[$name]) {
            Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $oldCi[$name])
        }
    }
}
Assert-LuiReleaseSet $candidate $fixture
foreach ($file in $before) {
    Assert-True ((Get-FileHash -LiteralPath $file.Path).Hash -ceq $file.Hash) "Project selection input changed: $($file.Path)"
}
Assert-True (@(Get-ChildItem -LiteralPath $fixture -Filter '.lucent-release-*').Count -eq 0) 'Temporary descriptor files leaked.'
Write-LuiImmutableJson (Join-Path $fixture 'results.json') @{ fixtureOnly = $true; server = $candidate.server.artifact; extension = $candidate.extension.artifact; cases = @($results.ToArray()) }
Write-Output "$($results.Count) release-set contracts passed. Fixtures retained at $fixture"
