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
}
'@

$source = (Resolve-Path $PublishDirectory).Path
$assets = @('SDL3.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll')
function Assert-NativePreflight([string] $Asset) {
    $copy = Join-Path ([IO.Path]::GetTempPath()) ("lucent-m0-missing-" + [Guid]::NewGuid())
    try {
        Copy-Item $source $copy -Recurse
        Remove-Item (Join-Path $copy $Asset) -Force
        $stderr = Join-Path $copy 'stderr.txt'
        $process = Start-Process (Join-Path $copy 'Lucent.IssueBrowser.exe') -WorkingDirectory $copy -RedirectStandardError $stderr -PassThru -Wait
        $actual = Get-Content $stderr -Raw
        $expected = "Lucent M0 startup failed: Required native asset missing: $Asset."
        if ($process.ExitCode -eq 0 -or $actual.Trim() -ne $expected) { throw "Missing $Asset did not fail preflight clearly: exit=$($process.ExitCode); stderr=$actual" }
        $Asset
    }
    finally { Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue }
}

function Get-BootstrapPixels([IntPtr] $Hwnd, [int] $Width, [int] $Height) {
    $origin = [M0Window+POINT]::new()
    if (-not [M0Window]::ClientToScreen($Hwnd, [ref]$origin)) { throw 'ClientToScreen could not locate the presentation.' }
    $desktop = [M0Window]::GetDC([IntPtr]::Zero)
    try { return @(([M0Window]::GetPixel($desktop, $origin.X + 10, $origin.Y + 10) -band 0xffffff), ([M0Window]::GetPixel($desktop, $origin.X + 400, $origin.Y + 250) -band 0xffffff)) }
    finally { [void][M0Window]::ReleaseDC([IntPtr]::Zero, $desktop) }
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
            if (-not [M0Window]::GetClientRect($process.MainWindowHandle, [ref]$client) -or $client.Right -ne 800 -or $client.Bottom -ne 500) { throw "Iteration $iteration did not expose the declared 800x500 backing output." }
            $pixelDeadline = [Environment]::TickCount64 + 5000
            do {
                $background, $accent = Get-BootstrapPixels $process.MainWindowHandle $client.Right $client.Bottom
                if ($background -eq 0x2A170F -and $accent -eq 0xFAA560) { break }
                Start-Sleep -Milliseconds 100
            } until ([Environment]::TickCount64 -ge $pixelDeadline)
            if ($background -ne 0x2A170F -or $accent -ne 0xFAA560) { throw "Iteration $iteration bootstrap pixel sample failed: background=0x$background accent=0x$accent" }
            if (($client.Right / 640.0) -ne 1.25 -or ($client.Bottom / 400.0) -ne 1.25) { throw "Iteration $iteration did not externally realize 1.25x logical/backing scale." }
            if (-not [M0Window]::PostMessage($process.MainWindowHandle, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)) { throw "Iteration $iteration ordinary WM_CLOSE request failed." }
            if (-not $process.WaitForExit(10000)) { $process.Kill(); throw "Iteration $iteration exceeded teardown timeout." }
            if ($process.ExitCode -ne 0) { throw "Iteration $iteration exited $($process.ExitCode)." }
            $observations += [ordered]@{ iteration = $iteration; hwnd = ('0x{0:X}' -f $process.MainWindowHandle.ToInt64()); backing = @($client.Right, $client.Bottom); scale = 1.25; pixels = 'background/accent'; exitCode = $process.ExitCode }
        }
        finally {
            if (-not $process.HasExited) { [void][M0Window]::PostMessage($process.MainWindowHandle, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero); if (-not $process.WaitForExit(1000)) { $process.Kill() } }
        }
        Start-Sleep -Milliseconds 250
    }
    [ordered]@{ ok = $true; copiedPublish = $true; missingNativePreflight = $preflight; iterations = $observations } | ConvertTo-Json -Compress
}
finally { Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue }
