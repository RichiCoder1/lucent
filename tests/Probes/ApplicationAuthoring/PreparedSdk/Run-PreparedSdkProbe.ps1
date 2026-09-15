param([string] $JsonGeneratorPath)

$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $probeRoot '../../../..')).Path
$dotnet = Join-Path $repositoryRoot '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnet = (Get-Command dotnet -CommandType Application).Source
}
if (-not $JsonGeneratorPath) {
    $JsonGeneratorPath = Join-Path $repositoryRoot '.dotnet/packs/Microsoft.NETCore.App.Ref/10.0.12/analyzers/dotnet/cs/System.Text.Json.SourceGeneration.dll'
}
if (-not (Test-Path -LiteralPath $JsonGeneratorPath -PathType Leaf)) {
    throw "Missing real JSON generator: $JsonGeneratorPath"
}

$artifactRoot = Join-Path $repositoryRoot 'artifacts/a0-prepared-sdk'
if (Test-Path -LiteralPath $artifactRoot) {
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactRoot)
    $resolvedRepository = [System.IO.Path]::GetFullPath($repositoryRoot)
    if (-not $resolvedArtifacts.StartsWith($resolvedRepository + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove artifact path outside the repository: $resolvedArtifacts"
    }
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot | Out-Null
$results = [System.Collections.Generic.List[string]]::new()

function Invoke-DotNet([string[]] $Arguments, [int] $ExpectedExit = 0) {
    & $dotnet @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
    $exit = $LASTEXITCODE
    $results.Add("dotnet $($Arguments -join ' ') => exit $exit")
    if ($exit -ne $ExpectedExit) {
        throw "Expected exit $ExpectedExit, got ${exit}: dotnet $($Arguments -join ' ')"
    }
}

function Build-Project([string] $Project, [string] $Name) {
    $arguments = @(
        'build', $Project, '-c', 'Release', '--nologo',
        '--artifacts-path', "$artifactRoot/$Name"
    )
    Invoke-DotNet $arguments
}

Build-Project (Join-Path $probeRoot 'Host/PreparedSdk.Host.csproj') 'host'
Build-Project (Join-Path $probeRoot 'Foreign/PreparedForeign.csproj') 'foreign'
Build-Project (Join-Path $probeRoot 'Tasks/PreparedSdk.Tasks.csproj') 'tasks'
$hostPath = (Get-ChildItem -LiteralPath (Join-Path $artifactRoot 'host/bin') -Recurse -Filter 'PreparedSdk.Host.dll' | Select-Object -First 1).FullName
$foreignPath = (Get-ChildItem -LiteralPath (Join-Path $artifactRoot 'foreign/bin') -Recurse -Filter 'PreparedForeign.dll' | Select-Object -First 1).FullName
$tasksPath = (Get-ChildItem -LiteralPath (Join-Path $artifactRoot 'tasks/bin') -Recurse -Filter 'PreparedSdk.Tasks.dll' | Select-Object -First 1).FullName
$consumerProject = Join-Path $probeRoot 'Consumer/Consumer.csproj'
$changedPayload = Join-Path $artifactRoot 'ChangedFinal.input'
[System.IO.File]::WriteAllText($changedPayload, [System.IO.File]::ReadAllText((Join-Path $probeRoot 'Consumer/FinalApplication.input')) + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

function Build-Consumer([string] $Name, [string] $Mismatch = 'none', [string] $FinalPayload = '', [string] $Arbitrary = 'alpha', [bool] $Nondeterministic = $false) {
    $caseRoot = Join-Path $artifactRoot $Name
    $arguments = @(
        'build', $consumerProject, '-c', 'Release', '--nologo',
        '--artifacts-path', "$caseRoot/artifacts",
        '-p:RuntimeIdentifier=win-x64', '-p:SelfContained=false',
        "-p:PreparedDotNetHost=$dotnet",
        "-p:PreparedHostPath=$hostPath",
        "-p:PreparedTasksPath=$tasksPath",
        "-p:PreparedForeignGeneratorPath=$foreignPath",
        "-p:PreparedJsonGeneratorPath=$JsonGeneratorPath",
        "-p:PreparedMismatchKind=$Mismatch",
        "-p:PreparedArbitrary=$Arbitrary",
        "-p:PreparedNondeterministic=$($Nondeterministic.ToString().ToLowerInvariant())"
    )
    if ($FinalPayload) {
        $arguments += "-p:PreparedFinalPayloadPath=$FinalPayload"
    }
    $expected = if ($Mismatch -eq 'none' -and -not $Nondeterministic) { 0 } else { 1 }
    Invoke-DotNet $arguments $expected
    $assembly = (Get-ChildItem -LiteralPath (Join-Path $caseRoot 'artifacts/bin') -Recurse -Filter 'Consumer.dll' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
    if (($Mismatch -ne 'none' -or $Nondeterministic) -and $assembly -and (Test-Path -LiteralPath $assembly)) {
        throw "Mismatch '$Mismatch' left a consumer assembly behind: $assembly"
    }
    return $caseRoot
}

$positive = Build-Consumer 'positive'
Invoke-DotNet @((Get-ChildItem -LiteralPath (Join-Path $positive 'artifacts/bin') -Recurse -Filter 'Consumer.dll' | Select-Object -First 1).FullName)
$repeat = Build-Consumer 'repeat'
$positiveEmitter = Get-ChildItem -LiteralPath (Join-Path $positive 'artifacts/obj') -Recurse -Filter 'Lucent.PreparedEmitter.*.dll' | Select-Object -First 1
$repeatEmitter = Get-ChildItem -LiteralPath (Join-Path $repeat 'artifacts/obj') -Recurse -Filter 'Lucent.PreparedEmitter.*.dll' | Select-Object -First 1
if ($positiveEmitter.Name -ne $repeatEmitter.Name -or (Get-FileHash $positiveEmitter.FullName).Hash -ne (Get-FileHash $repeatEmitter.FullName).Hash) {
    throw 'Identical payloads did not produce the same content-addressed deterministic emitter.'
}

$cacheEdit = Build-Consumer 'cache-edit'
$cacheFirstEmitter = Get-ChildItem -LiteralPath (Join-Path $cacheEdit 'artifacts/obj') -Recurse -Filter 'Lucent.PreparedEmitter.*.dll' | Select-Object -First 1
$cacheEdit = Build-Consumer 'cache-edit' 'none' $changedPayload
$cacheEmitters = @(Get-ChildItem -LiteralPath (Join-Path $cacheEdit 'artifacts/obj') -Recurse -Filter 'Lucent.PreparedEmitter.*.dll')
if ($cacheEmitters.Count -lt 2) {
    throw 'A same-project edit did not retain both immutable content-addressed emitters in the cache.'
}
$cacheCurrentPath = [System.IO.File]::ReadAllText((Get-ChildItem -LiteralPath (Join-Path $cacheEdit 'artifacts/obj') -Recurse -Filter 'emitter-path.txt' | Select-Object -First 1).FullName).Trim()
if ([System.IO.Path]::GetFileName($cacheCurrentPath) -eq $cacheFirstEmitter.Name) {
    throw 'A same-project edit retained the stale emitter as the current analyzer.'
}
$cacheGenerated = @(Get-ChildItem -LiteralPath (Join-Path $cacheEdit 'artifacts/obj') -Recurse -Filter '*.g.cs')
if ($cacheGenerated.FullName -match [Regex]::Escape([System.IO.Path]::GetFileNameWithoutExtension($cacheFirstEmitter.Name))) {
    throw 'The final compiler loaded a stale cached emitter after a same-project edit.'
}
if (-not ($cacheGenerated.FullName -match [Regex]::Escape([System.IO.Path]::GetFileNameWithoutExtension($cacheCurrentPath)))) {
    throw 'The final compiler did not load the current emitter after a same-project edit.'
}

$beta = Build-Consumer 'arbitrary-beta' 'none' '' 'beta'
$betaEmitter = Get-ChildItem -LiteralPath (Join-Path $beta 'artifacts/obj') -Recurse -Filter 'Lucent.PreparedEmitter.*.dll' | Select-Object -First 1
if ($positiveEmitter.Name -ne $betaEmitter.Name) {
    throw 'An outer global-property-only change unexpectedly changed the payload emitter address.'
}
$positiveManifest = Get-Content -Raw (Get-ChildItem -LiteralPath (Join-Path $positive 'artifacts/obj') -Recurse -Filter 'manifest.json' | Select-Object -First 1) | ConvertFrom-Json
$betaManifest = Get-Content -Raw (Get-ChildItem -LiteralPath (Join-Path $beta 'artifacts/obj') -Recurse -Filter 'manifest.json' | Select-Object -First 1) | ConvertFrom-Json
if ($positiveManifest.ProjectInputSha256 -eq $betaManifest.ProjectInputSha256) {
    throw 'An arbitrary outer global-property override did not invalidate project input identity.'
}
$betaForeign = Get-ChildItem -LiteralPath (Join-Path $beta 'artifacts/obj') -Recurse -Filter 'PreparedForeign.g.cs' | Select-Object -First 1
if (-not (Select-String -LiteralPath $betaForeign.FullName -SimpleMatch 'Arbitrary = "beta"')) {
    throw 'The final foreign generator did not receive the arbitrary outer global property.'
}

$changed = Build-Consumer 'changed-payload' 'none' $changedPayload
$changedEmitter = Get-ChildItem -LiteralPath (Join-Path $changed 'artifacts/obj') -Recurse -Filter 'Lucent.PreparedEmitter.*.dll' | Select-Object -First 1
if ($positiveEmitter.Name -eq $changedEmitter.Name) {
    throw 'Changed arbitrary payload retained the previous content address.'
}

foreach ($kind in @('changed', 'missing', 'extra', 'analyzer')) {
    Build-Consumer "negative-$kind" $kind | Out-Null
}
Build-Consumer 'negative-nondeterministic' 'none' '' 'alpha' $true | Out-Null

$results | Set-Content -LiteralPath (Join-Path $artifactRoot 'commands.log') -Encoding utf8
Write-Host "PASS: arbitrary early/final payloads produced deterministic content-addressed emitters; same-project edits selected only the current cached emitter; real JSON and evaluated options/defines/RID/AdditionalFiles passed; changed/missing/extra/analyzer/nondeterministic comparisons failed closed and removed assemblies."
