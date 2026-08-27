$ErrorActionPreference = 'Stop'
dotnet restore "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj" --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj" -c Release -r win-x64 --self-contained true --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe" @args
exit $LASTEXITCODE
