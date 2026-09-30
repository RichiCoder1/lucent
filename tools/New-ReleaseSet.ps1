#requires -Version 7.4
<#
.SYNOPSIS
Creates an immutable candidate from existing local packages, server archive and VSIX.
.DESCRIPTION
This command creates candidates only. Complete-ReleaseSet.ps1 finalizes a separate
descriptor from the gated CI publisher's recorded checks and authenticated downloads.
This offline command verifies content and compatibility, not publisher identity.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ArtifactDirectory,
    [Parameter(Mandatory)][ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')][string] $Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $SourceCommit,
    [ValidateSet('clean', 'dirty-development')][string] $SourceState = 'dirty-development',
    [Parameter(Mandatory)][string] $ServerArchive,
    [Parameter(Mandatory)][string] $Vsix,
    [Parameter(Mandatory)][string] $OutputPath
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseSet.psm1') -Force
$descriptor = New-LuiReleaseSet -Directory $ArtifactDirectory -Version $Version -SourceCommit $SourceCommit -SourceState $SourceState -ServerArchive $ServerArchive -Vsix $Vsix -OutputPath $OutputPath
Write-Output "Lucent release descriptor: $($descriptor.status) ($Version, $SourceCommit) -> $OutputPath"
