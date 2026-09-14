param([switch] $NoBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }

Push-Location $root
try {
    & $dotnet tool restore
    if ($LASTEXITCODE) { exit $LASTEXITCODE }

    $files = @(& git ls-files --cached --others --exclude-standard '*.cs' | Where-Object { Test-Path -LiteralPath $_ } | Sort-Object -Unique)
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    if ($files.Count -eq 0) { throw 'No authored C# files were found.' }

    & $dotnet csharpier check @files
    if ($LASTEXITCODE) { exit $LASTEXITCODE }

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
    & $dotnet $tooling --check @luiFiles
    # Preserve drift (1) versus unavailable/invalid input or tool failure (2).
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

Write-Output "CSharpier working-tree C# check: PASS ($($files.Count) files)"
Write-Output "LUI working-tree format check: PASS ($($luiFiles.Count) files)"
