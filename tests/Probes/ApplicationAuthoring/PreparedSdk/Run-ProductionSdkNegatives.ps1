param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-[0-9A-Za-z.-]+$')] [string] $Version,
    [switch] $KeepPackageCache,
    [string] $ArtifactRoot,
    [string] $CoreVersion = $Version,
    [switch] $UseExistingPackageCache
)

$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $probeRoot '../../../..')).Path
$dotnet = Join-Path $repositoryRoot '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
foreach ($packageId in @('Lucent.Core', 'Lucent.Lui.Sdk')) {
    $packageVersion = if ($packageId -eq 'Lucent.Core') { $CoreVersion } else { $Version }
    $package = Join-Path $feedPath "$packageId.$packageVersion.nupkg"
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Missing candidate package: $package"
    }
}
$customArtifactRoot = -not [string]::IsNullOrWhiteSpace($ArtifactRoot)
$artifactRoot = if ($customArtifactRoot) { [IO.Path]::GetFullPath($ArtifactRoot) } else { Join-Path $repositoryRoot 'artifacts/a0-production-sdk-negatives' }
if (Test-Path -LiteralPath $artifactRoot) {
    if ($customArtifactRoot) { throw "Supply a fresh artifact directory: $artifactRoot" }
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactRoot)
    $resolvedRepository = [System.IO.Path]::GetFullPath($repositoryRoot)
    if (-not $resolvedArtifacts.StartsWith($resolvedRepository + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove artifact path outside the repository: $resolvedArtifacts"
    }
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
$fixture = Join-Path $artifactRoot 'fixture'
$packages = Join-Path $artifactRoot 'packages'
$foreignArtifacts = Join-Path $artifactRoot 'foreign'
New-Item -ItemType Directory -Path $fixture, $packages | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'global.json') -Destination $artifactRoot
[System.IO.File]::WriteAllText((Join-Path $artifactRoot 'Directory.Build.props'), '<Project />')
[System.IO.File]::WriteAllText((Join-Path $artifactRoot 'Directory.Build.targets'), '<Project />')

