param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
foreach ($package in @('Lucent.Core', 'Lucent.Lui.Sdk')) {
    if (!(Test-Path -LiteralPath (Join-Path $feedPath "$package.$Version.nupkg"))) {
        throw "Required authoring package is missing: $package $Version"
    }
}
$proof = Join-Path $root ('artifacts/authoring-package-consumer/' + [Guid]::NewGuid().ToString('N'))
$library = Join-Path $proof 'library'
$consumer = Join-Path $proof 'consumer'
$proofFeed = Join-Path $proof 'feed'
$null = New-Item -ItemType Directory -Path $library, $consumer, $proofFeed -Force
'<Project />' | Set-Content (Join-Path $proof 'Directory.Build.props'), (Join-Path $proof 'Directory.Build.targets'), (Join-Path $proof 'Directory.Packages.props')
Copy-Item -LiteralPath (Join-Path $root 'global.json') -Destination $proof
$escapedFeed = [System.Security.SecurityElement]::Escape($feedPath)
$escapedProofFeed = [System.Security.SecurityElement]::Escape($proofFeed)
$config = Join-Path $proof 'NuGet.config'
@"
<configuration>
  <packageSources><clear /><add key="lucent" value="$escapedFeed" /><add key="proof" value="$escapedProofFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="lucent"><package pattern="Lucent.Core" /><package pattern="Lucent.Lui.Sdk" /></packageSource><packageSource key="proof"><package pattern="Lucent.AuthoringGate.Proof" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config
@"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>14.0</LangVersion><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors><IsAotCompatible>true</IsAotCompatible><PackageId>Lucent.AuthoringGate.Proof</PackageId><Version>$Version</Version></PropertyGroup>
  <ItemGroup><PackageReference Include="Lucent.Core" Version="[$Version]" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $library 'Proof.csproj')
@"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><LangVersion>14.0</LangVersion><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors><IsAotCompatible>true</IsAotCompatible></PropertyGroup>
  <ItemGroup><PackageReference Include="Lucent.Core" Version="[$Version]" /><PackageReference Include="Lucent.AuthoringGate.Proof" Version="[$Version]" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $consumer 'Consumer.csproj')
$fixtures = Join-Path $root 'tests/Lucent.Lui.Sdk.Fixtures/Authoring'
Copy-Item -LiteralPath (Join-Path $fixtures 'ProofLibrary.cs'), (Join-Path $fixtures 'GeneratedProbe.lui') -Destination $library
Copy-Item -LiteralPath (Join-Path $fixtures 'Program.cs'), (Join-Path $fixtures 'Consumer.lui') -Destination $consumer
$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = Join-Path $proof 'cache'
    & $dotnet pack (Join-Path $library 'Proof.csproj') -c Release --configfile $config -o $proofFeed -warnaserror
    if ($LASTEXITCODE) { throw 'Authoring proof library package failed.' }
    & $dotnet build (Join-Path $consumer 'Consumer.csproj') -c Release --configfile $config -warnaserror
    if ($LASTEXITCODE) { throw 'Packaged authoring consumer build failed.' }
    & $dotnet (Join-Path $consumer 'bin/Release/net10.0/Consumer.dll')
    if ($LASTEXITCODE) { throw 'Packaged authoring managed execution failed.' }
    $publish = Join-Path $proof 'publish'
    & $dotnet publish (Join-Path $consumer 'Consumer.csproj') -c Release -r win-x64 --self-contained true -p:PublishAot=true --configfile $config -o $publish -warnaserror
    if ($LASTEXITCODE) { throw 'Packaged authoring NativeAOT publication failed.' }
    $exe = Join-Path $publish 'Consumer.exe'
    if (!(Test-Path -LiteralPath $exe)) { throw 'Published authoring executable is missing.' }
    $output = @(& $exe)
    $output | Write-Output
    if ($LASTEXITCODE -ne 0 -or $output -notcontains 'Packaged authoring gate: PASS') { throw 'NativeAOT authoring proof failed.' }
    if (Get-ChildItem -LiteralPath $publish -Filter '*CodeAnalysis*') { throw 'Runtime output includes Roslyn tooling.' }
    Write-Output "Authoring package evidence: $proof"
    Get-FileHash -LiteralPath $exe -Algorithm SHA256
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
}
