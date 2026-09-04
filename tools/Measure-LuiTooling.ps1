param([switch] $Verify)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baseline = Get-Content (Join-Path $root 'docs/M7-TOOLING-BASELINE.json') -Raw | ConvertFrom-Json
$dotnet = Join-Path $root '.dotnet/dotnet.exe'; if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }

foreach ($project in @(
    'tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj',
    'tests/Lucent.Lui.Generator.Tests/Lucent.Lui.Generator.Tests.csproj',
    'tests/Lucent.Lui.LanguageServer.Tests/Lucent.Lui.LanguageServer.Tests.csproj'
)) {
    & $dotnet build (Join-Path $root $project) --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "Failed to prepare tooling measurement project '$project'." }
}

function Measure-Operation([string] $Name, [string[]] $Arguments) {
    $milliseconds = [Math]::Round((Measure-Command { & $dotnet @Arguments; if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." } }).TotalMilliseconds)
    $operation = $baseline.operations.$Name
    if (!$operation.command -or $operation.baselineMilliseconds -le 0 -or $operation.baselineMilliseconds -gt $operation.budgetMilliseconds) { throw "$Name has no valid frozen baseline/budget." }
    if ($milliseconds -gt $operation.budgetMilliseconds) { throw "$Name took $milliseconds ms; budget is $($operation.budgetMilliseconds) ms." }
    Write-Output "$Name=$milliseconds ms (budget $($operation.budgetMilliseconds) ms)"
}

function Measure-LspOperation([string] $Name) {
    $output = & $dotnet run --project (Join-Path $root 'tests/Lucent.Lui.LanguageServer.Tests/Lucent.Lui.LanguageServer.Tests.csproj') --no-build -- --measure $Name 2>&1
    if ($LASTEXITCODE -ne 0) { throw "LSP $Name measurement failed ($LASTEXITCODE): $($output -join "`n")" }
    $line = $output | Where-Object { $_ -match '^MeasureMilliseconds=\d+$' } | Select-Object -Last 1
    if (!$line) { throw "LSP $Name measurement did not report milliseconds: $($output -join "`n")" }
    $milliseconds = [int]($line -replace '^MeasureMilliseconds=', '')
    $operation = $baseline.operations.$Name
    if (!$operation.command -or $operation.baselineMilliseconds -le 0 -or $operation.baselineMilliseconds -gt $operation.budgetMilliseconds) { throw "$Name has no valid frozen baseline/budget." }
    if ($milliseconds -gt $operation.budgetMilliseconds) { throw "$Name took $milliseconds ms; budget is $($operation.budgetMilliseconds) ms." }
    Write-Output "$Name=$milliseconds ms (budget $($operation.budgetMilliseconds) ms)"
}

if ($baseline.schema -ne 2 -or !$baseline.frozenBeforeOptimization) { throw 'Invalid M7 tooling baseline.' }
foreach ($measure in @('coldProjectLoad', 'warmCompilerCorpus', 'formattingCorpus', 'incrementalNoOpAndOneFileInvalidation', 'warmCompletion', 'editToDiagnostic', 'rename')) {
    if (!$baseline.operations.$measure) { throw "Missing required tooling measure '$measure'." }
}

Measure-Operation 'coldProjectLoad' @('build', (Join-Path $root 'apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj'), '-c', 'Release', '--no-restore', '-t:Rebuild', '-warnaserror')
Measure-Operation 'warmCompilerCorpus' @('run', '--project', (Join-Path $root 'tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj'), '--no-build')
Measure-Operation 'formattingCorpus' @('run', '--project', (Join-Path $root 'tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj'), '--no-build')
Measure-Operation 'incrementalNoOpAndOneFileInvalidation' @('run', '--project', (Join-Path $root 'tests/Lucent.Lui.Generator.Tests/Lucent.Lui.Generator.Tests.csproj'), '--no-build')
Measure-LspOperation 'warmCompletion'
Measure-LspOperation 'editToDiagnostic'
Measure-LspOperation 'rename'

if ($Verify) { Write-Output 'M7 Lui tooling baseline: PASS' }
