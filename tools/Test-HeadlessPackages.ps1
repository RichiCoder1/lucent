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
Copy-Item (Join-Path $root 'tests/Lucent.Renderer.Skia.Tests/Fixtures/pixel.png') (Join-Path $proof 'Pixel.png')
@"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RootNamespace>HeadlessPackageProbe</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Testing" Version="[$Version]" />
    <PackageReference Include="Lucent.Testing.Skia" Version="[$Version]" />
    <PackageReference Include="Lucent.Icons.Lucide" Version="[$Version]" />
    <LucentAsset Include="Pixel.png" Path="Pixel.png" />
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
        Rows: GridTracks.Create(GridTrack.Content(), GridTrack.Content(), GridTrack.Fraction());
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
    GridPlacement: new GridPlacement(2, 0);
    MainGrow: 1;
}
internal component Probe(Action invoked, WindowBreakpoints points) {
    <Layout breakpoints={points} style={ProbeLayout(points)}>
        <Button leadingIcon={Lucent.Icons.Lucide.LucideIcons.RefreshCw} onInvoke={invoked} style={ProbeAction(points)}>Run</Button>
        <TextField label="Retained draft" initialValue="seed" style={ProbeEditor} />
        <IconButton source={Lucent.Icons.Lucide.LucideIcons.Ellipsis} label="More actions" onInvoke={invoked} style={Style.Empty.GridPlacement(new GridPlacement(1, 0))} />
    </Layout>
}
'@ | Set-Content (Join-Path $proof 'Probe.lui')
@'
namespace HeadlessPackageProbe;
internal component ImageProbe() {
    <Row>
        <Image source={Assets.Pixel} alternativeText="Packaged pixel" style={Style.Empty with { Width: 32; Height: 32; }} />
        <Icon source={Assets.Pixel} style={Style.Empty with { Width: 32; Height: 32; }} />
        <Icon source={Lucent.Icons.Lucide.LucideIcons.Search} style={Style.Empty with { Width: 32; Height: 32; }} />
    </Row>
}
'@ | Set-Content (Join-Path $proof 'ImageProbe.lui')
@'
using Lucent.Core;
using Lucent.Testing;
using Lucent.Testing.Skia;

var calls = 0;
await using var app = await SkiaHeadlessApplication.StartAsync(
    context => HeadlessPackageProbe.Components.Probe(
        () => Interlocked.Increment(ref calls),
        new(context.Composition.Root.Scope, HeadlessPackageProbe.ProbeBreakpoints.Set)),
    new() { Viewport = new(320, 120, 1) });
await app.KeyAsync(new(KeyCommandKind.Down, Key.Tab));
await app.KeyAsync(new(KeyCommandKind.Down, Key.Enter));
if (Volatile.Read(ref calls) != 1)
    throw new InvalidOperationException("Packaged .lui headless command did not invoke exactly once.");
Console.WriteLine("Packaged .lui headless command: PASS");
using var initial = await app.SnapshotAsync();
var more = initial.Require(SemanticRole.Button, "More actions");
var moreResult = await app.InvokeAsync(context => context.Composition.ExecuteSemanticCommand(
    more.Identity, new(SemanticCommandKind.Invoke)));
if (moreResult != SemanticCommandResult.Applied || Volatile.Read(ref calls) != 2)
    throw new InvalidOperationException("Packaged IconButton did not expose one labeled invocation.");
Console.WriteLine("Packaged Lucide Button/IconButton semantics: PASS");
var editor = initial.Require(SemanticRole.TextField, "Retained draft");
var edited = await app.InvokeAsync(context => context.Composition.ExecuteSemanticCommand(
    editor.Identity, new(SemanticCommandKind.SetValue, "keep this draft")));
if (edited != SemanticCommandResult.Applied)
    throw new InvalidOperationException("Packaged editor refused its current semantic command.");
using var wide = await app.ResizeAsync(new(640, 120, 1.5f));
var retained = wide.Require(SemanticRole.TextField, "Retained draft");
if (wide.RequireBox(wide.Require(SemanticRole.Button, "Run")).Bounds.Height != 64
    || retained.Identity.ElementId != editor.Identity.ElementId
    || retained.Identity.CompositionEpoch != editor.Identity.CompositionEpoch
    || retained.Value != "keep this draft")
    throw new InvalidOperationException("Packaged breakpoint style lost geometry or editor ownership.");
