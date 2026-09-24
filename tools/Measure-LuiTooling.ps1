param(
    [switch] $Verify,
    [string] $ConfigurationPath,
    [string] $ResultsPath,
    [string] $DotnetPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$DotnetPath) {
    $DotnetPath = Join-Path $root '.dotnet/dotnet.exe'
    if (!(Test-Path $DotnetPath)) { $DotnetPath = 'dotnet' }
}
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

function Write-ResultReport($report) {
    $resultDirectory = Split-Path $ResultsPath -Parent
    if ($resultDirectory) {
        New-Item $resultDirectory -ItemType Directory -Force | Out-Null
    }

    $resultPath = [IO.Path]::GetFullPath($ResultsPath)
    $temporaryPath = "$resultPath.$PID.tmp"
    $json = $report | ConvertTo-Json -Depth 8
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            $json + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false)
        )
        Move-Item -LiteralPath $temporaryPath -Destination $resultPath -Force | Out-Null
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Expand-Argument([string] $argument) {
    $argument.Replace('{root}', $root)
}

$prerequisites = @(
    'tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj',
    'tests/Lucent.Lui.Generator.Tests/Lucent.Lui.Generator.Tests.csproj',
    'tests/Lucent.Lui.LanguageServer.Tests/Lucent.Lui.LanguageServer.Tests.csproj',
    'tools/Lucent.Lui.Tooling.Benchmarks/Lucent.Lui.Tooling.Benchmarks.csproj'
)
$preparation = @()
$results = @()
$report = [ordered]@{
    schema = 2
    status = 'running'
    measuredAtUtc = [DateTime]::UtcNow.ToString('O')
    completedAtUtc = $null
    configuration = [IO.Path]::GetFullPath($ConfigurationPath)
    verified = [bool]$Verify
    failure = $null
    preparation = $preparation
    operations = $results
}
$activeStage = 'preparation'
$activeRecord = $null

