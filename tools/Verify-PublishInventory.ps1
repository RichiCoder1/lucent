param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [switch] $Negative
)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content (Join-Path $PSScriptRoot 'publish-inventory.json') -Raw | ConvertFrom-Json

function Get-RelativeFiles([string] $Directory) {
    $prefix = (Resolve-Path $Directory).Path.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    @(Get-ChildItem $Directory -Force -File -Recurse | ForEach-Object {
        $_.FullName.Substring($prefix.Length).Replace('\', '/')
    } | Sort-Object)
}

function Assert-Inventory([string] $Directory) {
    $expected = @($manifest.files.path | Sort-Object)
    $actual = Get-RelativeFiles $Directory
    if (Compare-Object $expected $actual) {
        throw "Publish inventory mismatch: expected=[$($expected -join ',')]; actual=[$($actual -join ',')]"
    }
    [ordered]@{ ok = $true; files = $actual } | ConvertTo-Json -Compress
}

function Assert-Rejected([string] $Name, [scriptblock] $Mutate) {
    $copy = Join-Path ([IO.Path]::GetTempPath()) ("lucent-inventory-negative-" + [Guid]::NewGuid())
    try {
        Copy-Item $resolved $copy -Recurse
        & $Mutate $copy
        try { Assert-Inventory $copy; throw "$Name was accepted." }
        catch [System.Management.Automation.RuntimeException] {
            if ($_.Exception.Message -notmatch 'Publish inventory mismatch') { throw }
            $Name
        }
    }
    finally { Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue }
}

$resolved = (Resolve-Path $PublishDirectory).Path
if ($Negative) {
    $negativeCases = @()
    $negativeCases += Assert-Rejected 'missing native asset rejected' { param($copy) Remove-Item (Join-Path $copy 'SDL3.dll') -Force }
    $negativeCases += Assert-Rejected 'missing notice rejected' { param($copy) Remove-Item (Join-Path $copy 'notices/SDL3-CS.txt') -Force }
    $negativeCases += Assert-Rejected 'undeclared nested asset rejected' { param($copy) New-Item (Join-Path $copy 'nested') -ItemType Directory | Out-Null; Set-Content (Join-Path $copy 'nested/rogue.bin') rogue }
    $negativeCases += Assert-Rejected 'undeclared nested notice rejected' { param($copy) Set-Content (Join-Path $copy 'notices/rogue.txt') rogue }
    $negativeCases += Assert-Rejected 'undeclared hidden system asset rejected' { param($copy) $rogue = Join-Path $copy 'nested/.rogue.bin'; New-Item (Split-Path $rogue) -ItemType Directory -Force | Out-Null; Set-Content $rogue rogue; (Get-Item $rogue).Attributes = [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System }
    [ordered]@{ ok = $true; negative = $negativeCases } | ConvertTo-Json -Compress
}
else { Assert-Inventory $resolved }
