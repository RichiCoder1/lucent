param([switch] $Negative, [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$forbidden = 'SDL3|SkiaSharp|Windows\.Win32|Microsoft\.Windows\.CsWin32|Lucent\.Platform\.Windows'

function Get-Assets([string] $Project) {
    Get-Content (Join-Path (Split-Path $Project) 'obj/project.assets.json') -Raw | ConvertFrom-Json
}

function Assert-ProjectHasNoForbiddenDependencies([string] $Project) {
    [xml]$xml = Get-Content $Project
    $direct = @($xml.Project.ItemGroup.PackageReference) + @($xml.Project.ItemGroup.ProjectReference) | Where-Object { $_ }
    if ($direct) { throw "Core has a direct dependency reference: $Project" }
    if (((Get-Assets $Project).libraries.psobject.Properties.Name -join "`n") -match $forbidden) {
        throw 'Core project.assets.json resolves a forbidden dependency.'
    }
}

function Assert-ProjectRejected([string] $Project) {
    try {
        Assert-ProjectHasNoForbiddenDependencies $Project
        throw "Injected forbidden dependency was accepted: $Project"
    }
    catch {
        if ($_.Exception.Message -match '^Injected forbidden dependency was accepted:') { throw }
    }
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
        if ($exitCode -eq 0 -or $text -notmatch 'Forbidden Core public API type: .*SDL3.SDL\+WindowFlags' -or $text -notmatch 'Forbidden Core runtime discovery type: System.ComponentModel.TypeDescriptor') {
            throw "Compiled metadata inspection did not report both platform and runtime-discovery fixture violations: $text"
        }
    }
    elseif ($exitCode -ne 0) { throw "Compiled Core public API inspection failed: $($output -join "`n")" }
}

$core = Join-Path $root 'src/Lucent.Core/Lucent.Core.csproj'
Assert-ProjectHasNoForbiddenDependencies $core
Assert-PublicApi (Join-Path $root "src/Lucent.Core/bin/$Configuration/net10.0/Lucent.Core.dll") $false

if ($Negative) {
    $fixtures = Join-Path $root 'tests/ArchitectureFixtures'
    $direct = Join-Path $fixtures 'DirectForbidden/Lucent.Core.DirectForbidden.csproj'
    $transitive = Join-Path $fixtures 'TransitiveForbidden/Lucent.Core.TransitiveForbidden.csproj'
    foreach ($project in @($direct, $transitive)) {
        & $dotnet restore $project --locked-mode; if ($LASTEXITCODE) { throw "Fixture locked restore failed: $project" }
        & $dotnet build $project --no-restore -warnaserror; if ($LASTEXITCODE) { throw "Fixture build failed: $project" }
        Assert-ProjectRejected $project
    }

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
