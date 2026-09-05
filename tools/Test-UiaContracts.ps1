param(
    [Parameter(Mandatory = $true)][string] $HostExe,
    [string[]] $HostArguments = @('--uia-fixture'),
    [ValidateRange(1, 60)][int] $TimeoutSeconds = 15,
    [ValidateRange(2, 4)][int] $Runs = 2
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -ReferencedAssemblies @([System.Windows.Automation.AutomationElement].Assembly.Location, [System.Windows.Automation.AutomationProperty].Assembly.Location) -TypeDefinition @'
using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Automation;

public static class LucentUiaProof
{
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    public static bool PreparePointLookup(IntPtr window)
    {
        ShowWindow(window, 5);
        // Screen-point queries need an unobscured fixture, not keyboard activation.
        // Change only this test-owned window; preserve its position and size.
        return SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
    }
    private static int focus, property, structure;
    private static string structureSource = "", structureType = "", structureRuntimeId = "";
    private static readonly AutomationFocusChangedEventHandler FocusHandler = (_, __) => focus++;
    private static readonly AutomationPropertyChangedEventHandler PropertyHandler = (sender, __) => { var ignored = ((AutomationElement)sender).Current.Name; property++; };
    private static readonly StructureChangedEventHandler StructureHandler = (sender, args) => { structure++; structureSource = ((AutomationElement)sender).Current.Name; structureType = args.StructureChangeType.ToString(); structureRuntimeId = string.Join(",", args.GetRuntimeId()); };
    public static string ReadName(AutomationElement element, int timeoutMilliseconds)
    {
        var task = Task.Run(() => element.Current.Name);
        if (!task.Wait(timeoutMilliseconds)) throw new TimeoutException("Cross-thread UIA Name read exceeded its bound.");
        return task.GetAwaiter().GetResult();
    }
    public static void Start(AutomationElement root)
    {
        focus = property = structure = 0; structureSource = structureType = structureRuntimeId = "";
        Automation.AddAutomationFocusChangedEventHandler(FocusHandler);
        Automation.AddAutomationPropertyChangedEventHandler(root, TreeScope.Subtree, PropertyHandler,
            AutomationElement.NameProperty, ValuePattern.ValueProperty, SelectionItemPattern.IsSelectedProperty,
            ScrollPattern.VerticalScrollPercentProperty);
        Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, StructureHandler);
    }
    public static void Stop(AutomationElement root)
    {
        Automation.RemoveAutomationFocusChangedEventHandler(FocusHandler);
        Automation.RemoveAutomationPropertyChangedEventHandler(root, PropertyHandler);
        Automation.RemoveStructureChangedEventHandler(root, StructureHandler);
    }
    public static int Focus { get { return focus; } }
    public static int Property { get { return property; } }
    public static int Structure { get { return structure; } }
    public static string StructureSource { get { return structureSource; } }
    public static string StructureType { get { return structureType; } }
    public static string StructureRuntimeId { get { return structureRuntimeId; } }
}
'@

