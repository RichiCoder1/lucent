param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$proof = Join-Path $root ('artifacts/headless-package-consumer/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $proof -Force | Out-Null
'<Project />' | Set-Content (Join-Path $proof 'Directory.Build.props'), (Join-Path $proof 'Directory.Build.targets'), (Join-Path $proof 'Directory.Packages.props')
Copy-Item (Join-Path $root 'global.json') $proof
@"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Testing" Version="[$Version]" />
    <PackageReference Include="Lucent.Testing.Skia" Version="[$Version]" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $proof 'Consumer.csproj')
@'
namespace HeadlessPackageProbe;
internal component Probe(Action invoked) {
    <Button onInvoke={invoked} style={Style.Empty.Height(40)}>Run</Button>
}
'@ | Set-Content (Join-Path $proof 'Probe.lui')
@'
using Lucent.Core;
using Lucent.Testing;
using Lucent.Testing.Skia;

var calls = 0;
await using var app = await HeadlessApplication.StartAsync(
    HeadlessPackageProbe.Components.Probe(() => Interlocked.Increment(ref calls)));
await app.KeyAsync(new(KeyCommandKind.Down, Key.Tab));
await app.KeyAsync(new(KeyCommandKind.Down, Key.Enter));
if (Volatile.Read(ref calls) != 1)
    throw new InvalidOperationException("Packaged .lui headless command did not invoke exactly once.");
Console.WriteLine("Packaged .lui headless command: PASS");
await using var rendered = await SkiaHeadlessApplication.StartAsync(
    HeadlessPackageProbe.Components.Probe(() => { }),
    new HeadlessApplicationOptions { Viewport = new(320, 120, 1.5f) });
var png = await rendered.CapturePngAsync();
if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
    throw new InvalidOperationException("Packaged Skia capture did not produce PNG data.");
Console.WriteLine("Packaged Skia capture: PASS");
'@ | Set-Content (Join-Path $proof 'Program.cs')
$escapedFeed = [Security.SecurityElement]::Escape($feedPath)
@"
<configuration><packageSources><clear /><add key="lucent" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><packageSourceMapping><packageSource key="lucent"><package pattern="Lucent.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>
"@ | Set-Content (Join-Path $proof 'NuGet.config')
$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = Join-Path $proof 'packages'
    Push-Location $proof
    & dotnet restore Consumer.csproj --configfile NuGet.config
    if ($LASTEXITCODE) { throw 'Headless package restore failed.' }
    & dotnet run --project Consumer.csproj -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Headless package consumer failed.' }
} finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
}
