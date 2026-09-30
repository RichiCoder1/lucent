param(
    [Parameter(Mandatory)] [string] $Fixture,
    [Parameter(Mandatory)] [string] $GuestOutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../..'))
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/windows-activation-registered')) + [IO.Path]::DirectorySeparatorChar
$proof = (Resolve-Path -LiteralPath $Fixture).Path
if (-not $proof.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture must be within the isolated activation artifact directory.' }
$manifest = Get-Content -LiteralPath (Join-Path $proof 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.scheme -notmatch '^lucent[0-9a-f]{32}$') { throw 'Unsupported fixture identity.' }
if (-not [IO.Path]::IsPathFullyQualified($GuestOutputDirectory)) { throw 'Provide an absolute guest output directory.' }
$sourceExe = Join-Path $proof 'publish/Consumer.exe'
if ((Get-FileHash -LiteralPath $sourceExe -Algorithm SHA256).Hash -ne $manifest.executableSha256) { throw 'Fixture executable changed after preparation.' }
$makeAppx = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe'
if (-not (Test-Path -LiteralPath $makeAppx -PathType Leaf)) { throw 'Pinned Windows SDK MakeAppx.exe is unavailable.' }
$build = Join-Path $proof ('msix-build-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $build 'stage'
$assets = Join-Path $stage 'Assets'
$null = New-Item -ItemType Directory -Path $stage, $assets -Force
Copy-Item -Path (Join-Path $proof 'publish/*') -Destination $stage -Recurse
@($manifest.instanceKey, $manifest.scheme, $GuestOutputDirectory, (Join-Path $GuestOutputDirectory 'stop')) | Set-Content -LiteralPath (Join-Path $stage 'fixture.txt')
Add-Type -AssemblyName System.Drawing
foreach ($size in 44, 150) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.Clear([Drawing.Color]::FromArgb(31, 78, 121)) } finally { $graphics.Dispose() }
        $bitmap.Save((Join-Path $assets "Square$size.png"), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}
$suffix = $manifest.scheme.Substring('lucent'.Length)
$identity = 'Lucent.Probe.' + $suffix
$scheme = $manifest.scheme
@"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap rescap">
  <Identity Name="$identity" Publisher="CN=Lucent Activation Fixture" Version="1.0.0.0" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>Lucent Activation Fixture</DisplayName>
    <PublisherDisplayName>Lucent Test Fixture</PublisherDisplayName>
    <Description>Isolated protocol transport fixture</Description>
    <Logo>Assets\Square150.png</Logo>
  </Properties>
  <Resources><Resource Language="en-us" /></Resources>
  <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
  <Applications>
    <Application Id="Probe" Executable="Consumer.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Lucent Activation Fixture" Description="Isolated protocol transport fixture"
                          Square150x150Logo="Assets\Square150.png" Square44x44Logo="Assets\Square44.png"
                          BackgroundColor="#1F4E79" />
      <Extensions><uap:Extension Category="windows.protocol"><uap:Protocol Name="$scheme" /></uap:Extension></Extensions>
    </Application>
  </Applications>
</Package>
"@ | Set-Content -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Encoding utf8
$package = Join-Path $build 'Lucent.ActivationFixture.msix'
& $makeAppx pack /d $stage /p $package /h SHA256
if ($LASTEXITCODE) { throw 'MakeAppx failed to build the unsigned MSIX fixture.' }
[ordered]@{
    packageIdentity = $identity
    publisher = 'CN=Lucent Activation Fixture'
    protocolScheme = $scheme
    guestOutputDirectory = $GuestOutputDirectory
    manifestSha256 = (Get-FileHash -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Algorithm SHA256).Hash
    packageSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    buildScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    packageSourceCommit = $manifest.sourceCommit
    signed = $false
    installed = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $build 'msix-evidence.json')
Write-Output "Built unsigned MSIX fixture without signing or installation: $package"
