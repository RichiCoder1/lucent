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
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
}
'@

function Assert-True([bool] $condition, [string] $message) { if (-not $condition) { throw $message } }
function Wait-Until([scriptblock] $predicate, [string] $message) {
    $until = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try { if (& $predicate) { return } }
        catch {
            if ($_.Exception -isnot [System.Windows.Automation.ElementNotAvailableException] -and $_.Exception.InnerException -isnot [System.Windows.Automation.ElementNotAvailableException]) { throw }
        }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $until)
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
function Find-OptionalNode([System.Windows.Automation.AutomationElement] $root, [string] $name, [System.Windows.Automation.ControlType] $type) {
    try {
        foreach ($node in @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))) {
            if ($node.Current.Name -eq $name -and $node.Current.ControlType -eq $type) { return $node }
        }
    } catch [System.Windows.Automation.ElementNotAvailableException] { }
    return $null
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
function Find-IssueRow([System.Windows.Automation.AutomationElement] $list, [int] $number) {
    foreach ($row in @(Rows $list)) { if ($row.Current.Name -like "#$number *") { return $row } }
    return $null
}
function Select-Issue([System.Windows.Automation.AutomationElement] $root, [System.Windows.Automation.AutomationElement] $list, [int] $number) {
    $rowRef = [ref]$null
    Wait-Until { $rowRef.Value = Find-IssueRow $list $number; $null -ne $rowRef.Value } "Issue #$number was not realized for the mutation proof."
    $rowRef.Value.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-Until {
        try {
            $title = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -like "#$number *" })
            $title.Count -eq 1 -and $null -ne (Find-OptionalNode $root 'Open/Close' ([System.Windows.Automation.ControlType]::Button))
        } catch [System.Windows.Automation.ElementNotAvailableException] { $false }
    } "Issue #$number selection did not publish an inspector semantic snapshot."
}
function Invoke-NodeEventually([System.Windows.Automation.AutomationElement] $root, [string] $name) {
    Wait-Until {
        try {
            $button = Find-OptionalNode $root $name ([System.Windows.Automation.ControlType]::Button)
            if ($null -eq $button) { return $false }
            $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            $true
        } catch { $false }
    } "$name did not accept Invoke after its semantic snapshot was observed."
}
function Invoke-MutationProof([System.Windows.Automation.AutomationElement] $root, [System.Windows.Automation.AutomationElement] $list) {
    Select-Issue $root $list 10000
    Assert-True ($null -eq (Find-OptionalNode $root 'Retry' ([System.Windows.Automation.ControlType]::Button))) 'Retry was available before the selected issue had a transient outcome.'
    Invoke-NodeEventually $root 'Open/Close'
    Wait-Until {
        $status = Find-OptionalNode $root 'closed · Not synced: Fixture source is temporarily unavailable.' ([System.Windows.Automation.ControlType]::StatusBar)
        $null -ne $status -and $null -ne (Find-OptionalNode $root 'Retry' ([System.Windows.Automation.ControlType]::Button))
    } 'Transient fixture mutation did not publish Not synced and Retry.'
    Invoke-NodeEventually $root 'Retry'
    Wait-Until {
        $status = Find-OptionalNode $root 'closed' ([System.Windows.Automation.ControlType]::StatusBar)
        $null -ne $status -and $null -eq (Find-OptionalNode $root 'Retry' ([System.Windows.Automation.ControlType]::Button))
    } 'Successful manual retry did not publish its semantic snapshot or retire Retry.'

    Select-Issue $root $list 9999
    Invoke-NodeEventually $root 'Open/Close'
    Wait-Until {
        $status = Find-OptionalNode $root 'closed · Rejected: Fixture policy rejected this change.' ([System.Windows.Automation.ControlType]::StatusBar)
        $null -ne $status -and $null -eq (Find-OptionalNode $root 'Retry' ([System.Windows.Automation.ControlType]::Button))
    } 'Rejected fixture mutation did not roll back, publish its reason, or retire Retry.'

    Select-Issue $root $list 9998
    Invoke-NodeEventually $root 'Open/Close'
    Wait-Until {
        $status = Find-OptionalNode $root 'closed' ([System.Windows.Automation.ControlType]::StatusBar)
        $null -ne $status -and $null -eq (Find-OptionalNode $root 'Retry' ([System.Windows.Automation.ControlType]::Button))
    } 'Saved fixture mutation did not publish its semantic snapshot or retire Retry.'
    'transient-retry-rejection-saved'
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
        $mutation = Invoke-MutationProof $root $list

        $search.SetFocus()
        Wait-Until { $search.Current.HasKeyboardFocus } 'UIA Search focus was rejected.'
        $focusedRow = $initialRows[0]
        $focusedRow.SetFocus()
        Wait-Until { $focusedRow.Current.HasKeyboardFocus } 'UIA did not focus a real app row.'
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
        $endRow = @($endRows | Where-Object { $_.Current.BoundingRectangle.Top -le $scroll.Current.BoundingRectangle.Top -and $_.Current.BoundingRectangle.Bottom -gt $scroll.Current.BoundingRectangle.Top } | Select-Object -First 1)
        Assert-True ($endRow.Count -eq 1) 'Actual app end list did not retain one top visible row.'
        $endRow = $endRow[0]
        $endPattern = $endRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $listRuntimeId = $list.GetRuntimeId() -join ','
        $boundContainer = $endPattern.Current.SelectionContainer
        Assert-True ($null -ne $boundContainer -and (($boundContainer.GetRuntimeId() -join ',') -eq $listRuntimeId)) 'End row provider was not bound to the actual app list.'
        $boundRuntimeId = $endRow.GetRuntimeId() -join ','
        $endName = $endRow.Current.Name
        Start-Sleep -Milliseconds 50
        Assert-True ($boundRuntimeId -eq ($endRow.GetRuntimeId() -join ',') -and $endName -eq $endRow.Current.Name) 'End row provider identity was not stable.'
        $scrollPattern.SetScrollPercent(-1, 50)
        Wait-Until { $scrollPattern.Current.VerticalScrollPercent -ge 49.9 -and $scrollPattern.Current.VerticalScrollPercent -le 50.1 } 'Actual app mid-list scroll did not converge for density proof.'
        $midRows = @(Rows $list)
        $endRow = @($midRows | Where-Object { $_.Current.BoundingRectangle.Top -le $scroll.Current.BoundingRectangle.Top -and $_.Current.BoundingRectangle.Bottom -gt $scroll.Current.BoundingRectangle.Top } | Select-Object -First 1)[0]
        Assert-True ($null -ne $endRow) 'Actual app mid-list did not retain one top visible row.'
        $boundRuntimeId = $endRow.GetRuntimeId() -join ','
        $endName = $endRow.Current.Name
        $endPattern = $endRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $endPattern.Select()
        Wait-Until { $endPattern.Current.IsSelected } 'Mid-list row did not retain UIA selection before density restyle.'
        $relative = $endRow.Current.BoundingRectangle.Top - $scroll.Current.BoundingRectangle.Top
        $density = Find-Node $root 'Density: Comfortable/Compact' ([System.Windows.Automation.ControlType]::Button)
        $densityRuntimeId = $density.GetRuntimeId() -join ','
        $endRow.SetFocus()
        Wait-Until { $endRow.Current.HasKeyboardFocus } 'End row did not accept UIA focus before density restyle.'
        $density.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-Until {
            $current = @((Rows $list) | Where-Object { $_.Current.Name -eq $endName })
            $current.Count -eq 1 -and [Math]::Abs($current[0].Current.BoundingRectangle.Height - (22 * $scale)) -le 2.5
        } 'Compact density did not retain the top issue key with a compact row height.'
        $compact = @((Rows $list) | Where-Object { $_.Current.Name -eq $endName })[0]
        $compactRuntimeId = $compact.GetRuntimeId() -join ','
        $compactFocus = $density.Current.HasKeyboardFocus
        $compactRows = (Rows $list).Count
        $compactRelative = $compact.Current.BoundingRectangle.Top - $scroll.Current.BoundingRectangle.Top
        $density = Find-Node $root 'Density: Comfortable/Compact' ([System.Windows.Automation.ControlType]::Button)
        Assert-True ($compactRuntimeId -eq $boundRuntimeId -and $endPattern.Current.IsSelected -and [Math]::Abs($compactRelative - $relative) -le 1.5 -and $compactRows -le 9 -and
            ($density.GetRuntimeId() -join ',') -eq $densityRuntimeId -and $compactFocus) "Compact density changed row identity/selection/anchor, realization bound, or density UIA focus: rowRuntime=$compactRuntimeId expected=$boundRuntimeId selected=$($endPattern.Current.IsSelected) relative=$compactRelative expectedRelative=$relative rows=$compactRows densityRuntime=$($density.GetRuntimeId() -join ',') expectedDensityRuntime=$densityRuntimeId focus=$compactFocus."
        $density.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-Until {
            $current = @((Rows $list) | Where-Object { $_.Current.Name -eq $endName })
            $current.Count -eq 1 -and [Math]::Abs($current[0].Current.BoundingRectangle.Height - (30 * $scale)) -le 2.5
        } 'Comfortable density did not restore the top issue key and row height.'
        $restored = @((Rows $list) | Where-Object { $_.Current.Name -eq $endName })[0]
        $restoredRelative = $restored.Current.BoundingRectangle.Top - $scroll.Current.BoundingRectangle.Top
        $density = Find-Node $root 'Density: Comfortable/Compact' ([System.Windows.Automation.ControlType]::Button)
        Assert-True (($restored.GetRuntimeId() -join ',') -eq $boundRuntimeId -and $endPattern.Current.IsSelected -and [Math]::Abs($restoredRelative - $relative) -le 1.5 -and (Rows $list).Count -le 6 -and
            ($density.GetRuntimeId() -join ',') -eq $densityRuntimeId -and $density.Current.HasKeyboardFocus) 'Comfortable restoration changed row identity/selection/anchor, realization bound, or density UIA focus.'
        $result = [ordered]@{ initialRows = $initialRows.Count; endRows = $endRows.Count; selected = $selectedName; endRow = $endName; boundRuntimeId = $boundRuntimeId; density = 'comfortable-compact-comfortable'; mutation = $mutation }
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
        $search.SetFocus(); Wait-Until { $search.Current.HasKeyboardFocus } 'Fixture Search focus was rejected.'
        $first.SetFocus(); Wait-Until { $first.Current.HasKeyboardFocus } 'UIA did not focus a supplemental virtual row.'
        $first.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 100
        $reorder.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-Until { (Rows $list)[0].Current.Name -eq 'Issue 9999' } 'Keyed reorder did not converge.'
        $reordered = Find-Node $root 'Issue 10000' ([System.Windows.Automation.ControlType]::ListItem)
        Assert-True ((($reordered.GetRuntimeId() -join ',') -eq $runtime) -and $reordered.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) 'Keyed move changed the realized row runtime ID or selection.'
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
