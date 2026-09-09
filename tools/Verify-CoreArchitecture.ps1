param([switch] $Negative, [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$forbidden = 'SDL3|SkiaSharp|Windows\.Win32|Microsoft\.Windows\.CsWin32|Lucent\.Platform\.Windows'
$forbiddenRuntimeTooling = 'Lucent\.Lui\.(Compiler|Generator)|Microsoft\.CodeAnalysis'
$allowedBuildTools = @(
    (Join-Path $root 'src/Lucent.Lui.Compiler/Lucent.Lui.Compiler.csproj'),
    (Join-Path $root 'src/Lucent.Lui.Generator/Lucent.Lui.Generator.csproj')
) | ForEach-Object { [IO.Path]::GetFullPath($_) }

function Get-Assets([string] $Project) {
    Get-Content (Join-Path (Split-Path $Project) 'obj/project.assets.json') -Raw | ConvertFrom-Json
}

function Assert-ProjectHasNoForbiddenDependencies([string] $Project) {
    [xml]$xml = Get-Content $Project
    $packages = @($xml.Project.ItemGroup.PackageReference) | Where-Object { $_ }
    if ($packages) { throw "Core has a direct dependency reference: $Project" }
    foreach ($reference in @($xml.Project.ItemGroup.ProjectReference) | Where-Object { $_ }) {
        $path = [IO.Path]::GetFullPath((Join-Path (Split-Path $Project) ([string]$reference.Include)))
        $buildOnly = $allowedBuildTools -contains $path -and
            [string]$reference.OutputItemType -eq 'Analyzer' -and
            [string]$reference.ReferenceOutputAssembly -eq 'false' -and
            [string]$reference.PrivateAssets -eq 'all'
        if (-not $buildOnly) { throw "Core has a direct dependency reference: $Project" }
    }
    Assert-AssetsHaveNoForbiddenDependencies $Project
}

function Assert-RuntimeHasNoBuildTooling([string] $AssemblyPath) {
    $dependencies = [IO.Path]::ChangeExtension($AssemblyPath, '.deps.json')
    if (-not (Test-Path -LiteralPath $dependencies -PathType Leaf)) {
        throw "Core runtime dependency manifest is missing: $dependencies"
    }
    if ((Get-Content -LiteralPath $dependencies -Raw) -match $forbiddenRuntimeTooling) {
        throw 'Core runtime output includes compiler, generator or Roslyn tooling.'
    }
}

function Assert-AssetsHaveNoForbiddenDependencies([string] $Project) {
    if (((Get-Assets $Project).libraries.psobject.Properties.Name -join "`n") -match $forbidden) {
        throw 'Core project.assets.json resolves a forbidden dependency.'
    }
}

function Assert-ProjectRejected([string] $Project, [string] $Expected, [switch] $AssetsOnly) {
    try {
        if ($AssetsOnly) { Assert-AssetsHaveNoForbiddenDependencies $Project }
        else { Assert-ProjectHasNoForbiddenDependencies $Project }
    }
    catch {
        if ($_.Exception.Message -ne $Expected) {
            throw "Wrong architecture rejection: expected='$Expected'; actual='$($_.Exception.Message)'"
        }
        Write-Output "Expected rejection: $Expected"
        return
    }
    throw "Injected forbidden dependency was accepted: $Project"
}

function Assert-PublicApi([string] $AssemblyPath, [bool] $ExpectFailure) {
    $probe = Join-Path $root "tests/Lucent.Core.ArchitectureVerifier/bin/$Configuration/net10.0/Lucent.Core.ArchitectureVerifier.dll"
    $prior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $dotnet $probe $AssemblyPath 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $prior
    if ($ExpectFailure) {
        $text = $output -join "`n"
        if ($exitCode -eq 0 -or $text -notmatch 'Forbidden Core public API type: .*SDL3.SDL\+WindowFlags' -or $text -notmatch 'Forbidden Core runtime discovery type: System.ComponentModel.TypeDescriptor' -or $text -notmatch 'Core component factories use an unexpected namespace: Misplaced.Components') {
            throw "Compiled metadata inspection did not report platform, runtime-discovery, and component-namespace fixture violations: $text"
        }
    }
    elseif ($exitCode -ne 0) { throw "Compiled Core public API inspection failed: $($output -join "`n")" }
}

$core = Join-Path $root 'src/Lucent.Core/Lucent.Core.csproj'
Assert-ProjectHasNoForbiddenDependencies $core
$coreAssembly = Join-Path $root "src/Lucent.Core/bin/$Configuration/net10.0/Lucent.Core.dll"
Assert-PublicApi $coreAssembly $false
Assert-RuntimeHasNoBuildTooling $coreAssembly

if ($Negative) {
    $fixtures = Join-Path $root 'tests/ArchitectureFixtures'
    $direct = Join-Path $fixtures 'DirectForbidden/Lucent.Core.DirectForbidden.csproj'
    $transitive = Join-Path $fixtures 'TransitiveForbidden/Lucent.Core.TransitiveForbidden.csproj'
    foreach ($project in @($direct, $transitive)) {
        & $dotnet restore $project --locked-mode; if ($LASTEXITCODE) { throw "Fixture locked restore failed: $project" }
        & $dotnet build $project --no-restore -warnaserror; if ($LASTEXITCODE) { throw "Fixture build failed: $project" }
    }
    Assert-ProjectRejected $direct "Core has a direct dependency reference: $direct"
    Assert-ProjectRejected $transitive 'Core project.assets.json resolves a forbidden dependency.' -AssetsOnly

    $directAssets = Get-Assets $direct
    $directDependencies = $directAssets.project.frameworks.psobject.Properties.Value.dependencies.psobject.Properties.Name
    if (($directDependencies -notcontains 'SDL3-CS') -or (($directAssets.libraries.psobject.Properties.Name -join "`n") -notmatch 'SDL3-CS')) {
        throw 'Direct forbidden dependency was not found in project.assets.json.'
    }
    Assert-PublicApi (Join-Path $fixtures 'DirectForbidden/bin/Debug/net10.0/Lucent.Core.DirectForbidden.dll') $true

    $transitiveAssets = Get-Assets $transitive
    $transitiveDirect = $transitiveAssets.project.frameworks.psobject.Properties.Value.dependencies.psobject.Properties.Name
    if (($transitiveDirect -contains 'SkiaSharp') -or (($transitiveAssets.libraries.psobject.Properties.Name -join "`n") -notmatch 'SkiaSharp')) {
        throw 'Transitive forbidden dependency was not found only through project.assets.json.'
    }
}

[ordered]@{ ok = $true; negative = [bool]$Negative; publicApi = 'compiled metadata'; dependencies = 'project.assets.json' } | ConvertTo-Json -Compress
exit 0
