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
Write-Output 'Packaged lint severity, LUI-only configuration and generated-symbol preservation: PASS'
