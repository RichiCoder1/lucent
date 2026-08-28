$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$solution = Join-Path $root 'Lucent.slnx'
$contracts = Join-Path $root 'tests/Lucent.Core.Tests/Lucent.Core.Tests.csproj'
$issueContracts = Join-Path $root 'tests/Lucent.IssueBrowser.Tests/Lucent.IssueBrowser.Tests.csproj'
$rendererContracts = Join-Path $root 'tests/Lucent.Renderer.Skia.Tests/Lucent.Renderer.Skia.Tests.csproj'
$platformContracts = Join-Path $root 'tests/Lucent.Platform.Windows.Tests/Lucent.Platform.Windows.Tests.csproj'

& $dotnet restore $solution --locked-mode; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet build $solution --no-restore -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet (Join-Path $root 'tests/Lucent.Core.Tests/bin/Debug/net10.0/Lucent.Core.Tests.dll') (Join-Path $root 'src/Lucent.Core/bin/Debug/net10.0/Lucent.Core.dll'); if ($LASTEXITCODE) { exit $LASTEXITCODE }
Write-Output 'Core compiled runtime-discovery/property-model scan: PASS'
& $dotnet run --project $contracts --no-build; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet run --project $rendererContracts --no-build; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet run --project $platformContracts --no-build; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet run --project $issueContracts --no-build; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet publish $contracts -c Release -r win-x64 --self-contained true --no-restore -p:PublishAot=true -p:PublishTrimmed=true -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $root 'tests/Lucent.Core.Tests/bin/Release/net10.0/win-x64/publish/Lucent.Core.Tests.exe'); if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet publish $rendererContracts -c Release -r win-x64 --self-contained true --no-restore -p:PublishAot=true -p:PublishTrimmed=true -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $root 'tests/Lucent.Renderer.Skia.Tests/bin/Release/net10.0/win-x64/publish/Lucent.Renderer.Skia.Tests.exe'); if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet publish $platformContracts -c Release -r win-x64 --self-contained true --no-restore -p:PublishAot=true -p:PublishTrimmed=true -warnaserror; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $root 'tests/Lucent.Platform.Windows.Tests/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/Lucent.Platform.Windows.Tests.exe'); if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'Smoke-WindowsListener.ps1') (Join-Path $root 'tests/Lucent.Platform.Windows.Tests/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/Lucent.Platform.Windows.Tests.exe'); if ($LASTEXITCODE) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'Verify-M0.ps1'); exit $LASTEXITCODE