& $dotnet build (Join-Path $probeRoot 'Foreign/PreparedForeign.csproj') -c Release --nologo --artifacts-path $foreignArtifacts
if ($LASTEXITCODE -ne 0) {
    throw "Foreign generator build failed with exit $LASTEXITCODE."
}
$foreign = (Get-ChildItem -LiteralPath (Join-Path $foreignArtifacts 'bin') -Recurse -Filter 'PreparedForeign.dll' | Select-Object -First 1).FullName
$templateRoot = Join-Path $probeRoot 'ProductionSdk'
Copy-Item -LiteralPath (Join-Path $templateRoot 'Program.cs'), (Join-Path $templateRoot 'Observed.input') -Destination $fixture
$component = Join-Path $fixture 'Component.lui'
[System.IO.File]::WriteAllText($component, [System.IO.File]::ReadAllText((Join-Path $templateRoot 'Component.lui.input')), [System.Text.UTF8Encoding]::new($false))
$project = Join-Path $fixture 'Consumer.csproj'
$projectText = [System.IO.File]::ReadAllText((Join-Path $templateRoot 'Consumer.csproj.input'))
$projectText = $projectText.Replace('__VERSION__', $Version).Replace('__FOREIGN_ANALYZER__', [Security.SecurityElement]::Escape($foreign))
$projectText = $projectText.Replace(('Version="[{0}]"' -f $Version), ('Version="[{0}]"' -f $CoreVersion))
$hook = Join-Path $fixture 'CommonHook.targets'
[IO.File]::WriteAllText($hook, '<Project><PropertyGroup><PreparedLeaseSentinel>common-hook-retained</PreparedLeaseSentinel></PropertyGroup><ItemGroup><CompilerVisibleProperty Include="PreparedLeaseSentinel" /><CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="LeaseMetadata" /></ItemGroup></Project>')
$hookPath = [Security.SecurityElement]::Escape($hook)
$projectText = $projectText.Replace('<PreparedNondeterministic>false</PreparedNondeterministic>', "<PreparedNondeterministic>false</PreparedNondeterministic><CustomAfterMicrosoftCommonTargets>$hookPath</CustomAfterMicrosoftCommonTargets>")
$projectText = $projectText.Replace('<AdditionalFiles Include="Observed.input" />', '<AdditionalFiles Include="Observed.input" LeaseMetadata="kept" />')
$leaseArm = Join-Path $fixture 'lease-arm'
$leaseRelease = Join-Path $fixture 'lease-release'
$leaseWait = "powershell -NoProfile -NonInteractive -Command &quot;while (-not [System.IO.File]::Exists('$leaseRelease')) { Start-Sleep -Milliseconds 20 }&quot;"
$leaseGate = @"
<Target Name="PreparationLeaseGate" BeforeTargets="GenerateMSBuildEditorConfigFile" Condition="'`$(DesignTimeBuild)' == 'true' and Exists('$leaseArm')">
<WriteLinesToFile File="$fixture/`$(PreparedLeaseOwner).entered" Lines="entered" Overwrite="true" />
<Exec Command="$leaseWait" />
</Target>
"@
$projectText = $projectText.Replace('</Project>', $leaseGate + '</Project>')
[System.IO.File]::WriteAllText($project, $projectText, [System.Text.UTF8Encoding]::new($false))
$escapedFeed = [Security.SecurityElement]::Escape($feedPath)
$config = Join-Path $artifactRoot 'NuGet.Config'
[System.IO.File]::WriteAllText($config, @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="candidate"><package pattern="Lucent.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@)

$commands = [System.Collections.Generic.List[string]]::new()
function Build-Consumer([int] $ExpectedExit, [bool] $Nondeterministic = $false) {
    $arguments = @(
        'build', $project, '-c', 'Release', '--nologo',
        '--artifacts-path', (Join-Path $artifactRoot 'consumer'),
        "-p:RestoreConfigFile=$config",
        '-p:RestorePackagesWithLockFile=true',
        "-p:PreparedNondeterministic=$($Nondeterministic.ToString().ToLowerInvariant())"
    )
    & $dotnet @arguments 2>&1 | ForEach-Object { Write-Host $_ }
    $exit = $LASTEXITCODE
    $commands.Add("dotnet $($arguments -join ' ') => exit $exit")
    if ($exit -ne $ExpectedExit) {
        throw "Expected consumer exit $ExpectedExit, got $exit."
    }
}

function Start-LeasePreparation([string] $Owner, [string] $Globals, [string] $SelectedProject = $project, [string[]] $CommandArguments) {
    $ownerRoot = Join-Path $artifactRoot "lease/$Owner"
    New-Item -ItemType Directory -Path $ownerRoot -Force | Out-Null
    $start = [Diagnostics.ProcessStartInfo]::new($dotnet)
    $start.WorkingDirectory = $fixture
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['LUCENT_PREPARATION_CANCEL_STDIN'] = '1'
    if (-not $CommandArguments) { $CommandArguments = @('prepare', $SelectedProject, (Join-Path $ownerRoot 'manifest.json'), (Join-Path $ownerRoot 'emitter'), 'Release', 'net10.0', '', $Globals, (Join-Path $ownerRoot 'generated')) }
    foreach ($argument in (@($preparationHostPath) + $CommandArguments)) { $start.ArgumentList.Add($argument) }
    return [Diagnostics.Process]::Start($start)
}

function Finish-LeaseProcess([Diagnostics.Process] $Process, [string] $Owner, [int] $ExpectedExit) {
    $stdout = $Process.StandardOutput.ReadToEndAsync()
    $stderr = $Process.StandardError.ReadToEndAsync()
    $timedOut = -not $Process.WaitForExit(30000)
    if ($timedOut) {
        $Process.Kill($true)
        if (-not $Process.WaitForExit(5000)) { throw "Timed-out lease process $Owner did not terminate." }
    }
    $output = $stdout.WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult() + $stderr.WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $artifactRoot "lease/$Owner/process.log"), $output)
    if ($timedOut) { throw "Lease process $Owner did not finish; its owned process tree was stopped and output retained." }
    if ($Process.ExitCode -ne $ExpectedExit) { throw "Lease process $Owner expected $ExpectedExit, got $($Process.ExitCode): $output" }
}

