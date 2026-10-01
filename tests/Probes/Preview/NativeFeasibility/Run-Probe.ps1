#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $DescriptorPath,
    [Parameter(Mandatory)][string] $ArtifactDirectory,
    [string] $OutputDirectory,
    [string] $DotNetPath = 'C:\Program Files\dotnet\dotnet.exe'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../../../../', $PSScriptRoot)
Import-Module (Join-Path $repo 'tools/ReleaseSet.psm1') -Force
$DescriptorPath = (Resolve-Path -LiteralPath $DescriptorPath).Path
$ArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$descriptor = Get-Content -Raw -LiteralPath $DescriptorPath | ConvertFrom-Json -AsHashtable
Assert-LuiReleaseSet $descriptor $ArtifactDirectory
$version = $descriptor.releaseSet.version
$dotnet = (Get-Command $DotNetPath -ErrorAction Stop).Source
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $env:TEMP ('lucent-native-preview-' + [Guid]::NewGuid().ToString('N')) }
$proof = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $proof) { throw 'The proof directory must be new.' }
New-Item -ItemType Directory -Path $proof | Out-Null
Write-Output "Evidence: $proof"
$saved = @{}
foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'MSBUILDDISABLENODEREUSE')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
$env:DOTNET_CLI_HOME = Join-Path $proof 'cli-home'
# Reuse the caller's existing cache or normal user cache. No deletion or cache reset.
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget/packages' }
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$commands = [Collections.Generic.List[object]]::new()
$generations = [Collections.Generic.List[object]]::new()
$workerLaunches = 0
$lastGood = $null

function Invoke-OwnedProcess([string[]] $Arguments, [string] $Label, [int] $Seconds = 120, [string] $FrameReady = '') {
    $stdoutPath = Join-Path $proof "$Label.stdout.log"
    $stderrPath = Join-Path $proof "$Label.stderr.log"
    $start = [Diagnostics.ProcessStartInfo]::new($dotnet)
    $start.WorkingDirectory = $proof
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $readyMs = $null
    $readyTimestamp = $null
    $started = $false
    $outStream = $null
    $errStream = $null
    try {
        if (-not $process.Start()) { throw "Cannot start $Label." }
        $started = $true
        $pidValue = $process.Id
        $outStream = [IO.File]::Create($stdoutPath)
        $errStream = [IO.File]::Create($stderrPath)
        $outTask = $process.StandardOutput.BaseStream.CopyToAsync($outStream)
        $errTask = $process.StandardError.BaseStream.CopyToAsync($errStream)
        while (-not $process.WaitForExit(50)) {
            if ($FrameReady -and $null -eq $readyMs -and (Test-Path -LiteralPath $FrameReady)) { $readyMs = $timer.Elapsed.TotalMilliseconds; $readyTimestamp = [Diagnostics.Stopwatch]::GetTimestamp() }
            if ($timer.Elapsed.TotalSeconds -gt $Seconds) { throw "$Label exceeded its bounded process deadline." }
            if ($outStream.Length + $errStream.Length -gt 4MB) { throw "$Label exceeded its output budget." }
        }
        if ($FrameReady -and $null -eq $readyMs -and (Test-Path -LiteralPath $FrameReady)) { $readyMs = $timer.Elapsed.TotalMilliseconds; $readyTimestamp = [Diagnostics.Stopwatch]::GetTimestamp() }
        if (-not [Threading.Tasks.Task]::WaitAll(@($outTask, $errTask), 5000)) { throw "$Label output streams did not settle." }
        $timer.Stop()
        $record = [ordered]@{ label = $Label; executable = $dotnet; arguments = $Arguments; processId = $pidValue;
            exitCode = $process.ExitCode; elapsedMs = $timer.Elapsed.TotalMilliseconds; firstValidatedFrameFromLaunchMs = $readyMs;
            firstValidatedFrameTimestamp = $readyTimestamp;
            stdout = "$Label.stdout.log"; stderr = "$Label.stderr.log" }
        $commands.Add($record)
        $commands | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $proof 'commands.json')
        return $record
    }
    finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
            if (-not $process.WaitForExit(5000)) { throw "Owned $Label process termination was not confirmed." }
        }
        if ($null -ne $outStream) { $outStream.Dispose() }
        if ($null -ne $errStream) { $errStream.Dispose() }
        $process.Dispose()
    }
}

function Require-Success($Result) {
    if ($Result.exitCode -ne 0) { throw "$($Result.label) failed; retained logs: $proof" }
}

