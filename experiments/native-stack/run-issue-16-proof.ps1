param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-16")

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path "$PSScriptRoot/../..").Path
$project = Join-Path $PSScriptRoot 'NativeStackProbe/NativeStackProbe.csproj'
$solution = Join-Path $PSScriptRoot 'NativeStack.sln'
$publish = Join-Path $PSScriptRoot 'NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish'
$exe = Join-Path $publish 'NativeStackProbe.exe'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}

function Invoke-Json([string]$Command, [string[]]$Arguments) {
    $output = & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE): $($Arguments -join ' ')" }
    try { return ($output | ConvertFrom-Json) }
    catch { throw "$Command did not emit one JSON value: $($output -join [Environment]::NewLine)" }
}

function Convert-LastJson($Output, [string]$Command) {
    $json = @($Output | Where-Object { $_ -is [string] -and $_.TrimStart().StartsWith('{') }) | Select-Object -Last 1
    if ($null -eq $json) { throw "$Command did not emit JSON." }
    try { return ($json | ConvertFrom-Json) }
    catch { throw "$Command emitted invalid JSON: $json" }
}

function Get-Sha256([string]$Path) { (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Get-RelativePath([string]$Base, [string]$Path) {
    [IO.Path]::GetRelativePath($Base, $Path).Replace('\', '/')
}

function Test-ManagedAssembly([string]$Path) {
    try { [Reflection.AssemblyName]::GetAssemblyName($Path) | Out-Null; return $true }
    catch [BadImageFormatException] { return $false }
}

function Test-PeFile([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { return $false }
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 64 -or $peOffset -gt $stream.Length - 4) { return $false }
        $stream.Position = $peOffset
        return $reader.ReadUInt32() -eq 0x00004550
    }
    finally { $reader.Dispose() }
}

function Assert-LockedPackage($Lock, [string]$Id, [string]$Version) {
    $found = @($Lock.dependencies.PSObject.Properties | ForEach-Object {
        $_.Value.PSObject.Properties | Where-Object { $_.Name -eq $Id -and $_.Value.resolved -eq $Version }
    })
    if ($found.Count -eq 0) { throw "Locked package missing: $Id $Version" }
}

function Assert-ManualIme([string]$Path, [bool]$FocusCase) {
    $events = @(Get-Content $Path | ForEach-Object { $_ | ConvertFrom-Json })
    $japanese = -join [char[]](0x65E5, 0x672C, 0x8A9E)
    $tesuto = -join [char[]](0x3066, 0x3059, 0x3068)
    $valid = if ($FocusCase) {
        $events[-1].Kind -eq 'close' -and $events[-1].Committed -eq '' -and
        @($events | Where-Object { $_.Kind -eq 'editing' -and $_.Preedit -eq $tesuto }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'focus-lost' -and $_.Committed -eq '' -and $_.Preedit -eq '' }).Count -gt 0
    } else {
        @($events | Where-Object { $_.Kind -eq 'editing' -and $_.Text.Length -gt 0 }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'editing' -and $_.Text -eq '' }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'input' -and $_.Text -eq $japanese }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'focus-lost' }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'focus-gained' }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'close' }).Count -gt 0
    }
    if (-not $valid) { throw "Recorded manual IME evidence failed validation: $Path" }
    [ordered]@{ label = 'manual supplemental SDL/Windows IME evidence; not rerun'; path = Get-RelativePath $repository $Path; sha256 = Get-Sha256 $Path; valid = $true }
}

$readme = Join-Path $OutputDirectory 'README.md'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Get-ChildItem $OutputDirectory -Force | Where-Object { $_.FullName -ne $readme } | Remove-Item -Recurse -Force

$sourceFiles = @(git -C $repository ls-files --cached --others --exclude-standard -- experiments/native-stack | Where-Object {
    $_ -notmatch '/(bin|obj|evidence)/'
} | Sort-Object)
$sourceIdentity = [ordered]@{
    head = (git -C $repository rev-parse HEAD).Trim()
    status = @(git -C $repository status --porcelain=v1 -- experiments/native-stack)
    files = @($sourceFiles | ForEach-Object { [ordered]@{ path = $_; sha256 = Get-Sha256 (Join-Path $repository $_) } })
}

