[CmdletBinding()]
param(
    [string]$Feed,
    [string]$Version,
    [string]$ProofDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }

if ([bool]$Feed -xor [bool]$Version) {
    throw 'Feed and Version must be supplied together.'
}
if (!$Version) { $Version = '0.3.0-dev.assets-proof' }

if (!$ProofDirectory) { $ProofDirectory = Join-Path $root 'artifacts/lui-assets-proof' }
$ProofDirectory = [IO.Path]::GetFullPath($ProofDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$ProofDirectory.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ProofDirectory must remain under repository artifacts: $ProofDirectory"
}

$feedRoot = Join-Path $ProofDirectory 'feed'
$packageCache = Join-Path $ProofDirectory 'packages'
$libraryRoot = Join-Path $ProofDirectory 'library'
$workspaceLibraryRoot = Join-Path $ProofDirectory 'workspace-probe-library'
$projectAppRoot = Join-Path $ProofDirectory 'project-reference-app'
$packageAppRoot = Join-Path $ProofDirectory 'package-reference-app'
$metadataRoot = Join-Path $ProofDirectory 'metadata'
$negativeRoot = Join-Path $ProofDirectory 'negative'
$applicationIconRoot = Join-Path $ProofDirectory 'application-icon'
$crossRoot = Join-Path $ProofDirectory 'cross-library'
$zeroRoot = Join-Path $ProofDirectory 'zero-assets'
$disabledRoot = Join-Path $ProofDirectory 'lui-disabled'
$removedRoot = Join-Path $ProofDirectory 'removed-last-asset'
$renameRoot = Join-Path $ProofDirectory 'fixed-path-rename'
$publishRoot = Join-Path $ProofDirectory 'publish'
$logPath = Join-Path $ProofDirectory 'proof.log'
$resultPath = Join-Path $ProofDirectory 'proof-result.json'
$configPath = Join-Path $ProofDirectory 'NuGet.config'

function Write-TextFile([string]$path, [string]$text) {
    $parent = Split-Path $path -Parent
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

function Write-BytesFile([string]$path, [byte[]]$bytes) {
    $parent = Split-Path $path -Parent
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    [IO.File]::WriteAllBytes($path, $bytes)
}

function Invoke-Dotnet([string]$label, [string[]]$arguments) {
    Add-Content -LiteralPath $logPath -Value ("`n>>> {0}: dotnet {1}" -f $label, ($arguments -join ' '))
    & $dotnet @arguments 2>&1 | Tee-Object -FilePath $logPath -Append
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $label failed with exit code $LASTEXITCODE"
    }
}

function Invoke-DotnetCapture([string]$label, [string[]]$arguments) {
    Add-Content -LiteralPath $logPath -Value ("`n>>> {0}: dotnet {1}" -f $label, ($arguments -join ' '))
    $output = (& $dotnet @arguments 2>&1) -join "`n"
    Add-Content -LiteralPath $logPath -Value $output
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Expected-Failure([string]$label, [string[]]$arguments, [string]$diagnostic) {
    $result = Invoke-DotnetCapture $label $arguments
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch [regex]::Escape($diagnostic)) {
        throw "Expected $diagnostic from $label, exit $($result.ExitCode).`n$($result.Output)"
    }
    return $result.Output
}

function Assert-That([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Get-Hash([byte[]]$bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-FileHashHex([string]$path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
}

function Copy-DirectoryContents([string]$source, [string]$destination) {
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination $_.Name) -Recurse -Force
    }
}

function New-NuGetConfig([string]$path, [string]$localFeed) {
    $escaped = [Security.SecurityElement]::Escape([IO.Path]::GetFullPath($localFeed))
    Write-TextFile $path @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="lucent-proof" value="$escaped" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
}

function New-AssetProbeProject(
    [string]$directory,
    [string]$name,
    [string]$fileName,
    [byte[]]$bytes,
    [string]$logicalPath,
    [string]$density,
    [string]$accessor = ''
) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    Write-BytesFile (Join-Path $directory $fileName) $bytes
    $densityAttribute = if ($null -eq $density) { '' } else { ' Density="' + $density + '"' }
    $accessorAttribute = if (!$accessor) { '' } else { ' Accessor="' + $accessor + '"' }
    $project = @"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>$name</AssemblyName>
    <RootNamespace>AssetProbe</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <LucentEnableLui>false</LucentEnableLui>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Core" Version="$Version" />
    <LucentAsset Include="$fileName" Path="$logicalPath"$densityAttribute$accessorAttribute />
  </ItemGroup>
</Project>
"@
    Write-TextFile (Join-Path $directory "$name.csproj") $project
    return Join-Path $directory "$name.csproj"
}

function Build-Probe([string]$directory, [string]$project, [string]$label) {
    $probeConfig = Join-Path $directory 'NuGet.config'
    New-NuGetConfig $probeConfig $feedRoot
    Invoke-Dotnet "$label-restore" @(
        'restore', $project, '--configfile', $probeConfig, '--packages', $packageCache,
        '--nologo'
    ) | Out-Null
    Invoke-Dotnet "$label-build" @(
        'build', $project, '--no-restore', '-c', 'Debug', '-warnaserror',
        '--nologo'
    ) | Out-Null
    return Get-ChildItem -LiteralPath $directory -Recurse -Filter inventory.json -File | Select-Object -First 1
}

function Build-ProbeExpectedFailure([string]$directory, [string]$project, [string]$label, [string]$diagnostic) {
    $probeConfig = Join-Path $directory 'NuGet.config'
    New-NuGetConfig $probeConfig $feedRoot
    Invoke-Dotnet "$label-restore" @(
        'restore', $project, '--configfile', $probeConfig, '--packages', $packageCache,
        '--nologo'
    ) | Out-Null
    return Expected-Failure "$label-build" @(
        'build', $project, '--no-restore', '-c', 'Debug', '--nologo'
    ) $diagnostic
}