function Build-Generation([string] $Name, [string] $Scenario, [string] $Title, [switch] $ExpectFailure, [string] $ExpectedDiagnostic, [string] $AuthoredFile = 'PreviewCard.lui') {
    $directory = Join-Path $proof "generations/$Name"
    New-Item -ItemType Directory -Path $directory | Out-Null
    $build = Invoke-OwnedProcess @('build', $workerProject, '-c', 'Release', '--no-restore', '-warnaserror', '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:PublishAot=false', '-p:PublishTrimmed=false') "$Name-build"
    if ($ExpectFailure) {
        if ($build.exitCode -eq 0) { throw 'The deliberately invalid authored input built successfully.' }
        $diagnosticLines = @(Get-Content -LiteralPath (Join-Path $proof $build.stdout), (Join-Path $proof $build.stderr) | Where-Object { $_ -match ([regex]::Escape($AuthoredFile) + '.*\berror ' + $ExpectedDiagnostic + '\b') })
        if ($diagnosticLines.Count -eq 0) { throw "The rejected build did not attribute $ExpectedDiagnostic to authored $AuthoredFile; retained logs: $proof" }
        if (Test-Path -LiteralPath (Join-Path $directory 'worker-entered.txt')) { throw 'A failed generation launched a worker.' }
        $rejected = [ordered]@{ generation = $Name; buildExitCode = $build.exitCode; launched = $false; diagnostic = $ExpectedDiagnostic; authoredFile = $AuthoredFile; diagnosticLine = $diagnosticLines[0];
            previousFrame = $lastGood; previousFrameCurrent = $false; workerLaunchCount = $script:workerLaunches }
        $rejected | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'rejected.json')
        return $rejected
    }
    Require-Success $build
    $script:workerLaunches++
    $worker = Invoke-OwnedProcess @($workerDll, $Scenario, $directory, $Title) "$Name-worker" 30 (Join-Path $directory 'frame-ready.json')
    Require-Success $worker
    $result = Get-Content -Raw -LiteralPath (Join-Path $directory 'result.json') | ConvertFrom-Json -AsHashtable
    if ($result.scenario -cne $Scenario -or $result.title -cne $Title -or -not $result.disposed -or $result.processId -ne $worker.processId) { throw 'The worker result does not match its successful generation.' }
    $record = [ordered]@{ generation = $Name; scenario = $Scenario; buildElapsedMs = $build.elapsedMs;
        firstValidatedFrameFromLaunchMs = $worker.firstValidatedFrameFromLaunchMs;
        firstValidatedFrameTimestamp = $worker.firstValidatedFrameTimestamp; result = $result }
    $generations.Add($record)
    $script:lastGood = "generations/$Name/frame.png"
    return $record
}

