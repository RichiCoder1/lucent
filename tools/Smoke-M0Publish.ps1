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
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct HIGHCONTRAST { public uint Size; public uint Flags; public IntPtr Scheme; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool SystemParametersInfo(uint action, uint parameter, ref HIGHCONTRAST value, uint flags);
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

function Get-EffectiveAppearance {
    $contrast = [M0Window+HIGHCONTRAST]::new()
    $contrast.Size = [Runtime.InteropServices.Marshal]::SizeOf([type][M0Window+HIGHCONTRAST])
    if (-not [M0Window]::SystemParametersInfo(0x0042, $contrast.Size, [ref]$contrast, 0)) { throw "Could not read Windows high-contrast state (Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error()))." }
    if (($contrast.Flags -band 1) -ne 0) { return [pscustomobject]@{ Name = 'high-contrast'; Header = 0x000000; Page = 0x000000; Focus = 0x00FFFF } }
    try { $light = [int](Get-ItemPropertyValue -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name AppsUseLightTheme -ErrorAction Stop) }
    catch { throw "Could not read Windows app color preference: $($_.Exception.Message)" }
    if ($light -eq 1) { return [pscustomobject]@{ Name = 'light'; Header = 0xF0E8E2; Page = 0xFCFAF8; Focus = 0x00FFFF } }
    if ($light -eq 0) { return [pscustomobject]@{ Name = 'dark'; Header = 0x3B291E; Page = 0x2A170F; Focus = 0x15CCFA } }
    throw "Windows AppsUseLightTheme was not finite: $light"
}

function Assert-ScenePixels([IntPtr] $Hwnd, [uint32] $Dpi, [M0Window+RECT] $Client, $Appearance, [int] $Iteration) {
    $scale = $Dpi / 96.0
    $headerX = [Math]::Round(790 * $scale); $headerY = [Math]::Round(12 * $scale)
    $pageX = [Math]::Round(20 * $scale); $pageY = [Math]::Round(160 * $scale)
    if ($headerX -ge $Client.Right -or $pageX -ge $Client.Right -or $headerY -ge $Client.Bottom -or $pageY -ge $Client.Bottom) { throw "Iteration $Iteration did not expose enough client backing pixels for the scale proof." }
    $deadline = [Environment]::TickCount64 + 5000
    do {
        $header = Get-WindowPixel $Hwnd $headerX $headerY
        $page = Get-WindowPixel $Hwnd $pageX $pageY
        if ($header -eq $Appearance.Header -and $page -eq $Appearance.Page) { return "$($Appearance.Name) header/page channels and one-scale geometry" }
        Start-Sleep -Milliseconds 100
    } until ([Environment]::TickCount64 -ge $deadline)
    throw ("Iteration {0} SDL/Skia {4} capture failed: header=0x{1:X6} page=0x{2:X6} at scale={3}." -f $Iteration, $header, $page, $scale, $Appearance.Name)
}

function Assert-KeyboardFocusPixels([IntPtr] $Hwnd, [uint32] $Dpi, [M0Window+RECT] $Client, $Appearance, [int] $Iteration) {
    if (-not [M0Window]::PostMessage($Hwnd, 0x0100, [UIntPtr]0x09, [IntPtr]::Zero) -or
        -not [M0Window]::PostMessage($Hwnd, 0x0101, [UIntPtr]0x09, [IntPtr]::Zero)) {
        throw "Iteration $Iteration could not post ordinary Tab input."
    }
    $deadline = [Environment]::TickCount64 + 5000
    $located = $false
    do {
        $origin = [M0Window+POINT]::new()
        if (-not [M0Window]::ClientToScreen($Hwnd, [ref]$origin)) { Start-Sleep -Milliseconds 100; continue }
        $located = $true
        $desktop = [M0Window]::GetDC([IntPtr]::Zero)
        if ($desktop -eq [IntPtr]::Zero) { throw "Iteration $Iteration could not observe keyboard focus pixels." }
        try {
            $scale = $Dpi / 96.0
            $x = [Math]::Round(20 * $scale); $y = [Math]::Round(35 * $scale)
            if ($x -lt $Client.Right -and $y -lt $Client.Bottom -and (([M0Window]::GetPixel($desktop, $origin.X + $x, $origin.Y + $y) -band 0xffffff) -eq $Appearance.Focus)) { return "ordinary Tab input produced $($Appearance.Name) visible focus" }
        }
        finally { [void][M0Window]::ReleaseDC([IntPtr]::Zero, $desktop) }
        Start-Sleep -Milliseconds 100
    } until ([Environment]::TickCount64 -ge $deadline)
    if (-not $located) { throw "Iteration $Iteration could not locate the client for keyboard focus observation." }
    throw "Iteration $Iteration did not paint the keyboard-visible focus color after ordinary Tab input."
}

function Assert-SettingsListener([IntPtr] $Hwnd, [uint32] $Dpi, [M0Window+RECT] $Client, $Appearance, [int] $Iteration) {
    if (-not [M0Window]::PostMessage($Hwnd, 0x031A, [UIntPtr]::Zero, [IntPtr]::Zero)) { throw "Iteration $Iteration could not post WM_THEMECHANGED to the host listener." }
    Start-Sleep -Milliseconds 250
    [void](Assert-ScenePixels $Hwnd $Dpi $Client $Appearance $Iteration)
    'WM_THEMECHANGED chained through host listener'
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
            $appearance = Get-EffectiveAppearance
            $pixels = Assert-ScenePixels $process.MainWindowHandle $dpi $client $appearance $iteration
            $listener = Assert-SettingsListener $process.MainWindowHandle $dpi $client $appearance $iteration
            $keyboard = Assert-KeyboardFocusPixels $process.MainWindowHandle $dpi $client $appearance $iteration
            $hwnd = ('0x{0:X}' -f $process.MainWindowHandle.ToInt64())
            if (-not [M0Window]::PostMessage($process.MainWindowHandle, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)) { throw "Iteration $iteration ordinary WM_CLOSE request failed." }
            if (-not $process.WaitForExit(10000)) { throw "Iteration $iteration exceeded teardown timeout." }
            if ($process.ExitCode -ne 0) { throw "Iteration $iteration exited $($process.ExitCode)." }
            $observations += [ordered]@{ iteration = $iteration; hwnd = $hwnd; client = @($client.Right, $client.Bottom); dpi = $dpi; appearance = $appearance.Name; presenter = 'persistent CPU Skia to SDL streaming texture'; pixels = $pixels; listener = $listener; keyboard = $keyboard; exitCode = $process.ExitCode }
        }
        finally { Stop-LaunchedProcess $process }
        Start-Sleep -Milliseconds 250
    }
    [ordered]@{ ok = $true; copiedPublish = $true; missingNativePreflight = $preflight; iterations = $observations } | ConvertTo-Json -Compress
}
finally { Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue }
