$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }

Push-Location $root
try {
    & $dotnet tool restore
    if ($LASTEXITCODE) { exit $LASTEXITCODE }

    $files = @(& git ls-files '*.cs')
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    if ($files.Count -eq 0) { throw 'No tracked C# files were found.' }

    & $dotnet csharpier check @files
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

Write-Output "CSharpier authored C# check: PASS ($($files.Count) files)"
