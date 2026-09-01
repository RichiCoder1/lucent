$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'; if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
$testProject = Join-Path $root 'tests/Lucent.IssueBrowser.Tests/Lucent.IssueBrowser.Tests.csproj'
$appProject = Join-Path $root 'apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj'
$publish = Join-Path $root 'artifacts/lui-filterbar-parity'

function Invoke-Dotnet([string[]] $Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}

Invoke-Dotnet @('run', '--project', $testProject, '-c', 'Release', '--no-restore')
Remove-Item $publish -Recurse -Force -ErrorAction Ignore
Invoke-Dotnet @('publish', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishAot=true', '-p:PublishTrimmed=true', '-warnaserror', '--no-restore', '-o', $publish)
& (Join-Path $PSScriptRoot 'Smoke-M0Publish.ps1') -PublishDirectory $publish -Iterations 1
if ($LASTEXITCODE -ne 0) { throw "NativeAOT Issue Browser proof failed ($LASTEXITCODE)." }
Write-Output 'Lui Filter Bar managed parity and NativeAOT production-path proof: PASS'
