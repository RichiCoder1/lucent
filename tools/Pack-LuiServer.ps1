#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ServerDirectory,
    [Parameter(Mandatory)][string] $OutputPath,
    [string] $DotNetPath = 'dotnet'
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseSet.psm1') -Force
function Add-Entry([string] $Name, [byte[]] $Bytes) {
    $entry = $zip.CreateEntry($Name, [IO.Compression.CompressionLevel]::Optimal)
    $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $entry.ExternalAttributes = 0
    $entryStream = $entry.Open()
    try { $entryStream.Write($Bytes, 0, $Bytes.Length) } finally { $entryStream.Dispose() }
}
$source = (Resolve-Path -LiteralPath $ServerDirectory).Path
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw "Immutable output already exists: $output" }
if ($output.StartsWith($source.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Server archive output must be outside its input directory.' }
foreach ($reserved in @('lucent-server.json', 'lucent-server-files.json')) {
    if (Test-Path -LiteralPath (Join-Path $source $reserved)) { throw "Server source contains reserved release metadata: $reserved" }
}
$identityText = & $DotNetPath (Join-Path $source 'Lucent.Lui.LanguageServer.dll') --identity
if ($LASTEXITCODE) { throw 'Server identity self-check failed.' }
$identity = ($identityText -join "`n") | ConvertFrom-Json -AsHashtable
$parent = [IO.Path]::GetDirectoryName($output)
[IO.Directory]::CreateDirectory($parent) | Out-Null
$temporary = Join-Path $parent ('.lucent-server-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $files = @()
        foreach ($file in (Get-ChildItem -LiteralPath $source -File -Recurse | Sort-Object FullName -CaseSensitive)) {
            $name = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
            $artifact = Get-LuiArtifact $source $name
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            Add-Entry $name $bytes
            $files += $artifact
        }
        $identityBytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-LuiCanonicalJson $identity) + "`n")
        Add-Entry 'lucent-server.json' $identityBytes
        $files += [ordered]@{ fileName = 'lucent-server.json'; bytes = $identityBytes.Length; sha256 = Get-LuiBytesHash $identityBytes }
        $inventory = [ordered]@{ schemaVersion = 1; files = @($files | Sort-Object fileName -CaseSensitive) }
        Add-Entry 'lucent-server-files.json' ([Text.Encoding]::UTF8.GetBytes((ConvertTo-LuiCanonicalJson $inventory) + "`n"))
    }
    finally { $zip.Dispose(); $stream.Dispose() }
    $null = Get-LuiServerArchive $temporary
    [IO.File]::Move($temporary, $output, $false)
    Write-Output "Lucent server archive: $output"
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
