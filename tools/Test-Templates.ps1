#requires -Version 7.4
<#
.SYNOPSIS
Exercises the four templates in an isolated CLI home and generated workspace.
.DESCRIPTION
GenerationOnly checks authoring without claiming package compatibility. Full mode
first verifies an explicit release descriptor and its local artifacts, then restores
and runs generated package-only consumers. Nothing opens a desktop window. The
template package is a local first slice, outside the current release-set allowlist.
#>
[CmdletBinding(DefaultParameterSetName = 'Release')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Release')][string] $DescriptorPath,
    [Parameter(Mandatory, ParameterSetName = 'Release')][string] $ArtifactDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Generation')][switch] $GenerationOnly,
    [Parameter(Mandatory, ParameterSetName = 'Generation')][ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')][string] $Version,
    [string] $OutputDirectory,
    [string] $DotNetPath = 'dotnet'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = (Get-Command $DotNetPath -ErrorAction Stop).Source
$policy = Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw | ConvertFrom-Json -AsHashtable
$sourceCommit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $sourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Unable to identify the template source checkout.' }
if (!$GenerationOnly) {
    $DescriptorPath = (Resolve-Path -LiteralPath $DescriptorPath).Path
    $ArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
    Import-Module (Join-Path $PSScriptRoot 'ReleaseSet.psm1') -Force
    $descriptor = Get-Content -LiteralPath $DescriptorPath -Raw | ConvertFrom-Json -AsHashtable
    Assert-LuiReleaseSet $descriptor $ArtifactDirectory
    $Version = $descriptor.releaseSet.version
    if ($descriptor.sdk.version -cne $policy.sdk.version -or $descriptor.sdk.rollForward -cne $policy.sdk.rollForward) {
        throw 'The release descriptor and template SDK policy differ.'
    }
}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/templates-proof/' + [Guid]::NewGuid().ToString('N')) }
$proof = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $proof) { throw "Proof directory must be new: $proof" }
[IO.Directory]::CreateDirectory($proof) | Out-Null
$savedEnvironment = @{}
foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'MSBUILDDISABLENODEREUSE', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
$env:DOTNET_CLI_HOME = Join-Path $proof 'cli-home'
$env:NUGET_PACKAGES = Join-Path $proof 'packages'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$sequence = 0
$checks = [Collections.Generic.List[string]]::new()
function Assert-That([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}
function Invoke-Dotnet([string[]] $Arguments, [string] $Label, [switch] $ExpectFailure) {
    $script:sequence++
    $log = Join-Path $proof ('{0:d2}-{1}.log' -f $script:sequence, $Label)
    & $dotnet @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $code = $LASTEXITCODE
    if ($ExpectFailure) { Assert-That ($code -ne 0) "$Label unexpectedly succeeded." }
    else { Assert-That ($code -eq 0) "$Label failed ($code); see $log" }
}
function Create-Project([string] $Template, [string] $Name, [string] $Folder, [string[]] $Extra = @()) {
    Invoke-Dotnet (@('new', $Template, '-n', $Name, '-o', $Folder) + $Extra) "create-$Template"
    Assert-That (!(Test-Path -LiteralPath (Join-Path $Folder 'obj'))) 'Generation performed an unexpected restore.'
    Assert-That (!(Test-Path -LiteralPath (Join-Path $Folder 'packages.lock.json'))) 'Generation shipped a stale lock file.'
    return (Get-ChildItem -LiteralPath $Folder -Filter '*.csproj').FullName
}
try {
    '<Project />' | Set-Content -LiteralPath (Join-Path $proof 'Directory.Build.props'), (Join-Path $proof 'Directory.Build.targets'), (Join-Path $proof 'Directory.Packages.props')
    Copy-Item -LiteralPath (Join-Path $root 'global.json') -Destination $proof
    & (Join-Path $root 'templates/Pack-Templates.ps1') -Version $Version -SourceCommit $sourceCommit -OutputDirectory (Join-Path $proof 'template-feed') -DotNetPath $dotnet
    $package = Join-Path $proof "template-feed/Lucent.Templates.$Version.nupkg"
    Push-Location $proof
    try {
        Invoke-Dotnet @('new', 'install', $package) 'install-local-template'
        $app = Create-Project 'lucent-app' 'Example.Desktop' (Join-Path $proof 'app')
        $library = Create-Project 'lucent-library' 'Example.Cards' (Join-Path $proof 'library')
        $tests = Create-Project 'lucent-tests' 'Example.Tests' (Join-Path $proof 'tests')
        $skia = Create-Project 'lucent-tests' 'Example.CaptureTests' (Join-Path $proof 'skia-tests') @('--Skia')
        $unicode = Create-Project 'lucent-library' 'Équipe Cards' (Join-Path $proof 'unicode path Ω')
        Invoke-Dotnet @('new', 'lucent-library', '-n', 'DefaultFolder') 'default-project-folder'
        Assert-That (Test-Path -LiteralPath (Join-Path $proof 'DefaultFolder/DefaultFolder.csproj')) 'Project generation did not prefer its named directory.'
        Assert-That (!(Test-Path -LiteralPath (Join-Path $proof 'DefaultFolder.csproj'))) 'Project generation escaped its named directory.'
        $noSkia = [IO.File]::ReadAllText($tests)
        Assert-That (!$noSkia.Contains('Lucent.Testing.Skia')) 'The base test project included optional Skia.'
        Assert-That ([IO.File]::ReadAllText($skia).Contains('Lucent.Testing.Skia')) 'The Skia option omitted its package.'
        Assert-That (!(Test-Path -LiteralPath (Join-Path $proof 'tests/CaptureTests.cs'))) 'The base test project included the capture test.'
        foreach ($folder in @('library', 'tests', 'skia-tests', 'unicode path Ω')) {
            foreach ($config in @('global.json', 'NuGet.config', '.config/dotnet-tools.json', '.vscode/settings.json')) {
                Assert-That (!(Test-Path -LiteralPath (Join-Path $proof "$folder/$config"))) "Template wrote workspace configuration: $folder/$config"
            }
        }
        $appSdk = Get-Content -LiteralPath (Join-Path $proof 'app/global.json') -Raw | ConvertFrom-Json
        Assert-That ($appSdk.sdk.version -ceq $policy.sdk.version -and $appSdk.sdk.rollForward -ceq $policy.sdk.rollForward) 'App SDK policy drifted.'
        foreach ($project in @($app, $library, $tests, $skia, $unicode)) {
            [xml]$xml = Get-Content -LiteralPath $project -Raw
            Assert-That ($xml.Project.Sdk.Contains("Lucent.Lui.Sdk/$Version")) 'Generated SDK pin differs from the template release.'
            foreach ($reference in $xml.Project.ItemGroup.PackageReference) {
                Assert-That ($reference.Version -ceq "[$Version]") 'Generated package pin is not exact.'
            }
        }
        $checks.Add('four templates; exact pins; Unicode and space path; no automatic restore; optional Skia; configuration scope')
        # Native template conflict handling must preserve an existing app SDK file.
        $conflict = Join-Path $proof 'conflict'
        [IO.Directory]::CreateDirectory($conflict) | Out-Null
        $sentinel = '{"sdk":{"version":"10.0.401","rollForward":"latestPatch"}}'
        [IO.File]::WriteAllText((Join-Path $conflict 'global.json'), $sentinel)
        Invoke-Dotnet @('new', 'lucent-app', '-n', 'Conflict', '-o', $conflict) 'app-config-conflict' -ExpectFailure
        Assert-That ([IO.File]::ReadAllText((Join-Path $conflict 'global.json')) -ceq $sentinel) 'App generation overwrote existing SDK policy.'
        $checks.Add('existing app global.json conflicts without overwrite')
        # Item templates use the SDK host's project context binding, without writing project/config files.
        $item = Join-Path $proof 'item'
        [IO.Directory]::CreateDirectory($item) | Out-Null
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Company.Custom</RootNamespace></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $item 'Host.csproj')
        '<configuration />' | Set-Content -LiteralPath (Join-Path $item 'NuGet.config')
        Copy-Item -LiteralPath (Join-Path $root 'global.json') -Destination $item
        [IO.Directory]::CreateDirectory((Join-Path $item '.config')) | Out-Null
        [IO.Directory]::CreateDirectory((Join-Path $item '.vscode')) | Out-Null
        '{"version":1,"isRoot":true,"tools":{}}' | Set-Content -LiteralPath (Join-Path $item '.config/dotnet-tools.json')
        '{"editor.tabSize":7}' | Set-Content -LiteralPath (Join-Path $item '.vscode/settings.json')
        Push-Location $item
        try {
            Invoke-Dotnet @('new', 'lucent-component', '-n', 'Greeting') 'item-requires-restored-project' -ExpectFailure
            Assert-That (!(Test-Path -LiteralPath (Join-Path $item 'Greeting.lui'))) 'Refused item generation left a component behind.'
            Invoke-Dotnet @('restore', 'Host.csproj', '--configfile', 'NuGet.config') 'explicit-item-host-restore'
        }
        finally { Pop-Location }
        $before = @{}
        foreach ($file in Get-ChildItem -LiteralPath $item -File -Recurse) { $before[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
        Push-Location $item
        try { Invoke-Dotnet @('new', 'lucent-component', '-n', 'Greeting') 'create-item' }
        finally { Pop-Location }
        $component = Join-Path $item 'Greeting.lui'
        Assert-That ([IO.File]::ReadAllText($component).Contains('namespace Company.Custom;')) 'Item did not bind the project RootNamespace.'
        foreach ($path in $before.Keys) { Assert-That ((Get-FileHash -LiteralPath $path).Hash -ceq $before[$path]) "Item changed existing file: $path" }
        Assert-That ((Get-ChildItem -LiteralPath $item -File -Recurse).Count -eq $before.Count + 1) 'Item emitted files besides its component.'
        $checks.Add('item refuses unrestored host; after explicit restore binds RootNamespace and leaves project/config files unchanged')
        if (!$GenerationOnly) {
            $feed = Join-Path $proof 'release-feed'
            [IO.Directory]::CreateDirectory($feed) | Out-Null
            foreach ($entry in $descriptor.packages) {
                Copy-Item -LiteralPath (Resolve-LuiArtifactPath $ArtifactDirectory $entry.artifact.fileName) -Destination $feed
            }
            $escapedFeed = [Security.SecurityElement]::Escape($feed)
            "<configuration><packageSources><clear /><add key=`"lucent`" value=`"$escapedFeed`" /><add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`" /></packageSources><packageSourceMapping><packageSource key=`"lucent`"><package pattern=`"Lucent.*`" /><package pattern=`"Example.Cards`" /></packageSource><packageSource key=`"nuget.org`"><package pattern=`"*`" /></packageSource></packageSourceMapping></configuration>" | Set-Content -LiteralPath (Join-Path $proof 'NuGet.config')
            foreach ($project in @($app, $library, $tests, $skia, $unicode)) {
                Invoke-Dotnet @('restore', $project, '--configfile', (Join-Path $proof 'NuGet.config')) 'restore-generated-project'
                Assert-That (Test-Path -LiteralPath (Join-Path (Split-Path $project -Parent) 'packages.lock.json')) 'First restore did not generate a lock.'
                Invoke-Dotnet @('restore', $project, '--locked-mode', '--configfile', (Join-Path $proof 'NuGet.config')) 'locked-generated-restore'
                Invoke-Dotnet @('build', $project, '-c', 'Release', '--no-restore', '-m:1', '-nr:false') 'build-generated-project'
            }
            Invoke-Dotnet @('run', '--project', $tests, '-c', 'Release', '--no-build', '--no-restore', '--', '--minimum-expected-tests', '1') 'semantic-test'
            Invoke-Dotnet @('run', '--project', $skia, '-c', 'Release', '--no-build', '--no-restore', '--', '--minimum-expected-tests', '2') 'capture-tests'
            $checks.Add('first restore locks; locked restore; all generated projects warning-clean; semantic and optional capture tests')
            # Exercise an item in a real Lucent SDK project after the independent binding/config check.
            Push-Location (Split-Path $library -Parent)
            try { Invoke-Dotnet @('new', 'lucent-component', '-n', 'PackageGreeting') 'create-library-item' }
            finally { Pop-Location }
            Invoke-Dotnet @('pack', $library, '-c', 'Release', '--no-restore', '-o', $feed, '-p:PackageVersion=1.0.0', '-m:1', '-nr:false') 'pack-generated-library'
            & (Join-Path $root 'templates/Test-LibraryConsumer.ps1') -ProofDirectory $proof -DotNetPath $dotnet -Version $Version
            $checks.Add('generated item compiles; generated library consumed only as NuGet, including embedded asset and retained state')
            Invoke-Dotnet @('publish', $app, '-c', 'Release', '-r', 'win-x64', '-p:PublishAot=true', '-o', (Join-Path $proof 'native-app'), '-m:1', '-nr:false') 'publish-native-app'
            $checks.Add('generated Windows app NativeAOT publish, not launched')
        }
        Invoke-Dotnet @('new', 'uninstall', 'Lucent.Templates') 'uninstall-isolated-template'
    }
    finally { Pop-Location }
    $evidence = [ordered]@{
        scope = $(if ($GenerationOnly) { 'generation-only' } else { 'validated-candidate-consumers' })
        version = $Version
        templateSourceCommit = $sourceCommit
        sdk = $policy.sdk
        templatePackage = @{ file = $package; sha256 = (Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant() }
        checks = @($checks)
        releaseIntegration = 'pending: template package is not yet part of the compatible release-set inventory'
        sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'templates') -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
            @{ path = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
        })
    }
    if (!$GenerationOnly) { $evidence.descriptorSha256 = (Get-FileHash -LiteralPath $DescriptorPath).Hash.ToLowerInvariant() }
    $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $proof 'evidence.json')
    Write-Output "Template proof: $proof"
}
finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}
