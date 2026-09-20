param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-[0-9A-Za-z.-]+$')] [string] $Version
)

$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $probeRoot '../../../..')).Path
$dotnet = Join-Path $repositoryRoot '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
foreach ($packageId in @('Lucent.Core', 'Lucent.Lui.Sdk')) {
    $package = Join-Path $feedPath "$packageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Missing candidate package: $package"
    }
}
$artifactRoot = Join-Path $repositoryRoot 'artifacts/a0-production-sdk-negatives'
if (Test-Path -LiteralPath $artifactRoot) {
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

$priorPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $packages
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

    $preparationHost = Get-ChildItem -LiteralPath $packages -Recurse -Filter 'Lucent.Lui.Sdk.PreparationHost.dll' | Select-Object -First 1
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
}

$commands | Set-Content -LiteralPath (Join-Path $artifactRoot 'commands.log') -Encoding utf8
Write-Host 'PRODUCTION SDK NEGATIVES PASS: same-project edits selected only the current cached emitter; missing-host and nondeterministic-generator failures removed consumer assemblies.'
exit 0
