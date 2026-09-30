param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../..'))
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$proof = Join-Path $root ('artifacts/windows-activation-registered/' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $proof 'publish'
$output = Join-Path $proof 'output'
$null = New-Item -ItemType Directory -Path $publish, $output -Force
Copy-Item (Join-Path $PSScriptRoot 'Program.cs') $proof
Copy-Item (Join-Path $root 'global.json') $proof
'<Project />' | Set-Content (Join-Path $proof 'Directory.Build.props'), (Join-Path $proof 'Directory.Build.targets'), (Join-Path $proof 'Directory.Packages.props')
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishAot>true</PublishAot>
    <SelfContained>true</SelfContained>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <WindowsAppSdkDeploymentManagerInitialize>false</WindowsAppSdkDeploymentManagerInitialize>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Platform.Windows.Activation" Version="[$Version]" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $proof 'Consumer.csproj')
$escapedFeed = [Security.SecurityElement]::Escape($feedPath)
@"
<configuration><packageSources><clear /><add key="lucent" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><packageSourceMapping><packageSource key="lucent"><package pattern="Lucent.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>
"@ | Set-Content (Join-Path $proof 'NuGet.config')
$previousPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $proof 'packages'
Push-Location $proof
try {
    & $dotnet restore Consumer.csproj --configfile NuGet.config
    if ($LASTEXITCODE) { throw 'Registered fixture package-only restore failed.' }
    & $dotnet publish Consumer.csproj -c Release --no-restore -o $publish -warnaserror -m:1 -nr:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE) { throw 'Registered fixture package-only NativeAOT publish failed.' }
}
finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
}
$scheme = 'lucent' + [Guid]::NewGuid().ToString('N')
$key = 'Lucent.RegisteredProbe.' + [Guid]::NewGuid().ToString('N')
$stop = Join-Path $proof 'stop'
@($key, $scheme, $output, $stop) | Set-Content (Join-Path $publish 'fixture.txt')
$manifest = [ordered]@{
    schemaVersion = 1
    scheme = $scheme
    host = 'navigation'
    instanceKey = $key
    packageVersion = $Version
    sourceCommit = (& git -C $root rev-parse HEAD).Trim()
    consumerSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Algorithm SHA256).Hash
    prepareScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    invokeScriptSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Invoke-RegisteredFixture.ps1') -Algorithm SHA256).Hash
    packages = @(Get-ChildItem -LiteralPath $feedPath -Filter '*.nupkg' | ForEach-Object { [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    executableSha256 = (Get-FileHash -LiteralPath (Join-Path $publish 'Consumer.exe') -Algorithm SHA256).Hash
    registrationAttempted = $false
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $proof 'manifest.json')
Write-Output "Prepared registered-delivery fixture without registration: $proof"
