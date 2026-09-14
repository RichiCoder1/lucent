[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

& $dotnet build (Join-Path $root "src/Lucent.Lui.Compiler/Lucent.Lui.Compiler.csproj") --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build (Join-Path $root "src/Lucent.Core/Lucent.Core.csproj") --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet restore (Join-Path $PSScriptRoot "CompanionProbe.csproj")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet run --project (Join-Path $PSScriptRoot "CompanionProbe.csproj") --configuration Release --no-restore
exit $LASTEXITCODE
