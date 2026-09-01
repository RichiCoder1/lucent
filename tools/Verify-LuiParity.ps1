$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'; if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
$testProject = Join-Path $root 'tests/Lucent.IssueBrowser.Tests/Lucent.IssueBrowser.Tests.csproj'
$appProject = Join-Path $root 'apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj'
$hostProject = Join-Path $root 'tests/Lucent.Platform.Windows.Tests/Lucent.Platform.Windows.Tests.csproj'
$publish = Join-Path $root 'artifacts/lui-parity'
$hostPublish = Join-Path $root 'artifacts/lui-parity-host'

function Invoke-Dotnet([string[]] $Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}

Invoke-Dotnet @('run', '--project', $testProject, '-c', 'Release', '--no-restore')
Remove-Item $publish, $hostPublish -Recurse -Force -ErrorAction Ignore
Invoke-Dotnet @('publish', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishAot=true', '-p:PublishTrimmed=true', '-warnaserror', '--no-restore', '-o', $publish)
& (Join-Path $PSScriptRoot 'Smoke-M0Publish.ps1') -PublishDirectory $publish -Iterations 1
if ($LASTEXITCODE -ne 0) { throw "NativeAOT Issue Browser proof failed ($LASTEXITCODE)." }
Invoke-Dotnet @('publish', $hostProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishAot=true', '-p:PublishTrimmed=true', '-warnaserror', '--no-restore', '-o', $hostPublish)
& (Join-Path $PSScriptRoot 'Invoke-VirtualizationProof.ps1') -AppExe (Join-Path $publish 'Lucent.IssueBrowser.exe') -HostExe (Join-Path $hostPublish 'Lucent.Platform.Windows.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw "NativeAOT virtualized Issue Row proof failed ($LASTEXITCODE)." }
Write-Output 'Lui Filter Bar/Issue Row managed parity and NativeAOT virtualization proof: PASS'