function New-JpegWithExif([byte[]]$jpeg, [bool]$littleEndian, [bool]$badOffset = $false) {
    $marker = if ($littleEndian) {
        [byte[]](0x49,0x49,0x2a,0x00,0x08,0x00,0x00,0x00,0x01,0x00,0x12,0x01,0x03,0x00,0x01,0x00,0x00,0x00,0x06,0x00,0x00,0x00,0x00,0x00,0x00,0x00)
    } else {
        [byte[]](0x4d,0x4d,0x00,0x2a,0x00,0x00,0x00,0x08,0x00,0x01,0x01,0x12,0x00,0x03,0x00,0x00,0x00,0x01,0x00,0x06,0x00,0x00,0x00,0x00,0x00,0x00)
    }
    if ($badOffset) {
        if ($littleEndian) {
            $marker[4] = 0x00; $marker[5] = 0x00; $marker[6] = 0x01; $marker[7] = 0x00
        }
        else {
            $marker[4] = 0x00; $marker[5] = 0x01; $marker[6] = 0x00; $marker[7] = 0x00
        }
    }
    $payload = [byte[]](0x45,0x78,0x69,0x66,0x00,0x00) + $marker
    $length = $payload.Length + 2
    $segment = [byte[]](0xff,0xe1,[byte]($length -shr 8),[byte]$length) + $payload
    return [byte[]](0xff,0xd8) + $segment + $jpeg[2..($jpeg.Length - 1)]
}

function Set-JpegFrameWidth([byte[]]$jpeg, [int]$width, [int]$height) {
    $copy = [byte[]]$jpeg.Clone()
    for ($index = 2; $index + 8 -lt $copy.Length; $index++) {
        if ($copy[$index] -eq 0xff -and $copy[$index + 1] -eq 0xc0) {
            $copy[$index + 5] = [byte](($height -shr 8) -band 0xff)
            $copy[$index + 6] = [byte]($height -band 0xff)
            $copy[$index + 7] = [byte](($width -shr 8) -band 0xff)
            $copy[$index + 8] = [byte]($width -band 0xff)
            return $copy
        }
    }
    throw 'The JPEG fixture has no baseline SOF0 marker.'
}

function Get-Inventory([string]$directory) {
    $file = Get-ChildItem -LiteralPath $directory -Recurse -Filter inventory.json -File | Select-Object -First 1
    Assert-That ($null -ne $file) "No generated asset inventory was found under $directory."
    return Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
}

function Assert-InventoryAsset($inventory, [string]$path, [string]$format, [double]$width, [double]$height, [double]$density, $relativeWidth = $null, $relativeHeight = $null) {
    $asset = @($inventory.assets | Where-Object path -eq $path)
    Assert-That ($asset.Count -eq 1) "Inventory did not contain exactly one '$path' asset."
    Assert-That ($asset[0].format -eq $format) "Inventory format changed for $path."
    Assert-That ([Math]::Abs([double]$asset[0].intrinsicWidth - $width) -lt 0.0001) "Inventory width changed for $path."
    Assert-That ([Math]::Abs([double]$asset[0].intrinsicHeight - $height) -lt 0.0001) "Inventory height changed for $path."
    Assert-That ([Math]::Abs([double]$asset[0].density - $density) -lt 0.0001) "Inventory density changed for $path."
    if ($null -eq $relativeWidth) { Assert-That ($null -eq $asset[0].relativeWidth) "Inventory relative width unexpectedly set for $path." }
    else { Assert-That ([Math]::Abs([double]$asset[0].relativeWidth - $relativeWidth) -lt 0.0001) "Inventory relative width changed for $path." }
    if ($null -eq $relativeHeight) { Assert-That ($null -eq $asset[0].relativeHeight) "Inventory relative height unexpectedly set for $path." }
    else { Assert-That ([Math]::Abs([double]$asset[0].relativeHeight - $relativeHeight) -lt 0.0001) "Inventory relative height changed for $path." }
    return $asset[0]
}

function Assert-NativePublish([string]$directory, [string]$name) {
    $exe = Join-Path $directory "$name.exe"
    Assert-That (Test-Path -LiteralPath $exe) "NativeAOT executable is missing: $exe"
    Assert-That (@(Get-ChildItem -LiteralPath $directory -Filter '*.dll' -File).Count -eq 0) "NativeAOT output contains managed DLLs: $directory"
    return $exe
}

function New-ConsumerProject([string]$directory, [string]$name, [string]$referenceXml) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>$name</AssemblyName>
    <RootNamespace>AssetConsumer</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishAot>true</PublishAot>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <InvariantGlobalization>true</InvariantGlobalization>
    <IsAotCompatible>false</IsAotCompatible>
    <IsTrimmable>false</IsTrimmable>
    <EnableTrimAnalyzer>false</EnableTrimAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    $referenceXml
  </ItemGroup>
</Project>
"@
    Write-TextFile (Join-Path $directory "$name.csproj") $project
    Write-TextFile (Join-Path $directory 'Program.cs') @'
Fixture.Library.AssetContract.Run();
Console.WriteLine("consumer: PASS");
'@
    return Join-Path $directory "$name.csproj"
}

function New-CaseProject([string]$directory, [string]$name, [string]$itemXml, [hashtable]$files, [bool]$application = $false) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    foreach ($entry in $files.GetEnumerator()) { Write-BytesFile (Join-Path $directory $entry.Key) $entry.Value }
    $outputType = if ($application) { '<OutputType>Exe</OutputType>' } else { '' }
    $project = @"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    $outputType
    <AssemblyName>$name</AssemblyName>
    <RootNamespace>AssetCase</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <LucentEnableLui>false</LucentEnableLui>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Core" Version="$Version" />
    $itemXml
  </ItemGroup>
</Project>
"@
    Write-TextFile (Join-Path $directory "$name.csproj") $project
    if ($application) { Write-TextFile (Join-Path $directory 'Program.cs') 'Console.WriteLine("application-icon: PASS");' }
    return Join-Path $directory "$name.csproj"
}

function New-CrossLibraryProject([string]$directory, [string]$name, [string]$logicalPath, [byte[]]$bytes) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    Write-BytesFile (Join-Path $directory 'image.svg') $bytes
    $project = @"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>$name</AssemblyName>
    <RootNamespace>$name</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <LucentAssetDomain>Shared.Asset.Domain</LucentAssetDomain>
    <LucentAssetAccessorNamespace>$name.Resources</LucentAssetAccessorNamespace>
    <LucentAssetAccessorClass>Assets</LucentAssetAccessorClass>
    <LucentEnableLui>false</LucentEnableLui>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Core" Version="$Version" />
    <LucentAsset Include="image.svg" Path="$logicalPath" />
  </ItemGroup>
</Project>
"@
    Write-TextFile (Join-Path $directory "$name.csproj") $project
    return Join-Path $directory "$name.csproj"
}

function New-CrossConsumerProject([string]$directory, [string]$name, [string]$firstProject, [string]$secondProject) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $project = @"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>$name</AssemblyName>
    <RootNamespace>$name</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <LucentLuiLangVersion>preview</LucentLuiLangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Core" Version="$Version" />
    <ProjectReference Include="$firstProject" />
    <ProjectReference Include="$secondProject" />
  </ItemGroup>
