param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version,
    [Parameter(Mandatory)] [string] $CandidateDescriptor
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../..'))
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$descriptorPath = (Resolve-Path -LiteralPath $CandidateDescriptor).Path
$candidate = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json
if ($candidate.schemaVersion -ne 1 -or $candidate.status -notin @('candidate', 'complete') -or $candidate.releaseSet.version -cne $Version -or $candidate.releaseSet.sourceState -cne 'clean' -or $candidate.releaseSet.sourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Candidate descriptor identity is invalid or does not match the requested version.' }
$packageEvidence = @()
$seenPackages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $candidate.packages) {
    if ($entry.id -cnotmatch '^Lucent\.[A-Za-z0-9.]+$' -or $entry.version -cne $Version -or $entry.repositoryCommit -cne $candidate.releaseSet.sourceCommit -or $entry.artifact.fileName -cne "$($entry.id).$Version.nupkg" -or $entry.artifact.sha256 -cnotmatch '^[0-9a-fA-F]{64}$' -or -not $seenPackages.Add($entry.id)) { throw 'Candidate descriptor package identities are incoherent.' }
    $packagePath = Join-Path $feedPath $entry.artifact.fileName
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -cne $entry.artifact.sha256.ToUpperInvariant()) { throw "Candidate package bytes differ from the descriptor: $($entry.id)" }
    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $nuspec = $archive.GetEntry("$($entry.id).nuspec")
        if ($null -eq $nuspec) { throw "Candidate package lacks its nuspec: $($entry.id)" }
        $reader = [IO.StreamReader]::new($nuspec.Open())
        try { [xml] $spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($spec.package.metadata.id -cne $entry.id -or $spec.package.metadata.version -cne $Version -or $spec.package.metadata.repository.commit -cne $entry.repositoryCommit) { throw "Candidate package nuspec identity differs from the descriptor: $($entry.id)" }
    }
    finally { $archive.Dispose() }
    $packageEvidence += [ordered]@{ id = $entry.id; version = $Version; repositoryCommit = $entry.repositoryCommit; fileName = $entry.artifact.fileName; sha256 = $entry.artifact.sha256 }
}
if (-not $seenPackages.Contains('Lucent.Platform.Windows.Activation') -or -not $seenPackages.Contains('Lucent.Platform.Windows') -or -not $seenPackages.Contains('Lucent.Core')) { throw 'Candidate descriptor omits an activation runtime package.' }
$proof = Join-Path $root ('artifacts/windows-activation-registered/' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $proof 'publish'
$output = Join-Path $proof 'output'
$null = New-Item -ItemType Directory -Path $publish, $output -Force
Copy-Item -LiteralPath $descriptorPath -Destination (Join-Path $proof 'candidate-descriptor.json')
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
    $assets = Get-Content -LiteralPath (Join-Path $proof 'obj/project.assets.json') -Raw | ConvertFrom-Json
    $consumed = @($assets.libraries.PSObject.Properties | Where-Object { $_.Name -match '^Lucent\.[^/]+/' -and $_.Value.type -eq 'package' } | ForEach-Object {
        $parts = $_.Name.Split('/')
        if ($parts.Count -ne 2 -or $parts[1] -cne $Version) { throw "Restore consumed an unexpected Lucent package version: $($_.Name)" }
        $packageFile = Join-Path $feedPath "$($parts[0]).$Version.nupkg"
        $expectedSha512 = [Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash -LiteralPath $packageFile -Algorithm SHA512).Hash))
        if ($_.Value.sha512 -cne $expectedSha512) { throw "Restore consumed different package bytes: $($_.Name)" }
        $parts[0]
    })
    if ($consumed.Count -eq 0 -or @($consumed | Where-Object { -not $seenPackages.Contains($_) }).Count -ne 0) { throw 'Package restore consumed Lucent packages absent from the validated candidate.' }
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
    schemaVersion = 2
    scheme = $scheme
    host = 'navigation'
    instanceKey = $key
    packageVersion = $Version
    fixturePreparationCommit = (& git -C $root rev-parse HEAD).Trim()
    packageSourceCommit = $candidate.releaseSet.sourceCommit
    candidateDescriptorSha256 = (Get-FileHash -LiteralPath (Join-Path $proof 'candidate-descriptor.json') -Algorithm SHA256).Hash
    consumerSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Algorithm SHA256).Hash
    prepareScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    invokeScriptSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Invoke-RegisteredFixture.ps1') -Algorithm SHA256).Hash
    packages = @($packageEvidence)
    consumedLucentPackages = @($consumed)
    executableSha256 = (Get-FileHash -LiteralPath (Join-Path $publish 'Consumer.exe') -Algorithm SHA256).Hash
    registrationAttempted = $false
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $proof 'manifest.json')
Write-Output "Prepared registered-delivery fixture without registration: $proof"