try {
    foreach ($project in $prerequisites) {
        $projectPath = Join-Path $root $project
        foreach ($verb in @('restore', 'build')) {
            $arguments = if ($verb -eq 'restore') {
                @('restore', $projectPath, '--locked-mode')
            } else {
                @('build', $projectPath, '--no-restore', '-c', 'Release', '-warnaserror')
            }
            $command = 'dotnet ' + ($arguments -join ' ')
            $record = [ordered]@{
                name = "$verb $project"
                status = 'running'
                command = $command
                exitCode = $null
                output = ''
                failure = $null
            }
            $preparation += $record

            $output = @()
            $invocationFailure = $null
            try {
                $global:LASTEXITCODE = 0
                $output = & $DotnetPath @arguments 2>&1
                $record.exitCode = [int]$LASTEXITCODE
            }
            catch {
                $invocationFailure = $_.Exception.Message
            }
            $record.output = $output -join [Environment]::NewLine

            if ($invocationFailure) {
                $record.status = 'failed'
                $record.failure = $invocationFailure
            }
            elseif ($record.exitCode -ne 0) {
                $record.status = 'failed'
                $record.failure = "Command exited with code $($record.exitCode)."
            }
            else {
                $record.status = 'completed'
            }

            if ($record.status -eq 'completed') {
                $record.output = ''
            }
            if ($record.status -ne 'completed') {
                $report.failure = [ordered]@{
                    stage = 'preparation'
                    operation = $record.name
                    message = $record.failure
                }
                throw "$($record.name) failed. $($record.failure) $($record.output)"
            }
        }
    }

    $activeStage = 'measurement'
    foreach ($operation in $configuration.operations) {
        if (!$operation.name -or !$operation.arguments) {
            throw 'Every tooling measurement operation requires a name and arguments.'
        }

        $arguments = @($operation.arguments | ForEach-Object { Expand-Argument $_ })
        $record = [ordered]@{
            name = $operation.name
            status = 'running'
            measurementStatus = 'incomplete'
            timingKind = $operation.timingKind
            measurementScope = $operation.measurementScope
            processMilliseconds = $null
            operationMilliseconds = $null
            milliseconds = $null
            maximumMilliseconds = $operation.maximumMilliseconds
            command = 'dotnet ' + ($arguments -join ' ')
            startedAtUtc = [DateTime]::UtcNow.ToString('O')
            completedAtUtc = $null
            exitCode = $null
            output = ''
            failure = $null
        }
        $results += $record
        $activeRecord = $record

        $output = @()
        $invocationFailure = $null
        $watch = [Diagnostics.Stopwatch]::StartNew()
        try {
            $global:LASTEXITCODE = 0
            $output = & $DotnetPath @arguments 2>&1
            $record.exitCode = [int]$LASTEXITCODE
        }
        catch {
            $invocationFailure = $_.Exception.Message
        }
        finally {
            $watch.Stop()
            $record.processMilliseconds = [Math]::Round($watch.Elapsed.TotalMilliseconds)
            $record.completedAtUtc = [DateTime]::UtcNow.ToString('O')
        }

        $record.output = $output -join [Environment]::NewLine
        if ($operation.resultPattern) {
            $resultMatch = [regex]::Match(
                $record.output,
                $operation.resultPattern
            )
            if ($resultMatch.Success -and $resultMatch.Groups['milliseconds'].Success) {
                $record.operationMilliseconds = [int]$resultMatch.Groups['milliseconds'].Value
            }
        }

        switch ($operation.timingKind) {
            'process' {
                $record.milliseconds = $record.processMilliseconds
                $record.measurementStatus = 'complete'
            }
            'operation' {
                if ($null -ne $record.operationMilliseconds) {
                    $record.milliseconds = $record.operationMilliseconds
                    $record.measurementStatus = 'complete'
                }
            }
            default {
                $record.failure = "Unsupported timingKind '$($operation.timingKind)' for '$($operation.name)'."
            }
        }

        if ($invocationFailure) {
            $record.status = 'failed'
            $record.failure = "Could not run command: $invocationFailure"
        }
        elseif ($null -ne $record.exitCode -and $record.exitCode -ne 0) {
            $record.status = 'failed'
            $record.failure = "Command exited with code $($record.exitCode)."
        }
        elseif ($record.failure) {
            $record.status = 'failed'
        }
        elseif ($record.measurementStatus -eq 'incomplete') {
            $record.status = 'incomplete'
            $record.failure = "$($operation.name) did not report an operation duration."
        }
        elseif ($Verify -and $null -ne $operation.maximumMilliseconds -and
            $record.milliseconds -gt $operation.maximumMilliseconds) {
            $record.status = 'failed'
            $record.failure = "$($operation.name) took $($record.milliseconds) ms; configured maximum is $($operation.maximumMilliseconds) ms."
        }
        else {
            $record.status = 'completed'
        }

        if ($record.status -eq 'completed') {
            $record.output = ''
        }
        if ($record.status -ne 'completed') {
            $report.failure = [ordered]@{
                stage = 'measurement'
                operation = $operation.name
                message = $record.failure
            }
            throw $record.failure
        }

        $maximum = if ($null -ne $operation.maximumMilliseconds) {
            " (maximum $($operation.maximumMilliseconds) ms)"
        } else {
            ''
        }
        Write-Output "$($operation.name)=$($record.milliseconds) ms$maximum"
        $activeRecord = $null
    }

    $report.status = 'completed'
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('O')
    $report.preparation = $preparation
    $report.operations = $results
    Write-ResultReport $report
    Write-Output "Tooling measurements: $ResultsPath"
}
catch {
    $failureMessage = $_.Exception.Message
    if ($activeRecord -and $activeRecord.status -eq 'running') {
        $activeRecord.status = 'failed'
        $activeRecord.completedAtUtc = [DateTime]::UtcNow.ToString('O')
        $activeRecord.failure = $failureMessage
    }
    if ($null -eq $report.failure) {
        $report.failure = [ordered]@{
            stage = $activeStage
            operation = if ($activeRecord) { $activeRecord.name } else { $null }
            message = $failureMessage
        }
    }

    $report.status = 'failed'
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('O')
    $report.preparation = $preparation
    $report.operations = $results
    try {
        Write-ResultReport $report
        Write-Output "Tooling measurements (failed): $ResultsPath"
    }
    catch {
        throw "Tooling measurement failed: $failureMessage Report writing also failed: $($_.Exception.Message)"
    }
    throw $failureMessage
}
