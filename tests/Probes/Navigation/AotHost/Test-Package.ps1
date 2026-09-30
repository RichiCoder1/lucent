param(
    [Parameter(Mandatory = $true)] [string] $Feed,
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
function Assert-ProofOutput([string[]] $Output, [string] $Mode) {
    $Output | Write-Output
    foreach ($marker in @('generated-navigation-native-aot=pass', 'generated-navigation-restoration=pass')) {
        if ($Output -notcontains $marker) { throw "$Mode navigation proof omitted $marker" }
    }
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$dotnet = Join-Path $repo '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'artifacts\context-navigation-aot'
}
$run = Join-Path $OutputRoot ('generated-navigation-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $run 'fixture'
$managed = Join-Path $run 'managed'
$publish = Join-Path $run 'publish'
$cache = Join-Path $OutputRoot 'cache'
New-Item -ItemType Directory -Path $fixture, $publish, $cache -Force | Out-Null
'<Project />' | Set-Content (Join-Path $run 'Directory.Build.props'), (Join-Path $run 'Directory.Build.targets'), (Join-Path $run 'Directory.Packages.props')
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
$packageEvidence = foreach ($name in @('Lucent.Core', 'Lucent.Lui.Sdk')) {
    $package = Join-Path $feedPath "$name.$Version.nupkg"
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $reader = [IO.StreamReader]::new($zip.GetEntry("$name.nuspec").Open())
        try { [xml] $spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($spec.package.metadata.version -cne $Version -or !$spec.package.metadata.repository.commit) {
            throw "Package identity was missing or did not match: $name"
        }
        [ordered]@{ name = $name; version = $Version; sourceCommit = $spec.package.metadata.repository.commit; sha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash }
    }
    finally { $zip.Dispose() }
}
if (@($packageEvidence.sourceCommit | Select-Object -Unique).Count -ne 1) {
    throw 'Core and SDK must come from the same source commit.'
}
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
$previousNodeReuse = $env:MSBUILDDISABLENODEREUSE
try {
    $env:NUGET_PACKAGES = $cache
    $env:MSBUILDDISABLENODEREUSE = '1'
    & $dotnet restore $project -r win-x64 --force-evaluate --use-lock-file --configfile $config "-p:LucentPackageVersion=$Version" -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw "Package restore failed with exit code $LASTEXITCODE." }
    & $dotnet build $project -c Release -r win-x64 --no-restore --nologo -o $managed "-p:LucentPackageVersion=$Version" -p:PublishAot=false -p:SelfContained=false -m:1 -nr:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "Managed package build failed with exit code $LASTEXITCODE." }
    $managedOutput = @(& $dotnet (Join-Path $managed 'AotHost.dll'))
    if ($LASTEXITCODE -ne 0) { throw "Managed executable failed with exit code $LASTEXITCODE." }
    Assert-ProofOutput $managedOutput 'Managed'
    & $dotnet publish $project -c Release -r win-x64 --no-restore --nologo -o $publish "-p:LucentPackageVersion=$Version" -m:1 -nr:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "NativeAOT publish failed with exit code $LASTEXITCODE." }
    $executable = Join-Path $publish 'AotHost.exe'
    $nativeOutput = @(& $executable)
    if ($LASTEXITCODE -ne 0) { throw "NativeAOT executable failed with exit code $LASTEXITCODE." }
    Assert-ProofOutput $nativeOutput 'NativeAOT'
    $fixtureEvidence = foreach ($file in @('AotHost.csproj', 'Program.cs', 'Routes.cs', 'Test-Package.ps1')) {
        [ordered]@{ name = $file; sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $file) -Algorithm SHA256).Hash }
    }
    [ordered]@{
        packages = @($packageEvidence)
        fixture = @($fixtureEvidence)
        managed = 'pass'
        nativeAot = 'pass'
        interaction = 'not exercised; console composition without NavigationInteraction or input dispatch'
        executableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'proof.json')
    Write-Output ("package-proof-root=" + $run)
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
    $env:MSBUILDDISABLENODEREUSE = $previousNodeReuse
}
