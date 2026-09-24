param(
    [ValidateSet('Managed', 'Native', 'Published', 'Sdk', 'Assets', 'Performance', 'Accessibility')]
    [string] $Suite = 'Managed',
    [string[]] $Project = @(),
    [string] $Filter,
    [ValidateSet('Fixed', 'IssueBrowser', 'LegacyIssueBrowser')]
    [string] $PerformanceWorkload = 'Fixed'
)

$ErrorActionPreference = 'Stop'
if ($Suite -ne 'Managed' -and ($Project.Count -ne 0 -or -not [string]::IsNullOrWhiteSpace($Filter))) {
    throw '-Project and -Filter apply only to the Managed suite.'
}
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$configuration = 'Release'
$managedProjects = @(
    'tests/Lucent.Core.Tests/Lucent.Core.Tests.csproj',
    'tests/Lucent.Testing.Tests/Lucent.Testing.Tests.csproj',
    'tests/Lucent.Reactive.R3.Tests/Lucent.Reactive.R3.Tests.csproj',
    'tests/Lucent.Hosting.Tests/Lucent.Hosting.Tests.csproj',
    'tests/Lucent.Renderer.Skia.Tests/Lucent.Renderer.Skia.Tests.csproj',
    'tests/Lucent.Platform.Windows.Tests/Lucent.Platform.Windows.Tests.csproj',
    'tests/Lucent.IssueBrowser.Tests/Lucent.IssueBrowser.Tests.csproj',
    'tests/Lucent.ComponentBrowser.Tests/Lucent.ComponentBrowser.Tests.csproj',
    'tests/Lucent.Performance.Tests/Lucent.Performance.Tests.csproj',
    'tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj',
    'tests/Lucent.Lui.Tooling.Tests/Lucent.Lui.Tooling.Tests.csproj',
    'tests/Lucent.Lui.Generator.Tests/Lucent.Lui.Generator.Tests.csproj',
    'tests/Lucent.Lui.LanguageServer.Tests/Lucent.Lui.LanguageServer.Tests.csproj'
)

function Invoke-Dotnet([string[]] $Arguments) {
    Write-Output "dotnet $($Arguments -join ' ')"
    & $dotnet @Arguments
    if ($LASTEXITCODE) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}

