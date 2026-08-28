param(
    [Parameter(Position = 0)]
    [Alias('Paths')]
    [AllowEmptyCollection()]
    [string[]] $Path,
    [switch] $ListOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Invoke-Git([string[]] $Arguments) {
    $stderrPath = [IO.Path]::GetTempFileName()
    try {
        $output = @(& git -C $root @Arguments 2> $stderrPath)
        $stderr = Get-Content $stderrPath -Raw
        if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $stderr" }
        @($output | ForEach-Object { $_.ToString() })
    }
    finally { Remove-Item $stderrPath -Force -ErrorAction SilentlyContinue }
}

function Get-WorkingTreeChanges {
    $paths = [System.Collections.Generic.List[string]]::new()
    $unsafe = [System.Collections.Generic.List[string]]::new()

    foreach ($line in Invoke-Git @('diff', '--name-status', '--find-renames', 'HEAD', '--')) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split "`t"
        if ($parts.Count -lt 2) { throw "Could not parse git change: $line" }
        $status = $parts[0]
        if ($status -match '^[DRCUXTB]') {
            if ($status -match '^[RC]' -and $parts.Count -ne 3) { throw "Could not parse git rename/copy: $line" }
            if ($status -notmatch '^[RC]' -and $parts.Count -ne 2) { throw "Could not parse git change: $line" }
            $unsafe.Add(($parts[1..($parts.Count - 1)] -join ' -> '))
            continue
        }
        if ($parts.Count -ne 2) { throw "Could not parse git change: $line" }
        $paths.Add($parts[1])
    }

    foreach ($path in Invoke-Git @('ls-files', '--others', '--exclude-standard', '--')) {
        if (-not [string]::IsNullOrWhiteSpace($path)) { $paths.Add($path) }
    }

    [pscustomobject]@{ Paths = @($paths | Sort-Object -Unique); Unsafe = @($unsafe | Sort-Object -Unique) }
}

function Normalize-Path([string] $InputPath) {
    if ([string]::IsNullOrWhiteSpace($InputPath)) { return $null }
    $relative = $InputPath.Replace('\', '/')
    while ($relative.StartsWith('./', [StringComparison]::Ordinal)) { $relative = $relative.Substring(2) }
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|/)\.\.(?:/|$)') { return $null }
    $relative
}

function New-DotnetCheck([string] $Name, [string] $Why, [string] $Project) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $Project) -PathType Leaf)) {
        throw "Selected gate is missing: $Project"
    }
    [pscustomobject]@{
        Name = $Name
        Why = $Why
        Display = "dotnet run --project $Project"
        Kind = 'dotnet'
        Arguments = @('run', '--project', (Join-Path $root $Project))
    }
}

function New-M1Check([string] $Why) {
    $script = Join-Path $PSScriptRoot 'Verify-M1.ps1'
    if (-not (Test-Path -LiteralPath $script -PathType Leaf)) { throw "Selected gate is missing: tools/Verify-M1.ps1" }
    [pscustomobject]@{
        Name = 'M1 milestone gate'
        Why = $Why
        Display = 'tools/Verify-M1.ps1'
        Kind = 'script'
        Arguments = @($script)
    }
}

function Select-Checks([string[]] $ChangedPaths, [string[]] $UnsafePaths) {
    if ($UnsafePaths.Count -gt 0) {
        return @(New-M1Check "Deleted or renamed paths require the mandatory gate: $($UnsafePaths -join ', ')")
    }
    if ($ChangedPaths.Count -eq 0) { return @() }

    $normalized = foreach ($inputPath in $ChangedPaths) {
        $path = Normalize-Path $inputPath
        if ($null -eq $path) {
            [pscustomobject]@{ Path = $inputPath; Gate = 'fallback'; Reason = 'path is outside the repository or ambiguous' }
            continue
        }
        $full = Join-Path $root $path
        if (-not (Test-Path -LiteralPath $full)) {
            [pscustomobject]@{ Path = $path; Gate = 'fallback'; Reason = 'path is deleted or unavailable' }
            continue
        }
        $lower = $path.ToLowerInvariant()
        $gate = if ($lower -match '^src/lucent\.core/.*\.cs$|^tests/lucent\.core\.tests/.*\.cs$') { 'core' }
            elseif ($lower -match '^src/lucent\.renderer\.skia/.*\.cs$|^tests/lucent\.renderer\.skia\.tests/.*\.cs$') { 'renderer' }
            elseif ($lower -match '^apps/lucent\.issuebrowser/.*\.cs$|^tests/lucent\.issuebrowser\.tests/.*\.cs$') { 'issue' }
            else { 'fallback' }
        $reason = switch ($gate) {
            'core' { 'Core production or contract-test source changed' }
            'renderer' { 'Renderer production or contract-test source changed' }
            'issue' { 'Issue Browser or contract-test source changed' }
            default { 'Unknown, shared, build, configuration, architecture, or platform path' }
        }
        [pscustomobject]@{ Path = $path; Gate = $gate; Reason = $reason }
    }

    $fallback = @($normalized | Where-Object Gate -eq 'fallback')
    if ($fallback.Count -gt 0) {
        return @(New-M1Check (($fallback | ForEach-Object { "$($_.Path): $($_.Reason)" }) -join '; '))
    }

    $checks = [System.Collections.Generic.List[object]]::new()
    if (@($normalized | Where-Object Gate -eq 'core').Count -gt 0) {
        $checks.Add((New-DotnetCheck 'Core contracts' 'Core-only changes have a focused runnable contract check' 'tests/Lucent.Core.Tests/Lucent.Core.Tests.csproj'))
    }
    if (@($normalized | Where-Object Gate -eq 'renderer').Count -gt 0) {
        $checks.Add((New-DotnetCheck 'Renderer contracts' 'Renderer-only changes have a focused headless/AOT seam check' 'tests/Lucent.Renderer.Skia.Tests/Lucent.Renderer.Skia.Tests.csproj'))
    }
    if (@($normalized | Where-Object Gate -eq 'issue').Count -gt 0) {
        $checks.Add((New-DotnetCheck 'Issue Browser contracts' 'Issue Browser changes have a focused composition contract check' 'tests/Lucent.IssueBrowser.Tests/Lucent.IssueBrowser.Tests.csproj'))
    }
    @($checks)
}

function Invoke-Check($Check) {
    Write-Output "Running: $($Check.Display)"
    if ($Check.Kind -eq 'dotnet') {
        $dotnet = Join-Path $root '.dotnet/dotnet.exe'
        if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
        $arguments = $Check.Arguments
        & $dotnet @arguments
    }
    else { & $Check.Arguments[0] }
    if ($LASTEXITCODE -ne 0) { throw "Selected gate failed ($($LASTEXITCODE)): $($Check.Display)" }
}

try {
    if ($PSBoundParameters.ContainsKey('Path')) {
        $requested = @($Path | ForEach-Object { $_ -split ',' } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $changes = [pscustomobject]@{ Paths = $requested; Unsafe = @() }
    }
    else { $changes = Get-WorkingTreeChanges }

    $checks = @(Select-Checks $changes.Paths $changes.Unsafe)
    if ($checks.Count -eq 0) {
        Write-Output 'No changed files; nothing to verify.'
        exit 0
    }

    foreach ($check in $checks) {
        Write-Output "Selected: $($check.Name)"
        Write-Output "Why: $($check.Why)"
        Write-Output "Command: $($check.Display)"
    }
    if ($ListOnly) { exit 0 }
    foreach ($check in $checks) { Invoke-Check $check }
    exit 0
}
catch {
    Write-Error "Affected verification failed: $($_.Exception.Message)"
    exit 1
}
