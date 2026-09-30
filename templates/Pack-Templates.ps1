#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')][string] $Version,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $SourceCommit,
    [string] $DotNetPath = 'dotnet'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$policy = Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputDirectory)
$package = Join-Path $output "Lucent.Templates.$Version.nupkg"
if (Test-Path -LiteralPath $package) { throw "Immutable template package already exists: $package" }
$stage = Join-Path $root ('artifacts/templates-pack/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Lucent.Templates.csproj') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'content') -Destination $stage -Recurse
# A content-only package must not inherit the repository's build graph or dependencies.
'<Project />' | Set-Content -LiteralPath (Join-Path $stage 'Directory.Build.props'), (Join-Path $stage 'Directory.Build.targets'), (Join-Path $stage 'Directory.Packages.props')
'<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $stage 'NuGet.config')
Copy-Item -LiteralPath (Join-Path $root 'global.json') -Destination $stage
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $stage 'content') -File -Recurse -Force) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $text = $text.Replace('__LUCENT_VERSION__', $Version).
        Replace('__DOTNET_SDK_VERSION__', $policy.sdk.version).
        Replace('__DOTNET_ROLL_FORWARD__', $policy.sdk.rollForward).
        Replace('__MSTEST_SDK_VERSION__', $policy.'msbuild-sdks'.'MSTest.Sdk')
    if ($text -match '__(LUCENT|DOTNET|MSTEST)_[A-Z_]+__') { throw "Unstamped template value: $($file.FullName)" }
    [IO.File]::WriteAllText($file.FullName, $text)
}
Push-Location $stage
try {
    & $DotNetPath restore Lucent.Templates.csproj --configfile NuGet.config
    if ($LASTEXITCODE) { throw 'Template content package restore failed.' }
    & $DotNetPath pack Lucent.Templates.csproj -c Release --no-build --no-restore "-p:PackageVersion=$Version" "-p:RepositoryCommit=$SourceCommit" -o packed -m:1 -nr:false
    if ($LASTEXITCODE) { throw 'Template content package pack failed.' }
    [IO.Directory]::CreateDirectory($output) | Out-Null
    [IO.File]::Copy((Join-Path $stage "packed/Lucent.Templates.$Version.nupkg"), $package, $false)
}
finally { Pop-Location }
Write-Output "Template package: $package"
