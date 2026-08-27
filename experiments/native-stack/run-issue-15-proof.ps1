param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-15")

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path "$PSScriptRoot/../..").Path
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$solution = "$PSScriptRoot/NativeStack.sln"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
$budgets = [ordered]@{ sampleCount = 500; warmupCount = 50; p95Milliseconds = 16.7; p99Milliseconds = 33.3; idleSeconds = 10; idleFrames = 0; rows = 10000; realizedMultiplier = 3; scrollCycles = 20; liveGrowthBytes = 16777216; returnAllowance = '10% + 8388608 bytes' }

Remove-Item -Recurse -Force $OutputDirectory -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
@"
# Issue #15 performance gate

Run ``../run-issue-15-proof.ps1``. It locked-restores and warning-free NativeAOT-publishes the real issue browser, then fail-closes on 500 serial semantic-input-to-present samples, 500 real SDL-resize-to-layout/paint/present samples, 20 end-proven scroll cycles, idle presenter calls, virtualization, and managed-memory budgets.

``proof.json`` retains both raw sample arrays, records the published process's ``!RuntimeFeature.IsDynamicCodeSupported`` NativeAOT result, and ``input-manifest.json`` hashes every measured local source input when the experiment tree is dirty.
"@ | Set-Content (Join-Path $OutputDirectory 'README.md') -NoNewline -Encoding UTF8
$sourceFiles = @(git -C $repository ls-files --cached --others --exclude-standard -- experiments/native-stack | Where-Object {
    $_ -like 'experiments/native-stack/NativeStackProbe/*' -or $_ -in @(
        'experiments/native-stack/NativeStack.sln', 'experiments/native-stack/Directory.Packages.props',
        'experiments/native-stack/run-issue-15-proof.ps1', 'experiments/native-stack/gauntlet/issues.seed.json')
})
$dirty = @(git -C $repository status --porcelain -- experiments/native-stack)
$manifest = [ordered]@{
    identityMode = if ($dirty.Count -eq 0) { 'clean-committed' } else { 'exact-input-manifest' }
    commit = (git -C $repository rev-parse HEAD).Trim()
    dirtyPaths = $dirty
    files = @($sourceFiles | Sort-Object | ForEach-Object {
        [ordered]@{ path = $_; sha256 = (Get-FileHash (Join-Path $repository $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'input-manifest.json') -NoNewline -Encoding UTF8

dotnet restore $solution --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build $solution --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$benchmark = & $exe --issue-15-benchmark | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $benchmark.Ok -or -not $benchmark.RuntimeNativeAot -or $benchmark.SampleCount -ne $budgets.sampleCount -or $benchmark.SemanticInputToPresentSamples.Count -ne $budgets.sampleCount -or $benchmark.ResizeSamples.Count -ne $budgets.sampleCount -or $benchmark.ScrollCycleEvidence.Count -ne $budgets.scrollCycles -or @($benchmark.ScrollCycleEvidence | Where-Object { -not $_.ReachedListEnd -or -not $_.ResetToStart }).Count -ne 0 -or $benchmark.SemanticInputToPresentP95Milliseconds -gt $budgets.p95Milliseconds -or $benchmark.SemanticInputToPresentP99Milliseconds -gt $budgets.p99Milliseconds -or $benchmark.ResizeP95Milliseconds -gt $budgets.p95Milliseconds -or $benchmark.IdleElapsedMilliseconds -lt ($budgets.idleSeconds * 1000) -or $benchmark.IdleFrames -ne 0 -or $benchmark.IdleFrameRequests -ne 0 -or $benchmark.IdleNativePresentCalls -ne 0 -or $benchmark.RealizedRows -gt $benchmark.RealizedRowLimit -or $benchmark.LiveGrowthBytes -gt $budgets.liveGrowthBytes -or $benchmark.PostCollectionBytes -gt $benchmark.ReturnLimitBytes) { throw 'Issue #15 benchmark budget failed.' }
$regressionWork = Join-Path ([IO.Path]::GetTempPath()) ('native-stack-issue15-' + [Guid]::NewGuid())
try {
    New-Item -ItemType Directory $regressionWork | Out-Null
    $issue14 = & $exe --issue-14-provider-contract (Join-Path $regressionWork 'issue-14') | ConvertFrom-Json
    $virtualization = & $exe --virtualization-self-check | ConvertFrom-Json
    $composition = & $exe --composition-self-check | ConvertFrom-Json
    $reactive = & $exe --reactive-self-check | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $issue14.Ok -or -not $virtualization.Ok -or -not $composition.Ok -or -not $reactive.Ok) { throw 'Issue #15 regression proof failed.' }
}
finally { Remove-Item -Recurse -Force $regressionWork -ErrorAction SilentlyContinue }

git -C $repository diff --check
if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
$staged = @(git -C $repository diff --cached --name-only)
if ($LASTEXITCODE -ne 0 -or $staged.Count -ne 0) { throw 'Issue #15 proof requires no staged files.' }
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1 Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed
$os = Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, OSArchitecture
$proof = [ordered]@{
    ok = $true
    issue = 15
    nativeAot = $benchmark.RuntimeNativeAot
    budgets = $budgets
    benchmark = $benchmark
    regressions = [ordered]@{ issue14ProviderContract = $issue14.Ok; virtualization = $virtualization.Ok; composition = $composition.Ok; reactiveStaleRetention = $reactive.StaleRetention; compositionUnrelatedInvalidation = $composition.UnrelatedWriteIdle }
    machine = [ordered]@{ computerName = $env:COMPUTERNAME; cpu = $cpu; os = $os; runtime = $benchmark.FrameworkDescription; processArchitecture = $benchmark.ProcessArchitecture; dotnet = (& dotnet --version).Trim() }
    identities = [ordered]@{
        source = $manifest
        executable = [ordered]@{ path = (Resolve-Path $exe).Path; sha256 = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant() }
        project = [ordered]@{ path = 'experiments/native-stack/NativeStackProbe/NativeStackProbe.csproj'; sha256 = (Get-FileHash $project -Algorithm SHA256).Hash.ToLowerInvariant() }
        locks = @('experiments/native-stack/NativeStackProbe/packages.lock.json', 'experiments/native-stack/Directory.Packages.props' | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash (Join-Path $repository $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
    }
    commands = @('dotnet restore experiments/native-stack/NativeStack.sln --locked-mode', 'dotnet build experiments/native-stack/NativeStack.sln --no-restore -warnaserror', 'dotnet publish experiments/native-stack/NativeStackProbe/NativeStackProbe.csproj -c Release -r win-x64 --self-contained true --no-restore -warnaserror', 'NativeStackProbe.exe --issue-15-benchmark', 'NativeStackProbe.exe --issue-14-provider-contract <temp>', 'NativeStackProbe.exe --virtualization-self-check', 'NativeStackProbe.exe --composition-self-check', 'NativeStackProbe.exe --reactive-self-check', 'git diff --check', 'git diff --cached --name-only')
    noStagedFiles = $true
}
$proof | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 12 -Compress
