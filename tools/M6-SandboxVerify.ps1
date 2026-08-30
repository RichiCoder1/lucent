$ErrorActionPreference = 'Stop'

$inputRoot = 'C:\M6\Input'
$outputRoot = 'C:\M6\Output'
$evidencePath = Join-Path $inputRoot 'm6-evidence.json'
$zipPath = Join-Path $inputRoot 'lucent-win-x64.zip'
$resultPath = Join-Path $outputRoot 'clean-machine-gate.json'
$completionPath = Join-Path $outputRoot 'verification-complete.json'

$candidateEvidenceSha256 = ''
$packageSha256 = ''
$sourceCommit = ''
$inventoryPass = $false
$launchPass = $false
$noDevelopmentSdk = $false

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class M6SandboxUser32 {
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    public static IntPtr FindWindow(uint processId, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != processId) return true;
            var text = new StringBuilder(256);
            GetWindowText(window, text, text.Capacity);
            if (!string.Equals(text.ToString(), title, StringComparison.Ordinal)) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
'@

function Get-Hash([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-JsonInteger($Value) { $Value -is [int] -or $Value -is [long] }

function Assert-Properties($Value, [string[]] $Expected, [string] $Name) {
    if ($Value -isnot [System.Management.Automation.PSCustomObject]) { throw "$Name has the wrong type." }
    $actual = @($Value.PSObject.Properties.Name)
    if ($actual.Count -ne $Expected.Count) { throw "$Name has missing or undeclared fields." }
    foreach ($expectedName in $Expected) { if (@($actual | Where-Object { [string]::Equals($_, $expectedName, [StringComparison]::Ordinal) }).Count -ne 1) { throw "$Name has missing, undeclared, or wrongly-cased fields." } }
}

function Get-Inventory([string] $Directory) {
    $prefix = (Resolve-Path -LiteralPath $Directory).Path.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    @(
        Get-ChildItem -LiteralPath $Directory -Force -File -Recurse | Sort-Object FullName | ForEach-Object {
            [ordered]@{
                path = $_.FullName.Substring($prefix.Length).Replace('\', '/')
                bytes = [long]$_.Length
                sha256 = Get-Hash $_.FullName
            }
        }
    )
}

function Assert-Inventory($Expected, $Actual) {
    if ($Expected -isnot [array] -or $Actual -isnot [array] -or $Expected.Count -eq 0 -or $Expected.Count -ne $Actual.Count) { throw 'Candidate extracted inventory is missing or has the wrong length.' }
    for ($i = 0; $i -lt $Expected.Count; $i++) {
        Assert-Properties $Expected[$i] @('bytes', 'path', 'sha256') "candidate inventory[$i]"
        if (-not (Test-JsonInteger $Expected[$i].bytes) -or $Expected[$i].bytes -lt 0 -or $Expected[$i].path -isnot [string] -or [string]::IsNullOrWhiteSpace($Expected[$i].path) -or $Expected[$i].sha256 -notmatch '^[0-9a-f]{64}$') { throw "candidate inventory[$i] is malformed." }
        foreach ($part in $Expected[$i].path.Split('/')) { if ($part -eq '..' -or $part -eq '') { throw "candidate inventory[$i] contains an unsafe path." } }
        if ($Expected[$i].path -cne $Actual[$i].path -or $Expected[$i].bytes -ne $Actual[$i].bytes -or $Expected[$i].sha256 -cne $Actual[$i].sha256) { throw "Candidate extracted file evidence differs at index $i." }
    }
}

function Write-Result([string] $Environment) {
    New-Item -Path $outputRoot -ItemType Directory -Force | Out-Null
    $temporary = Join-Path $outputRoot ('.clean-machine-gate.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $result = [ordered]@{
        candidateEvidenceSha256 = $candidateEvidenceSha256
        environment = $Environment
        inventoryPass = [bool]$inventoryPass
        issue = 38
        launchPass = [bool]$launchPass
        noDevelopmentSdk = [bool]$noDevelopmentSdk
        packageSha256 = $packageSha256
        recordedAtUtc = [DateTime]::UtcNow.ToString('O')
        schema = 1
        sourceCommit = $sourceCommit
    }
    [IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temporary -Destination $resultPath -Force
    $completion = [ordered]@{ schema = 1; resultSha256 = Get-Hash $resultPath }
    $completionTemporary = Join-Path $outputRoot ('.verification-complete.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($completionTemporary, ($completion | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $completionTemporary -Destination $completionPath -Force
}

try {
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf) -or -not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw 'Mapped candidate evidence or package is missing.' }
    $candidateEvidenceSha256 = Get-Hash $evidencePath
    $packageSha256 = Get-Hash $zipPath
    $candidate = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    Assert-Properties $candidate @('acceptance', 'baseline', 'environment', 'gates', 'hashes', 'informational', 'issue', 'manualNotRun', 'mode', 'operations', 'packaging', 'pending', 'schema') 'candidate evidence'
    if (-not (Test-JsonInteger $candidate.schema) -or $candidate.schema -ne 2 -or -not (Test-JsonInteger $candidate.issue) -or $candidate.issue -ne 38 -or $candidate.mode -cne 'Candidate' -or $candidate.acceptance -cne 'clean-automated-candidate' -or $null -ne $candidate.gates) { throw 'Candidate evidence is not a clean automated candidate.' }
    Assert-Properties $candidate.environment @('build', 'cpu', 'display', 'gpu', 'logicalProcessors', 'machine', 'os', 'powerPlan', 'ramBytes', 'recordedAtUtc', 'runtime', 'scaleAuthority', 'source') 'candidate.environment'
    Assert-Properties $candidate.environment.source @('branch', 'commit', 'status', 'tree', 'untracked', 'workingTreeSha256') 'candidate.environment.source'
    if ($candidate.environment.source.status.Count -ne 0 -or $candidate.environment.source.commit -isnot [string] -or [string]::IsNullOrWhiteSpace($candidate.environment.source.commit)) { throw 'Candidate evidence does not prove a clean source commit.' }
    $sourceCommit = $candidate.environment.source.commit
    Assert-Properties $candidate.packaging @('copiedPublish', 'extractedChecksumsMatch', 'extractedPackageInventory', 'nativeAssetAndLicenseInventory', 'publishBytes', 'zip', 'zipBytes', 'zipSha256') 'candidate.packaging'
    if ($candidate.packaging.zip -cne 'lucent-win-x64.zip' -or (Split-Path $zipPath -Leaf) -cne $candidate.packaging.zip -or $candidate.packaging.zipSha256 -cne $packageSha256 -or $candidate.packaging.zipSha256 -notmatch '^[0-9a-f]{64}$') { throw 'Candidate package name or SHA-256 does not match the supplied zip.' }
    if ($candidate.packaging.copiedPublish -isnot [bool] -or -not $candidate.packaging.copiedPublish -or $candidate.packaging.extractedChecksumsMatch -isnot [bool] -or -not $candidate.packaging.extractedChecksumsMatch) { throw 'Candidate package evidence does not contain passing copy/checksum gates.' }
    if ($candidate.packaging.nativeAssetAndLicenseInventory -isnot [array] -or $candidate.packaging.extractedPackageInventory -isnot [array] -or (ConvertTo-Json $candidate.packaging.nativeAssetAndLicenseInventory -Compress) -cne (ConvertTo-Json $candidate.packaging.extractedPackageInventory -Compress)) { throw 'Candidate package inventories differ.' }

    $sdkRoots = @(
        (Join-Path ${env:ProgramFiles} 'dotnet\sdk'),
        (Join-Path ${env:ProgramFiles(x86)} 'dotnet\sdk'),
        (Join-Path $env:USERPROFILE '.dotnet\sdk')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) }
    $noDevelopmentSdk = $null -eq (Get-Command dotnet -ErrorAction SilentlyContinue) -and $sdkRoots.Count -eq 0
    if (-not $noDevelopmentSdk) { throw 'The clean machine has a dotnet command or development SDK.' }

    $extract = Join-Path $env:TEMP ('lucent-m6-sandbox-' + [Guid]::NewGuid().ToString('N'))
    New-Item -Path $extract -ItemType Directory -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extract -Force
        $actualInventory = Get-Inventory $extract
        Assert-Inventory $candidate.packaging.extractedPackageInventory $actualInventory
        $inventoryPass = $true
        $exe = Join-Path $extract 'Lucent.IssueBrowser.exe'
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Extracted package lacks the exact Lucent.IssueBrowser.exe.' }
        $stderr = Join-Path $extract 'stderr.txt'
        $process = Start-Process -FilePath $exe -WorkingDirectory $extract -RedirectStandardError $stderr -PassThru
        try {
            $deadline = [Environment]::TickCount64 + 15000
            do { Start-Sleep -Milliseconds 100; $process.Refresh(); $hwnd = [M6SandboxUser32]::FindWindow([uint32]$process.Id, 'Lucent Issue Browser') } until ($hwnd -ne [IntPtr]::Zero -or $process.HasExited -or [Environment]::TickCount64 -ge $deadline)
            if ($process.HasExited -or $hwnd -eq [IntPtr]::Zero) { throw "Extracted Lucent.IssueBrowser.exe did not expose its SDL HWND: $(((Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue) | Out-String).Trim())" }
            if (Get-ChildItem -LiteralPath $outputRoot -Force | Select-Object -First 1) { throw 'Tested application wrote into the verifier output mapping.' }
            $launchPass = $true
        }
        finally {
            if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
            if ($process) { $process.Dispose() }
        }
    }
    finally { Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Result 'Windows Sandbox; networking disabled; no repository or source mount'
    exit 0
}
catch {
    [Console]::Error.WriteLine("M6 clean-machine verification failed: $($_.Exception.Message)")
    try { [IO.File]::WriteAllText((Join-Path $outputRoot 'verification-error.txt'), $_.Exception.ToString(), (New-Object Text.UTF8Encoding($false))) } catch { }
    try { Write-Result 'Windows Sandbox; networking disabled; verification failed' } catch { [Console]::Error.WriteLine("Could not write clean-machine result: $($_.Exception.Message)") }
    exit 1
}
