$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $root ("artifacts/package-set-tests/" + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
$version = '0.3.0-dev.fixture.1'
$ids = Get-Content (Join-Path $PSScriptRoot 'package-set.json') -Raw | ConvertFrom-Json
foreach ($id in $ids) { [IO.File]::WriteAllBytes((Join-Path $fixture "$id.$version.nupkg"), @()) }
$check = Join-Path $PSScriptRoot 'Get-PackageSet.ps1'
$valid = @(& $check -Directory $fixture -Version $version)
if ($valid.Count -ne $ids.Count) { throw "Expected $($ids.Count) packages; received $($valid.Count)." }

function Assert-Rejected([string] $Expected) {
    $emitted = [Collections.Generic.List[object]]::new()
    try {
        & $check -Directory $fixture -Version $version | ForEach-Object { $emitted.Add($_) }
    }
    catch {
        if ($_.Exception.Message -ne $Expected) { throw "Unexpected rejection: $($_.Exception.Message)" }
        if ($emitted.Count -ne 0) { throw 'The incomplete package set emitted files before failing.' }
        return
    }
    throw "Invalid package set was accepted; expected: $Expected"
}

$extra = Join-Path $fixture "Unexpected.$version.nupkg"
[IO.File]::WriteAllBytes($extra, @())
Assert-Rejected "Unexpected package files: Unexpected.$version.nupkg"
Remove-Item -LiteralPath $extra
$missing = "$($ids[-1]).$version.nupkg"
Remove-Item -LiteralPath (Join-Path $fixture $missing)
Assert-Rejected "Missing package: $missing"
Write-Output 'Package publication allowlist: PASS (complete, extra, missing; no partial output).'
