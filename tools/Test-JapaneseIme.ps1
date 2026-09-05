param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [Parameter(Mandatory)] [ValidateSet('1', '1.25', '1.5', '2')] [string] $DeclaredScale,
    [Parameter(Mandatory)] [string] $EvidencePath
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ImeSmokeWindow {
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
}
'@

$exe = Join-Path (Resolve-Path $PublishDirectory) 'Lucent.IssueBrowser.exe'
if (-not (Test-Path $exe -PathType Leaf)) { throw "Missing published app: $exe" }
$process = Start-Process $exe -WorkingDirectory (Split-Path $exe) -PassThru
try {
    $deadline = [Environment]::TickCount64 + 10000
    do { Start-Sleep -Milliseconds 100; $process.Refresh() } until ($process.MainWindowHandle -ne 0 -or $process.HasExited -or [Environment]::TickCount64 -ge $deadline)
    if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw 'Issue Browser did not present a window.' }
    $actualScale = [ImeSmokeWindow]::GetDpiForWindow($process.MainWindowHandle) / 96.0
    if ([Math]::Abs($actualScale - [double]$DeclaredScale) -gt 0.01) { throw "Declared scale $DeclaredScale does not match window scale $actualScale." }
    Write-Host 'Click Filter issues, switch to an already-installed Japanese IME, and record each result without changing system language settings.'
    $evidence = [ordered]@{ scale = $actualScale; preedit = (Read-Host 'Preedit remains visible and SDL input stays active (pass/fail)'); commit = (Read-Host 'Japanese commit inserts once (pass/fail)'); cancel = (Read-Host 'Escape/cancel removes only preedit (pass/fail)'); focusLoss = (Read-Host 'Switching window cancels preedit; queued text is rejected (pass/fail)'); candidate = (Read-Host 'Candidate window tracks the visible caret in Filter issues (pass/fail)'); screenshots = (Read-Host 'Screenshot paths, comma-separated') }
    $evidence | ConvertTo-Json | Set-Content $EvidencePath
    Write-Host "Wrote $EvidencePath"
}
finally {
    if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; if (-not $process.WaitForExit(1000)) { $process.Kill(); $process.WaitForExit() } }
    $process.Dispose()
}
