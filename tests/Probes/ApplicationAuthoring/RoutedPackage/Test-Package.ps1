param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-[0-9A-Za-z.-]+$')] [string] $Version,
    [string] $OutputRoot,
    [switch] $KeepPackageCache
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$dotnet = Join-Path $repo '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$packages = @('Lucent.Core', 'Lucent.Lui.Sdk') | ForEach-Object {
    $package = Join-Path $feedPath "$_.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Missing candidate package: $package"
    }
    $package
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'artifacts/a0-routed-package'
}
$run = Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $run 'fixture'
$publish = Join-Path $run 'publish'
$cache = Join-Path $run 'packages'
New-Item -ItemType Directory -Path $fixture, $publish, $cache -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination $run
# Do not inherit repository source references, analyzers, or build targets.
[IO.File]::WriteAllText((Join-Path $run 'Directory.Build.props'), '<Project />')
[IO.File]::WriteAllText((Join-Path $run 'Directory.Build.targets'), '<Project />')
Get-ChildItem -LiteralPath $PSScriptRoot -File |
    Where-Object { $_.Extension -in '.cs', '.csproj', '.lui' } |
    Copy-Item -Destination $fixture
$project = Join-Path $fixture 'RoutedPackage.csproj'
$projectText = [IO.File]::ReadAllText($project).Replace(
    '<Project Sdk="Microsoft.NET.Sdk">',
    '<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/' + $Version + '">'
)
[IO.File]::WriteAllText($project, $projectText)
if ($projectText -match 'ProjectReference|HintPath') {
    throw 'Package acceptance cannot use source or direct assembly references.'
}
if (@(Get-ChildItem -LiteralPath $fixture -Filter '*.cs').Count -ne 1) {
    throw 'The all-LUI fixture must have only Program.cs as hand-authored C#.'
}
$escapedFeed = [Security.SecurityElement]::Escape($feedPath)
$config = Join-Path $run 'NuGet.Config'
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
$commands = Join-Path $run 'commands-and-exits.txt'
try {
    $env:NUGET_PACKAGES = $cache
    # This is the first consumer build: no prior restore, managed build, or generated files.
    $arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--nologo', '-o', $publish,
        "-p:LucentAuthoringVersion=$Version", "-p:RestoreConfigFile=$config",
        '-p:RestorePackagesWithLockFile=true')
    "dotnet $($arguments -join ' ')" | Set-Content -LiteralPath $commands
    & $dotnet @arguments *> (Join-Path $run 'publish.log')
    $publishExit = $LASTEXITCODE
    "publish exit=$publishExit" | Add-Content -LiteralPath $commands
    if ($publishExit -ne 0) { throw "Cold package NativeAOT publish failed; see $run/publish.log" }
    $executable = Join-Path $publish 'RoutedPackage.exe'
    & $executable *> (Join-Path $run 'execution.log')
    $runExit = $LASTEXITCODE
    "native execution exit=$runExit" | Add-Content -LiteralPath $commands
    if ($runExit -ne 0) { throw "Routed NativeAOT acceptance failed; see $run/execution.log" }
    if (-not (Select-String -LiteralPath (Join-Path $run 'execution.log') -SimpleMatch 'ROUTED PACKAGE PASS:')) {
        throw 'The acceptance executable did not report its positive case.'
    }
    $hashes = [ordered]@{ version = $Version; executable = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash }
    foreach ($package in $packages) {
        $hashes[[IO.Path]::GetFileName($package)] = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    }
    $hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'candidate.json')
    Write-Output "routed-package-evidence=$run"
}
finally {
    $env:NUGET_PACKAGES = $priorPackages
    if (-not $KeepPackageCache -and (Test-Path -LiteralPath $cache)) {
        $resolvedRun = (Resolve-Path -LiteralPath $run).Path
        $resolvedCache = (Resolve-Path -LiteralPath $cache).Path
        if (-not $resolvedCache.StartsWith($resolvedRun + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedCache) -ne 'packages') {
            throw "Unexpected test cache cleanup target: $resolvedCache"
        }
        Remove-Item -LiteralPath $resolvedCache -Recurse -Force
    }
}
