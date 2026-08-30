param([Parameter(Mandatory)] [string] $Executable)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ListenerProofWindow {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
  [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr hdc, int x, int y);
}
'@

function Stop-Proof([Diagnostics.Process] $Process) {
    if ($null -eq $Process) { return }
    try {
        $Process.Refresh()
        if (-not $Process.HasExited) {
            if ($Process.MainWindowHandle -ne 0) { [void][ListenerProofWindow]::PostMessage($Process.MainWindowHandle, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) }
            if (-not $Process.WaitForExit(3000)) { $Process.Kill(); $Process.WaitForExit() }
        }
    }
    finally { $Process.Dispose() }
}

function Get-Pixel([IntPtr] $Hwnd) {
    $dc = [ListenerProofWindow]::GetDC($Hwnd)
    if ($dc -eq [IntPtr]::Zero) { return $null }
    try { return [ListenerProofWindow]::GetPixel($dc, 10, 10) -band 0xffffff }
    finally { [void][ListenerProofWindow]::ReleaseDC($Hwnd, $dc) }
}

$process = $null
try {
    $path = (Resolve-Path $Executable).Path
    $process = Start-Process $path -ArgumentList '--listener-proof' -WorkingDirectory (Split-Path $path) -PassThru
    $deadline = [Environment]::TickCount64 + 10000
    do { Start-Sleep -Milliseconds 100; $process.Refresh() } until ($process.MainWindowHandle -ne 0 -or $process.HasExited -or [Environment]::TickCount64 -ge $deadline)
    if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw 'Listener proof did not present a test-owned HWND.' }
    if (-not [ListenerProofWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x0013)) { throw 'Listener proof window could not be exposed.' }
    $deadline = [Environment]::TickCount64 + 5000
    do { $before = Get-Pixel $process.MainWindowHandle; if ($before -eq 0x0000FF) { break }; Start-Sleep -Milliseconds 100 } until ([Environment]::TickCount64 -ge $deadline)
    if ($before -ne 0x0000FF) { throw ('Listener proof pre-refresh marker was 0x{0:X6}, expected 0x0000FF.' -f $before) }
    if (-not [ListenerProofWindow]::PostMessage($process.MainWindowHandle, 0x031A, [UIntPtr]::Zero, [IntPtr]::Zero)) { throw 'Could not post WM_THEMECHANGED to listener proof.' }
    $deadline = [Environment]::TickCount64 + 5000
    do { $after = Get-Pixel $process.MainWindowHandle; if ($after -eq 0x00FF00) { break }; Start-Sleep -Milliseconds 100 } until ([Environment]::TickCount64 -ge $deadline)
    if ($after -ne 0x00FF00) { throw ('Listener proof post-refresh marker was 0x{0:X6}, expected 0x00FF00.' -f $after) }
    if (-not [ListenerProofWindow]::PostMessage($process.MainWindowHandle, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)) { throw 'Listener proof ordinary WM_CLOSE failed.' }
    if (-not $process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw "Listener proof did not close cleanly: exit=$($process.ExitCode)." }
    [ordered]@{ ok = $true; before = ('0x{0:X6}' -f $before); after = ('0x{0:X6}' -f $after); trigger = 'WM_THEMECHANGED through SetWindowSubclass'; close = 'ordinary WM_CLOSE' } | ConvertTo-Json -Compress
}
finally { Stop-Proof $process }
