$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$solution = Join-Path $root 'Lucent.slnx'
$contracts = Join-Path $root 'tests/Lucent.Core.Tests/Lucent.Core.Tests.csproj'

& $dotnet restore $solution --locked-mode; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet build $solution --no-restore -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet run --project $contracts --no-build; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet publish $contracts -c Release -r win-x64 --self-contained true --no-restore -p:PublishAot=true -p:PublishTrimmed=true -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $root 'tests/Lucent.Core.Tests/bin/Release/net10.0/win-x64/publish/Lucent.Core.Tests.exe'); if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'Verify-M0.ps1'); exit $LASTEXITCODE
