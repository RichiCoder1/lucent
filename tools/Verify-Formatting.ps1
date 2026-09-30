param([switch] $NoBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }

function Invoke-FileCheckBatches([string[]] $FixedArguments, [string[]] $Files) {
    # Keep each native command comfortably below Windows' 32,767-character command-line limit.
    $maxCommandCharacters = 24000
    $fixedCharacters = [Math]::Max($dotnet.Length, 'dotnet'.Length) + 520
    foreach ($argument in $FixedArguments) { $fixedCharacters += $argument.Length + 3 }

    $batch = [System.Collections.Generic.List[string]]::new()
    $batchCharacters = $fixedCharacters
    foreach ($file in $Files) {
        # Reserve a separator and quotes for every path, whether or not it needs quoting.
        $fileCharacters = $file.Length + 3
        if ($batch.Count -gt 0 -and $batchCharacters + $fileCharacters -gt $maxCommandCharacters) {
            $batchArguments = @($FixedArguments) + $batch.ToArray()
            & $dotnet @batchArguments
            if ($LASTEXITCODE) { exit $LASTEXITCODE }
            $batch.Clear()
            $batchCharacters = $fixedCharacters
        }
        if ($batchCharacters + $fileCharacters -gt $maxCommandCharacters) {
            throw "File path exceeds the safe command-line budget: $file"
        }
        $batch.Add($file)
        $batchCharacters += $fileCharacters
    }
    if ($batch.Count -gt 0) {
        $batchArguments = @($FixedArguments) + $batch.ToArray()
        & $dotnet @batchArguments
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
}

Push-Location $root
try {
    & $dotnet tool restore
    if ($LASTEXITCODE) { exit $LASTEXITCODE }

    $files = @(& git ls-files --cached --others --exclude-standard '*.cs' | Where-Object { Test-Path -LiteralPath $_ } | Sort-Object -Unique)
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    if ($files.Count -eq 0) { throw 'No authored C# files were found.' }

    $csharpierArguments = @('csharpier', 'check')
    Invoke-FileCheckBatches -FixedArguments $csharpierArguments -Files $files

    $luiFiles = @(& git ls-files --cached --others --exclude-standard '*.lui' | Where-Object { Test-Path -LiteralPath $_ } | Sort-Object -Unique)
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    if ($luiFiles.Count -eq 0) { throw 'No authored LUI files were found.' }
    if (-not $NoBuild) {
        & $dotnet restore src/Lucent.Lui.Tooling/Lucent.Lui.Tooling.csproj --locked-mode
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
        & $dotnet build src/Lucent.Lui.Tooling/Lucent.Lui.Tooling.csproj -c Release --no-restore
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
    $tooling = Join-Path $root 'src/Lucent.Lui.Tooling/bin/Release/net10.0/Lucent.Lui.Tooling.dll'
    if (-not (Test-Path -LiteralPath $tooling -PathType Leaf)) { throw 'Build LUI tooling before using -NoBuild.' }
    # Preserve drift (1) versus unavailable/invalid input or tool failure (2).
    $luiArguments = @($tooling, '--check')
    Invoke-FileCheckBatches -FixedArguments $luiArguments -Files $luiFiles
}
finally {
    Pop-Location
}

Write-Output "CSharpier working-tree C# check: PASS ($($files.Count) files)"
Write-Output "LUI working-tree format check: PASS ($($luiFiles.Count) files)"
