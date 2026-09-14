param(
    [Parameter(Mandatory = $true)] [string] $Feed,
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$dotnet = Join-Path $repo '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'artifacts\context-navigation-aot'
}
$run = Join-Path $OutputRoot ('generated-navigation-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $run 'fixture'
$publish = Join-Path $run 'publish'
$cache = Join-Path $OutputRoot 'cache'
New-Item -ItemType Directory -Path $fixture, $publish, $cache -Force | Out-Null
Copy-Item (Join-Path $repo 'global.json') (Join-Path $run 'global.json')
Copy-Item (Join-Path $PSScriptRoot 'AotHost.csproj'), (Join-Path $PSScriptRoot 'Program.cs'), (Join-Path $PSScriptRoot 'Routes.cs') $fixture

$project = Join-Path $fixture 'AotHost.csproj'
$text = [IO.File]::ReadAllText($project)
$text = $text.Replace('<Project Sdk="Microsoft.NET.Sdk">', '<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/' + $Version + '">')
[IO.File]::WriteAllText($project, $text)
if (Select-String -LiteralPath $project -Pattern 'ProjectReference' -Quiet) {
    throw 'The package fixture contains a forbidden source project reference.'
}

$config = Join-Path $run 'NuGet.Config'
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
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
$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $cache
    & $dotnet restore $project -r win-x64 --force-evaluate --use-lock-file --configfile $config "-p:LucentPackageVersion=$Version"
    if ($LASTEXITCODE -ne 0) { throw "Package restore failed with exit code $LASTEXITCODE." }
    & $dotnet publish $project -c Release -r win-x64 --no-restore --nologo -o $publish "-p:LucentPackageVersion=$Version"
    if ($LASTEXITCODE -ne 0) { throw "NativeAOT publish failed with exit code $LASTEXITCODE." }
    & (Join-Path $publish 'AotHost.exe')
    if ($LASTEXITCODE -ne 0) { throw "NativeAOT executable failed with exit code $LASTEXITCODE." }
    Write-Output ("package-proof-root=" + $run)
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
}
