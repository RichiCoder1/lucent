param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version,
    [switch] $SkipDesktopSmoke
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$proof = Join-Path $root ("artifacts/package-consumer/" + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $proof
# Consume the maintained real app through packages in a fresh, isolated directory.
Get-ChildItem (Join-Path $root 'apps/Lucent.IssueBrowser') -File | Where-Object { $_.Extension -in '.cs', '.lui' } | Copy-Item -Destination $proof
$artwork = Join-Path $proof 'Artwork'
$null = New-Item -ItemType Directory -Path $artwork
Copy-Item (Join-Path $root 'apps/Lucent.IssueBrowser/Artwork/issue-browser.svg') $artwork
@'
using Lucent.Core;
using Lucent.Reactive.R3;

namespace Lucent.IssueBrowser;

internal static class R3PackageProbe
{
    public static void Run()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("package-r3");
        using var scheduler = new OwnedDebouncedAction(scope, TimeProvider.System);
        var delivered = 0;
        scheduler.Restart(TimeSpan.Zero, () => delivered++);
        var deadline = Environment.TickCount64 + 2000;
        while (Volatile.Read(ref delivered) == 0 && Environment.TickCount64 < deadline)
        {
            graph.Drain();
            Thread.Sleep(1);
        }
        if (Volatile.Read(ref delivered) != 1)
            throw new InvalidOperationException("R3 NativeAOT package probe did not deliver its owner-thread callback.");
    }
}
'@ | Set-Content (Join-Path $proof 'R3PackageProbe.cs')
$programPath = Join-Path $proof 'Program.cs'
$program = Get-Content -Raw $programPath
if (-not $program.Contains('R3PackageProbe.Run();', [StringComparison]::Ordinal)) {
    $anchor = '(?m)^        try\r?\n        \{\r?\n'
    $replacement = "        try`r`n        {`r`n            R3PackageProbe.Run();`r`n"
    $updated = [Text.RegularExpressions.Regex]::Replace($program, $anchor, $replacement, 1)
    if ($updated -eq $program) { throw 'Package consumer probe anchor was not found in the maintained Program.cs.' }
    $program = $updated
    Set-Content $programPath $program
}
if (-not (Get-Content -Raw $programPath).Contains('R3PackageProbe.Run();', [StringComparison]::Ordinal)) {
    throw 'Package consumer R3 probe was not injected.'
}
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
    <PackageReference Include="Lucent.Icons.Lucide" Version="[$Version]" />
    <PackageReference Include="Lucent.Reactive.R3" Version="[$Version]" />
    <LucentAsset Include="Artwork/issue-browser.svg" Path="Artwork/issue-browser.svg" ApplicationIcon="true" />
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
    foreach ($notice in (Get-Content (Join-Path $root 'tools/package-notices.json') -Raw | ConvertFrom-Json)) {
        if ($notice.output -and -not (Test-Path -LiteralPath (Join-Path $published $notice.output) -PathType Leaf)) {
            throw "Missing published dependency notice: $($notice.output)"
        }
    }
    foreach ($file in 'SDL3.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'vcruntime140.dll', 'notices/SDL3-CS.txt', 'notices/Microsoft.Extensions-LICENSE.txt', 'notices/R3-LICENSE.txt') {
        if (-not (Test-Path -LiteralPath (Join-Path $published $file))) { throw "Missing published asset: $file" }
    }
    $publishedExe = Join-Path $published 'Lucent.IssueBrowser.exe'
    Add-Type -AssemblyName System.Drawing.Common
    $associatedIcon = [Drawing.Icon]::ExtractAssociatedIcon($publishedExe)
    if ($null -eq $associatedIcon) { throw 'Published NativeAOT executable omitted its generated ICO resource.' }
    $associatedIcon.Dispose()
    Remove-Item -LiteralPath $artwork -Recurse -Force
    if ($SkipDesktopSmoke) {
        Write-Output "Package-only restore/NativeAOT/assets/notices: PASS ($Version); desktop startup/close was not run."
        return
    }
    $stdout = Join-Path $proof 'stdout.log'; $stderr = Join-Path $proof 'stderr.log'
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PackageWindowIconProbe {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
'@
    $process = Start-Process $publishedExe -WorkingDirectory $published -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Published app exited early: $(Get-Content $stderr -Raw)" }
        if ($process.MainWindowTitle -eq 'Lucent Issue Browser') { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowTitle -ne 'Lucent Issue Browser') { throw 'Published app did not create its window.' }
    if ([PackageWindowIconProbe]::SendMessage($process.MainWindowHandle, 0x007F, [IntPtr]1, [IntPtr]::Zero) -eq [IntPtr]::Zero) {
        throw 'Published app did not install its embedded application icon after source artwork removal.'
    }
    if (-not $process.CloseMainWindow()) { throw "Published app rejected the close request. $(Get-Content $stderr -Raw)" }
    if (-not $process.WaitForExit(10000)) { throw "Published app did not exit within 10 seconds. $(Get-Content $stderr -Raw)" }
    if ($process.ExitCode -ne 0) { throw "Published app shutdown exited with code $($process.ExitCode). $(Get-Content $stderr -Raw)" }
    Write-Output "Package-only real application restore/NativeAOT/startup/close: PASS ($Version)"
} finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
}