try {
    '<Project />' | Set-Content -LiteralPath (Join-Path $proof 'Directory.Build.props'), (Join-Path $proof 'Directory.Build.targets'), (Join-Path $proof 'Directory.Packages.props')
    @{ sdk = $descriptor.sdk } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $proof 'global.json')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Scenario'), (Join-Path $PSScriptRoot 'Worker') -Destination $proof -Recurse
    foreach ($projectFile in @('Scenario/Scenario.csproj', 'Worker/Worker.csproj')) {
        $inputFile = Join-Path $proof ($projectFile + '.input')
        $file = Join-Path $proof $projectFile
        [IO.File]::WriteAllText($file, [IO.File]::ReadAllText($inputFile).Replace('__LUCENT_VERSION__', $version))
    }
    $escapedFeed = [Security.SecurityElement]::Escape($ArtifactDirectory)
    "<configuration><packageSources><clear /><add key=`"lucent`" value=`"$escapedFeed`" /><add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`" /></packageSources><packageSourceMapping><packageSource key=`"lucent`"><package pattern=`"Lucent.*`" /></packageSource><packageSource key=`"nuget.org`"><package pattern=`"*`" /></packageSource></packageSourceMapping></configuration>" | Set-Content -LiteralPath (Join-Path $proof 'NuGet.Config')
    $template = Join-Path $ArtifactDirectory "Lucent.Templates.$version.nupkg"
    Require-Success (Invoke-OwnedProcess @('new', 'install', $template) 'template-install')
    Require-Success (Invoke-OwnedProcess @('new', 'lucent-app', '-n', 'NativePreviewStarter', '-o', (Join-Path $proof 'Starter')) 'starter-create')
    $workerProject = Join-Path $proof 'Worker/Worker.csproj'
    $workerDll = Join-Path $proof 'Worker/bin/Release/net10.0-windows10.0.26100.0/win-x64/Worker.dll'
    Require-Success (Invoke-OwnedProcess @('restore', $workerProject, '--configfile', (Join-Path $proof 'NuGet.Config'), '-p:PublishAot=false', '-p:PublishTrimmed=false') 'restore' 180)
    $card = Join-Path $proof 'Scenario/PreviewCard.lui'
    $original = [IO.File]::ReadAllText($card)
    $initial = Build-Generation 'card-initial' 'card' 'Preview baseline'
    $editTimer = [Diagnostics.Stopwatch]::StartNew()
    $editStarted = [Diagnostics.Stopwatch]::GetTimestamp()
    [IO.File]::WriteAllText($card, $original.Replace('Preview baseline', 'Preview revised'))
    $edited = Build-Generation 'card-edited' 'card' 'Preview revised'
    $editTimer.Stop()
    if ($initial.result.titleRegion.Sha256 -ceq $edited.result.titleRegion.Sha256) { throw 'The changed compiled title did not change its attributable pixel region.' }
    if ($initial.result.processId -eq $edited.result.processId) { throw 'A generation reused the earlier worker process.' }
    $launchesBeforeFailures = $workerLaunches
    [IO.File]::WriteAllText($card, $original.Replace('<Text>Preview baseline</Text>', '<Text>Preview baseline</Bogus>'))
    $syntax = Build-Generation 'syntax-rejected' 'card' 'invalid' -ExpectFailure -ExpectedDiagnostic 'LUI1009'
    [IO.File]::WriteAllText($card, $original)
    $invalidCSharp = Join-Path $proof 'Scenario/InvalidPreview.cs'
    [IO.File]::WriteAllText($invalidCSharp, 'internal static class InvalidPreview { internal static int Value => MissingPreviewSymbol; }')
    $semantic = Build-Generation 'csharp-rejected' 'card' 'invalid' -ExpectFailure -ExpectedDiagnostic 'CS0103' -AuthoredFile 'InvalidPreview.cs'
    if ($workerLaunches -ne $launchesBeforeFailures) { throw 'A failed build entered the worker launch path.' }
    [IO.File]::WriteAllText($card, $original)
    Remove-Item -LiteralPath $invalidCSharp
    $starter = Build-Generation 'starter' 'starter' 'Welcome to Lucent'
    $sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        @{ path = [IO.Path]::GetRelativePath($repo, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    $report = [ordered]@{ kind = 'native-preview-feasibility'; version = $version; packageSourceCommit = $descriptor.releaseSet.sourceCommit;
        descriptorSha256 = (Get-FileHash -LiteralPath $DescriptorPath).Hash; status = 'passed'; workerLaunches = $workerLaunches;
        editBuildAndWorkerCompletionMs = $editTimer.Elapsed.TotalMilliseconds;
        editToValidatedFrameMs = [Diagnostics.Stopwatch]::GetElapsedTime($editStarted, $edited.firstValidatedFrameTimestamp).TotalMilliseconds;
        generations = @($generations); rejectedGenerations = @($syntax, $semantic); commands = @($commands); sources = $sources;
        projects = @(@{ name = 'PreviewCard'; luiFiles = 1; scope = 'compiled library with embedded SVG and packaged icon' },
            @{ name = 'NativePreviewStarter'; luiFiles = 1; scope = 'official generated app component; app Main not invoked' });
        machine = @{ os = [Runtime.InteropServices.RuntimeInformation]::OSDescription; architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString(); logicalProcessors = [Environment]::ProcessorCount };
        limits = @('Managed worker and offscreen rendering only; no native window, UI, IME or accessibility proof.',
            'Both named projects are small; this is not Issue Browser or large-project characterization.',
            'Allocation deltas are process-wide managed capture intervals, not isolated renderer or native allocations.',
            'Idle is three seconds without requested frames/input/polling; internal wake counts are not instrumented.',
            'Coordinator frame-ready observation uses 50 ms polling resolution.',
            'First frame includes JIT, mount, settling, image preload, capture, pixel validation and writing the PNG.',
            'Edit refresh rebuilds/restarts and resets state; no Hot Reload or continuous-frame claim.',
            'Local artifact validation binds bytes; authentication relies on the caller-supplied authenticated CI bundle.') }
    $report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $proof 'report.json')
    Write-Output "PASS: three fresh workers, changed authored title pixels, two rejected builds; report $proof/report.json"
}
finally {
    foreach ($name in $saved.Keys) {
        if ($null -eq $saved[$name]) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
}
