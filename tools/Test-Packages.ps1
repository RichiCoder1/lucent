param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$proof = Join-Path $root ("artifacts/package-consumer/" + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $proof
# Consume the maintained real app through packages in a fresh, isolated directory.
Get-ChildItem (Join-Path $root 'apps/Lucent.IssueBrowser') -File | Where-Object { $_.Extension -in '.cs', '.lui' } | Copy-Item -Destination $proof
@"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <OutputType>Exe</OutputType><AssemblyName>Lucent.IssueBrowser</AssemblyName><RootNamespace>Lucent.IssueBrowser</RootNamespace>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier><PublishAot>true</PublishAot><PublishTrimmed>true</PublishTrimmed><SelfContained>true</SelfContained>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Platform.Windows" Version="[$Version]" />
    <PackageReference Include="Lucent.Hosting" Version="[$Version]" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $proof 'Consumer.csproj')
# Stop parent MSBuild imports: this must not inherit Lucent's source build or central dependencies.
'<Project />' | Set-Content (Join-Path $proof 'Directory.Build.props'), (Join-Path $proof 'Directory.Build.targets'), (Join-Path $proof 'Directory.Packages.props')
Copy-Item (Join-Path $root 'global.json') $proof
$escapedFeed = [Security.SecurityElement]::Escape($feedPath)
@"
<configuration><packageSources><clear /><add key="lucent" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><packageSourceMapping><packageSource key="lucent"><package pattern="Lucent.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>
"@ | Set-Content (Join-Path $proof 'NuGet.config')
$previousPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $proof 'packages'
$process = $null
Push-Location $proof
try {
    & dotnet restore Consumer.csproj --configfile NuGet.config
    if ($LASTEXITCODE) { throw 'Clean package-only restore failed.' }
    & dotnet publish Consumer.csproj -c Release --no-restore -o publish -warnaserror
    if ($LASTEXITCODE) { throw 'Package-only NativeAOT publish failed.' }
    $published = Join-Path $proof 'publish'
    foreach ($file in 'SDL3.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'vcruntime140.dll', 'notices/SDL3-CS.txt', 'notices/Microsoft.Extensions-LICENSE.txt') {
        if (-not (Test-Path -LiteralPath (Join-Path $published $file))) { throw "Missing published asset: $file" }
    }
    $stdout = Join-Path $proof 'stdout.log'; $stderr = Join-Path $proof 'stderr.log'
    $process = Start-Process (Join-Path $published 'Lucent.IssueBrowser.exe') -WorkingDirectory $published -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Published app exited early: $(Get-Content $stderr -Raw)" }
        if ($process.MainWindowTitle -eq 'Lucent Issue Browser') { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowTitle -ne 'Lucent Issue Browser') { throw 'Published app did not create its window.' }
    if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'Published app failed ordinary shutdown.' }
    Write-Output "Package-only real application restore/NativeAOT/startup/close: PASS ($Version)"
} finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
}