</Project>
"@
    Write-TextFile (Join-Path $directory "$name.csproj") $project
    Write-TextFile (Join-Path $directory 'Program.cs') 'Console.WriteLine("cross-library probe");'
    return Join-Path $directory "$name.csproj"
}

$knownDirectories = @('feed','packages','library','workspace-probe-library','project-reference-app','package-reference-app','metadata','negative','cross-library','zero-assets','lui-disabled','removed-last-asset','fixed-path-rename','application-icon','publish')
New-Item -ItemType Directory -Force -Path $ProofDirectory | Out-Null
foreach ($name in $knownDirectories) {
    $path = Join-Path $ProofDirectory $name
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
Write-TextFile $logPath ("Lucent SDK embedded-assets proof`nVersion=$Version`nUTC=$([DateTime]::UtcNow.ToString('O'))")
New-Item -ItemType Directory -Force -Path $feedRoot, $packageCache, $publishRoot | Out-Null

$oldNugetPackagesWasSet = Test-Path -LiteralPath Env:NUGET_PACKAGES
$oldNugetPackages = $env:NUGET_PACKAGES
$useProofPackageCache = {
    $env:NUGET_PACKAGES = $packageCache
}
$restoreCallerPackageCache = {
    if ($oldNugetPackagesWasSet) {
        $env:NUGET_PACKAGES = $oldNugetPackages
    }
    else {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    }
}
$result = $null
try {
    # Reuse a CI feed when supplied, while keeping all generated packages and cleanup inside artifacts.
    if ($Feed) {
        $inputFeed = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Feed).Path)
        Get-ChildItem -LiteralPath $inputFeed -Filter '*.nupkg' -File | Copy-Item -Destination $feedRoot -Force
    }
    $sdkPackage = Join-Path $feedRoot "Lucent.Lui.Sdk.$Version.nupkg"
    $corePackage = Join-Path $feedRoot "Lucent.Core.$Version.nupkg"
    if (!(Test-Path -LiteralPath $sdkPackage)) {
        if ($Feed) { throw "The supplied feed does not contain $sdkPackage." }
        # Source bootstrap must use the caller's normal NuGet cache. Only copied
        # fixture/consumer projects use the disposable proof cache below.
        & $restoreCallerPackageCache
        Invoke-Dotnet 'sdk-restore' @('restore', (Join-Path $root 'src/Lucent.Lui.Sdk/Lucent.Lui.Sdk.csproj'), '--locked-mode', '--nologo') | Out-Null
        Invoke-Dotnet 'sdk-pack' @('pack', (Join-Path $root 'src/Lucent.Lui.Sdk/Lucent.Lui.Sdk.csproj'), '-c', 'Release', '--no-restore', '--nologo', '-o', $feedRoot, "-p:LucentPackageVersion=$Version") | Out-Null
    }
    if (!(Test-Path -LiteralPath $corePackage)) {
        if ($Feed) { throw "The supplied feed does not contain $corePackage; Feed mode never rebuilds a missing package." }
        & $restoreCallerPackageCache
        Invoke-Dotnet 'core-restore' @('restore', (Join-Path $root 'src/Lucent.Core/Lucent.Core.csproj'), '--locked-mode', '--nologo') | Out-Null
        Invoke-Dotnet 'core-pack' @('pack', (Join-Path $root 'src/Lucent.Core/Lucent.Core.csproj'), '-c', 'Release', '--no-restore', '--nologo', '-o', $feedRoot, "-p:LucentPackageVersion=$Version") | Out-Null
    }
    Assert-That (Test-Path -LiteralPath $sdkPackage) "SDK package is unavailable for $Version."
    Assert-That (Test-Path -LiteralPath $corePackage) "Core package is unavailable for $Version."
    & $useProofPackageCache

    $libraryFixture = Join-Path $root 'tests/Lucent.Lui.Sdk.Fixtures/Assets'
    Copy-DirectoryContents $libraryFixture $libraryRoot
    Move-Item -LiteralPath (Join-Path $libraryRoot 'Assets.csproj.template') -Destination (Join-Path $libraryRoot 'Assets.csproj')
    $libraryProject = Join-Path $libraryRoot 'Assets.csproj'
    (Get-Content -LiteralPath $libraryProject -Raw).Replace('__LUCENT_SDK_VERSION__', $Version).Replace('__LUCENT_VERSION__', $Version) | Set-Content -LiteralPath $libraryProject -Encoding utf8
    $libraryConfig = Join-Path $libraryRoot 'NuGet.config'
    New-NuGetConfig $libraryConfig $feedRoot
    Invoke-Dotnet 'library-restore' @('restore', $libraryProject, '--configfile', $libraryConfig, '--packages', $packageCache, '--nologo') | Out-Null
    Invoke-Dotnet 'library-build' @('build', $libraryProject, '-c', 'Release', '--no-restore', '-warnaserror', '--nologo') | Out-Null
    $inventory = Get-Inventory $libraryRoot
    Assert-That ($inventory.version -eq 1) 'Asset inventory schema version changed.'
    Assert-That ($inventory.domain -eq 'Fixture.Library.Domain') 'Asset inventory domain changed.'
    $brand = Assert-InventoryAsset $inventory 'images/brand.svg' 'Svg' 24 12 1
    $png = Assert-InventoryAsset $inventory 'pixels/tiny.png' 'Png' .5 .5 2
    $jpeg = Assert-InventoryAsset $inventory 'photos/tiny.jpeg' 'Jpeg' .5 .5 2
    $binary = Assert-InventoryAsset $inventory 'data/payload.bin' 'Binary' 0 0 0
    Assert-That ($inventory.assets.Count -eq 4) 'Asset inventory did not contain exactly four entries.'
    $inventoryFile = Get-ChildItem -LiteralPath $libraryRoot -Recurse -Filter inventory.json -File | Select-Object -First 1
    $generated = Get-ChildItem -LiteralPath $libraryRoot -Recurse -Filter 'Lucent.Assets.g.cs' -File | Select-Object -First 1
    Assert-That ($null -ne $generated) 'Generated asset accessor source is missing.'
    $generatedText = Get-Content -LiteralPath $generated.FullName -Raw
    foreach ($marker in @('namespace Fixture.Resources','namespace Fixture.Branding','namespace Fixture.Binary','partial class Catalog','ImageSource.FromAsset','AssetReference','AssemblyMetadataAttribute')) {
        Assert-That ($generatedText.Contains($marker)) "Generated accessor source omitted '$marker'."
    }
    Copy-DirectoryContents $libraryRoot $workspaceLibraryRoot
    foreach ($generatedDirectory in @(Get-ChildItem -LiteralPath $workspaceLibraryRoot -Directory -Filter 'lucent-assets' -Recurse -ErrorAction SilentlyContinue)) {
        Remove-Item -LiteralPath $generatedDirectory.FullName -Recurse -Force
    }
    $workspaceAssetOutputs = @(Get-ChildItem -LiteralPath $workspaceLibraryRoot -Directory -Filter 'lucent-assets' -Recurse -ErrorAction SilentlyContinue)
    Assert-That ($workspaceAssetOutputs.Count -eq 0) 'Cold workspace fixture retained generated lucent-assets output.'
    $workspaceLibraryProject = Join-Path $workspaceLibraryRoot 'Assets.csproj'
    $workspaceProbeProject = Join-Path $root 'tests/Lucent.Lui.Sdk.Fixtures/AssetWorkspaceProbe/AssetWorkspaceProbe.csproj'
    Assert-That (Test-Path -LiteralPath $workspaceProbeProject) 'The cold MSBuildWorkspace asset probe is missing.'
    $workspaceProbeConfig = Join-Path $ProofDirectory 'workspace.NuGet.config'
    New-NuGetConfig $workspaceProbeConfig $feedRoot
    # Keep the maintained helper's own restore metadata in the caller's cache;
    # the proof cache is disposable and is reserved for copied fixture projects.
    & $restoreCallerPackageCache
    Invoke-Dotnet 'workspace-probe-restore' @('restore', $workspaceProbeProject, '--locked-mode', '--configfile', $workspaceProbeConfig, '--nologo') | Out-Null
    Invoke-Dotnet 'workspace-probe-build' @('build', $workspaceProbeProject, '-c', 'Release', '--no-restore', '-warnaserror', '--nologo') | Out-Null
    & $useProofPackageCache
    $workspaceProbe = Invoke-DotnetCapture 'workspace-probe-run' @('run', '--project', $workspaceProbeProject, '-c', 'Release', '--no-build', '--no-restore', '--', $workspaceLibraryProject)
    Assert-That ($workspaceProbe.ExitCode -eq 0 -and $workspaceProbe.Output -match 'workspace-assets: PASS') "Cold MSBuildWorkspace asset probe failed.`n$($workspaceProbe.Output)"
    $assetOutput = $generated.Directory.FullName
    $inventoryHashBefore = Get-FileHashHex $inventoryFile.FullName
    $generatedHashBefore = Get-FileHashHex $generated.FullName
    $payloadFilesBefore = @(Get-ChildItem -LiteralPath (Join-Path $assetOutput 'payload') -File | Sort-Object Name)
    Assert-That ($payloadFilesBefore.Count -eq 4) 'The generated payload directory did not contain exactly four payloads.'
    $payloadHashesBefore = @{}; foreach ($payload in $payloadFilesBefore) { $payloadHashesBefore[$payload.Name] = Get-FileHashHex $payload.FullName }
    $warm = Invoke-DotnetCapture 'library-warm-build' @('build', $libraryProject, '-c', 'Release', '--no-restore', '-warnaserror', '--nologo')
    Assert-That ($warm.ExitCode -eq 0) "Warm asset build failed.`n$($warm.Output)"
    Assert-That ($warm.Output -notmatch 'Lucent asset catalog:') 'Warm asset build unexpectedly regenerated the catalog.'
    $inventoryFileAfterWarm = Get-ChildItem -LiteralPath $libraryRoot -Recurse -Filter inventory.json -File | Select-Object -First 1
    $generatedAfterWarm = Get-ChildItem -LiteralPath $libraryRoot -Recurse -Filter 'Lucent.Assets.g.cs' -File | Select-Object -First 1
    Assert-That ((Get-FileHashHex $inventoryFileAfterWarm.FullName) -eq $inventoryHashBefore -and (Get-FileHashHex $generatedAfterWarm.FullName) -eq $generatedHashBefore) 'Warm asset build changed generated catalog bytes.'
    $touchedSource = Get-Item -LiteralPath (Join-Path $libraryRoot 'Artwork/brand.svg')
    # Move the source timestamp beyond the prior stamp without putting it in the
    # future; the first rebuild then owns a newer stamp and the next build can settle.
    Start-Sleep -Seconds 1
    $touchedSource.LastWriteTimeUtc = [DateTime]::UtcNow
    $touched = Invoke-DotnetCapture 'library-touched-same-bytes-build' @('build', $libraryProject, '-c', 'Release', '--no-restore', '-warnaserror', '--nologo')
    Assert-That ($touched.ExitCode -eq 0 -and $touched.Output -match 'Lucent asset catalog:') "Touched same-byte asset did not regenerate its catalog.`n$($touched.Output)"
    Assert-That ((Get-FileHashHex $generatedAfterWarm.FullName) -eq $generatedHashBefore) 'Touched same-byte asset changed generated source bytes.'
    $secondWarm = Invoke-DotnetCapture 'library-second-warm-build' @('build', $libraryProject, '-c', 'Release', '--no-restore', '-warnaserror', '--nologo')
    Assert-That ($secondWarm.ExitCode -eq 0 -and $secondWarm.Output -notmatch 'Lucent asset catalog:') 'Second warm build did not settle after a same-byte source touch.'
    $missingPayload = $payloadFilesBefore[0]
    $missingPayloadBytes = [IO.File]::ReadAllBytes($missingPayload.FullName)
    Remove-Item -LiteralPath $missingPayload.FullName -Force
    $repair = Invoke-DotnetCapture 'library-missing-payload-build' @('build', $libraryProject, '-c', 'Release', '--no-restore', '-warnaserror', '--nologo')
    Assert-That ($repair.ExitCode -eq 0 -and $repair.Output -match 'Lucent asset catalog:') "Missing-payload repair did not rerun catalog generation.`n$($repair.Output)"
    Assert-That (Test-Path -LiteralPath $missingPayload.FullName) 'Missing payload was not regenerated.'
    Assert-That ((Get-FileHashHex $missingPayload.FullName) -eq (Get-Hash $missingPayloadBytes)) 'Regenerated payload bytes changed.'
    foreach ($payload in @(Get-ChildItem -LiteralPath (Join-Path $assetOutput 'payload') -File)) {
        Assert-That ($payloadHashesBefore[$payload.Name] -eq (Get-FileHashHex $payload.FullName)) "Payload changed during warm/repair proof: $($payload.Name)"
    }
    $libraryPackage = Join-Path $feedRoot "AssetFixture.Library.$Version.nupkg"
    Invoke-Dotnet 'library-pack' @('pack', $libraryProject, '-c', 'Release', '--no-restore', '--nologo', '-o', $feedRoot, "-p:PackageVersion=$Version", "-p:LucentPackageVersion=$Version") | Out-Null
    Assert-That (Test-Path -LiteralPath $libraryPackage) 'Asset fixture package was not created.'

    $projectAppProject = New-ConsumerProject $projectAppRoot 'ProjectReferenceConsumer' '<ProjectReference Include="..\library\Assets.csproj" />'
    $packageReferences = '<PackageReference Include="AssetFixture.Library" Version="' + $Version + '" />' + "`n    " + '<PackageReference Include="Lucent.Core" Version="' + $Version + '" />'
    $packageAppProject = New-ConsumerProject $packageAppRoot 'PackageReferenceConsumer' $packageReferences
    New-NuGetConfig (Join-Path $projectAppRoot 'NuGet.config') $feedRoot
    New-NuGetConfig (Join-Path $packageAppRoot 'NuGet.config') $feedRoot
    Invoke-Dotnet 'project-app-restore' @('restore', $projectAppProject, '-r', 'win-x64', '--configfile', (Join-Path $projectAppRoot 'NuGet.config'), '--packages', $packageCache, '--nologo') | Out-Null
    Invoke-Dotnet 'package-app-restore' @('restore', $packageAppProject, '-r', 'win-x64', '--configfile', (Join-Path $packageAppRoot 'NuGet.config'), '--packages', $packageCache, '--nologo') | Out-Null
    Invoke-Dotnet 'project-app-build' @('build', $projectAppProject, '-c', 'Release', '--no-restore', '-warnaserror', '--nologo') | Out-Null
    Invoke-Dotnet 'package-app-build' @('build', $packageAppProject, '-c', 'Release', '--no-restore', '-warnaserror', '--nologo') | Out-Null
    $projectPublish = Join-Path $publishRoot 'project-reference'
    $packagePublish = Join-Path $publishRoot 'package-reference'
    New-Item -ItemType Directory -Force -Path $projectPublish, $packagePublish | Out-Null
    foreach ($pair in @(@('project', $projectAppProject, $projectPublish), @('package', $packageAppProject, $packagePublish))) {
        Invoke-Dotnet "$($pair[0])-app-publish" @('publish', $pair[1], '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '--nologo', '-o', $pair[2], '-p:PublishAot=true', '-p:StripSymbols=true', '-p:InvariantGlobalization=true') | Out-Null
    }
    $projectExe = Assert-NativePublish $projectPublish 'ProjectReferenceConsumer'
    $packageExe = Assert-NativePublish $packagePublish 'PackageReferenceConsumer'

    # Metadata reader contracts are exercised through the shipped generator, not a fake decoder.
    $baseJpeg = [IO.File]::ReadAllBytes((Join-Path $libraryFixture 'Artwork/rotated.jpeg'))
    $asymmetricJpeg = Set-JpegFrameWidth $baseJpeg 2 1
    $png = [IO.File]::ReadAllBytes((Join-Path $libraryFixture 'Artwork/tiny.png'))
    $svgComma = [Text.Encoding]::UTF8.GetBytes('<svg xmlns="http://www.w3.org/2000/svg" width="50%" height="auto" viewBox="0,0,20,10" />')
    $svgOneAxis = [Text.Encoding]::UTF8.GetBytes('<svg xmlns="http://www.w3.org/2000/svg" height="20" viewBox="0,0,2,1" />')
    $svgPercent = [Text.Encoding]::UTF8.GetBytes('<svg xmlns="http://www.w3.org/2000/svg" width="50%" height="25%" viewBox="0,0,2,1" />')
    $metadataCases = @(
        @{ Name='jpeg-little-endian'; Bytes=(New-JpegWithExif $asymmetricJpeg $true); File='orientation.jpg'; Path='orientation.jpg'; Density='2'; Expected=@{ Path='orientation.jpg'; Format='Jpeg'; Width=.5; Height=1; Density=2 } },
        @{ Name='jpeg-big-endian'; Bytes=(New-JpegWithExif $asymmetricJpeg $false); File='orientation.jpg'; Path='orientation.jpg'; Density='2'; Expected=@{ Path='orientation.jpg'; Format='Jpeg'; Width=.5; Height=1; Density=2 } },
        @{ Name='svg-comma-viewbox'; Bytes=$svgComma; File='icon.svg'; Path='icon.svg'; Density=$null; Expected=@{ Path='icon.svg'; Format='Svg'; Width=300; Height=150; Density=1; RelativeWidth=.5; RelativeHeight=$null } },
        @{ Name='svg-one-axis'; Bytes=$svgOneAxis; File='icon.svg'; Path='icon.svg'; Density=$null; Expected=@{ Path='icon.svg'; Format='Svg'; Width=40; Height=20; Density=1 } },
        @{ Name='svg-percent-axes'; Bytes=$svgPercent; File='icon.svg'; Path='icon.svg'; Density=$null; Expected=@{ Path='icon.svg'; Format='Svg'; Width=300; Height=150; Density=1; RelativeWidth=.5; RelativeHeight=.25 } }
    )
    foreach ($case in $metadataCases) {
        $caseRoot = Join-Path $metadataRoot $case.Name
        $caseProject = New-AssetProbeProject $caseRoot $case.Name $case.File $case.Bytes $case.Path $case.Density
        Build-Probe $caseRoot $caseProject $case.Name | Out-Null
        $caseInventory = Get-Inventory $caseRoot
        $expected = $case.Expected
        Assert-InventoryAsset $caseInventory $expected.Path $expected.Format $expected.Width $expected.Height $expected.Density $expected.RelativeWidth $expected.RelativeHeight | Out-Null
    }
    $invalidMetadata = @(
        @{ Name='jpeg-malformed-exif-offset'; Bytes=(New-JpegWithExif $asymmetricJpeg $true $true); File='bad.jpg'; Path='bad.jpg'; Density='2' },
        @{ Name='png-truncated-ihdr'; Bytes=$png[0..23]; File='bad.png'; Path='bad.png'; Density=$null },
        @{ Name='svg-invalid-axis'; Bytes=([Text.Encoding]::UTF8.GetBytes('<svg xmlns="http://www.w3.org/2000/svg" width="not-a-length" height="12" />')); File='bad.svg'; Path='bad.svg'; Density=$null },
        @{ Name='svg-density-rejected'; Bytes=$svgComma; File='bad.svg'; Path='bad.svg'; Density='2' },
        @{ Name='density-outside-single'; Bytes=$png; File='bad.png'; Path='bad.png'; Density='1e100' }
    )
    foreach ($case in $invalidMetadata) {
        $caseRoot = Join-Path $metadataRoot $case.Name
        $caseProject = New-AssetProbeProject $caseRoot $case.Name $case.File $case.Bytes $case.Path $case.Density
        Build-ProbeExpectedFailure $caseRoot $caseProject $case.Name 'LUIA0004' | Out-Null
    }

    # Application artwork uses one current-executable default, a finite generated ICO and
    # packaged PNG renditions that the Windows host can consume before first presentation.
    $applicationSvg = [IO.File]::ReadAllBytes((Join-Path $libraryFixture 'Artwork/brand.svg'))
    $generatedIconRoot = Join-Path $applicationIconRoot 'generated'
    $generatedIconProject = New-CaseProject $generatedIconRoot 'GeneratedApplicationIcon' '<LucentAsset Include="brand.svg" Path="application/brand.svg" ApplicationIcon="true" />' @{ 'brand.svg' = $applicationSvg } $true
    $generatedInventoryFile = Build-Probe $generatedIconRoot $generatedIconProject 'application-icon-generated'
    $generatedInventory = Get-Inventory $generatedIconRoot
    $generatedArtwork = $generatedInventory.applicationIconArtwork
    Assert-That ($null -ne $generatedArtwork) 'Generated application inventory omitted artwork provenance.'
    Assert-That ($generatedArtwork.svgPolicy -eq 'lucent-secure-static-v1/svg.skia-5.2.3/skia-4.151.1') 'Generated application inventory omitted the SVG policy identity.'
    Assert-That ($generatedArtwork.svgFont -eq 'none') 'Generated application inventory omitted the default SVG font identity.'
    Assert-That (@($generatedArtwork.renditions).Count -eq 7) 'Generated application artwork did not contain seven finite renditions.'
    $generatedSource = Get-ChildItem -LiteralPath $generatedIconRoot -Recurse -Filter 'Lucent.Assets.g.cs' -File | Select-Object -First 1
    $generatedText = Get-Content -LiteralPath $generatedSource.FullName -Raw
    Assert-That ($generatedText.Contains('ApplicationIconDefault') -and $generatedText.Contains('ApplicationIconRendition')) 'Generated source omitted the static application-icon registration.'
    $generatedIco = Get-ChildItem -LiteralPath $generatedIconRoot -Recurse -Filter 'application.ico' -File | Select-Object -First 1
    Assert-That ($null -ne $generatedIco) 'Generated application.ico is missing.'
    $generatedIcoBytes = [IO.File]::ReadAllBytes($generatedIco.FullName)
    Assert-That ([BitConverter]::ToUInt16($generatedIcoBytes, 4) -eq 7) 'Generated application.ico did not contain seven entries.'
    $generatedPayloads = @(Get-ChildItem -LiteralPath (Join-Path $generatedIco.DirectoryName 'payload') -File)
    Assert-That ($generatedPayloads.Count -eq 8) 'Generated application icon did not package its source and seven PNG rendition payloads.'

    $suppliedIconRoot = Join-Path $applicationIconRoot 'supplied'
    $suppliedProject = New-CaseProject $suppliedIconRoot 'SuppliedApplicationIcon' '<LucentAsset Include="brand.svg" Path="application/brand.svg" ApplicationIcon="true" IconFile="supplied.ico" />' @{ 'brand.svg' = $applicationSvg; 'supplied.ico' = $generatedIcoBytes } $true
    Build-Probe $suppliedIconRoot $suppliedProject 'application-icon-supplied' | Out-Null
    $suppliedOutput = Get-ChildItem -LiteralPath $suppliedIconRoot -Recurse -Filter 'application.ico' -File | Select-Object -First 1
    Assert-That ((Get-Hash ([IO.File]::ReadAllBytes($suppliedOutput.FullName))) -eq (Get-Hash $generatedIcoBytes)) 'Supplied ICO optical bytes were not preserved for the apphost.'
    $suppliedBefore = Get-FileHashHex $suppliedOutput.FullName
    Write-BytesFile (Join-Path $suppliedIconRoot 'supplied.ico') ([byte[]]($generatedIcoBytes + [byte]0))
    Start-Sleep -Seconds 1
    $suppliedChanged = Invoke-DotnetCapture 'application-icon-supplied-edited' @('build', $suppliedProject, '--no-restore', '--nologo')
    Assert-That ($suppliedChanged.ExitCode -eq 0 -and $suppliedChanged.Output -match 'Lucent asset catalog:') "Edited IconFile did not regenerate the catalog.`n$($suppliedChanged.Output)"
    Assert-That ((Get-FileHashHex $suppliedOutput.FullName) -ne $suppliedBefore) 'Edited IconFile did not change the generated application.ico bytes.'

    $overlapBytes = [byte[]]$generatedIcoBytes.Clone()
    $firstPayloadOffset = [BitConverter]::ToUInt32($overlapBytes, 6 + 12)
    [Array]::Copy([BitConverter]::GetBytes([uint32]$firstPayloadOffset), 0, $overlapBytes, 6 + 16 + 12, 4)
    $overlapRoot = Join-Path $applicationIconRoot 'overlap'
    $overlapProject = New-CaseProject $overlapRoot 'OverlappingApplicationIcon' '<LucentAsset Include="brand.svg" Path="application/brand.svg" ApplicationIcon="true" IconFile="overlap.ico" />' @{ 'brand.svg' = $applicationSvg; 'overlap.ico' = $overlapBytes } $true
    Build-ProbeExpectedFailure $overlapRoot $overlapProject 'application-icon-overlap' 'LUIA0008' | Out-Null

    # Empty catalogs and removal must erase stale generated payload/source directories.
    $zeroProject = New-CaseProject $zeroRoot 'ZeroAssets' '' @{}
    New-Item -ItemType Directory -Force -Path (Join-Path $zeroRoot 'obj/Debug/net10.0/lucent-assets') | Out-Null
    Write-TextFile (Join-Path $zeroRoot 'obj/Debug/net10.0/lucent-assets/stale.txt') 'stale'
    New-NuGetConfig (Join-Path $zeroRoot 'NuGet.config') $feedRoot
    Invoke-Dotnet 'zero-assets-restore' @('restore', $zeroProject, '--configfile', (Join-Path $zeroRoot 'NuGet.config'), '--packages', $packageCache, '--nologo') | Out-Null
    Invoke-Dotnet 'zero-assets-build' @('build', $zeroProject, '--no-restore', '--nologo') | Out-Null
    Assert-That (!(Test-Path -LiteralPath (Join-Path $zeroRoot 'obj/Debug/net10.0/lucent-assets'))) 'Zero-asset build left stale generated output.'

    # Opting out of LUI syntax processing must still leave asset analysis active.
    $disabledBytes = [byte[]](0x31, 0x41, 0x59)
    $disabledProject = New-CaseProject $disabledRoot 'LuiDisabledAssets' '<LucentAsset Include="payload.bin" Path="data/payload.bin" Kind="Binary" />' @{ 'payload.bin' = $disabledBytes }
    Write-TextFile (Join-Path $disabledRoot 'invalid.lui') 'this is deliberately not valid Lui syntax'
    New-NuGetConfig (Join-Path $disabledRoot 'NuGet.config') $feedRoot
    Invoke-Dotnet 'lui-disabled-restore' @('restore', $disabledProject, '--configfile', (Join-Path $disabledRoot 'NuGet.config'), '--packages', $packageCache, '--nologo') | Out-Null
    $disabledBuild = Invoke-DotnetCapture 'lui-disabled-build' @('build', $disabledProject, '--no-restore', '-warnaserror', '--nologo')
    Assert-That ($disabledBuild.ExitCode -eq 0 -and $disabledBuild.Output -notmatch 'LUI\d{4}') "LucentEnableLui=false did not suppress invalid .lui diagnostics.`n$($disabledBuild.Output)"
    $disabledInventory = Get-Inventory $disabledRoot
    Assert-That (@($disabledInventory.assets).Count -eq 1 -and $disabledInventory.assets[0].path -eq 'data/payload.bin') 'Asset analysis was disabled along with LUI syntax processing.'

    $removedBytes = [byte[]](1,2,3,4,5)
    $removedProject = New-CaseProject $removedRoot 'RemovedLastAsset' '<LucentAsset Include="payload.bin" Path="data/payload.bin" Kind="Binary" />' @{ 'payload.bin' = $removedBytes }
    New-NuGetConfig (Join-Path $removedRoot 'NuGet.config') $feedRoot
    Invoke-Dotnet 'removed-asset-restore' @('restore', $removedProject, '--configfile', (Join-Path $removedRoot 'NuGet.config'), '--packages', $packageCache, '--nologo') | Out-Null
    Invoke-Dotnet 'removed-asset-build' @('build', $removedProject, '--no-restore', '--nologo') | Out-Null
    $removedOutput = Join-Path $removedRoot 'obj/Debug/net10.0/lucent-assets'
    Assert-That (Test-Path -LiteralPath $removedOutput) 'Asset build did not create generated output before removal.'
    (Get-Content -LiteralPath $removedProject -Raw).Replace('<LucentAsset Include="payload.bin" Path="data/payload.bin" Kind="Binary" />', '') | Set-Content -LiteralPath $removedProject -Encoding utf8
    Invoke-Dotnet 'removed-last-asset-build' @('build', $removedProject, '--no-restore', '--nologo') | Out-Null
    Assert-That (!(Test-Path -LiteralPath $removedOutput)) 'Removing the last asset left stale generated output.'

    # A physical source rename with a fixed logical Path preserves identity and generated accessor shape.
    $renameBytes = [IO.File]::ReadAllBytes((Join-Path $libraryFixture 'Artwork/brand.svg'))
    $renameProject = New-CaseProject $renameRoot 'FixedPathRename' '<LucentAsset Include="first.svg" Path="stable/icon.svg" />' @{ 'first.svg' = $renameBytes }
    New-NuGetConfig (Join-Path $renameRoot 'NuGet.config') $feedRoot
    Invoke-Dotnet 'rename-restore' @('restore', $renameProject, '--configfile', (Join-Path $renameRoot 'NuGet.config'), '--packages', $packageCache, '--nologo') | Out-Null
    Invoke-Dotnet 'rename-first-build' @('build', $renameProject, '--no-restore', '--nologo') | Out-Null
    $firstInventory = Get-Inventory $renameRoot
    $firstAsset = @($firstInventory.assets)[0]
    Move-Item -LiteralPath (Join-Path $renameRoot 'first.svg') -Destination (Join-Path $renameRoot 'second.svg')
    (Get-Content -LiteralPath $renameProject -Raw).Replace('first.svg', 'second.svg') | Set-Content -LiteralPath $renameProject -Encoding utf8
    Invoke-Dotnet 'rename-second-build' @('build', $renameProject, '--no-restore', '--nologo') | Out-Null
    $secondInventory = Get-Inventory $renameRoot
    $secondAsset = @($secondInventory.assets)[0]
    Assert-That ($firstAsset.path -eq $secondAsset.path -and $firstAsset.hash -eq $secondAsset.hash -and $firstAsset.accessor -eq $secondAsset.accessor) 'Physical rename changed fixed logical asset identity.'

    # Fail-closed path, missing-source, identity and accessor collision diagnostics.
    $validSvg = [IO.File]::ReadAllBytes((Join-Path $libraryFixture 'Artwork/brand.svg'))
    $negativeCases = @(
        @{ Name='missing-source'; Items='<LucentAsset Include="missing.svg" Path="missing.svg" />'; Files=@{}; Code='LUIA0001' },
        @{ Name='path-traversal'; Items='<LucentAsset Include="brand.svg" Path="../escape.svg" />'; Files=@{ 'brand.svg' = $validSvg }; Code='LUIA0002' },
        @{ Name='absolute-path'; Items='<LucentAsset Include="brand.svg" Path="/absolute.svg" />'; Files=@{ 'brand.svg' = $validSvg }; Code='LUIA0002' },
        @{ Name='case-collision'; Items='<LucentAsset Include="one.svg" Path="icons/Brand.svg" /><LucentAsset Include="two.svg" Path="icons/brand.svg" />'; Files=@{ 'one.svg' = $validSvg; 'two.svg' = $validSvg }; Code='LUIA0006' },
        @{ Name='path-collision'; Items='<LucentAsset Include="one.svg" Path="icons/brand.svg" /><LucentAsset Include="two.svg" Path="icons/brand.svg" />'; Files=@{ 'one.svg' = $validSvg; 'two.svg' = $validSvg }; Code='LUIA0006' },
        @{ Name='member-collision'; Items='<LucentAsset Include="one.svg" Path="one.svg" Accessor="Thing" /><LucentAsset Include="two.svg" Path="two.svg" Accessor="Thing" />'; Files=@{ 'one.svg' = $validSvg; 'two.svg' = $validSvg }; Code='LUIA0007' },
        @{ Name='type-collision'; Items='<LucentAsset Include="one.svg" Path="one.svg" Accessor="Thing" /><LucentAsset Include="two.svg" Path="two.svg" Accessor="Thing.Child" />'; Files=@{ 'one.svg' = $validSvg; 'two.svg' = $validSvg }; Code='LUIA0007' },
        @{ Name='application-icon-library'; Items='<LucentAsset Include="brand.svg" Path="brand.svg" ApplicationIcon="true" />'; Files=@{ 'brand.svg' = $validSvg }; Code='LUIA0008' },
        @{ Name='application-icon-multiple'; Items='<LucentAsset Include="one.svg" Path="one.svg" ApplicationIcon="true" /><LucentAsset Include="two.svg" Path="two.svg" ApplicationIcon="true" />'; Files=@{ 'one.svg' = $validSvg; 'two.svg' = $validSvg }; Code='LUIA0008'; Application=$true }
    )
    foreach ($case in $negativeCases) {
        $caseRoot = Join-Path $negativeRoot $case.Name
        $caseProject = New-CaseProject $caseRoot $case.Name $case.Items $case.Files ([bool]$case.Application)
        Build-ProbeExpectedFailure $caseRoot $caseProject $case.Name $case.Code | Out-Null
        Assert-That (!(Get-ChildItem -LiteralPath $caseRoot -Recurse -Filter 'Lucent.Assets.g.cs' -File -ErrorAction SilentlyContinue)) "Failed catalog '$($case.Name)' emitted generated source."
    }

    $sharedSvg = [IO.File]::ReadAllBytes((Join-Path $libraryFixture 'Artwork/brand.svg'))
    $changedSvg = [Text.Encoding]::UTF8.GetBytes('<svg xmlns="http://www.w3.org/2000/svg" width="24" height="12"><path d="M1 1h22v10H1z"/></svg>')
    $differentRoot = Join-Path $crossRoot 'different-revision'
    $differentFirst = New-CrossLibraryProject (Join-Path $differentRoot 'First') 'FirstAssetLibrary' 'images/shared.svg' $sharedSvg
    $differentSecond = New-CrossLibraryProject (Join-Path $differentRoot 'Second') 'SecondAssetLibrary' 'images/shared.svg' $changedSvg
    Build-Probe (Split-Path $differentFirst -Parent) $differentFirst 'cross-different-first' | Out-Null
    Build-Probe (Split-Path $differentSecond -Parent) $differentSecond 'cross-different-second' | Out-Null
    $differentConsumerRoot = Join-Path $differentRoot 'Consumer'
    $differentApp = New-CrossConsumerProject $differentConsumerRoot 'DifferentRevisionConsumer' $differentFirst $differentSecond
    Build-ProbeExpectedFailure $differentConsumerRoot $differentApp 'cross-different-consumer' 'LUIA0008' | Out-Null

    $caseRoot = Join-Path $crossRoot 'casefold-identity'
    $caseFirst = New-CrossLibraryProject (Join-Path $caseRoot 'First') 'CaseFirstAssetLibrary' 'images/shared.svg' $sharedSvg
    $caseSecond = New-CrossLibraryProject (Join-Path $caseRoot 'Second') 'CaseSecondAssetLibrary' 'Images/Shared.svg' $sharedSvg
    Build-Probe (Split-Path $caseFirst -Parent) $caseFirst 'cross-case-first' | Out-Null
    Build-Probe (Split-Path $caseSecond -Parent) $caseSecond 'cross-case-second' | Out-Null
    $caseConsumerRoot = Join-Path $caseRoot 'Consumer'
    $caseApp = New-CrossConsumerProject $caseConsumerRoot 'CasefoldConsumer' $caseFirst $caseSecond
    Build-ProbeExpectedFailure $caseConsumerRoot $caseApp 'cross-case-consumer' 'LUIA0009' | Out-Null

    # Source and package staging are disposable; the two NativeAOT executables must still read exact bytes.
    foreach ($path in @($libraryRoot, $projectAppRoot, $packageAppRoot, $feedRoot, $packageCache)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    $projectOutput = & $projectExe 2>&1
    $projectExit = $LASTEXITCODE
    $packageOutput = & $packageExe 2>&1
    $packageExit = $LASTEXITCODE
    Add-Content -LiteralPath $logPath -Value "`n>>> project-reference NativeAOT after source/feed/cache removal"
    $projectOutput | Tee-Object -FilePath $logPath -Append
    Add-Content -LiteralPath $logPath -Value "exit=$projectExit"
    Add-Content -LiteralPath $logPath -Value "`n>>> package-reference NativeAOT after source/feed/cache removal"
    $packageOutput | Tee-Object -FilePath $logPath -Append
    Add-Content -LiteralPath $logPath -Value "exit=$packageExit"
    Assert-That ($projectExit -eq 0 -and ($projectOutput -join "`n") -match 'assets: PASS') 'ProjectReference NativeAOT asset contract failed after source removal.'
    Assert-That ($packageExit -eq 0 -and ($packageOutput -join "`n") -match 'assets: PASS') 'PackageReference NativeAOT asset contract failed after source removal.'
    $result = [pscustomobject]@{
        status = 'PASS'
        version = $Version
        sourceFeed = $Feed
        metadataCases = $metadataCases.Name + $invalidMetadata.Name
        diagnostics = $negativeCases | ForEach-Object { "$($_.Name):$($_.Code)" }
        nativeAot = @{
            projectReference = [pscustomobject]@{ exitCode = $projectExit; sourceRemoved = !(Test-Path -LiteralPath $libraryRoot); feedRemoved = !(Test-Path -LiteralPath $feedRoot); cacheRemoved = !(Test-Path -LiteralPath $packageCache) }
            packageReference = [pscustomobject]@{ exitCode = $packageExit; sourceRemoved = !(Test-Path -LiteralPath $packageAppRoot); feedRemoved = !(Test-Path -LiteralPath $feedRoot); cacheRemoved = !(Test-Path -LiteralPath $packageCache) }
        }
    }
}
finally {
    & $restoreCallerPackageCache
    if (Test-Path -LiteralPath $configPath) { Remove-Item -LiteralPath $configPath -Force -ErrorAction SilentlyContinue }
}

if ($null -ne $result) {
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding utf8
    Get-Content -LiteralPath $resultPath
}
