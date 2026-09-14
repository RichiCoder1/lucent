$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$repositoryRoot = Resolve-Path (Join-Path $probeRoot '../../../..')
$dotnet = Join-Path $repositoryRoot '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$artifactRoot = Join-Path $repositoryRoot 'artifacts/a0-editor'

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

$hostProject = Join-Path $repositoryRoot 'tests/Probes/ApplicationAuthoring/SdkHost/Host/ProbeHost.csproj'
$hostOutput = Join-Path $artifactRoot 'tools/host/bin'
$hostArguments = @('build', $hostProject, '-c', 'Release', "-p:BaseIntermediateOutputPath=$(Join-Path $artifactRoot 'tools/host/obj/')", "-p:OutputPath=$hostOutput")
& $dotnet @hostArguments
$hostExit = $LASTEXITCODE
$results.Add("dotnet $($hostArguments -join ' ') => exit $hostExit")
if ($hostExit) { throw 'Shared probe host build failed.' }
$probeHost = Join-Path $hostOutput 'ProbeHost.dll'

$editorProject = Join-Path $probeRoot 'EditorProbe.csproj'
$editorOutput = Join-Path $artifactRoot 'tools/editor/bin'
$editorArguments = @('build', $editorProject, '-c', 'Release', "-p:ProbeHostPath=$probeHost", "-p:BaseIntermediateOutputPath=$(Join-Path $artifactRoot 'tools/editor/obj/')", "-p:OutputPath=$editorOutput")
& $dotnet @editorArguments
$editorExit = $LASTEXITCODE
$results.Add("dotnet $($editorArguments -join ' ') => exit $editorExit")
if ($editorExit) { throw 'Editor probe build failed.' }

$fixtureProject = Join-Path $probeRoot 'Fixture/Fixture.csproj'
$fixtureObjectPath = Join-Path $artifactRoot 'fixture-obj/'
$modelPath = Join-Path $artifactRoot 'inputs/Model.lui'
New-Item -ItemType Directory -Path (Split-Path -Parent $modelPath) | Out-Null
Copy-Item -LiteralPath (Join-Path $probeRoot 'Fixture/Model.lui.input') -Destination $modelPath
$restoreArguments = @('restore', $fixtureProject, "-p:ProbeModelPath=$modelPath", "-p:BaseIntermediateOutputPath=$fixtureObjectPath")
& $dotnet @restoreArguments
$restoreExit = $LASTEXITCODE
$results.Add("dotnet $($restoreArguments -join ' ') => exit $restoreExit")
if ($restoreExit) { throw 'Fixture restore failed.' }

$editor = Join-Path $editorOutput 'EditorProbe.exe'
& $editor $fixtureProject $modelPath $fixtureObjectPath $artifactRoot 2>&1 | Tee-Object -FilePath (Join-Path $artifactRoot 'editor.log') | ForEach-Object { Write-Host $_ }
$runExit = $LASTEXITCODE
$results.Add("$editor $fixtureProject $modelPath $fixtureObjectPath $artifactRoot => exit $runExit")
$results | Set-Content -LiteralPath (Join-Path $artifactRoot 'commands-and-exits.txt')
if ($runExit) { throw 'Editor feasibility probe failed.' }
