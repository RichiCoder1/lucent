$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet restore (Join-Path $root 'Lucent.slnx') --locked-mode; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet build (Join-Path $root 'Lucent.slnx') --no-restore -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet publish (Join-Path $root 'apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj') -c Release -r win-x64 --self-contained true --no-restore -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
$publish = Join-Path $root 'apps/Lucent.IssueBrowser/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish'
& (Join-Path $PSScriptRoot 'Verify-M0Assets.ps1') $publish; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'Verify-M0Assets.ps1') $publish -Negative; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'Verify-CoreArchitecture.ps1') -Negative; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'Smoke-M0Publish.ps1') $publish; if ($LASTEXITCODE) { exit $LASTEXITCODE }
