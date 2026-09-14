param([string] $JsonGeneratorPath)

$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $probeRoot '../../../..')).Path
$dotnet = Join-Path $repositoryRoot '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnet = (Get-Command dotnet -CommandType Application).Source
}
if (-not $JsonGeneratorPath) {
    $JsonGeneratorPath = Join-Path $repositoryRoot '.dotnet/packs/Microsoft.NETCore.App.Ref/10.0.12/analyzers/dotnet/cs/System.Text.Json.SourceGeneration.dll'
}
if (-not (Test-Path -LiteralPath $JsonGeneratorPath -PathType Leaf)) {
    throw "Missing real JSON generator: $JsonGeneratorPath"
}

$artifactRoot = Join-Path $repositoryRoot 'artifacts/a0-isolated-emitter'
if (Test-Path -LiteralPath $artifactRoot) {
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactRoot)
    $resolvedRepository = [System.IO.Path]::GetFullPath($repositoryRoot)
    if (-not $resolvedArtifacts.StartsWith($resolvedRepository + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove artifact path outside the repository: $resolvedArtifacts"
    }
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot | Out-Null
$results = [System.Collections.Generic.List[string]]::new()

function Build-ProofProject([string] $project, [string] $name) {
    $output = Join-Path $artifactRoot "$name/bin"
    $intermediate = Join-Path $artifactRoot "$name/obj/"
    $arguments = @(
        'build', $project, '-c', 'Release', '--nologo',
        "-p:BaseIntermediateOutputPath=$intermediate",
        "-p:OutputPath=$output"
    )
    & $dotnet @arguments 2>&1 | ForEach-Object { Write-Host $_ }
    $results.Add("dotnet $($arguments -join ' ') => exit $LASTEXITCODE")
    if ($LASTEXITCODE) {
        throw "Build failed: $project (exit $LASTEXITCODE)"
    }
    return Join-Path $output ((Split-Path -Leaf $project) -replace '\.csproj$', '.dll')
}

$emitter = Build-ProofProject (Join-Path $probeRoot 'Emitter/ProjectEmitter.csproj') 'emitter'
$observer = Build-ProofProject (Join-Path $probeRoot 'External/AdditionalFileObserver.csproj') 'observer'
$proof = Build-ProofProject (Join-Path $probeRoot 'IsolatedEmitter.csproj') 'proof'

& $dotnet $proof $JsonGeneratorPath $emitter $observer
$proofExit = $LASTEXITCODE
$results.Add("dotnet $proof $JsonGeneratorPath $emitter $observer => exit $proofExit")
$results | Set-Content -LiteralPath (Join-Path $artifactRoot 'commands-and-exits.txt')
if ($proofExit) {
    throw "Isolated emitter proof failed (exit $proofExit)."
}
Write-Output 'PASS: precompiled emitter transport keeps authored AdditionalTexts isolated from the external generator while real JSON consumes early declarations.'
