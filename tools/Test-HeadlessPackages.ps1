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
style ProbeLayout(WindowBreakpoints points) {
    MainGrow: 1;
    Axis: LayoutAxis.Column;
    CrossAlignment: LayoutAlignment.Stretch;
    when (points.IsActive(ProbeBreakpoints.Wide)) {
        Algorithm: LayoutAlgorithms.Grid;
        Columns: GridTracks.Create(GridTrack.Fraction());
        Rows: GridTracks.Create(GridTrack.Content(), GridTrack.Fraction());
    }
}
style ProbeAction(WindowBreakpoints points) {
    Height: 40;
    GridPlacement: new GridPlacement(0, 0);
    when (points.IsActive(ProbeBreakpoints.Wide)) {
        Height: 64;
    }
}
style ProbeEditor {
    GridPlacement: new GridPlacement(1, 0);
    MainGrow: 1;
}
internal component Probe(Action invoked, WindowBreakpoints points) {
    <Layout breakpoints={points} style={ProbeLayout(points)}>
        <Button onInvoke={invoked} style={ProbeAction(points)}>Run</Button>
        <TextField label="Retained draft" initialValue="seed" style={ProbeEditor} />
    </Layout>
}
'@ | Set-Content (Join-Path $proof 'Probe.lui')
@'
using Lucent.Core;
using Lucent.Testing;
using Lucent.Testing.Skia;

var calls = 0;
await using var app = await HeadlessApplication.StartAsync(
    context => HeadlessPackageProbe.Components.Probe(
        () => Interlocked.Increment(ref calls),
        new(context.Composition.Root.Scope, HeadlessPackageProbe.ProbeBreakpoints.Set)),
    new() { Viewport = new(320, 120, 1) });
await app.KeyAsync(new(KeyCommandKind.Down, Key.Tab));
await app.KeyAsync(new(KeyCommandKind.Down, Key.Enter));
if (Volatile.Read(ref calls) != 1)
    throw new InvalidOperationException("Packaged .lui headless command did not invoke exactly once.");
Console.WriteLine("Packaged .lui headless command: PASS");
var initial = await app.SnapshotAsync();
var editor = initial.Require(SemanticRole.TextField, "Retained draft");
var edited = await app.InvokeAsync(context => context.Composition.ExecuteSemanticCommand(
    editor.Identity, new(SemanticCommandKind.SetValue, "keep this draft")));
if (edited != SemanticCommandResult.Applied)
    throw new InvalidOperationException("Packaged editor refused its current semantic command.");
var wide = await app.ResizeAsync(new(640, 120, 1.5f));
var retained = wide.Require(SemanticRole.TextField, "Retained draft");
if (wide.RequireBox(wide.Require(SemanticRole.Button, "Run")).Bounds.Height != 64
    || retained.Identity.ElementId != editor.Identity.ElementId
    || retained.Identity.CompositionEpoch != editor.Identity.CompositionEpoch
    || retained.Value != "keep this draft")
    throw new InvalidOperationException("Packaged breakpoint style lost geometry or editor ownership.");
var narrow = await app.ResizeAsync(new(320, 120, 2));
if (narrow.RequireBox(narrow.Require(SemanticRole.Button, "Run")).Bounds.Height != 40
    || narrow.Require(SemanticRole.TextField, "Retained draft").Value != "keep this draft")
    throw new InvalidOperationException("Packaged conditional style did not restore its base value.");
Console.WriteLine("Packaged parameterized styles, window breakpoints and retained layout: PASS");
var noticeCalls = 0;
await using (var notice = await HeadlessApplication.StartAsync(
    Components.ErrorNotice(() => "Package failure", () => noticeCalls++)))
{
    var noticeSnapshot = await notice.SnapshotAsync();
    var status = noticeSnapshot.Require(SemanticRole.Status, "Package failure");
    var retry = noticeSnapshot.Require(SemanticRole.Button, "Retry");
    var result = await notice.InvokeAsync(context => context.Composition.ExecuteSemanticCommand(
        retry.Identity, new(SemanticCommandKind.Invoke)));
    if (status.Name != "Package failure" || result != SemanticCommandResult.Applied || noticeCalls != 1)
        throw new InvalidOperationException("Packaged ErrorNotice lost status or retry behavior.");
}
Console.WriteLine("Packaged ErrorNotice composition: PASS");
await using var rendered = await SkiaHeadlessApplication.StartAsync(
    context => HeadlessPackageProbe.Components.Probe(() => { },
        new(context.Composition.Root.Scope, HeadlessPackageProbe.ProbeBreakpoints.Set)),
    new HeadlessApplicationOptions { Viewport = new(320, 120, 1.5f) });
var png = await rendered.CapturePngAsync();
if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
    throw new InvalidOperationException("Packaged Skia capture did not produce PNG data.");
Console.WriteLine("Packaged Skia capture: PASS");

namespace HeadlessPackageProbe {
    internal static class ProbeBreakpoints {
        internal static readonly Breakpoint Wide = new("wide", 600);
        internal static readonly BreakpointSet Set = BreakpointSet.Create(Wide);
    }
}
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
    foreach ($notice in (Get-Content (Join-Path $root 'tools/package-notices.json') -Raw | ConvertFrom-Json | Where-Object package -eq 'Lucent.Renderer.Skia')) {
        if (-not (Test-Path -LiteralPath (Join-Path $proof "bin/Release/net10.0/$($notice.output)") -PathType Leaf)) {
            throw "Standalone renderer package omitted notice: $($notice.output)"
        }
    }
} finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
}
