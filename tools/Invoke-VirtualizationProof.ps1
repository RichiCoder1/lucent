param(
    [Parameter(Mandatory = $true)][string] $AppExe,
    [Parameter(Mandatory = $true)][string] $HostExe,
    [ValidateRange(5, 60)][int] $TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class VirtualProofInput {
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
  [StructLayout(LayoutKind.Explicit)] public struct U { [FieldOffset(0)] public KEYBDINPUT Key; [FieldOffset(0)] public MOUSEINPUT Mouse; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint Type; public U Data; }
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint count, INPUT[] input, int size);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
}
'@

function Assert-True([bool] $condition, [string] $message) { if (-not $condition) { throw $message } }
function Wait-Until([scriptblock] $predicate, [string] $message) {
    $until = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do { if (& $predicate) { return }; Start-Sleep -Milliseconds 50 } while ([DateTime]::UtcNow -lt $until)
    throw $message
}
function Assert-Running([Diagnostics.Process] $process, [string] $name) {
    $process.Refresh()
    if ($process.HasExited) { throw "$name exited before the proof completed (exit=$($process.ExitCode))." }
}
function Find-Node([System.Windows.Automation.AutomationElement] $root, [string] $name, [System.Windows.Automation.ControlType] $type) {
    foreach ($node in @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))) {
        if ($node.Current.Name -eq $name -and $node.Current.ControlType -eq $type) { return $node }
    }
    throw "Missing $name/$($type.ProgrammaticName)."
}
function Find-Search([System.Windows.Automation.AutomationElement] $root) {
    foreach ($node in @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))) {
        if ($node.Current.Name -like 'Search*' -and $node.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) { return $node }
    }
    throw 'Missing Search UIA provider.'
}
function Rows([System.Windows.Automation.AutomationElement] $list) {
    @($list.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))))
}
function Send-OrdinaryTab([IntPtr] $hwnd) {
    [void][VirtualProofInput]::SetForegroundWindow($hwnd)
    $keys = @(
        [VirtualProofInput+INPUT]@{ Type = 1; Data = [VirtualProofInput+U]@{ Key = [VirtualProofInput+KEYBDINPUT]@{ Vk = 9 } } },
        [VirtualProofInput+INPUT]@{ Type = 1; Data = [VirtualProofInput+U]@{ Key = [VirtualProofInput+KEYBDINPUT]@{ Vk = 9; Flags = 2 } } }
    )
    $sent = [VirtualProofInput]::SendInput(2, $keys, [Runtime.InteropServices.Marshal]::SizeOf([type][VirtualProofInput+INPUT]))
    Assert-True ($sent -eq 2) "Ordinary Tab input was not sent (Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
}
function Stop-LaunchedProcess([Diagnostics.Process] $process) {
    if ($null -eq $process) { return [pscustomobject]@{ ExitCode = $null; Killed = $false } }
    $killed = $false
    try {
        $process.Refresh()
        if (-not $process.HasExited) {
            if ($process.MainWindowHandle -ne 0) { [void][VirtualProofInput]::PostMessage($process.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
            if (-not $process.WaitForExit(5000)) {
                $killed = $true
                $process.Kill()
                Assert-True ($process.WaitForExit(5000)) 'Launched proof process did not exit after bounded kill cleanup.'
            }
            $process.WaitForExit()
            $process.Refresh()
        }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Killed = $killed }
    }
    finally { $process.Dispose() }
}
function Read-Diagnostics([string] $path, [string] $name) {
    Assert-True (Test-Path $path -PathType Leaf) "$name did not produce diagnostics."
    $lines = @((Get-Content $path -Raw) -split '\r?\n' | Where-Object { $_.Length -ne 0 })
    Assert-True ($lines.Count -eq 1 -and $lines[0].StartsWith('Lucent UIA diagnostics: ', [StringComparison]::Ordinal)) "Unexpected $name stderr: $($lines -join ' | ')"
    $line = $lines[0]
    Assert-True ($line -match 'phase=(\w+).*cache=(\d+) maxCache=(\d+) stale=(\d+) root=(\d+) listenerFailure=(\d+) timeout=(\d+)') "$name diagnostics omitted provider/lifetime evidence: $line"
    [pscustomobject]@{ Phase = $Matches[1]; Cache = [int]$Matches[2]; MaxCache = [int]$Matches[3]; Stale = [int]$Matches[4]; Root = [int]$Matches[5]; ListenerFailure = [int]$Matches[6]; Timeout = [int]$Matches[7] }
}
function Assert-RowGeometry([object[]] $rows, [double] $scale, [string] $phase) {
    Assert-True ($rows.Count -gt 0) "$phase exposed no realized ListItem providers."
    foreach ($row in $rows) {
        Assert-True ($row.Current.Name -match '^#\d+ ') "$phase exposed a non-issue row provider: $($row.Current.Name)."
        Assert-True ([Math]::Abs($row.Current.BoundingRectangle.Height - (30 * $scale)) -le 2.5) "$phase row height was not 30px: $($row.Current.BoundingRectangle.Height)."
    }
}

function Invoke-AppProof {
    $stderr = Join-Path ([IO.Path]::GetTempPath()) ('lucent-virtual-app-' + [Guid]::NewGuid() + '.stderr')
    $process = $null; $cleanup = $null; $result = $null
    try {
        $process = Start-Process -FilePath $AppExe -RedirectStandardError $stderr -PassThru
        Wait-Until { Assert-Running $process 'NativeAOT Issue Browser'; $process.MainWindowHandle -ne 0 } 'NativeAOT Issue Browser did not expose an HWND.'
        $hwnd = $process.MainWindowHandle
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        Assert-True ($null -ne $root -and $root.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) 'UIA did not attach to the actual published app root.'
        $searchRef = [ref]$null; $scrollRef = [ref]$null; $listRef = [ref]$null
        Wait-Until {
            try {
                $searchRef.Value = Find-Search $root
                $scrollRef.Value = Find-Node $root 'Issues' ([System.Windows.Automation.ControlType]::Pane)
                $listRef.Value = Find-Node $root 'Issues' ([System.Windows.Automation.ControlType]::List)
                $true
            }
            catch { $false }
        } 'Actual app did not publish Search and Issues UIA providers.'
        $search = $searchRef.Value; $scroll = $scrollRef.Value; $list = $listRef.Value
        $scale = [VirtualProofInput]::GetDpiForWindow($hwnd) / 96.0
        Assert-True ($scale -gt 0) 'Actual app window did not report a usable DPI scale.'
        Assert-True ([Math]::Abs($scroll.Current.BoundingRectangle.Height - (60 * $scale)) -le 2.5) "Actual app viewport was not 60px: $($scroll.Current.BoundingRectangle.Height)."

        Wait-Until { Assert-Running $process 'NativeAOT Issue Browser'; (Rows $list).Count -gt 0 } 'Actual app list did not load realized rows.'
        $initialRows = @(Rows $list)
        Assert-True ($initialRows.Count -le 6) "Actual app initial realized rows exceeded the 3x visible bound: $($initialRows.Count)."
        Assert-RowGeometry $initialRows $scale 'Actual app initial list'

        $search.SetFocus()
        Wait-Until { [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq $search.Current.Name } 'UIA Search focus was rejected.'
        $focusedRow = $null
        for ($tab = 0; $tab -lt 8 -and $null -eq $focusedRow; $tab++) {
            Send-OrdinaryTab $hwnd
            Start-Sleep -Milliseconds 75
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $focused.Current.Name -match '^#\d+ ') { $focusedRow = $focused }
        }
        Assert-True ($null -ne $focusedRow) 'Ordinary Tab did not focus a real app row.'
        $selectedName = $focusedRow.Current.Name
        $selectedRow = @(Rows $list | Where-Object { $_.Current.Name -eq $selectedName } | Select-Object -First 1)
        Assert-True ($selectedRow.Count -eq 1) "Focused app row was not a current realized list provider: $selectedName."
        $selectedRow = $selectedRow[0]
        $selectedRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $selectedPattern = $selectedRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        Wait-Until { $selectedPattern.Current.IsSelected } 'UIA row selection did not converge.'

        $scrollPattern = $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        $scrollPattern.SetScrollPercent(-1, 100)
        Wait-Until { $scrollPattern.Current.VerticalScrollPercent -ge 99.9 } 'UIA end scroll did not converge in the actual app.'
        $endRows = @(Rows $list)
        Assert-True ($endRows.Count -le 6) "Actual app end realized rows exceeded the 3x visible bound: $($endRows.Count)."
        Assert-RowGeometry $endRows $scale 'Actual app end list'
        $endRow = $endRows[0]
        $endPattern = $endRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $listRuntimeId = $list.GetRuntimeId() -join ','
        $boundContainer = $endPattern.Current.SelectionContainer
        Assert-True ($null -ne $boundContainer -and (($boundContainer.GetRuntimeId() -join ',') -eq $listRuntimeId)) 'End row provider was not bound to the actual app list.'
        $boundRuntimeId = $endRow.GetRuntimeId() -join ','
        $endName = $endRow.Current.Name
        Start-Sleep -Milliseconds 50
        Assert-True ($boundRuntimeId -eq ($endRow.GetRuntimeId() -join ',') -and $endName -eq $endRow.Current.Name) 'End row provider identity was not stable.'
        $result = [ordered]@{ initialRows = $initialRows.Count; endRows = $endRows.Count; selected = $selectedName; endRow = $endName; boundRuntimeId = $boundRuntimeId }
    }
    finally {
        $cleanup = Stop-LaunchedProcess $process
    }
    Assert-True ($null -ne $cleanup -and -not $cleanup.Killed -and ($null -eq $cleanup.ExitCode -or $cleanup.ExitCode -eq 0)) "Actual app did not close normally (exit=$($cleanup.ExitCode), killed=$($cleanup.Killed))."
    try {
        $diagnostics = Read-Diagnostics $stderr 'NativeAOT Issue Browser'
        Assert-True ($diagnostics.Phase -eq 'shutdown' -and $diagnostics.Root -ge 1 -and $diagnostics.ListenerFailure -eq 0 -and $diagnostics.Timeout -eq 0) "Actual app UIA diagnostics failed: phase=$($diagnostics.Phase) root=$($diagnostics.Root) listenerFailure=$($diagnostics.ListenerFailure) timeout=$($diagnostics.Timeout)."
        [pscustomobject]$result
    }
    finally { Remove-Item $stderr -Force -ErrorAction SilentlyContinue }
}

function Invoke-FixtureProof {
    $stderr = Join-Path ([IO.Path]::GetTempPath()) ('lucent-virtual-fixture-' + [Guid]::NewGuid() + '.stderr')
    $process = $null; $cleanup = $null; $result = $null
    try {
        $process = Start-Process -FilePath $HostExe -ArgumentList '--virtualization-fixture' -RedirectStandardError $stderr -PassThru
        Wait-Until { Assert-Running $process 'virtualization fixture'; $process.MainWindowHandle -ne 0 } 'Virtualization fixture did not expose an HWND.'
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        Wait-Until {
            Assert-Running $process 'virtualization fixture'
            $nodes = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
            (@($nodes | Where-Object { $_.Current.Name -eq 'Issues' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Pane }).Count -gt 0) -and
                (@($nodes | Where-Object { $_.Current.Name -eq 'Issues' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List }).Count -gt 0)
        } 'Supplemental virtualization fixture did not publish its UIA providers.'
        $all = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
        $search = Find-Node $root 'Search' ([System.Windows.Automation.ControlType]::Edit)
        $scroll = $all | Where-Object { $_.Current.Name -eq 'Issues' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Pane } | Select-Object -First 1
        $list = $all | Where-Object { $_.Current.Name -eq 'Issues' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List } | Select-Object -First 1
        Assert-True ($null -ne $scroll -and $null -ne $list) 'Missing supplemental virtual scroll/list providers.'
        $reorder = Find-Node $root 'Reorder' ([System.Windows.Automation.ControlType]::Button)
        $remove = Find-Node $root 'Remove' ([System.Windows.Automation.ControlType]::Button)
        $initial = @(Rows $list)
        Assert-True ($initial.Count -le 8) "Supplemental fixture initial realized rows exceeded its visible bound: $($initial.Count)."
        $first = Find-Node $root 'Issue 10000' ([System.Windows.Automation.ControlType]::ListItem)
        $runtime = $first.GetRuntimeId() -join ','
        $search.SetFocus(); Wait-Until { [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq 'Search' } 'Fixture Search focus was rejected.'
        Send-OrdinaryTab $process.MainWindowHandle
        Wait-Until { [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -like 'Issue *' } 'Ordinary Tab did not focus a supplemental virtual row.'
        $first.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 100
        $reorder.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-Until { (Rows $list)[0].Current.Name -eq 'Issue 9999' } 'Keyed reorder did not converge.'
        Assert-True (((Find-Node $root 'Issue 10000' ([System.Windows.Automation.ControlType]::ListItem)).GetRuntimeId() -join ',') -eq $runtime) 'Keyed move changed the realized row runtime ID.'
        $remove.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-Until { $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Issue 10000'))) -eq $null } 'Removed virtual row remained realized.'
        $stale = $false
        Wait-Until { try { $first.Current.Name -ne 'Issue 10000' } catch [System.Windows.Automation.ElementNotAvailableException] { $true } } 'Removed virtual row provider was not stale.'
        $stale = $true
        $pattern = $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern); $pattern.SetScrollPercent(-1, 100)
        Wait-Until { $pattern.Current.VerticalScrollPercent -eq 100 } 'Supplemental fixture UIA end scroll did not converge.'
        $end = @(Rows $list)
        Assert-True ($end.Count -le 8) "Supplemental fixture end realized rows exceeded its visible bound: $($end.Count)."
        $result = [ordered]@{ initialRows = $initial.Count; endRows = $end.Count; runtimeId = $runtime; reorderedRuntimeId = $runtime; removed = 'Issue 10000'; staleProvider = $stale }
    }
    finally {
        $cleanup = Stop-LaunchedProcess $process
    }
    Assert-True ($null -ne $cleanup -and -not $cleanup.Killed -and ($null -eq $cleanup.ExitCode -or $cleanup.ExitCode -eq 0)) "Supplemental fixture did not close normally (exit=$($cleanup.ExitCode), killed=$($cleanup.Killed))."
    try {
        $diagnostics = Read-Diagnostics $stderr 'supplemental virtualization fixture'
        Assert-True ($diagnostics.Phase -eq 'shutdown' -and $diagnostics.Cache -le 18 -and $diagnostics.MaxCache -le 18 -and $diagnostics.Stale -ge 1) "Supplemental fixture provider bound/lifetime failed: phase=$($diagnostics.Phase) cache=$($diagnostics.Cache) maxCache=$($diagnostics.MaxCache) stale=$($diagnostics.Stale)."
        $result.cache = $diagnostics.Cache; $result.maxCache = $diagnostics.MaxCache; $result.staleProviders = $diagnostics.Stale
        [pscustomobject]$result
    }
    finally { Remove-Item $stderr -Force -ErrorAction SilentlyContinue }
}

Assert-True (Test-Path $AppExe -PathType Leaf) "Missing published NativeAOT app: $AppExe"
Assert-True (Test-Path $HostExe -PathType Leaf) "Missing published virtualization host: $HostExe"
$prior = [Environment]::GetEnvironmentVariable('LUCENT_UIA_DIAGNOSTICS')
try {
    [Environment]::SetEnvironmentVariable('LUCENT_UIA_DIAGNOSTICS', '1')
    $app = Invoke-AppProof
    $fixture = Invoke-FixtureProof
    [ordered]@{ app = $app; fixture = $fixture } | ConvertTo-Json -Compress
}
finally { [Environment]::SetEnvironmentVariable('LUCENT_UIA_DIAGNOSTICS', $prior) }
