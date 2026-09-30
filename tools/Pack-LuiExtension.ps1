[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ServerArchivePath,
    [Parameter(Mandatory)][string] $ServerDirectory,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ReleaseSet.psm1') -Force

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$extensionRoot = Join-Path $repoRoot 'extensions/lucent-lui-vscode'
$manifestPath = Join-Path $extensionRoot 'package.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$toolRoot = Join-Path $PSScriptRoot 'vsce'
$serverArchive = (Resolve-Path -LiteralPath $ServerArchivePath).Path
$serverDirectory = (Resolve-Path -LiteralPath $ServerDirectory).Path
$bundle = Get-LuiServerBundle $serverArchive
$sourceCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $sourceCommit -cne $bundle.identity.sourceCommit) { throw 'Extension and server archive source commits differ.' }
$archive = Open-LuiArchive $serverArchive
try {
    $inventory = Read-LuiArchiveJson $archive 'lucent-server-files.json'
    $publishedNames = @($inventory.files | Where-Object fileName -notin @('lucent-server.json') | ForEach-Object fileName)
    $actualNames = @(Get-ChildItem -LiteralPath $serverDirectory -File -Recurse -Force | ForEach-Object {
        [IO.Path]::GetRelativePath($serverDirectory, $_.FullName).Replace('\', '/')
    })
    if ((ConvertTo-LuiCanonicalJson @($publishedNames | Sort-Object -CaseSensitive)) -cne (ConvertTo-LuiCanonicalJson @($actualNames | Sort-Object -CaseSensitive))) {
        throw 'Published server directory and validated archive contain different files.'
    }
    foreach ($file in $inventory.files) {
        if ($file.fileName -eq 'lucent-server.json') { continue }
        $path = Resolve-LuiArtifactPath $serverDirectory $file.fileName
        if ((Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -cne $file.sha256) {
            throw "Published server directory differs from validated archive: $($file.fileName)"
        }
    }
}
finally { $archive.Zip.Dispose() }

if (!$OutputPath) {
    $OutputPath = Join-Path $repoRoot "artifacts/lucent-lui-vscode/$($manifest.name)-$($manifest.version).vsix"
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path $OutputPath -Parent
$stageRoot = Join-Path $repoRoot "artifacts/lui-extension-stage-$([Guid]::NewGuid().ToString('N'))"

New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

try {
    $runtimeFiles = @(
        'package.json',
        'extension.js',
        'server-bundle.js',
        'language-configuration.json',
        'README.md'
    )
    foreach ($relativePath in $runtimeFiles) {
        Copy-Item (Join-Path $extensionRoot $relativePath) (Join-Path $stageRoot $relativePath)
    }
    $stagedManifest = Get-Content (Join-Path $stageRoot 'package.json') -Raw | ConvertFrom-Json -AsHashtable
    $stagedManifest.lucentRelease.serverDelivery = 'bundled'
    $stagedManifest.lucentRelease.sourceCommit = $sourceCommit
    $stagedManifest.lucentRelease.bundledServer = $bundle
    $stagedManifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $stageRoot 'package.json') -Encoding utf8
    $serverStage = Join-Path $stageRoot 'server'
    [IO.Directory]::CreateDirectory($serverStage) | Out-Null
    $archive = Open-LuiArchive $serverArchive
    try {
        foreach ($entry in $archive.Entries.Keys) {
            $destination = Join-Path $serverStage $entry
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            [IO.File]::WriteAllBytes($destination, (Read-LuiArchiveBytes $archive $entry))
        }
    }
    finally { $archive.Zip.Dispose() }
    Copy-Item (Join-Path $extensionRoot 'syntaxes') (Join-Path $stageRoot 'syntaxes') -Recurse
    Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $stageRoot 'LICENSE')

    $npmCache = Join-Path $repoRoot 'artifacts/npm-cache-vsce'
    $previousNpmCache = $env:NPM_CONFIG_CACHE
    $env:NPM_CONFIG_CACHE = $npmCache
    try {
        & npm ci --prefix $toolRoot --ignore-scripts --no-audit --no-fund
        if ($LASTEXITCODE) { throw 'Locked VSCE tool restore failed.' }
        $vsce = Join-Path $toolRoot 'node_modules/@vscode/vsce/vsce'
        Push-Location $stageRoot
        try {
            $vsceOutput = & node $vsce package `
                --no-dependencies `
                --out $OutputPath 2>&1
            $vsceExitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }
    finally {
        $env:NPM_CONFIG_CACHE = $previousNpmCache
    }

    $vsceOutput | Write-Output
    if ($vsceExitCode -ne 0) {
        throw "vsce package failed with exit code $vsceExitCode."
    }
    if (!(Test-Path $OutputPath -PathType Leaf)) {
        throw "vsce did not produce the requested VSIX '$OutputPath'."
    }

    Write-Output "VSIX: $OutputPath"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        $resolvedStage = (Resolve-Path -LiteralPath $stageRoot).Path
        $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
        if (!$resolvedStage.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove extension staging outside '$artifactsRoot'."
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