function Wait-LeaseContention([Diagnostics.Process] $Process) {
    $line = $Process.StandardError.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(15)).GetAwaiter().GetResult()
    if ($line -ne 'Waiting for another Lucent preparation workspace to finish.') { throw "The second host did not acknowledge lease contention: $line" }
}

$priorPackages = $env:NUGET_PACKAGES
try {
    if (-not $UseExistingPackageCache) { $env:NUGET_PACKAGES = $packages }
    elseif (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $HOME '.nuget/packages' }
    Build-Consumer 0
    $consumerArtifacts = Join-Path $artifactRoot 'consumer'
    $firstEmitter = Get-ChildItem -LiteralPath (Join-Path $consumerArtifacts 'obj') -Recurse -Filter 'Lucent.PreparedEmitter.*.dll' | Select-Object -First 1
    if (-not $firstEmitter) {
        throw 'The production SDK did not create a prepared emitter.'
    }
    $earlyComponent = Get-ChildItem -LiteralPath (Join-Path $consumerArtifacts 'obj') -Recurse -Filter 'Lui.Component*.g.cs' | Select-Object -First 1
    $earlySource = [System.IO.File]::ReadAllText($earlyComponent.FullName)
    if (-not $earlySource.Contains('#nullable enable') -or -not $earlySource.Contains('Component.lui')) {
        throw 'The production SDK early component did not retain nullable context and authored source origin.'
    }

    $preparationHostPath = Join-Path $env:NUGET_PACKAGES "lucent.lui.sdk/$Version/tools/preparation/net10.0/Lucent.Lui.Sdk.PreparationHost.dll"
    $manifest = Get-ChildItem -LiteralPath (Join-Path $consumerArtifacts 'obj') -Recurse -Filter 'manifest.json' | Select-Object -First 1
    $firstManifest = [IO.File]::ReadAllText($manifest.FullName) | ConvertFrom-Json
    $generatedRoot = (& $dotnet msbuild $project -getProperty:CompilerGeneratedFilesOutputPath '-p:Configuration=Release' "-p:ArtifactsPath=$consumerArtifacts" -nologo | Out-String).Trim()
    $normalConfig = @($firstManifest.AnalyzerConfigFiles | Where-Object { $_.EndsWith('.GeneratedMSBuildEditorConfig.editorconfig') })
    $configText = [IO.File]::ReadAllText($normalConfig[0])
    if (-not $configText.Contains('build_property.PreparedLeaseSentinel = common-hook-retained') -or -not $configText.Contains('build_metadata.AdditionalFiles.LeaseMetadata = kept')) { throw 'Preparation lost local hooks, compiler-visible options or AdditionalFiles metadata.' }
    $globalsPath = Join-Path $manifest.Directory.FullName 'global-properties.json'
    $originalGlobals = [IO.File]::ReadAllText($globalsPath) | ConvertFrom-Json -AsHashtable
    $ownerGlobals = @{}
    foreach ($owner in @('A', 'B', 'C', 'Failure')) {
        $snapshot = @{} + $originalGlobals
        $snapshot['PreparedLeaseOwner'] = $owner
        $ownerGlobals[$owner] = Join-Path $fixture "$owner-globals.json"
        [IO.File]::WriteAllText($ownerGlobals[$owner], ($snapshot | ConvertTo-Json))
    }
    [IO.File]::WriteAllText($leaseArm, 'armed')
    $contenders = [Collections.Generic.List[Diagnostics.Process]]::new()
    try {
        $ownerA = Start-LeasePreparation 'A' $ownerGlobals['A']
        $contenders.Add($ownerA)
        $enteredA = Join-Path $fixture 'A.entered'
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path -LiteralPath $enteredA)) {
            if ($ownerA.HasExited -or $deadline.Elapsed.TotalSeconds -gt 20) { throw 'Owner A did not enter its leased workspace.' }
            Start-Sleep -Milliseconds 20
        }
        $ownerB = Start-LeasePreparation 'B' $ownerGlobals['B']
        $contenders.Add($ownerB)
        Wait-LeaseContention $ownerB
        if (Test-Path -LiteralPath (Join-Path $fixture 'B.entered')) { throw 'The queued host entered the workspace before lease release.' }
        $ownerB.StandardInput.WriteLine('cancel')
        Finish-LeaseProcess $ownerB 'B' 1
        if (Test-Path -LiteralPath (Join-Path $fixture 'B.entered')) { throw 'A cancelled waiter entered its workspace.' }
        $compareWaiter = Start-LeasePreparation -Owner 'Compare' -CommandArguments @('compare', $manifest.FullName, $generatedRoot, $firstManifest.EmitterAssemblyName, 'none')
        $contenders.Add($compareWaiter)
        Wait-LeaseContention $compareWaiter
        $compareWaiter.StandardInput.WriteLine('cancel')
        Finish-LeaseProcess $compareWaiter 'Compare' 1
        $ownerC = Start-LeasePreparation 'C' $ownerGlobals['C']
        $contenders.Add($ownerC)
        Wait-LeaseContention $ownerC
        if (Test-Path -LiteralPath (Join-Path $fixture 'C.entered')) { throw 'A successor entered the workspace while owner A retained the lease.' }
        $ownerA.StandardInput.WriteLine('cancel')
        [IO.File]::WriteAllText($leaseRelease, 'release')
        Finish-LeaseProcess $ownerA 'A' 1
        Finish-LeaseProcess $ownerC 'C' 0
        if (-not (Test-Path -LiteralPath (Join-Path $fixture 'C.entered'))) { throw 'The successor never entered its workspace after cancellation released the lease.' }
        $failed = Start-LeasePreparation 'Failure' $ownerGlobals['Failure'] (Join-Path $fixture 'Missing.csproj')
        $contenders.Add($failed)
        Finish-LeaseProcess $failed 'Failure' 1
    }
    finally {
        [IO.File]::WriteAllText($leaseRelease, 'release')
        foreach ($process in $contenders) {
            if (-not $process.HasExited) { $process.Kill($true); if (-not $process.WaitForExit(5000)) { throw 'Owned lease process did not terminate during cleanup.' } }
            $process.Dispose()
        }
        Remove-Item -LiteralPath $leaseArm -ErrorAction SilentlyContinue
    }
    $firstWarm = [IO.File]::ReadAllText((Join-Path $artifactRoot 'lease/C/manifest.json')) | ConvertFrom-Json
    $warmProcess = Start-LeasePreparation 'C' $ownerGlobals['C']
    try { Finish-LeaseProcess $warmProcess 'C' 0 } finally { $warmProcess.Dispose() }
    $secondWarm = [IO.File]::ReadAllText((Join-Path $artifactRoot 'lease/C/manifest.json')) | ConvertFrom-Json
    if ($secondWarm.ProjectInputSha256 -ne $firstWarm.ProjectInputSha256 -or $secondWarm.EmitterAssemblyName -ne $firstWarm.EmitterAssemblyName) { throw 'A warm leased preparation changed input or emitter identity.' }
    & $dotnet $preparationHostPath compare (Join-Path $artifactRoot 'lease/C/manifest.json') $generatedRoot $secondWarm.EmitterAssemblyName none
    if ($LASTEXITCODE -ne 0) { throw 'Leased snapshots did not survive final output comparison.' }
    $commands.Add('Preparation lease: deterministic owner/successor contention; cancelled waiter never entered; active cancellation and failure released lease; options/metadata, warm identity and final comparison retained.')

    $edited = [System.IO.File]::ReadAllText($component).Replace('<Text>first</Text>', '<Text>second</Text>')
    [System.IO.File]::WriteAllText($component, $edited, [System.Text.UTF8Encoding]::new($false))
    Build-Consumer 0
    $emitters = @(Get-ChildItem -LiteralPath (Join-Path $consumerArtifacts 'obj') -Recurse -Filter 'Lucent.PreparedEmitter.*.dll')
    if ($emitters.Count -lt 2) {
        throw 'The production SDK did not retain immutable emitters across a same-project edit.'
    }
    $currentEmitterPath = [System.IO.File]::ReadAllText((Get-ChildItem -LiteralPath (Join-Path $consumerArtifacts 'obj') -Recurse -Filter 'emitter-path.txt' | Select-Object -First 1).FullName).Trim()
    if ([System.IO.Path]::GetFileName($currentEmitterPath) -eq $firstEmitter.Name) {
        throw 'The production SDK selected the stale emitter after a same-project edit.'
    }
    $generated = @(Get-ChildItem -LiteralPath (Join-Path $consumerArtifacts 'obj') -Recurse -Filter '*.g.cs')
    if ($generated.FullName -match [Regex]::Escape([System.IO.Path]::GetFileNameWithoutExtension($firstEmitter.Name))) {
        throw 'The final production compilation loaded the stale emitter.'
    }
    if (-not ($generated.FullName -match [Regex]::Escape([System.IO.Path]::GetFileNameWithoutExtension($currentEmitterPath)))) {
        throw 'The final production compilation did not load the current emitter.'
    }

    $preparationHostPath = Join-Path $env:NUGET_PACKAGES "lucent.lui.sdk/$Version/tools/preparation/net10.0/Lucent.Lui.Sdk.PreparationHost.dll"
    $preparationHost = Get-Item -LiteralPath $preparationHostPath -ErrorAction SilentlyContinue
    if (-not $preparationHost) {
        throw 'The installed SDK package did not contain the preparation host.'
    }
    $disabledHost = $preparationHost.FullName + '.disabled'
    Move-Item -LiteralPath $preparationHost.FullName -Destination $disabledHost
    try {
        Build-Consumer 1
        $assemblies = @(Get-ChildItem -LiteralPath $consumerArtifacts -Recurse -Filter 'Consumer.dll' -ErrorAction SilentlyContinue)
        if ($assemblies.Count -ne 0) {
            throw "A warm missing-host failure left consumer assemblies: $($assemblies.FullName -join ', ')"
        }
    }
    finally {
        Move-Item -LiteralPath $disabledHost -Destination $preparationHost.FullName
    }

    Build-Consumer 1 $true
    $assemblies = @(Get-ChildItem -LiteralPath $consumerArtifacts -Recurse -Filter 'Consumer.dll' -ErrorAction SilentlyContinue)
    if ($assemblies.Count -ne 0) {
        throw "A nondeterministic generator mismatch left consumer assemblies: $($assemblies.FullName -join ', ')"
    }
}
finally {
    $env:NUGET_PACKAGES = $priorPackages
    if (-not $KeepPackageCache -and (Test-Path -LiteralPath $packages)) {
        $resolvedRoot = (Resolve-Path -LiteralPath $artifactRoot).Path
        $resolvedPackages = (Resolve-Path -LiteralPath $packages).Path
        if (-not $resolvedPackages.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedPackages) -ne 'packages') {
            throw "Unexpected SDK probe cache cleanup target: $resolvedPackages"
        }
        Remove-Item -LiteralPath $resolvedPackages -Recurse -Force
    }
}

$commands | Set-Content -LiteralPath (Join-Path $artifactRoot 'commands.log') -Encoding utf8
Write-Host 'PRODUCTION SDK NEGATIVES PASS: same-project edits selected only the current cached emitter; missing-host and nondeterministic-generator failures removed consumer assemblies.'
exit 0
