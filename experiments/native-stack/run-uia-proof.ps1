$ErrorActionPreference = 'Stop'

function Wait-Path([string]$Path, [int]$Seconds) {
    $until = [DateTime]::UtcNow.AddSeconds($Seconds)
    while (-not (Test-Path $Path) -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 50 }
    if (-not (Test-Path $Path)) { throw "Timed out waiting for $Path." }
}

function Stop-Tree($Process) {
    if ($null -eq $Process -or $Process.HasExited) { return }
    try { $Process.Kill($true) }
    catch {
        & taskkill.exe /PID $Process.Id /T /F *> $null
        if (-not $Process.HasExited) { $Process.Kill() }
    }
    $Process.WaitForExit(5000) | Out-Null
}

dotnet restore "$PSScriptRoot/NativeStack.sln" --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build "$PSScriptRoot/NativeStack.sln" --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj" -c Release -r win-x64 --self-contained true --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$work = Join-Path ([IO.Path]::GetTempPath()) ("native-stack-uia-" + [Guid]::NewGuid())
$null = New-Item -ItemType Directory -Path $work
$ready = Join-Path $work 'ready.json'
$close = Join-Path $work 'close.signal'
$helperResult = Join-Path $work 'helper.json'
$hostOut = Join-Path $work 'host.out.json'
$hostErr = Join-Path $work 'host.err.txt'
$hostExit = Join-Path $work 'host.exitcode.txt'
$hostExe = Join-Path $PSScriptRoot 'NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe'
$helperDll = Join-Path $PSScriptRoot 'UiaExternalHelper/bin/Debug/net9.0-windows/UiaExternalHelper.dll'
$hostProcess = $null
$helperProcess = $null
$success = $false
try {
    $hostCommand = '""{0}" --uia-host "{1}" "{2}" & echo %errorlevel% > "{3}""' -f $hostExe, $ready, $close, $hostExit
    $hostProcess = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/d', '/c', $hostCommand) -PassThru -RedirectStandardOutput $hostOut -RedirectStandardError $hostErr
    Wait-Path $ready 10
    $helperProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($helperDll, $ready, $close, $helperResult) -PassThru
    if (-not $helperProcess.WaitForExit(15000)) { Stop-Tree $helperProcess; throw 'UIA helper timed out.' }
    if ($helperProcess.ExitCode -ne 0) { throw "UIA helper failed with exit code $($helperProcess.ExitCode)." }
    if (-not $hostProcess.WaitForExit(20000)) { throw 'UIA host timed out.' }
    $hostExitCode = [int](Get-Content $hostExit -Raw)
    if ($hostExitCode -ne 0) { throw "UIA host failed with exit code ${hostExitCode}: $(Get-Content $hostErr -Raw)" }
    $proof = [ordered]@{ host = (Get-Content $hostOut -Raw | ConvertFrom-Json); helper = (Get-Content $helperResult -Raw | ConvertFrom-Json) }
    if (-not $proof.host.criticalClaimsPass -or -not $proof.helper.valid) { throw 'UIA proof JSON contains a failed claim.' }
    $proof | ConvertTo-Json -Depth 6 -Compress
    $success = $true
}
finally {
    Stop-Tree $helperProcess
    Stop-Tree $hostProcess
    if ($success) { Remove-Item -Recurse -Force $work } else { Write-Host "UIA evidence retained: $work" }
}
