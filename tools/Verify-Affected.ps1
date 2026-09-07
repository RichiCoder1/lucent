param(
    [Parameter(Position = 0)]
    [Alias('Paths')]
    [AllowEmptyCollection()]
    [string[]] $Path,
    [switch] $ListOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Read-GitPaths([string[]] $Arguments) {
    $result = @(& git -C $root @Arguments)
    if ($LASTEXITCODE) { throw 'Could not read changed paths.' }
    $result
}

try {
    $paths = if ($PSBoundParameters.ContainsKey('Path')) {
        @($Path | ForEach-Object { $_ -split ',' })
    }
    else {
        # --no-renames includes both sides of a move so the old owner stays covered.
        @(Read-GitPaths @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames', 'HEAD', '--')) +
            @(Read-GitPaths @('-c', 'core.quotepath=false', 'ls-files', '--others', '--exclude-standard', '--'))
    }
    $projects = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $allManaged = $false
    foreach ($inputPath in $paths) {
        if ([string]::IsNullOrWhiteSpace($inputPath)) { continue }
        $relative = $inputPath.Replace('\', '/') -replace '^(\./)+', ''
        if ([IO.Path]::IsPathRooted($inputPath) -or $inputPath -match '(^|[\\/])\.\.([\\/]|$)') {
            $allManaged = $true
            continue
        }
        if ($relative -match '^(docs/|[^/]+\.md$)') { continue }
        $selected = switch -Regex ($relative) {
            '^tests/(Lucent\.(Core|Reactive\.R3|Hosting|Renderer\.Skia|Platform\.Windows|IssueBrowser|Lui\.(Compiler|Generator|LanguageServer))\.Tests)/' { $Matches[1]; break }
            '^src/Lucent\.Reactive\.R3/' { 'Lucent.Reactive.R3.Tests'; break }
            '^src/Lucent\.Hosting/' { 'Lucent.Hosting.Tests'; break }
            '^src/Lucent\.Renderer\.Skia/' { 'Lucent.Renderer.Skia.Tests'; 'Lucent.IssueBrowser.Tests'; break }
            '^src/Lucent\.Platform\.Windows/' { 'Lucent.Platform.Windows.Tests'; 'Lucent.IssueBrowser.Tests'; break }
            '^apps/Lucent\.IssueBrowser/' { 'Lucent.IssueBrowser.Tests'; 'Lucent.Lui.LanguageServer.Tests'; break }
            '^src/Lucent\.Lui\.LanguageServer/' { 'Lucent.Lui.LanguageServer.Tests'; break }
            '^src/Lucent\.Lui\.Generator/' { 'Lucent.Lui.Generator.Tests'; 'Lucent.IssueBrowser.Tests'; break }
            '^src/Lucent\.Lui\.Compiler/' { 'Lucent.Lui.Compiler.Tests'; 'Lucent.Lui.Generator.Tests'; 'Lucent.Lui.LanguageServer.Tests'; 'Lucent.IssueBrowser.Tests'; break }
            default { $allManaged = $true }
        }
        foreach ($project in $selected) { [void] $projects.Add($project) }
    }
    if (-not $allManaged -and $projects.Count -eq 0) {
        Write-Output 'No source checks selected. Inspect changed documentation and links when applicable.'
        exit 0
    }
    $testScript = Join-Path $PSScriptRoot 'Test-Repository.ps1'
    $selectedProjects = @($projects | Sort-Object)
    if ($allManaged) {
        Write-Output 'Selected: tools/Test-Repository.ps1 -Suite Managed (shared or unrecognized paths changed).'
    }
    else {
        Write-Output "Selected managed projects: $($selectedProjects -join ', ')"
    }
    Write-Output 'Select Published, Sdk, Performance, or Accessibility separately when the changed behavior requires it; path selection covers managed tests only.'
    if ($ListOnly) { exit 0 }
    if ($allManaged) { & $testScript -Suite Managed }
    else { & $testScript -Suite Managed -Project $selectedProjects }
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    exit 0
}
catch {
    Write-Error "Affected verification failed: $($_.Exception.Message)"
    exit 1
}
