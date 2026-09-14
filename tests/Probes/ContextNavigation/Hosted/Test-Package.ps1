param(
    [Parameter(Mandatory = $true)] [string] $Feed,
    [Parameter(Mandatory = $true)] [ValidatePattern('^0\.3\.0-[0-9A-Za-z.-]+$')] [string] $Version,
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$dotnet = Join-Path $repo '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$packages = @(
    Join-Path $feedPath "Lucent.Core.$Version.nupkg"
    Join-Path $feedPath "Lucent.Hosting.$Version.nupkg"
    Join-Path $feedPath "Lucent.Lui.Sdk.$Version.nupkg"
)
foreach ($package in $packages) {
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Required candidate package is missing: $package"
    }
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'artifacts\context-navigation-hosted'
}
$run = Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $run 'fixture'
$publish = Join-Path $run 'publish'
$cache = Join-Path $OutputRoot 'cache'
New-Item -ItemType Directory -Path $fixture, $publish, $cache -Force | Out-Null
Copy-Item (Join-Path $repo 'global.json') (Join-Path $run 'global.json')
Get-ChildItem -LiteralPath $PSScriptRoot -File |
    Where-Object { $_.Extension -in '.cs', '.csproj', '.lui' } |
    Copy-Item -Destination $fixture
$project = Join-Path $fixture 'HostedContextNavigation.csproj'
$projectText = [IO.File]::ReadAllText($project)
$projectText = $projectText.Replace(
    '<Project Sdk="Microsoft.NET.Sdk">',
    '<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/' + $Version + '">'
)
[IO.File]::WriteAllText($project, $projectText)
if (Get-ChildItem -LiteralPath $fixture -File -Recurse | Select-String -Pattern 'ProjectReference') {
    throw 'The hosted package fixture contains a forbidden source project reference.'
}
$config = Join-Path $run 'NuGet.Config'
$escapedFeed = [Security.SecurityElement]::Escape($feedPath)
[IO.File]::WriteAllText($config, @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="candidate"><package pattern="Lucent.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@)
$priorPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $cache
    & $dotnet restore $project -r win-x64 --force-evaluate --use-lock-file --configfile $config "-p:LucentContextNavigationVersion=$Version"
    if ($LASTEXITCODE -ne 0) { throw "Package restore failed with exit code $LASTEXITCODE." }
    & $dotnet build $project -c Release --no-restore --nologo "-p:LucentContextNavigationVersion=$Version"
    if ($LASTEXITCODE -ne 0) { throw "Managed package build failed with exit code $LASTEXITCODE." }
    & $dotnet (Join-Path $fixture 'bin\Release\net10.0\HostedContextNavigation.dll')
    if ($LASTEXITCODE -ne 0) { throw "Managed package execution failed with exit code $LASTEXITCODE." }
    & $dotnet publish $project -c Release -r win-x64 --no-restore --nologo -o $publish "-p:LucentContextNavigationVersion=$Version"
    if ($LASTEXITCODE -ne 0) { throw "NativeAOT publish failed with exit code $LASTEXITCODE." }
    & (Join-Path $publish 'HostedContextNavigation.exe')
    if ($LASTEXITCODE -ne 0) { throw "NativeAOT execution failed with exit code $LASTEXITCODE." }
    $hashes = [ordered]@{ version = $Version }
    foreach ($package in $packages) {
        $hashes[[IO.Path]::GetFileName($package)] =
            (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    }
    $hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'candidate.json')
    Write-Output "hosted-context-navigation-evidence=$run"
}
finally {
    $env:NUGET_PACKAGES = $priorPackages
}
