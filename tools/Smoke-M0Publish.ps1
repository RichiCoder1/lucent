param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [ValidateRange(1, 10)] [int] $Iterations = 3
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class M0Window {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
  [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr hdc, int x, int y);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
}
'@

$source = (Resolve-Path $PublishDirectory).Path
$assets = @('SDL3.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll')
function Assert-NativePreflight([string] $Asset) {
    $copy = Join-Path ([IO.Path]::GetTempPath()) ("lucent-m0-missing-" + [Guid]::NewGuid())
    $process = $null
    try {
        Copy-Item $source $copy -Recurse
        Remove-Item (Join-Path $copy $Asset) -Force
        $stderr = Join-Path $copy 'stderr.txt'
        $process = Start-Process (Join-Path $copy 'Lucent.IssueBrowser.exe') -WorkingDirectory $copy -RedirectStandardError $stderr -PassThru
        if (-not $process.WaitForExit(10000)) { throw "Missing $Asset did not exit during native preflight." }
        $actual = Get-Content $stderr -Raw
        $expected = "Lucent M0 startup failed: Required native asset missing: $Asset."
        if ($process.ExitCode -eq 0 -or $actual.Trim() -ne $expected) { throw "Missing $Asset did not fail preflight clearly: exit=$($process.ExitCode); stderr=$actual" }
        $Asset
    }
    finally {
        Stop-LaunchedProcess $process
        Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Stop-LaunchedProcess([Diagnostics.Process] $Process) {
    if ($null -eq $Process) { return }
    try {
        $Process.Refresh()
        if (-not $Process.HasExited) {
            if ($Process.MainWindowHandle -ne 0) { [void][M0Window]::PostMessage($Process.MainWindowHandle, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) }
            if (-not $Process.WaitForExit(1000)) {
                $Process.Kill()
                $Process.WaitForExit()
            }
        }
    }
    finally { $Process.Dispose() }
}

function Get-WindowPixel([IntPtr] $Hwnd, [int] $X, [int] $Y) {
    $origin = [M0Window+POINT]::new()
    if (-not [M0Window]::ClientToScreen($Hwnd, [ref]$origin)) { return $null }
    $desktop = [M0Window]::GetDC([IntPtr]::Zero)
    if ($desktop -eq [IntPtr]::Zero) { return $null }
    try { return [M0Window]::GetPixel($desktop, $origin.X + $X, $origin.Y + $Y) -band 0xffffff }
    finally { [void][M0Window]::ReleaseDC([IntPtr]::Zero, $desktop) }
}

function Assert-ScenePixels([IntPtr] $Hwnd, [uint32] $Dpi, [M0Window+RECT] $Client, [int] $Iteration) {
    $scale = $Dpi / 96.0
    $headerX = [Math]::Round(20 * $scale); $headerY = [Math]::Round(45 * $scale)
    $pageX = [Math]::Round(20 * $scale); $pageY = [Math]::Round(160 * $scale)
    if ($headerX -ge $Client.Right -or $pageX -ge $Client.Right -or $headerY -ge $Client.Bottom -or $pageY -ge $Client.Bottom) { throw "Iteration $Iteration did not expose enough client backing pixels for the scale proof." }
    $deadline = [Environment]::TickCount64 + 5000
    do {
        $header = Get-WindowPixel $Hwnd $headerX $headerY
        $page = Get-WindowPixel $Hwnd $pageX $pageY
        if ($header -eq 0xF0E8E2 -and $page -eq 0xFCFAF8) { return 'header/page channels and one-scale geometry' }
        Start-Sleep -Milliseconds 100
    } until ([Environment]::TickCount64 -ge $deadline)
    throw ("Iteration {0} SDL/Skia capture failed: header=0x{1:X6} page=0x{2:X6} at scale={3}." -f $Iteration, $header, $page, $scale)
}

$copy = Join-Path ([IO.Path]::GetTempPath()) ("lucent-m0-publish-" + [Guid]::NewGuid())
try {
    $preflight = @($assets | ForEach-Object { Assert-NativePreflight $_ })
    Copy-Item $source $copy -Recurse
    $exe = Join-Path $copy 'Lucent.IssueBrowser.exe'
    if (-not (Test-Path $exe)) { throw 'Copied publish output lacks Lucent.IssueBrowser.exe.' }
    $observations = @()
    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        $process = Start-Process $exe -WorkingDirectory $copy -PassThru
        try {
            $deadline = [Environment]::TickCount64 + 10000
            do { Start-Sleep -Milliseconds 100; $process.Refresh() } until ($process.MainWindowHandle -ne 0 -or $process.HasExited -or [Environment]::TickCount64 -ge $deadline)
            if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw "Iteration $iteration did not present a window." }
            if (-not [M0Window]::SetWindowPos($process.MainWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x0013)) { throw "Iteration $iteration could not expose the presentation for external sampling." }
            Start-Sleep -Milliseconds 500
            $client = [M0Window+RECT]::new()
            if (-not [M0Window]::GetClientRect($process.MainWindowHandle, [ref]$client) -or $client.Right -le 0 -or $client.Bottom -le 0) { throw "Iteration $iteration did not expose a positive client backing size." }
            $dpi = [M0Window]::GetDpiForWindow($process.MainWindowHandle)
            if ($dpi -lt 96) { throw "Iteration $iteration did not expose a usable Per-Monitor V2 DPI." }
            $pixels = Assert-ScenePixels $process.MainWindowHandle $dpi $client $iteration
            $hwnd = ('0x{0:X}' -f $process.MainWindowHandle.ToInt64())
            if (-not [M0Window]::PostMessage($process.MainWindowHandle, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)) { throw "Iteration $iteration ordinary WM_CLOSE request failed." }
            if (-not $process.WaitForExit(10000)) { throw "Iteration $iteration exceeded teardown timeout." }
            if ($process.ExitCode -ne 0) { throw "Iteration $iteration exited $($process.ExitCode)." }
            $observations += [ordered]@{ iteration = $iteration; hwnd = $hwnd; client = @($client.Right, $client.Bottom); dpi = $dpi; presenter = 'persistent CPU Skia to SDL streaming texture'; pixels = $pixels; exitCode = $process.ExitCode }
        }
        finally { Stop-LaunchedProcess $process }
        Start-Sleep -Milliseconds 250
    }
    [ordered]@{ ok = $true; copiedPublish = $true; missingNativePreflight = $preflight; iterations = $observations } | ConvertTo-Json -Compress
}
finally { Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue }
