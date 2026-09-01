param([switch] $Verify)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baseline = Get-Content (Join-Path $root 'docs/M7-TOOLING-BASELINE.json') -Raw | ConvertFrom-Json
$dotnet = Join-Path $root '.dotnet/dotnet.exe'; if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }

function Measure-Operation([string] $Name, [string[]] $Arguments) {
    $milliseconds = [Math]::Round((Measure-Command { & $dotnet @Arguments; if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." } }).TotalMilliseconds)
    $operation = $baseline.operations.$Name
    if ($operation.baselineMilliseconds -le 0 -or $operation.baselineMilliseconds -gt $operation.budgetMilliseconds) { throw "$Name has no valid frozen baseline/budget." }
    if ($milliseconds -gt $operation.budgetMilliseconds) { throw "$Name took $milliseconds ms; budget is $($operation.budgetMilliseconds) ms." }
    Write-Output "$Name=$milliseconds ms (budget $($operation.budgetMilliseconds) ms)"
}

if ($baseline.schema -ne 1 -or !$baseline.frozenBeforeOptimization) { throw 'Invalid M7 tooling baseline.' }
if ($Verify) {
    foreach ($measure in @('warmCompletion', 'editToDiagnostic', 'rename')) {
        if ($baseline.deferred.measure -notcontains $measure) { throw "Later-editor measure '$measure' must remain explicitly deferred." }
    }
}

Measure-Operation 'coldProjectLoad' @('build', (Join-Path $root 'apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj'), '-c', 'Release', '--no-restore', '-t:Rebuild', '-warnaserror')
Measure-Operation 'warmCompilerCorpus' @('run', '--project', (Join-Path $root 'tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj'), '--no-build')
Measure-Operation 'formattingCorpus' @('run', '--project', (Join-Path $root 'tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj'), '--no-build')
Measure-Operation 'incrementalNoOpAndOneFileInvalidation' @('run', '--project', (Join-Path $root 'tests/Lucent.Lui.Generator.Tests/Lucent.Lui.Generator.Tests.csproj'), '--no-build')

if ($Verify) { Write-Output 'M7 Lui tooling baseline: PASS' }
