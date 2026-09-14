$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$repositoryRoot = Resolve-Path (Join-Path $probeRoot '../../../..')
$dotnet = Join-Path $repositoryRoot '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = (Get-Command dotnet -CommandType Application).Source }
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

function Invoke-Consumer([string] $case, [string] $mismatchKind, [bool] $expectSuccess, [string] $generatorPath = $generator) {
    $caseRoot = Join-Path $artifactRoot $case
    $modelPath = Join-Path $caseRoot 'inputs/Model.lui'
    New-Item -ItemType Directory -Path (Split-Path -Parent $modelPath) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $probeRoot 'Consumer/Model.lui.input') -Destination $modelPath
    $arguments = @(
        'build', $consumerProject, '-c', 'Release',
        "-p:ProbeDotNetHost=$dotnet",
        "-p:ProbeGeneratorPath=$generatorPath",
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
            'invalid-analyzer' { 'PROBE0007' }
        }
        $hasProbeFailure = Select-String -LiteralPath $log -SimpleMatch 'PROBE9001'
        $hasExpectedCategory = Select-String -LiteralPath $log -Pattern ([regex]::Escape($expected) + '[^\]]+\]')
        if ($mismatchKind -eq 'invalid-analyzer') {
            $hasProbeFailure = Select-String -LiteralPath $log -SimpleMatch 'PROBE0007'
            $hasExpectedCategory = $hasProbeFailure
            if (Get-ChildItem -LiteralPath $caseRoot -Recurse -Filter manifest.json) {
                throw 'Failed analyzer load produced a preparation manifest.'
            }
        }
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

$positiveObj = Join-Path $positive 'obj/Release/net10.0'
$finalSources = Join-Path $positiveObj 'final-generated'
$markerFixture = Join-Path $finalSources 'ForeignMarker.g.cs'
$markerLog = Join-Path $positive 'foreign-marker.log'
try {
    # Output ownership comes from generator identity, never a source-text marker.
    '// PROBE_PROJECTION_OUTPUT PROBE_BINDING_OUTPUT' | Set-Content -LiteralPath $markerFixture
    $compareArguments = @($probeHostPath, 'compare', (Join-Path $positiveObj 'probe-preparation/manifest.json'), $finalSources)
    & $dotnet @compareArguments 2>&1 | Tee-Object -FilePath $markerLog | ForEach-Object { Write-Host $_ }
    $markerExit = $LASTEXITCODE
    $results.Add("dotnet $($compareArguments -join ' ') [foreign marker output] => exit $markerExit")
    if ($markerExit -ne 2 -or -not (Select-String -LiteralPath $markerLog -SimpleMatch 'extra=[ForeignMarker.g.cs]')) {
        throw 'Foreign output with a Lucent marker was not diagnosed as extra.'
    }
}
finally {
    Remove-Item -LiteralPath $markerFixture -ErrorAction SilentlyContinue
}

Invoke-Consumer 'negative-changed' 'changed' $false | Out-Null
Invoke-Consumer 'negative-missing' 'missing' $false | Out-Null
Invoke-Consumer 'negative-extra' 'extra' $false | Out-Null

$invalidGenerator = Join-Path $artifactRoot 'BrokenGenerator.dll'
'Not an analyzer assembly.' | Set-Content -LiteralPath $invalidGenerator
Invoke-Consumer 'negative-analyzer-load' 'invalid-analyzer' $false $invalidGenerator | Out-Null

$warmArguments = @(
    'build', $consumerProject, '-c', 'Release',
    "-p:ProbeDotNetHost=$dotnet",
    "-p:ProbeGeneratorPath=$generator",
    "-p:ProbeHostPath=$(Join-Path $positive 'missing-host.dll')",
    "-p:ProbeModelPath=$(Join-Path $positive 'inputs/Model.lui')",
    "-p:BaseIntermediateOutputPath=$(Join-Path $positive 'obj/')",
    "-p:OutputPath=$(Join-Path $positive 'bin')"
)
$warmLog = Join-Path $positive 'warm-missing-host.log'
& $dotnet @warmArguments 2>&1 | Tee-Object -FilePath $warmLog | ForEach-Object { Write-Host $_ }
$warmExit = $LASTEXITCODE
$results.Add("dotnet $($warmArguments -join ' ') [warm failure] => exit $warmExit")
if ($warmExit -ne 1 -or -not (Select-String -LiteralPath $warmLog -SimpleMatch 'Probe host was not built:')) {
    throw 'Warm rebuild did not fail for the expected missing host.'
}
if (Get-ChildItem -LiteralPath $positive -Recurse -Filter Consumer.dll) {
    throw 'Warm preparation failure left a stale Consumer.dll.'
}

$results | Set-Content -LiteralPath (Join-Path $artifactRoot 'commands-and-exits.txt')
Write-Output 'PASS: cold SDK build, real JSON generation, binding-only consumption, final identity/hash match, execution, and changed/missing/extra fail-closed checks.'
exit 0