using var narrow = await app.ResizeAsync(new(320, 120, 2));
if (narrow.RequireBox(narrow.Require(SemanticRole.Button, "Run")).Bounds.Height != 40
    || narrow.Require(SemanticRole.TextField, "Retained draft").Value != "keep this draft")
    throw new InvalidOperationException("Packaged conditional style did not restore its base value.");
Console.WriteLine("Packaged parameterized styles, window breakpoints and retained layout: PASS");
var noticeCalls = 0;
await using (var notice = await HeadlessApplication.StartAsync(
    Components.ErrorNotice(() => "Package failure", () => noticeCalls++)))
{
    using var noticeSnapshot = await notice.SnapshotAsync();
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

await using (var artwork = await SkiaHeadlessApplication.StartAsync(
    HeadlessPackageProbe.Components.ImageProbe(), new() { Viewport = new(80, 40, 1.5f) }))
{
    var svgPreparation = await artwork.InvokeAsync(context => context.Composition.Images!.PreloadAsync(
        context.Composition.Root.Scope, Lucent.Icons.Lucide.LucideIcons.Search, new ImageRendition(64, 64)));
    var pixelPreparation = await artwork.InvokeAsync(context => context.Composition.Images!.PreloadAsync(
        context.Composition.Root.Scope, HeadlessPackageProbe.Assets.Pixel, new ImageRendition(64, 64)));
    var svgOutcome = await svgPreparation.WaitAsync(TimeSpan.FromSeconds(10));
    var pixelOutcome = await pixelPreparation.WaitAsync(TimeSpan.FromSeconds(10));
    if (svgOutcome.Status != ImagePreloadStatus.Ready || pixelOutcome.Status != ImagePreloadStatus.Ready)
        throw new InvalidOperationException("Packaged artwork did not prepare: SVG=" + svgOutcome.Status + " PNG=" + pixelOutcome.Status);
    using var snapshot = await artwork.SnapshotAsync();
    var images = ImageNodes(snapshot.Scene.Nodes).ToArray();
    if (images.Length != 3 || !ReferenceEquals(images[0].Image, images[1].Image)
        || images[0].ColorMode != ImageColorMode.Source
        || images[1].ColorMode != ImageColorMode.Monochrome
        || images[2].ColorMode != ImageColorMode.Monochrome
        || snapshot.FindAll(SemanticRole.Image).Count != 1)
        throw new InvalidOperationException("Packaged Image/Icon/Lucide SVG did not preserve loading or accessible intent.");
    var frame = await artwork.CapturePngAsync();
    using var decoded = SkiaSharp.SKBitmap.Decode(frame);
    if (decoded is null || decoded.Pixels.Count(pixel => pixel.Alpha != 0) < 32)
        throw new InvalidOperationException("Packaged Image/Icon/Lucide SVG did not paint pixels at 150% scale.");
    Console.WriteLine("Packaged typed PNG and Lucide SVG loading, semantics and 150% paint: PASS");
}

static IEnumerable<ImageSceneNode> ImageNodes(IEnumerable<SceneNode> nodes)
{
    foreach (var node in nodes)
    {
        if (node is ImageSceneNode image) yield return image;
        var children = node is ClipSceneNode clip ? clip.Children : node is OpacitySceneNode opacity ? opacity.Children : null;
        if (children is not null) foreach (var child in ImageNodes(children)) yield return child;
    }
}

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
    foreach ($notice in (Get-Content (Join-Path $root 'tools/package-notices.json') -Raw | ConvertFrom-Json | Where-Object package -in @('Lucent.Renderer.Skia', 'Lucent.Icons.Lucide'))) {
        if (-not (Test-Path -LiteralPath (Join-Path $proof "bin/Release/net10.0/$($notice.output)") -PathType Leaf)) {
            throw "Standalone renderer package omitted notice: $($notice.output)"
        }
    }
} finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
}
