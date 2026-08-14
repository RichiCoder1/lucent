[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$project = Join-Path $repoRoot 'src\Lucent.LanguageServer\Lucent.LanguageServer.csproj'
$output = Join-Path $PSScriptRoot '..\server'

dotnet publish $project `
    --configuration $Configuration `
    --framework net9.0 `
    --output $output `
    --no-self-contained

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Published Lucent.LanguageServer to $((Resolve-Path $output).Path)"
