param([string] $JsonGeneratorPath)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$dotnet = Join-Path $repo '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw 'Install the repository-pinned SDK first.' }
if (-not $JsonGeneratorPath) {
    $JsonGeneratorPath = Join-Path $repo '.dotnet/packs/Microsoft.NETCore.App.Ref/10.0.12/analyzers/dotnet/cs/System.Text.Json.SourceGeneration.dll'
}
if (-not (Test-Path -LiteralPath $JsonGeneratorPath -PathType Leaf)) { throw "Missing real JSON generator: $JsonGeneratorPath" }
$JsonGeneratorPath = (Resolve-Path -LiteralPath $JsonGeneratorPath).Path

Push-Location $repo
try {
    foreach ($project in @('src/Lucent.Core/Lucent.Core.csproj', 'src/Lucent.Lui.Compiler/Lucent.Lui.Compiler.csproj')) {
        & $dotnet restore $project --locked-mode
        if ($LASTEXITCODE) { throw "Restore failed: $project (exit $LASTEXITCODE)" }
        & $dotnet build $project -c Release --no-restore
        if ($LASTEXITCODE) { throw "Build failed: $project (exit $LASTEXITCODE)" }
    }
    & $dotnet run --project (Join-Path $PSScriptRoot 'GenerationProbe.csproj') -c Release -- $JsonGeneratorPath
    if ($LASTEXITCODE) { throw "Generation phase probe failed (exit $LASTEXITCODE)." }
}
finally { Pop-Location }
