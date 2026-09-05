param(
    [switch] $Verify,
    [string] $ConfigurationPath,
    [string] $ResultsPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
if (!$ConfigurationPath) {
    $ConfigurationPath = Join-Path $PSScriptRoot 'LuiTooling.Measurements.json'
}
if (!$ResultsPath) {
    $ResultsPath = Join-Path $root 'artifacts/lui-tooling-measurements.json'
}

$configuration = Get-Content $ConfigurationPath -Raw | ConvertFrom-Json
if ($configuration.schema -ne 1 -or !$configuration.operations) {
    throw "Invalid tooling measurement configuration '$ConfigurationPath'."
}

$prerequisites = @(
    'tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj',
    'tests/Lucent.Lui.Generator.Tests/Lucent.Lui.Generator.Tests.csproj',
    'tests/Lucent.Lui.LanguageServer.Tests/Lucent.Lui.LanguageServer.Tests.csproj',
    'tools/Lucent.Lui.Tooling.Benchmarks/Lucent.Lui.Tooling.Benchmarks.csproj'
)
foreach ($project in $prerequisites) {
    $projectPath = Join-Path $root $project
    & $dotnet restore $projectPath --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Failed to restore tooling prerequisite '$project'." }
    & $dotnet build $projectPath --no-restore -c Release -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "Failed to build tooling prerequisite '$project'." }
}
function Expand-Argument([string] $argument) {
    $argument.Replace('{root}', $root)
}

$results = @()
foreach ($operation in $configuration.operations) {
    if (!$operation.name -or !$operation.arguments) {
        throw 'Every tooling measurement operation requires a name and arguments.'
    }

    $arguments = @($operation.arguments | ForEach-Object { Expand-Argument $_ })
    $output = $null
    $elapsed = Measure-Command {
        $output = & $dotnet @arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            $details = $output -join [Environment]::NewLine
            throw "dotnet $($arguments -join ' ') failed ($LASTEXITCODE). $details"
        }
    }
    $milliseconds = [Math]::Round($elapsed.TotalMilliseconds)
    if ($operation.resultPattern) {
        $resultMatch = [regex]::Match(
            ($output -join [Environment]::NewLine),
            $operation.resultPattern
        )
        if (!$resultMatch.Success -or !$resultMatch.Groups['milliseconds'].Success) {
            throw "$($operation.name) did not report an operation duration."
        }
        $milliseconds = [int]$resultMatch.Groups['milliseconds'].Value
    }
    if ($Verify -and $operation.maximumMilliseconds -and $milliseconds -gt $operation.maximumMilliseconds) {
        throw "$($operation.name) took $milliseconds ms; configured maximum is $($operation.maximumMilliseconds) ms."
    }

    $results += [ordered]@{
        name = $operation.name
        timingKind = $operation.timingKind
        measurementScope = $operation.measurementScope
        milliseconds = $milliseconds
        maximumMilliseconds = $operation.maximumMilliseconds
        command = 'dotnet ' + ($arguments -join ' ')
    }
    $maximum = if ($operation.maximumMilliseconds) {
        " (maximum $($operation.maximumMilliseconds) ms)"
    } else {
        ''
    }
    Write-Output "$($operation.name)=$milliseconds ms$maximum"
}

$resultDirectory = Split-Path $ResultsPath -Parent
if ($resultDirectory) {
    New-Item $resultDirectory -ItemType Directory -Force | Out-Null
}
[ordered]@{
    schema = 1
    measuredAtUtc = [DateTime]::UtcNow.ToString('O')
    configuration = [IO.Path]::GetFullPath($ConfigurationPath)
    verified = [bool]$Verify
    operations = $results
} | ConvertTo-Json -Depth 5 | Set-Content $ResultsPath

Write-Output "Tooling measurements: $ResultsPath"
