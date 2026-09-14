$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$repositoryRoot = Resolve-Path (Join-Path $probeRoot '../../../..')
$dotnet = Join-Path $repositoryRoot '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$artifactRoot = Join-Path $repositoryRoot 'artifacts/a0-sdk-host'

if (Test-Path -LiteralPath $artifactRoot) {
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactRoot)
    $resolvedRepository = [System.IO.Path]::GetFullPath($repositoryRoot)
    if (-not $resolvedArtifacts.StartsWith($resolvedRepository + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove artifact path outside the repository: $resolvedArtifacts"
    }
    Remove-Item -LiteralPath $resolvedArtifacts -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot | Out-Null
$results = [System.Collections.Generic.List[string]]::new()

$generatorOutput = Join-Path $artifactRoot 'tools/generators/bin'
$generatorObj = Join-Path $artifactRoot 'tools/generators/obj/'
$hostOutput = Join-Path $artifactRoot 'tools/host/bin'
$hostObj = Join-Path $artifactRoot 'tools/host/obj/'
$generatorProject = Join-Path $probeRoot 'Generators/ProbeGenerators.csproj'
$hostProject = Join-Path $probeRoot 'Host/ProbeHost.csproj'
$consumerProject = Join-Path $probeRoot 'Consumer/Consumer.csproj'

$generatorArguments = @('build', $generatorProject, '-c', 'Release', "-p:BaseIntermediateOutputPath=$generatorObj", "-p:OutputPath=$generatorOutput")
& $dotnet @generatorArguments
$generatorExit = $LASTEXITCODE
$results.Add("dotnet $($generatorArguments -join ' ') => exit $generatorExit")
if ($generatorExit) { throw 'Probe generator build failed.' }
$hostArguments = @('build', $hostProject, '-c', 'Release', "-p:BaseIntermediateOutputPath=$hostObj", "-p:OutputPath=$hostOutput")
& $dotnet @hostArguments
$hostExit = $LASTEXITCODE
$results.Add("dotnet $($hostArguments -join ' ') => exit $hostExit")
if ($hostExit) { throw 'Probe host build failed.' }

$generator = Join-Path $generatorOutput 'ProbeGenerators.dll'
$probeHostPath = Join-Path $hostOutput 'ProbeHost.dll'

function Invoke-Consumer([string] $case, [string] $mismatchKind, [bool] $expectSuccess) {
    $caseRoot = Join-Path $artifactRoot $case
    $modelPath = Join-Path $caseRoot 'inputs/Model.lui'
    New-Item -ItemType Directory -Path (Split-Path -Parent $modelPath) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $probeRoot 'Consumer/Model.lui.input') -Destination $modelPath
    $arguments = @(
        'build', $consumerProject, '-c', 'Release',
        "-p:ProbeGeneratorPath=$generator",
        "-p:ProbeHostPath=$probeHostPath",
        "-p:ProbeMismatchKind=$mismatchKind",
        "-p:ProbeModelPath=$modelPath",
        "-p:BaseIntermediateOutputPath=$(Join-Path $caseRoot 'obj/')",
        "-p:OutputPath=$(Join-Path $caseRoot 'bin')"
    )
    $log = Join-Path $caseRoot 'build.log'
    New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
    & $dotnet @arguments 2>&1 | Tee-Object -FilePath $log | ForEach-Object { Write-Host $_ }
    $exitCode = $LASTEXITCODE
    $results.Add("dotnet $($arguments -join ' ') => exit $exitCode")
    if ($expectSuccess -and $exitCode) { throw "Positive SDK-host build failed; see $log" }
    if (-not $expectSuccess -and -not $exitCode) { throw "Negative '$case' build unexpectedly succeeded." }
    if (-not $expectSuccess) {
        $expected = switch ($mismatchKind) {
            'changed' { 'changed=[' }
            'missing' { 'missing=[' }
            'extra' { 'extra=[' }
        }
        $hasProbeFailure = Select-String -LiteralPath $log -SimpleMatch 'PROBE9001'
        $hasExpectedCategory = Select-String -LiteralPath $log -SimpleMatch $expected
        if (-not $hasProbeFailure -or -not $hasExpectedCategory) {
            throw "Negative '$case' did not fail with the expected mismatch evidence."
        }
        $leftoverAssemblies = Get-ChildItem -LiteralPath $caseRoot -Recurse -Filter Consumer.dll
        if ($leftoverAssemblies) {
            throw "Negative '$case' left output assemblies: $($leftoverAssemblies.FullName -join ', ')"
        }
    }
    return $caseRoot
}

$positive = Invoke-Consumer 'positive' 'none' $true
$application = Join-Path $positive 'bin/Consumer.exe'
& $application | Tee-Object -FilePath (Join-Path $positive 'run.log')
$runExit = $LASTEXITCODE
$results.Add("$application => exit $runExit")
if ($runExit) { throw 'Positive SDK-host application execution failed.' }

Invoke-Consumer 'negative-changed' 'changed' $false | Out-Null
Invoke-Consumer 'negative-missing' 'missing' $false | Out-Null
Invoke-Consumer 'negative-extra' 'extra' $false | Out-Null

$results | Set-Content -LiteralPath (Join-Path $artifactRoot 'commands-and-exits.txt')
Write-Output 'PASS: cold SDK build, real JSON generation, binding-only consumption, final identity/hash match, execution, and changed/missing/extra fail-closed checks.'
