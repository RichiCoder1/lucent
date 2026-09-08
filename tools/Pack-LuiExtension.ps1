[CmdletBinding()]
param(
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$extensionRoot = Join-Path $repoRoot 'extensions/lucent-lui-vscode'
$manifestPath = Join-Path $extensionRoot 'package.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$vsceVersion = '3.9.2'

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
        'language-configuration.json',
        'README.md'
    )
    foreach ($relativePath in $runtimeFiles) {
        Copy-Item (Join-Path $extensionRoot $relativePath) (Join-Path $stageRoot $relativePath)
    }
    Copy-Item (Join-Path $extensionRoot 'syntaxes') (Join-Path $stageRoot 'syntaxes') -Recurse
    Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $stageRoot 'LICENSE')

    $npmCache = Join-Path $repoRoot "artifacts/npm-cache-vsce-$vsceVersion"
    $previousNpmCache = $env:NPM_CONFIG_CACHE
    $env:NPM_CONFIG_CACHE = $npmCache
    try {
        Push-Location $stageRoot
        try {
            $vsceOutput = & npx --yes "@vscode/vsce@$vsceVersion" package `
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
