param(
    [Parameter(Mandatory)] [string] $Directory,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version
)
$ErrorActionPreference = 'Stop'
$ids = Get-Content (Join-Path $PSScriptRoot 'package-set.json') -Raw | ConvertFrom-Json
$expected = @($ids | ForEach-Object { "$($_).$Version.nupkg" })
$actual = @(Get-ChildItem -LiteralPath $Directory -File -Filter '*.nupkg')
$unexpected = @($actual | Where-Object { $_.Name -cnotin $expected })
if ($unexpected.Count) { throw "Unexpected package files: $($unexpected.Name -join ', ')" }
# Resolve the entire set before returning anything to the credential-bearing publisher.
$packages = @($expected | ForEach-Object {
    $path = Join-Path $Directory $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package: $_" }
    Get-Item -LiteralPath $path
})
$packages