Invoke-Checked -Command dotnet -Arguments @('restore', $solution, '--locked-mode')
Invoke-Checked -Command dotnet -Arguments @('build', $solution, '--no-restore', '-warnaserror')

$projectText = Get-Content $project -Raw
if ($projectText -notmatch '<PublishAot>true</PublishAot>' -or $projectText -notmatch '<PublishTrimmed>true</PublishTrimmed>') { throw 'NativeAOT/trimming project directives are missing.' }
$registrationCount = @([regex]::Matches((Get-Content (Join-Path $PSScriptRoot 'NativeStackProbe/Program.cs') -Raw), '\[JsonSerializable\(')).Count
if ($registrationCount -eq 0) { throw 'Source-generated JSON registration table is missing.' }

$lockPath = Join-Path $PSScriptRoot 'NativeStackProbe/packages.lock.json'
$lock = Get-Content $lockPath -Raw | ConvertFrom-Json
$nugetPackages = $env:NUGET_PACKAGES ?? (Join-Path $HOME '.nuget/packages')
$obligations = @(
    [ordered]@{ package = 'Microsoft.DotNet.ILCompiler'; version = '9.0.19'; contribution = 'NativeAOT compiler/runtime build input; compiled into the application, not copied as a standalone asset'; shipped = $false; license = 'MIT'; source = 'https://www.nuget.org/packages/Microsoft.DotNet.ILCompiler/9.0.19'; licenseFiles = @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT') },
    [ordered]@{ package = 'Microsoft.NET.ILLink.Tasks'; version = '9.0.19'; contribution = 'trimming build task only'; shipped = $false; license = 'MIT'; source = 'https://www.nuget.org/packages/Microsoft.NET.ILLink.Tasks/9.0.19'; licenseFiles = @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT') },
    [ordered]@{ package = 'runtime.win-x64.Microsoft.DotNet.ILCompiler'; version = '9.0.19'; contribution = 'RID-specific NativeAOT compiler/runtime build input; compiled into the application, not copied as a standalone asset'; shipped = $false; license = 'MIT'; source = 'https://www.nuget.org/packages/runtime.win-x64.Microsoft.DotNet.ILCompiler/9.0.19'; licenseFiles = @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT') },
    [ordered]@{ package = 'Microsoft.Windows.CsWin32'; version = '0.3.321'; contribution = 'PrivateAssets source generator; generated interop is compiled into the application'; shipped = $false; license = 'MIT'; source = 'https://www.nuget.org/packages/Microsoft.Windows.CsWin32/0.3.321'; licenseFiles = @('NOTICE.txt','microsoft.windows.cswin32.nuspec') },
    [ordered]@{ package = 'Microsoft.Windows.SDK.Win32Docs'; version = '0.1.42-alpha'; contribution = 'CsWin32 build-time documentation metadata only'; shipped = $false; license = 'Windows SDK license'; source = 'https://www.nuget.org/packages/Microsoft.Windows.SDK.Win32Docs/0.1.42-alpha'; licenseFiles = @('microsoft.windows.sdk.win32docs.nuspec') },
    [ordered]@{ package = 'Microsoft.Windows.SDK.Win32Metadata'; version = '71.0.14-preview'; contribution = 'CsWin32 build-time Win32 metadata input'; shipped = $false; license = 'Windows SDK license'; source = 'https://www.nuget.org/packages/Microsoft.Windows.SDK.Win32Metadata/71.0.14-preview'; licenseFiles = @('sdk_license.txt') },
    [ordered]@{ package = 'Microsoft.Windows.WDK.Win32Metadata'; version = '0.13.25-experimental'; contribution = 'CsWin32 build-time WDK metadata input'; shipped = $false; license = 'Windows SDK license'; source = 'https://www.nuget.org/packages/Microsoft.Windows.WDK.Win32Metadata/0.13.25-experimental'; licenseFiles = @('sdk_license.txt') },
    [ordered]@{ package = 'SDL3-CS'; version = '3.4.14.1'; contribution = 'managed SDL bindings statically compiled into NativeStackProbe'; shipped = $true; license = 'zlib'; source = 'https://www.nuget.org/packages/SDL3-CS/3.4.14.1'; licenseFiles = @('LICENSE') },
    [ordered]@{ package = 'SDL3-CS.Windows'; version = '3.4.14.1'; contribution = 'ships SDL3.dll'; shipped = $true; license = 'zlib'; source = 'https://www.nuget.org/packages/SDL3-CS.Windows/3.4.14.1'; licenseFiles = @('LICENSE') },
    [ordered]@{ package = 'SkiaSharp'; version = '4.151.1'; contribution = 'managed rendering bindings statically compiled into NativeStackProbe'; shipped = $true; license = 'MIT'; source = 'https://www.nuget.org/packages/SkiaSharp/4.151.1'; licenseFiles = @('LICENSE.txt') },
    [ordered]@{ package = 'SkiaSharp.HarfBuzz'; version = '4.151.1'; contribution = 'managed shaping bindings statically compiled into NativeStackProbe'; shipped = $true; license = 'MIT'; source = 'https://www.nuget.org/packages/SkiaSharp.HarfBuzz/4.151.1'; licenseFiles = @('LICENSE.txt') },
    [ordered]@{ package = 'SkiaSharp.NativeAssets.Win32'; version = '4.151.1'; contribution = 'ships libSkiaSharp.dll'; shipped = $true; license = 'MIT plus third-party notices'; source = 'https://www.nuget.org/packages/SkiaSharp.NativeAssets.Win32/4.151.1'; licenseFiles = @('LICENSE.txt','THIRD-PARTY-NOTICES.txt') },
    [ordered]@{ package = 'SkiaSharp.NativeAssets.macOS'; version = '4.151.1'; contribution = 'not selected for win-x64 publish'; shipped = $false; license = 'MIT'; source = 'https://www.nuget.org/packages/SkiaSharp.NativeAssets.macOS/4.151.1'; licenseFiles = @('LICENSE.txt') },
    [ordered]@{ package = 'HarfBuzzSharp'; version = '14.2.1.1'; contribution = 'managed shaping bindings statically compiled into NativeStackProbe'; shipped = $true; license = 'MIT'; source = 'https://www.nuget.org/packages/HarfBuzzSharp/14.2.1.1'; licenseFiles = @('LICENSE.txt') },
    [ordered]@{ package = 'HarfBuzzSharp.NativeAssets.Win32'; version = '14.2.1.1'; contribution = 'ships libHarfBuzzSharp.dll'; shipped = $true; license = 'MIT plus third-party notices'; source = 'https://www.nuget.org/packages/HarfBuzzSharp.NativeAssets.Win32/14.2.1.1'; licenseFiles = @('LICENSE.txt','THIRD-PARTY-NOTICES.txt') },
    [ordered]@{ package = 'HarfBuzzSharp.NativeAssets.macOS'; version = '14.2.1.1'; contribution = 'not selected for win-x64 publish'; shipped = $false; license = 'MIT'; source = 'https://www.nuget.org/packages/HarfBuzzSharp.NativeAssets.macOS/14.2.1.1'; licenseFiles = @('LICENSE.txt') }
)
$lockedPackages = @($lock.dependencies.PSObject.Properties | ForEach-Object { $_.Value.PSObject.Properties.Name } | Sort-Object -Unique)
if (Compare-Object $lockedPackages @($obligations.package | Sort-Object)) { throw 'Dependency obligation manifest does not cover packages.lock.json.' }
foreach ($obligation in $obligations) {
    Assert-LockedPackage $lock $obligation.package $obligation.version
    $root = Join-Path $nugetPackages "$($obligation.package.ToLowerInvariant())/$($obligation.version)"
    $obligation['licenseSources'] = @($obligation.licenseFiles | ForEach-Object {
        $path = Join-Path $root $_
        if (-not (Test-Path $path)) { throw "Authoritative license/notice source missing: $path" }
        [ordered]@{ path = $path; sha256 = Get-Sha256 $path }
    })
}
$dotnetRoot = Split-Path (Get-Command dotnet).Source -Parent
$sdkSources = @((Join-Path $dotnetRoot 'LICENSE.txt'))
if (@($sdkSources | Where-Object { -not (Test-Path $_) }).Count -ne 0) { throw 'Authoritative SDK license/notice source is missing.' }
$sdkObligation = [ordered]@{ sdk = 'dotnet SDK 9.0.317 / Microsoft.NETCore.App 9.0.19'; contribution = 'NativeAOT build SDK/runtime source'; licenseSources = @($sdkSources | ForEach-Object { [ordered]@{ path = $_; sha256 = Get-Sha256 $_ } }) }

$nativeAssets = @(
    [ordered]@{ asset = 'NativeStackProbe.exe'; package = 'NativeStackProbe project'; version = (git -C $repository rev-parse HEAD).Trim(); contribution = 'NativeAOT application executable'; source = 'experiments/native-stack/NativeStackProbe/NativeStackProbe.csproj' },
    [ordered]@{ asset = 'SDL3.dll'; package = 'SDL3-CS.Windows'; version = '3.4.14.1'; contribution = 'native SDL host'; source = 'https://www.nuget.org/packages/SDL3-CS.Windows/3.4.14.1' },
    [ordered]@{ asset = 'libSkiaSharp.dll'; package = 'SkiaSharp.NativeAssets.Win32'; version = '4.151.1'; contribution = 'native rendering'; source = 'https://www.nuget.org/packages/SkiaSharp.NativeAssets.Win32/4.151.1' },
    [ordered]@{ asset = 'libHarfBuzzSharp.dll'; package = 'HarfBuzzSharp.NativeAssets.Win32'; version = '14.2.1.1'; contribution = 'native shaping'; source = 'https://www.nuget.org/packages/HarfBuzzSharp.NativeAssets.Win32/14.2.1.1' }
)
foreach ($nativeAsset in $nativeAssets | Where-Object { $_.package -ne 'NativeStackProbe project' }) {
    $obligation = @($obligations | Where-Object { $_.package -eq $nativeAsset.package -and $_.version -eq $nativeAsset.version })
    if ($obligation.Count -ne 1) { throw "Native asset has no locked obligation: $($nativeAsset.asset)" }
    $nativeAsset['license'] = $obligation[0].license
    $nativeAsset['licenseSources'] = $obligation[0].licenseSources
}
$expectedNative = @($nativeAssets.asset)
$expectedSymbols = @('NativeStackProbe.pdb', 'libSkiaSharp.pdb', 'libHarfBuzzSharp.pdb')
$notice = Join-Path $publish 'THIRD-PARTY-NOTICES.md'
$noticeContent = @('# NativeStackProbe NativeAOT dependency notices', '')
foreach ($obligation in $obligations) {
    $noticeContent += "## $($obligation.package) $($obligation.version) — $($obligation.license)"
    $noticeContent += "Source: $($obligation.source)"
    foreach ($licenseSource in $obligation.licenseSources) { $noticeContent += ''; $noticeContent += Get-Content $licenseSource.path }
    $noticeContent += ''
}
$noticeContent += '## .NET SDK/runtime build source'; foreach ($source in $sdkObligation.licenseSources) { $noticeContent += ''; $noticeContent += Get-Content $source.path }

Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $publish | Out-Null
Invoke-Checked -Command dotnet -Arguments @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishAot=true', '-p:PublishTrimmed=true', '--no-restore', '-warnaserror')
if (-not (Test-Path $exe)) { throw "Published executable missing: $exe" }
Set-Content $notice $noticeContent -Encoding UTF8

$assets = @(Get-ChildItem $publish -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = Get-RelativePath $publish $_.FullName
    if ($relative -eq 'THIRD-PARTY-NOTICES.md') { $kind = 'notice' }
    elseif (Test-PeFile $_.FullName) { $kind = if (Test-ManagedAssembly $_.FullName) { 'managed' } else { 'native' } }
    elseif ($relative -in $expectedSymbols) { $kind = 'symbols' }
    else { throw "Unexpected publish output type: $relative" }
    [ordered]@{ path = $relative; kind = $kind; bytes = $_.Length; sha256 = Get-Sha256 $_.FullName }
})
$publishedNative = @($assets | Where-Object kind -eq 'native')
if (Compare-Object $expectedNative @($publishedNative.path)) { throw "Unexpected or missing native publish assets: $(@($publishedNative.path) -join ', ')" }
if (Compare-Object $expectedSymbols @($assets | Where-Object kind -eq 'symbols' | ForEach-Object path)) { throw 'Unexpected or missing symbol assets.' }
if (@($assets | Where-Object kind -eq 'managed').Count -ne 0 -or @($assets | Where-Object kind -eq 'notice').Count -ne 1) { throw 'Publish asset classification failed.' }

$automated = Invoke-Json -Command $exe -Arguments @('--automated')
if (-not $automated.CriticalClaimsPass) { throw 'Published SDL host lifecycle proof failed.' }
$dependencyProbe = Invoke-Json -Command $exe -Arguments @()
if (-not $dependencyProbe.Ok) { throw 'Published dependency probe failed.' }
foreach ($package in $nativeAssets | Where-Object { $_.package -ne 'NativeStackProbe project' }) {
    $module = @($dependencyProbe.Modules | Where-Object {
        $_.Name -eq $package.asset -and (Get-RelativePath $publish $_.Path) -eq $package.asset
    })
    if ($module.Count -ne 1) { throw "Published native asset was not loaded and invoked: $($package.asset)" }
    $package['loadedModule'] = $module[0]
}

$text = Invoke-Json -Command $exe -Arguments @('--text-self-check')
$controls = Invoke-Json -Command $exe -Arguments @('--controls-self-check')
if (-not $text.Ok -or -not $controls.Ok -or -not $controls.ImeComposition) { throw 'Published text/IME state proof failed.' }

$browser = Join-Path $OutputDirectory 'issue-browser'
$walkthrough = @((& $exe --issue-browser-walkthrough $browser) | ForEach-Object { $_ | ConvertFrom-Json })
if ($LASTEXITCODE -ne 0 -or $walkthrough.Count -ne 12 -or @($walkthrough | Where-Object { -not $_.Pass }).Count -ne 0) { throw 'Published issue-browser W1-W12 walkthrough failed.' }
$expectedSteps = @('W1 cold-start','W2 search','W3 latest-query','W4 filters','W5 scroll','W6 selection','W7 filtered-selection','W8 title-edit','W9 failure-retry','W10 theme','W11 keyboard','W12 semantics')
if ((@($walkthrough.Step) -join "`n") -ne ($expectedSteps -join "`n")) { throw 'Published issue-browser W1-W12 sequence changed.' }

$sceneOutput = & (Join-Path $PSScriptRoot 'run-scene-proof.ps1') -OutputDirectory (Join-Path $OutputDirectory 'scene') -NoPublish
if ($LASTEXITCODE -ne 0) { throw 'run-scene-proof.ps1 failed.' }
$scene = Convert-LastJson $sceneOutput 'run-scene-proof.ps1'
if (-not $scene.ok -or $scene.rasterTolerance -ne 0) { throw 'Published headless/native rendering parity proof failed.' }
$virtualizationOutput = & (Join-Path $PSScriptRoot 'run-virtualization-proof.ps1') -OutputDirectory (Join-Path $OutputDirectory 'virtualization') -NoPublish
if ($LASTEXITCODE -ne 0) { throw 'run-virtualization-proof.ps1 failed.' }
$virtualization = Convert-LastJson $virtualizationOutput 'run-virtualization-proof.ps1'
if (-not $virtualization.ok) { throw 'Published virtualization proof failed.' }
$issue14Output = Join-Path $OutputDirectory 'issue-14'
New-Item -ItemType Directory -Force $issue14Output | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'evidence/issue-14/manual-checklist.md') $issue14Output
Copy-Item (Join-Path $PSScriptRoot 'evidence/issue-14/manual') $issue14Output -Recurse
$issue14OutputText = & (Join-Path $PSScriptRoot 'run-issue-14-proof.ps1') -OutputDirectory $issue14Output -NoPublish
if ($LASTEXITCODE -ne 0) { throw 'run-issue-14-proof.ps1 failed.' }
$issue14 = Convert-LastJson $issue14OutputText 'run-issue-14-proof.ps1'
if (-not $issue14.ok -or -not $issue14.providerContract.Ok -or $issue14.manualEvidence -notmatch '^passed;') { throw 'Published issue #14 accessibility proof failed.' }
$issue15Output = & (Join-Path $PSScriptRoot 'run-issue-15-proof.ps1') -OutputDirectory (Join-Path $OutputDirectory 'issue-15') -NoPublish
if ($LASTEXITCODE -ne 0) { throw 'run-issue-15-proof.ps1 failed.' }
$issue15 = Convert-LastJson $issue15Output 'run-issue-15-proof.ps1'
if (-not $issue15.ok -or -not $issue15.nativeAot -or -not $issue15.benchmark.RuntimeNativeAot) { throw 'Published issue #15 NativeAOT performance proof failed.' }

$ime = @(
    Assert-ManualIme (Join-Path $PSScriptRoot 'evidence/issue-3-ime-ja-JP.jsonl') $false
    Assert-ManualIme (Join-Path $PSScriptRoot 'evidence/issue-3-ime-focus-ja-JP.jsonl') $true
)
$manualChecklist = Join-Path $PSScriptRoot 'evidence/issue-14/manual-checklist.md'
$manualAccessibility = [ordered]@{
    label = 'manual Accessibility Insights/Narrator evidence; not rerun'
    path = Get-RelativePath $repository $manualChecklist
    sha256 = Get-Sha256 $manualChecklist
    valid = ((Get-Content $manualChecklist -Raw) -match 'manual UIA/Narrator gate — passed') -and @('insights-button.png','insights-list-item.png','insights-tree.png','native-window-focus.png' | ForEach-Object { Test-Path (Join-Path $PSScriptRoot "evidence/issue-14/manual/$_") }) -notcontains $false
}
if (-not $manualAccessibility.valid) { throw 'Recorded manual accessibility evidence is incomplete.' }

$forbiddenPattern = 'System\.Reflection|Assembly\.Load|Assembly\.GetTypes|Type\.GetType|Activator\.CreateInstance|MakeGenericType|MakeGenericMethod|Expression\.Compile|DynamicMethod|Reflection\.Emit|CodeDom|CSharpCodeProvider'
$corePaths = @(
    Get-ChildItem (Join-Path $PSScriptRoot 'NativeStackProbe') -File -Recurse -Include '*.cs','*.csproj' |
        Where-Object FullName -NotMatch '[\\/]obj[\\/]'
) + (Get-Item (Join-Path $PSScriptRoot 'Directory.Packages.props'))
$forbidden = @($corePaths | Select-String -Pattern $forbiddenPattern)
if ($forbidden.Count -ne 0) { throw "Forbidden reflection/runtime-generation API: $($forbidden.Path -join ', ')" }

Copy-Item $notice (Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.md') -Force

Invoke-Checked -Command git -Arguments @('-C', $repository, 'diff', '--check')
$staged = @(git -C $repository diff --cached --name-only)
if ($staged.Count -ne 0) { throw 'Issue #16 proof requires no staged files.' }

$proof = [ordered]@{
    ok = $true
    issue = 16
    source = $sourceIdentity
    publish = [ordered]@{
        command = 'dotnet publish experiments/native-stack/NativeStackProbe/NativeStackProbe.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:PublishTrimmed=true --no-restore -warnaserror'
        configuration = 'Release'; rid = 'win-x64'; nativeAot = $true; trimmed = $true
        executable = [ordered]@{ path = Get-RelativePath $repository $exe; sha256 = Get-Sha256 $exe; bytes = (Get-Item $exe).Length }
        assets = $assets
        nativeAssets = $nativeAssets
        noManagedAssemblies = $true
    }
    nativeAot = [ordered]@{ dynamicCodeUnsupported = $issue15.benchmark.RuntimeNativeAot; directives = @('PublishAot=true', 'PublishTrimmed=true'); sourceGeneratedJsonRegistrations = $registrationCount; forbiddenCoreApiMatches = 0 }
    smoke = [ordered]@{
        sdlHost = $automated; dependencyLoad = $dependencyProbe; textAndImeState = [ordered]@{ text = $text; controls = $controls }
        issueBrowser = [ordered]@{ passed = $walkthrough.Count; steps = @($walkthrough.Step) }
        headlessNativeParity = $scene; virtualization = $virtualization; issue14Accessibility = $issue14; issue15Performance = $issue15
    }
    manualEvidence = [ordered]@{ japaneseIme = $ime; accessibility = $manualAccessibility }
    dependencyObligations = $obligations
    sdkObligation = $sdkObligation
    thirdPartyNotices = [ordered]@{ shippedPath = Get-RelativePath $repository $notice; evidencePath = Get-RelativePath $repository (Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.md'); sha256 = Get-Sha256 $notice; sourcePackageCount = $obligations.Count }
    commands = @('dotnet restore experiments/native-stack/NativeStack.sln --locked-mode', 'dotnet build experiments/native-stack/NativeStack.sln --no-restore -warnaserror', 'published NativeStackProbe executable seams and no-publish issue #14/#15 wrappers', 'git diff --check', 'git diff --cached --name-only')
    noStagedFiles = $true
}
$proof | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 20 -Compress
