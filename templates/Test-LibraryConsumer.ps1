#requires -Version 7.4
param(
    [Parameter(Mandatory)][string] $ProofDirectory,
    [Parameter(Mandatory)][string] $DotNetPath,
    [Parameter(Mandatory)][string] $Version
)
$ErrorActionPreference = 'Stop'
$consumer = Join-Path $ProofDirectory 'library-consumer'
[IO.Directory]::CreateDirectory($consumer) | Out-Null
@"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Example.Cards" Version="[1.0.0]" />
    <PackageReference Include="Lucent.Testing.Skia" Version="[$Version]" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'Consumer.csproj')
@'
namespace PackageConsumer;

public component LibraryView() {
    <Example.Cards.CounterCard title="Packaged counter" />
    <Example.Cards.PackageGreeting title="Packaged item" />
}
'@ | Set-Content -LiteralPath (Join-Path $consumer 'LibraryView.lui')
@'
using System.Security.Cryptography;
using Lucent.Core;
using Lucent.Testing.Skia;

using (var stream = Example.Cards.Assets.Mark.PackagedAsset!.OpenRead())
{
    var actual = Convert.ToHexString(SHA256.HashData(stream));
    if (!actual.Equals(args[0], StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("The library package changed or omitted its embedded artwork.");
}
await using var app = await SkiaHeadlessApplication.StartAsync(PackageConsumer.Components.LibraryView());
using var before = await app.SnapshotAsync();
_ = before.Require(SemanticRole.Text, "Packaged counter");
_ = before.Require(SemanticRole.Text, "Packaged item");
_ = before.Require(SemanticRole.Image, "Counter card mark");
_ = before.Require(SemanticRole.Text, "Count: 0");
var button = before.Require(SemanticRole.Button, "Increment");
var result = await app.InvokeAsync(context => context.Composition.ExecuteSemanticCommand(
    button.Identity, new(SemanticCommandKind.Invoke)));
using var after = await app.SnapshotAsync();
_ = after.Require(SemanticRole.Text, "Count: 1");
if (result != SemanticCommandResult.Applied || after.Require(SemanticRole.Button, "Increment").Identity != button.Identity)
    throw new InvalidOperationException("Packaged library component lost its action or retained identity.");
Console.WriteLine("Package-only generated library, item, embedded artwork and retained action: PASS");
'@ | Set-Content -LiteralPath (Join-Path $consumer 'Program.cs')
$hash = (Get-FileHash -LiteralPath (Join-Path $ProofDirectory 'library/Artwork/mark.svg')).Hash
Push-Location $consumer
try {
    & $DotNetPath restore Consumer.csproj --configfile (Join-Path $ProofDirectory 'NuGet.config') 2>&1 | Tee-Object -FilePath (Join-Path $consumer 'restore.log') | Out-Host
    if ($LASTEXITCODE) { throw 'Generated library consumer restore failed.' }
    & $DotNetPath run --project Consumer.csproj -c Release --no-restore -- $hash 2>&1 | Tee-Object -FilePath (Join-Path $consumer 'run.log') | Out-Host
    if ($LASTEXITCODE) { throw 'Generated library consumer failed.' }
}
finally { Pop-Location }
