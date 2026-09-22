param(
    [Parameter(Mandatory)] [string] $Project,
    [Parameter(Mandatory)] [string] $Tooling,
    [Parameter(Mandatory)] [string] $Dotnet
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path ([IO.Path]::GetFullPath($Project)) -Parent
# These callers create throwaway consumers. Prevent repository targets from
# masking a missing import in the packaged SDK under test.
'<Project />' | Set-Content (Join-Path $projectDirectory 'Directory.Build.targets')
$fixture = Join-Path $projectDirectory 'lint-policy'
$null = New-Item -ItemType Directory -Path $fixture -Force
$source = Join-Path $fixture 'PolicyProbe.lui'
$configuration = Join-Path $fixture '.editorconfig'
# Keep C# outside this directory: normal Roslyn discovery only walks C# ancestors.
# The SDK must discover this LUI-only configuration and transport its text itself.
@'
namespace LintPolicyProbe;
using Lucent.Core;
public static class Factory
{
    [LucentComponent]
    public static ComponentRecipe Caption([DefaultContent] string label) => null!;
    public static ComponentRecipe Generated() => Components.PolicyProbe();
}
'@ | Set-Content (Join-Path $projectDirectory 'LintPolicyFactory.cs')
@'
namespace LintPolicyProbe;
using static LintPolicyProbe.Factory;
internal component PolicyProbe() { <Caption label="Lint policy" /> }
'@ | Set-Content $source

function Invoke-Expected([string[]] $Arguments, [int] $ExpectedExit, [string] $Diagnostic) {
    $output = (& $Dotnet @Arguments 2>&1) -join "`n"
    $actualExit = $LASTEXITCODE
    if ($actualExit -ne $ExpectedExit -or ($Diagnostic -and $output -notmatch [regex]::Escape($Diagnostic))) {
        throw "Expected exit $ExpectedExit and diagnostic '$Diagnostic', got $actualExit.`n$output"
    }
    if ($output -match 'CS0103|CS0117') { throw "Lint severity caused a generated-symbol error.`n$output" }
}

Set-Content $configuration "root = true`n[*.lui]`ndotnet_diagnostic.LUI5003.severity = error"
Invoke-Expected @('build', $Project, '-c', 'Release', '--no-restore') 1 'LUI5003'
Invoke-Expected @($Tooling, '--lint', '--project', $Project, $source) 2 'LUI5003'

# Changing configuration alone must invalidate the diagnostic on the next build.
Set-Content $configuration "root = true`n[*.lui]`ndotnet_diagnostic.LUI5003.severity = none"
Invoke-Expected @('build', $Project, '-c', 'Release', '--no-restore', '-warnaserror') 0 ''
Invoke-Expected @($Tooling, '--lint', '--project', $Project, $source) 0 ''

$namedFixture = Join-Path $projectDirectory 'named-lint-policy'
$null = New-Item -ItemType Directory -Path $namedFixture -Force
$namedInput = Join-Path $namedFixture 'lui-input'
$null = New-Item -ItemType Directory -Path $namedInput -Force
$namedVersionPath = Split-Path (Split-Path (Split-Path $Tooling -Parent) -Parent) -Parent
$namedVersion = Split-Path $namedVersionPath -Leaf
$namedProject = Join-Path $namedFixture 'NamedLintPolicy.csproj'
$namedSource = Join-Path $namedInput 'PolicyProbe.lui'
$namedConfiguration = Join-Path $namedInput '.editorconfig'
$namedCompanion = Join-Path $namedFixture 'PolicyProbe.lui.cs'
@"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$namedVersion">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>14.0</LangVersion><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><LucentLuiNamedComponents>true</LucentLuiNamedComponents><TreatWarningsAsErrors>false</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup><PackageReference Include="Lucent.Core" Version="[$namedVersion]" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $namedProject
@'
namespace LintPolicyProbe;
using Lucent.Core;
public static class Factory
{
    [LucentComponent]
    public static ComponentRecipe Caption([DefaultContent] string label) => null!;
}
'@ | Set-Content (Join-Path $namedFixture 'Factory.cs')
$namedSourceText = @'
namespace LintPolicyProbe;
using static LintPolicyProbe.Factory;
public component PolicyProbe(string[] items) {
    foreach (var item in items) keyed by System.Guid.NewGuid() { <Caption>{item}</Caption> }
}
'@
Set-Content -LiteralPath $namedSource -Value $namedSourceText
Set-Content -LiteralPath $namedConfiguration "root = true`n[*.lui]`ndotnet_diagnostic.LUI5001.severity = warning"
$namedNuGetConfig = Join-Path (Split-Path $projectDirectory -Parent) 'NuGet.config'
Invoke-Expected @('restore', $namedProject, '--configfile', $namedNuGetConfig) 0 ''

function Set-NamedLintSeverity([string] $Severity) {
    Set-Content -LiteralPath $namedConfiguration "root = true`n[*.lui]`ndotnet_diagnostic.LUI5001.severity = $Severity"
}

function Invoke-NamedBuild([int] $ExpectedExit, [string] $Required, [string] $Forbidden) {
    $arguments = @('build', $namedProject, '-c', 'Release', '--no-restore', '-p:TreatWarningsAsErrors=false')
    $output = (& $Dotnet @arguments 2>&1) -join "`n"
    $actualExit = $LASTEXITCODE
    if (
        $actualExit -ne $ExpectedExit
        -or ($Required -and $output -notmatch [regex]::Escape($Required))
        -or ($Forbidden -and $output -match [regex]::Escape($Forbidden))
    ) {
        throw "Expected named build exit $ExpectedExit with '$Required' and without '$Forbidden', got $actualExit.`n$output"
    }
    return $output
}

$warningOutput = Invoke-NamedBuild 0 'warning LUI5001' ''
Set-NamedLintSeverity 'error'
Invoke-NamedBuild 1 'error LUI5001' '' | Out-Null
Set-NamedLintSeverity 'none'
Invoke-NamedBuild 0 '' 'LUI5001' | Out-Null

Set-NamedLintSeverity 'warning'
$lintLine = '    foreach (var item in items) keyed by System.Guid.NewGuid() { <Caption>{item}</Caption> }'
$suppressedSource = $namedSourceText.Replace(
    $lintLine,
    '    // lui-lint-disable-next LUI5001: This package probe checks authored suppression.'
        + [Environment]::NewLine
        + $lintLine
)
Set-Content -LiteralPath $namedSource -Value $suppressedSource
Invoke-NamedBuild 0 '' 'LUI5001' | Out-Null
Set-Content -LiteralPath $namedSource -Value $namedSourceText

# An invalid component that is not referenced by C# must still fail preparation
# when its structural diagnostics are downgraded or suppressed.
$unusedNamedSource = Join-Path $namedInput 'UnusedBroken.lui'
try {
    @'
namespace LintPolicyProbe;
using Lucent.Core;
using static Lucent.Core.Components;
public component UnusedBroken() { <Text content={MissingValue} /> }
'@ | Set-Content -LiteralPath $unusedNamedSource
    Set-Content -LiteralPath $namedConfiguration "root = true`n[*.lui]`ndotnet_diagnostic.LUI2000.severity = none`ndotnet_diagnostic.LUI5006.severity = none"
    Invoke-NamedBuild 1 'LUI2000' '' | Out-Null
}
finally {
    Remove-Item -LiteralPath $unusedNamedSource -Force -ErrorAction SilentlyContinue
}

Set-NamedLintSeverity 'fatal'
Invoke-NamedBuild 1 'LUI6102' '' | Out-Null

Set-NamedLintSeverity 'none'
try {
    @'
namespace LintPolicyProbe;
[Lucent.Core.ComponentState]
public sealed partial class PolicyProbe { }
'@ | Set-Content -LiteralPath $namedCompanion
    Invoke-NamedBuild 1 'LUI4108' 'LUI5001' | Out-Null
}
finally {
    Remove-Item -LiteralPath $namedCompanion -Force -ErrorAction SilentlyContinue
}

Write-Output 'Packaged lint severity, LUI-only configuration and generated-symbol preservation: PASS'
Write-Output 'Packaged named lint warning, error, configuration and suppression parity: PASS'
