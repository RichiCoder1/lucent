$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root 'extensions/lucent-lui-vscode'
$tests = @(Get-ChildItem -LiteralPath $directory -File -Filter '*.test.cjs' | Sort-Object Name | ForEach-Object FullName)
if ($tests.Count -eq 0) { throw 'No Lucent editor contract suites were found.' }
& node --test @tests
exit $LASTEXITCODE