function Resolve-ManagedProjects {
    if ($Project.Count -eq 0) { return @($managedProjects) }
    $resolved = foreach ($requested in $Project) {
        $normalized = $requested.Replace('\', '/')
        $match = @($managedProjects | Where-Object {
            $_ -eq $normalized -or [IO.Path]::GetFileNameWithoutExtension($_) -eq $requested
        })
        if ($match.Count -ne 1) { throw "Unknown or ambiguous managed test project: $requested" }
        $match[0]
    }
    @($resolved | Sort-Object -Unique)
}

function Invoke-Managed {
    $selected = @(Resolve-ManagedProjects)
    $runCoreArchitecture = $Project.Count -eq 0 -or $selected -contains 'tests/Lucent.Core.Tests/Lucent.Core.Tests.csproj'
    if ($Project.Count -eq 0) {
        & (Join-Path $PSScriptRoot 'Test-PackageSet.ps1')
        Invoke-Dotnet @('restore', 'Lucent.slnx', '--locked-mode')
        Invoke-Dotnet @('build', 'Lucent.slnx', '--no-restore', '-c', $configuration)
        & (Join-Path $PSScriptRoot 'Verify-Formatting.ps1') -NoBuild
        if ($LASTEXITCODE) { throw "Formatting check failed ($LASTEXITCODE)." }
    }
    else {
        foreach ($testProject in $selected) {
            Invoke-Dotnet @('restore', $testProject, '--locked-mode')
            Invoke-Dotnet @('build', $testProject, '--no-restore', '-c', $configuration)
        }
    }
    if ($runCoreArchitecture -and $Project.Count -ne 0) {
        Invoke-Dotnet @('restore', 'tests/Lucent.Core.ArchitectureVerifier/Lucent.Core.ArchitectureVerifier.csproj', '--locked-mode')
        Invoke-Dotnet @('build', 'tests/Lucent.Core.ArchitectureVerifier/Lucent.Core.ArchitectureVerifier.csproj', '--no-restore', '-c', $configuration)
    }
    if ($runCoreArchitecture) {
        Write-Output 'Running Core architecture/public API preflight before managed tests.'
        & (Join-Path $PSScriptRoot 'Verify-CoreArchitecture.ps1') -Configuration $configuration
        if ($LASTEXITCODE) { throw 'Core architecture preflight failed.' }
    }
    foreach ($testProject in $selected) {
        $results = Reset-ArtifactDirectory ('artifacts/test/managed/' + [IO.Path]::GetFileNameWithoutExtension($testProject))
        $arguments = @('test', '--project', $testProject, '--no-build', '--no-restore', '-c', $configuration,
            '--minimum-expected-tests', '1', '--report-trx', '--report-trx-filename', 'results.trx', '--results-directory', $results)
        if (-not [string]::IsNullOrWhiteSpace($Filter)) { $arguments += @('--filter', $Filter) }
        Invoke-Dotnet $arguments
    }
    if ($runCoreArchitecture) {
        Write-Output 'Running Core architecture negative fixture proof after managed tests.'
        & (Join-Path $PSScriptRoot 'Verify-CoreArchitecture.ps1') -Configuration $configuration -Negative
        if ($LASTEXITCODE) { throw 'Core architecture proof failed.' }
    }
}
if ($Suite -ne 'Performance' -and $PSBoundParameters.ContainsKey('PerformanceWorkload')) {
    throw '-PerformanceWorkload applies only to the Performance suite.'
}

function Reset-ArtifactDirectory([string] $RelativePath) {
    $artifacts = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/test'))
    $path = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    if (-not $path.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset artifact directory outside artifacts/test: $path"
    }
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $path
}

function Publish-DesktopFixtures([switch] $IncludeHost) {
    $appPublish = Reset-ArtifactDirectory 'artifacts/test/issue-browser'
    Invoke-Dotnet @('restore', 'apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj', '--locked-mode') | Out-Host
    Invoke-Dotnet @('publish', 'apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj', '--no-restore', '-c', $configuration, '-r', 'win-x64', '-o', $appPublish) | Out-Host
    $hostExe = $null
    if ($IncludeHost) {
        $hostPublish = Reset-ArtifactDirectory 'artifacts/test/windows-test-host'
        Invoke-Dotnet @('restore', 'tests/Lucent.Platform.Windows.TestHost/Lucent.Platform.Windows.TestHost.csproj', '--locked-mode') | Out-Host
        Invoke-Dotnet @('publish', 'tests/Lucent.Platform.Windows.TestHost/Lucent.Platform.Windows.TestHost.csproj', '--no-restore', '-c', $configuration, '-r', 'win-x64', '-o', $hostPublish) | Out-Host
        $hostExe = Join-Path $hostPublish 'Lucent.Platform.Windows.TestHost.exe'
    }
    @{
        AppDirectory = $appPublish
        AppExe = Join-Path $appPublish 'Lucent.IssueBrowser.exe'
        HostExe = $hostExe
    }
}

function Invoke-Published {
    $published = Publish-DesktopFixtures -IncludeHost
    $componentPublish = Reset-ArtifactDirectory 'artifacts/test/component-browser'
    Invoke-Dotnet @('restore', 'apps/Lucent.ComponentBrowser/Lucent.ComponentBrowser.csproj', '--locked-mode')
    Invoke-Dotnet @('publish', 'apps/Lucent.ComponentBrowser/Lucent.ComponentBrowser.csproj', '--no-restore', '-c', $configuration, '-r', 'win-x64', '-o', $componentPublish)
    $published.ComponentBrowserExe = Join-Path $componentPublish 'Lucent.ComponentBrowser.exe'
    & (Join-Path $PSScriptRoot 'Verify-PublishInventory.ps1') -PublishDirectory $published.AppDirectory
    if ($LASTEXITCODE) { throw 'Publish inventory proof failed.' }
    & (Join-Path $PSScriptRoot 'Verify-PublishInventory.ps1') -PublishDirectory $published.AppDirectory -Negative
    if ($LASTEXITCODE) { throw 'Negative publish inventory proof failed.' }
    & (Join-Path $PSScriptRoot 'Test-PublishedIssueBrowser.ps1') -PublishDirectory $published.AppDirectory
    if ($LASTEXITCODE) { throw 'Published Issue Browser proof failed.' }
    & (Join-Path $PSScriptRoot 'Test-EditorSessions.ps1') -Executable $published.HostExe
    & (Join-Path $PSScriptRoot 'Test-WindowsLifecycle.ps1') -Executable $published.HostExe
    if ($LASTEXITCODE) { throw 'Windows application lifecycle proof failed.' }
    & (Join-Path $PSScriptRoot 'Test-WindowsSettingsListener.ps1') -Executable $published.HostExe
    if ($LASTEXITCODE) { throw 'Windows settings listener proof failed.' }
    Invoke-DesktopTests $published 'FullyQualifiedName~FlaUi|FullyQualifiedName~PublishedFilePickerTests'
}

function Invoke-Native {
    foreach ($testProject in @(
        'tests/Lucent.Core.Tests/Lucent.Core.Tests.csproj',
        'tests/Lucent.Reactive.R3.Tests/Lucent.Reactive.R3.Tests.csproj',
        'tests/Lucent.Renderer.Skia.Tests/Lucent.Renderer.Skia.Tests.csproj',
        'tests/Lucent.Platform.Windows.Tests/Lucent.Platform.Windows.Tests.csproj'
    )) {
        $publish = Reset-ArtifactDirectory ('artifacts/test/native-tests/' + [IO.Path]::GetFileNameWithoutExtension($testProject))
        Invoke-Dotnet @('restore', $testProject, '--locked-mode')
        Invoke-Dotnet @('publish', $testProject, '--no-restore', '-c', $configuration, '-r', 'win-x64', '-o', $publish)
        $executable = Join-Path $publish ([IO.Path]::GetFileNameWithoutExtension($testProject) + '.exe')
        & $executable --minimum-expected-tests 1
        if ($LASTEXITCODE) { throw "NativeAOT test executable failed: $executable" }
    }
}

function Invoke-DesktopTests([hashtable] $Published, [string] $TestFilter) {
    Invoke-Dotnet @('restore', 'tests/Lucent.Desktop.Tests/Lucent.Desktop.Tests.csproj', '--locked-mode')
    Invoke-Dotnet @('build', 'tests/Lucent.Desktop.Tests/Lucent.Desktop.Tests.csproj', '--no-restore', '-c', $configuration)
    $priorApp = $env:LUCENT_DESKTOP_APP
    $priorHost = $env:LUCENT_DESKTOP_HOST
    $priorComponentBrowser = $env:LUCENT_COMPONENT_BROWSER_APP
    $priorOutput = $env:LUCENT_ACCESSIBILITY_OUTPUT
    try {
        $env:LUCENT_DESKTOP_APP = $Published.AppExe
        $env:LUCENT_DESKTOP_HOST = $Published.HostExe
        $env:LUCENT_COMPONENT_BROWSER_APP = $Published.ComponentBrowserExe
        $env:LUCENT_ACCESSIBILITY_OUTPUT = Reset-ArtifactDirectory 'artifacts/test/accessibility'
        Invoke-Dotnet @('test', '--project', 'tests/Lucent.Desktop.Tests/Lucent.Desktop.Tests.csproj', '--no-build', '--no-restore', '-c', $configuration, '--minimum-expected-tests', '1', '--filter', $TestFilter)
    }
    finally {
        $env:LUCENT_DESKTOP_APP = $priorApp
        $env:LUCENT_DESKTOP_HOST = $priorHost
        $env:LUCENT_COMPONENT_BROWSER_APP = $priorComponentBrowser
        $env:LUCENT_ACCESSIBILITY_OUTPUT = $priorOutput
    }
}

function Invoke-Accessibility {
    $published = Publish-DesktopFixtures -IncludeHost
    & (Join-Path $PSScriptRoot 'Test-UiaContracts.ps1') -HostExe $published.HostExe
    if ($LASTEXITCODE) { throw 'UIA provider contract proof failed.' }
    & (Join-Path $PSScriptRoot 'Test-PublishedAccessibility.ps1') -AppExe $published.AppExe -HostExe $published.HostExe
    if ($LASTEXITCODE) { throw 'Published accessibility and virtualization proof failed.' }
    Invoke-DesktopTests $published 'FullyQualifiedName~AxeScan'
}
function Invoke-Performance {
    # Publish before measurement. Keep every invocation's evidence, including failures.
    $run = Join-Path $root ('artifacts/test/performance/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $run -Force
    Write-Output "Performance $PerformanceWorkload evidence: $run"
    $preparationLog = Join-Path $run 'preparation.log'
    if ($PerformanceWorkload -eq 'Fixed') {
        $appPublish = Reset-ArtifactDirectory 'artifacts/test/performance-fixture'
        $appProject = 'tests/Lucent.Performance.Fixture/Lucent.Performance.Fixture.csproj'
        $appName = 'Lucent.Performance.Fixture.exe'
    }
    else {
        $appPublish = Reset-ArtifactDirectory 'artifacts/test/issue-browser'
        $appProject = 'apps/Lucent.IssueBrowser/Lucent.IssueBrowser.csproj'
        $appName = 'Lucent.IssueBrowser.exe'
    }
    Invoke-Dotnet @('restore', $appProject, '--locked-mode') | Tee-Object -FilePath $preparationLog -Append
    Invoke-Dotnet @('publish', $appProject, '--no-restore', '-c', $configuration, '-r', 'win-x64', '-o', $appPublish) | Tee-Object -FilePath $preparationLog -Append
    & (Join-Path $PSScriptRoot 'Write-PerformanceIdentity.ps1') -PublishDirectory $appPublish -Executable $appName -DotnetPath $dotnet
    $verifierPublish = Reset-ArtifactDirectory 'artifacts/test/performance-verifier'
    Invoke-Dotnet @('restore', 'tests/Lucent.Performance.Verifier/Lucent.Performance.Verifier.csproj', '--locked-mode') | Tee-Object -FilePath $preparationLog -Append
    Invoke-Dotnet @('publish', 'tests/Lucent.Performance.Verifier/Lucent.Performance.Verifier.csproj', '--no-restore', '-c', $configuration, '-r', 'win-x64', '-o', $verifierPublish) | Tee-Object -FilePath $preparationLog -Append
    $verifier = Join-Path $verifierPublish 'Lucent.Performance.Verifier.exe'
    $appExe = Join-Path $appPublish $appName
    if ($PerformanceWorkload -eq 'Fixed') {
        & (Join-Path $PSScriptRoot 'Measure-LuiTooling.ps1') -Verify -ResultsPath (Join-Path $run 'tooling.json')
        if ($LASTEXITCODE) { throw "LUI tooling performance proof failed; evidence: $run" }
        $arguments = @('--fixed-app', $appExe)
    }
    elseif ($PerformanceWorkload -eq 'IssueBrowser') {
        try {
            & (Join-Path $PSScriptRoot 'Measure-IssueBrowser.ps1') -AppExe $appExe -OutputDirectory $run
        }
        catch { Write-Warning $_.Exception.Message }
        # Even an interrupted corpus is evaluated, with its partial observations retained.
        $arguments = @('--characterization', (Join-Path $run 'characterization.json'))
    }
    else { $arguments = @('--app', $appExe) }
    $reportPath = Join-Path $run 'report.json'
    & $verifier @arguments 1> $reportPath 2> (Join-Path $run 'verifier.log')
    $verifierExit = $LASTEXITCODE
    Write-Output "Performance $PerformanceWorkload evidence: $run"
    if ($PerformanceWorkload -ne 'IssueBrowser' -and (Test-Path -LiteralPath $reportPath)) {
        $rawLog = $null
        try { $rawLog = (Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json).rawLog }
        catch {
            Write-Warning "Verifier output was not valid JSON; original streams remain in $run."
            $verifierExit = 1
            $marker = Get-Content -LiteralPath (Join-Path $run 'verifier.log') | Where-Object { $_.StartsWith('Lucent performance diagnostics: ') } | Select-Object -Last 1
            if ($marker) { $rawLog = $marker.Substring('Lucent performance diagnostics: '.Length) }
        }
        if ($rawLog -and (Test-Path -LiteralPath $rawLog)) {
            Copy-Item -LiteralPath $rawLog -Destination (Join-Path $run 'frames.log')
            if (Test-Path -LiteralPath ($rawLog + '.phases.jsonl')) {
                Copy-Item -LiteralPath ($rawLog + '.phases.jsonl') -Destination (Join-Path $run 'phases.jsonl')
            }
        }
    }
    if ($verifierExit) { throw "Performance $PerformanceWorkload verifier failed ($verifierExit); evidence: $run" }
}

Push-Location $root
try {
    switch ($Suite) {
        'Managed' { Invoke-Managed }
        'Native' { Invoke-Native }
        'Published' { Invoke-Published }
        'Sdk' {
            & (Join-Path $PSScriptRoot 'Verify-LuiSdk.ps1')
            if ($LASTEXITCODE) { throw 'LUI SDK integration proof failed.' }
        }
        'Assets' {
            & (Join-Path $PSScriptRoot 'Verify-LuiAssets.ps1')
            if ($LASTEXITCODE) { throw 'Packaged asset integration proof failed.' }
        }
        'Performance' { Invoke-Performance }
        'Accessibility' { Invoke-Accessibility }
    }
}
finally {
    Pop-Location
}

Write-Output "Lucent $Suite suite: PASS"