function Assert-True([bool] $condition, [string] $message) { if (-not $condition) { throw $message } }
function Wait-Until([scriptblock] $predicate, [string] $message) {
    $until = [DateTime]::UtcNow.AddSeconds(5)
    do { if (& $predicate) { return }; Start-Sleep -Milliseconds 25 } while ([DateTime]::UtcNow -lt $until)
    throw $message
}
function Get-Descendants([System.Windows.Automation.AutomationElement] $root) {
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $queue = [System.Collections.Generic.Queue[System.Windows.Automation.AutomationElement]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    $result = [System.Collections.Generic.List[System.Windows.Automation.AutomationElement]]::new()
    $queue.Enqueue($root); $steps = 0
    while ($queue.Count -ne 0) {
        $parent = $queue.Dequeue(); $child = $walker.GetFirstChild($parent)
        while ($null -ne $child) {
            if (++$steps -gt 64) { throw 'Bounded TreeWalker probe exceeded 64 steps.' }
            $id = $child.GetRuntimeId() -join ','
            if (-not $seen.Add($id)) { throw "TreeWalker cycle at runtime ID $id." }
            $result.Add($child); $queue.Enqueue($child); $child = $walker.GetNextSibling($child)
        }
    }
    return @($result)
}
function Assert-Patterns([System.Windows.Automation.AutomationElement] $element, [object[]] $expected, [string] $name) {
    $actual = @($element.GetSupportedPatterns())
    Assert-True ($actual.Count -eq $expected.Count) "$name exposed $($actual.Count) patterns rather than the declared $($expected.Count)."
    foreach ($pattern in $expected) { Assert-True ($actual -contains $pattern) "$name omitted a declared UIA pattern." }
}
function Find-Node([hashtable] $nodes, [string] $name, [System.Windows.Automation.ControlType] $type) {
    $node = $nodes[$name]
    Assert-True ($null -ne $node -and $node.Current.ControlType -eq $type) "Missing declared node $name/$($type.ProgrammaticName)."
    return $node
}
function Invoke-Once([int] $run) {
    $stderr = Join-Path ([IO.Path]::GetTempPath()) ("lucent-uia-" + [Guid]::NewGuid() + ".stderr")
    $process = Start-Process -FilePath $HostExe -ArgumentList $HostArguments -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    $guard = Start-Job -ArgumentList $PID, $process.Id, ($TimeoutSeconds + 15) -ScriptBlock {
        param($clientId, $hostId, $seconds)
        Start-Sleep -Seconds $seconds
        Stop-Process -Id $hostId -Force -ErrorAction SilentlyContinue
        Stop-Process -Id $clientId -Force -ErrorAction SilentlyContinue
    }
    $root = $null; $stderrTrace = $null
    try {
        $until = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        do { Start-Sleep -Milliseconds 50; $handle = $process.MainWindowHandle } while ($handle -eq 0 -and [DateTime]::UtcNow -lt $until)
        Assert-True ($handle -ne 0) 'Published UIA fixture did not expose an HWND.'
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        Assert-True ($null -ne $root -and $root.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) 'UIA did not attach to the published fixture root.'
        Assert-True ($root.Current.Name -eq 'Lucent UIA Fixture') 'UIA root did not expose the hosted application title.'
        Assert-Patterns $root @([System.Windows.Automation.WindowPattern]::Pattern, [System.Windows.Automation.TransformPattern]::Pattern) 'root host'
        1..3 | ForEach-Object { Assert-True (([System.Windows.Automation.AutomationElement]::FromHandle($handle)).Current.Name -eq 'Lucent UIA Fixture') 'Repeated WM_GETOBJECT attach changed the root provider or application identity.' }
        $nodes = @{}
        foreach ($node in Get-Descendants $root) { $nodes[$node.Current.Name] = $node }
        Assert-True ($nodes.Count -eq 13) "Fixture exposed $($nodes.Count) descendants rather than the expected 13."
        $group = Find-Node $nodes 'Controls' ([System.Windows.Automation.ControlType]::Group)
        $text = Find-Node $nodes 'Read only' ([System.Windows.Automation.ControlType]::Text)
        $edit = Find-Node $nodes 'Value' ([System.Windows.Automation.ControlType]::Edit)
        $button = Find-Node $nodes 'Invoke' ([System.Windows.Automation.ControlType]::Button)
        $list = Find-Node $nodes 'Choices' ([System.Windows.Automation.ControlType]::List)
        $keep = Find-Node $nodes 'Keep' ([System.Windows.Automation.ControlType]::ListItem)
        $retire = Find-Node $nodes 'Retire' ([System.Windows.Automation.ControlType]::ListItem)
        $disabledItem = Find-Node $nodes 'Disabled' ([System.Windows.Automation.ControlType]::ListItem)
        $retireIdentity = $retire.GetRuntimeId() -join ','
        $status = Find-Node $nodes 'Ready' ([System.Windows.Automation.ControlType]::StatusBar)
        $scroll = Find-Node $nodes 'Scroll' ([System.Windows.Automation.ControlType]::Pane)
        $partlyClipped = Find-Node $nodes 'Partly clipped' ([System.Windows.Automation.ControlType]::Text)
        $fullyClipped = Find-Node $nodes 'Fully clipped' ([System.Windows.Automation.ControlType]::Text)
        Assert-Patterns $group @() 'group'; Assert-Patterns $text @() 'text'; Assert-Patterns $edit @([System.Windows.Automation.ValuePattern]::Pattern) 'text field'
        Assert-Patterns $button @([System.Windows.Automation.InvokePattern]::Pattern) 'button'; Assert-Patterns $list @([System.Windows.Automation.SelectionPattern]::Pattern) 'list'
        Assert-Patterns $keep @([System.Windows.Automation.SelectionItemPattern]::Pattern) 'list item'; Assert-Patterns $status @() 'status'; Assert-Patterns $scroll @([System.Windows.Automation.ScrollPattern]::Pattern) 'scroll viewport'
        Assert-Patterns $disabledItem @([System.Windows.Automation.SelectionItemPattern]::Pattern) 'disabled list item'
        Wait-Until { [LucentUiaProof]::PreparePointLookup($handle) } 'Fixture could not be shown above other windows for screen-point queries.'
        $scrollBounds = $scroll.Current.BoundingRectangle
        $partlyBounds = $partlyClipped.Current.BoundingRectangle
        $fullyBounds = $fullyClipped.Current.BoundingRectangle
        Assert-True ($partlyBounds.Top -lt $scrollBounds.Top -and $partlyBounds.Bottom -gt $scrollBounds.Top) 'Partly clipped fixture node did not cross the viewport clip boundary.'
        Assert-True ($fullyBounds.Top -ge $scrollBounds.Bottom) 'Fully clipped fixture node remained inside the viewport clip boundary.'
        $partlyIdentity = $partlyClipped.GetRuntimeId() -join ','
        $fullyIdentity = $fullyClipped.GetRuntimeId() -join ','
        $visiblePoint = [System.Windows.Point]::new([Math]::Max($scrollBounds.Left, $partlyBounds.Left) + 1, $scrollBounds.Top + 1)
        $visibleHit = [System.Windows.Automation.AutomationElement]::FromPoint($visiblePoint)
        Assert-True ($null -ne $visibleHit -and ($visibleHit.GetRuntimeId() -join ',') -eq $partlyIdentity) "Point lookup did not return the partly clipped child inside its visible intersection: hit=$($visibleHit.Current.Name), hitProcess=$($visibleHit.Current.ProcessId), expectedProcess=$($process.Id), type=$($visibleHit.Current.ControlType.ProgrammaticName), point=$visiblePoint, viewport=$scrollBounds, child=$partlyBounds."
        $partlyOutsidePoint = [System.Windows.Point]::new([Math]::Max($scrollBounds.Left, $partlyBounds.Left) + 1, $partlyBounds.Top + (($scrollBounds.Top - $partlyBounds.Top) / 2))
        $partlyOutsideHit = [System.Windows.Automation.AutomationElement]::FromPoint($partlyOutsidePoint)
        Assert-True ($null -eq $partlyOutsideHit -or ($partlyOutsideHit.GetRuntimeId() -join ',') -ne $partlyIdentity) 'Point lookup returned the partly clipped child from outside its ancestor clip.'
        $fullyOutsidePoint = [System.Windows.Point]::new($fullyBounds.Left + 1, $fullyBounds.Top + 1)
        $fullyOutsideHit = [System.Windows.Automation.AutomationElement]::FromPoint($fullyOutsidePoint)
        Assert-True ($null -eq $fullyOutsideHit -or ($fullyOutsideHit.GetRuntimeId() -join ',') -ne $fullyIdentity) 'Point lookup returned a fully clipped child.'
    $identity = $edit.GetRuntimeId() -join ','
    $null = [LucentUiaProof]::ReadName($edit, 5000)
    Assert-True ($identity -eq ($edit.GetRuntimeId() -join ',')) 'Runtime ID was not stable across cross-thread reads.'
    $scrollPattern = $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    $badScroll = $false; try { $scrollPattern.SetScrollPercent(-1, 101) } catch [System.ArgumentException] { $badScroll = $true }
    Assert-True $badScroll 'Invalid scroll percent did not fail closed.'
    [LucentUiaProof]::Start($root)
        try {
            $edit.SetFocus(); Wait-Until { [LucentUiaProof]::Focus -gt 0 } 'Focus event did not arrive.'
            $value = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            $value.SetValue('proof'); Wait-Until { $value.Current.Value -eq 'proof' } 'Value action did not update the retained field.'
            $selectionItem = $keep.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $retireSelectionItem = $retire.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $disabledSelectionItem = $disabledItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            foreach ($operation in @('Select', 'AddToSelection', 'RemoveFromSelection')) {
                $disabled = $false; try { $disabledSelectionItem.$operation() } catch [System.Windows.Automation.ElementNotEnabledException] { $disabled = $true }
                Assert-True $disabled "Disabled $operation did not return UIA_E_ELEMENTNOTENABLED."
            }
            $selectionItem.Select(); Wait-Until { $selectionItem.Current.IsSelected } 'SelectionItem action did not select the retained item.'
            $selection = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
            Assert-True (($selection.Current.GetSelection() | Where-Object { $_.Current.Name -eq 'Keep' }).Count -eq 1) 'Selection action did not update the containing list.'
            $retireSelectionItem.Select(); Wait-Until { $retireSelectionItem.Current.IsSelected -and -not $selectionItem.Current.IsSelected } 'SelectionItem action did not clear its selected sibling.'
            Assert-True (($selection.Current.GetSelection() | Where-Object { $_.Current.Name -eq 'Retire' }).Count -eq 1) 'Selection provider did not expose exactly the switched item.'
            Assert-True ($scrollPattern.Current.VerticallyScrollable) 'Scroll viewport did not expose retained vertical geometry.'
            $beforeScroll = $scrollPattern.Current.VerticalScrollPercent
            $scrollPattern.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount, [System.Windows.Automation.ScrollAmount]::SmallIncrement)
            Wait-Until { $scrollPattern.Current.VerticalScrollPercent -gt $beforeScroll } 'Scroll action did not update the retained offset.'
            $scrollPattern.SetScrollPercent(-1, 100); Wait-Until { $scrollPattern.Current.VerticalScrollPercent -eq 100 } 'Scroll percent action did not reach the retained end.'
            $scrollPattern.SetScrollPercent(-1, -1); Assert-True ($scrollPattern.Current.VerticalScrollPercent -eq 100) 'UIA NoScroll changed the retained offset.'
            $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $threw = $false
            try { $invoke.Invoke() }
            catch {
                $failure = $_.Exception
                while ($null -ne $failure.InnerException) { $failure = $failure.InnerException }
                $threw = $failure -is [System.ComponentModel.Win32Exception] -and $failure.NativeErrorCode -eq 5 -and $failure.HResult -eq [int]0x80004005
            }
            Assert-True $threw 'Throwing semantic action escaped the provider without an HRESULT.'
            $invoke.Invoke()
            Wait-Until { $status.Current.Name -eq 'Invoked' } 'Invoke action did not update the retained status.'
            Wait-Until { [LucentUiaProof]::Structure -gt 0 -and [LucentUiaProof]::Property -ge 3 } 'Property or structure event did not arrive.'
            Assert-True ([LucentUiaProof]::StructureSource -eq 'Choices' -and [LucentUiaProof]::StructureType -eq 'ChildRemoved' -and [LucentUiaProof]::StructureRuntimeId -eq $retireIdentity) "Structure event did not identify the changed child and its list parent: source=$([LucentUiaProof]::StructureSource) type=$([LucentUiaProof]::StructureType) runtime=$([LucentUiaProof]::StructureRuntimeId) expected=$retireIdentity."
            foreach ($operation in @('Select', 'AddToSelection', 'RemoveFromSelection')) {
                $stale = $false; try { $retireSelectionItem.$operation() } catch [System.Windows.Automation.ElementNotAvailableException] { $stale = $true }
                Assert-True $stale "Pruned retained provider $operation was not rejected as stale."
            }
            Assert-True ([LucentUiaProof]::Focus -eq 5 -and [LucentUiaProof]::Property -eq 8 -and [LucentUiaProof]::Structure -eq 1) "Unexpected event matrix: focus=$([LucentUiaProof]::Focus) property=$([LucentUiaProof]::Property) structure=$([LucentUiaProof]::Structure)."
        }
        finally { [LucentUiaProof]::Stop($root) }
        [pscustomobject]@{ run = $run; runtimeId = $identity; descendants = $nodes.Count; focusEvents = [LucentUiaProof]::Focus; propertyEvents = [LucentUiaProof]::Property; structureEvents = [LucentUiaProof]::Structure }
    }
    catch {
        Write-Warning ("UIA proof failed before cleanup: " + $_.Exception.Message)
        throw
    }
    finally {
        Stop-Job $guard -ErrorAction SilentlyContinue; Remove-Job $guard -Force -ErrorAction SilentlyContinue
        if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; if (-not $process.WaitForExit(5000)) { $process.Kill(); $null = $process.WaitForExit(5000) } }
        Assert-True $process.HasExited 'UIA fixture host did not exit after the proof window closed.'
        Assert-True ($process.ExitCode -eq 0) "UIA fixture host exited with $($process.ExitCode)."
        if (Test-Path $stderr) { $stderrTrace = Get-Content $stderr -Raw; Remove-Item $stderr -Force }
        $stderrLines = @($stderrTrace -split '\r?\n' | Where-Object { $_.Length -ne 0 })
        Assert-True ($stderrLines.Count -eq 1 -and $stderrLines[0].StartsWith('Lucent UIA diagnostics: ', [StringComparison]::Ordinal)) "Unexpected host stderr: $($stderrTrace.TrimEnd())"
        $line = $stderrLines[0]
        Assert-True ($line -match 'cache=(\d+) maxCache=(\d+) stale=(\d+) root=(\d+) listenerFailure=(\d+) timeout=(\d+)') 'UIA diagnostics omitted cache/lifetime evidence.'
        Assert-True ([int]$Matches[1] -eq 12 -and [int]$Matches[2] -eq 13 -and [int]$Matches[3] -eq 1 -and [int]$Matches[4] -ge 4 -and [int]$Matches[5] -eq 0 -and [int]$Matches[6] -eq 0) "UIA diagnostics failed bounds/lifetime checks: $line"
    }
}

$priorDiagnostics = [Environment]::GetEnvironmentVariable('LUCENT_UIA_DIAGNOSTICS')
try {
    [Environment]::SetEnvironmentVariable('LUCENT_UIA_DIAGNOSTICS', '1')
    $results = @(1..$Runs | ForEach-Object { Invoke-Once $_ })
    $results | ConvertTo-Json -Compress
}
finally { [Environment]::SetEnvironmentVariable('LUCENT_UIA_DIAGNOSTICS', $priorDiagnostics) }
