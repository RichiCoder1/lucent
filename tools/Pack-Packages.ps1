param(
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version,
    [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts/packages/$Version" }
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE) { throw 'Cannot resolve package source identity.' }
$names = Get-Content (Join-Path $PSScriptRoot 'package-set.json') -Raw | ConvertFrom-Json
foreach ($name in $names) {
    $project = Join-Path $root "src/$name/$name.csproj"
    & dotnet restore $project --locked-mode
    if ($LASTEXITCODE) { throw "Restore failed: $name" }
    & dotnet pack $project -c Release --no-restore -warnaserror "-p:LucentPackageVersion=$Version" "-p:RepositoryCommit=$commit" -o $OutputDirectory
    if ($LASTEXITCODE) { throw "Pack failed: $name" }
}
$null = & (Join-Path $PSScriptRoot 'Get-PackageSet.ps1') -Directory $OutputDirectory -Version $Version
Add-Type -AssemblyName System.IO.Compression.FileSystem
$notices = Get-Content (Join-Path $PSScriptRoot 'package-notices.json') -Raw | ConvertFrom-Json
foreach ($name in $names) {
    $path = Join-Path $OutputDirectory "$name.$Version.nupkg"
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        if (-not $zip.GetEntry('LICENSE') -or -not $zip.GetEntry('README.md')) { throw "Missing package attribution: $name" }
        foreach ($notice in @($notices | Where-Object package -eq $name)) {
            if (-not $zip.GetEntry($notice.entry)) { throw "Missing package notice: $name/$($notice.entry)" }
        }
        if ($name -eq 'Lucent.Reactive.R3' -and -not $zip.GetEntry('buildTransitive/notices/R3-LICENSE.txt')) { throw 'R3 package omitted upstream license notice.' }
        if ($name -eq 'Lucent.Lui.Sdk' -and -not $zip.GetEntry('Sdk/Assets.targets')) { throw 'Lucent.Lui.Sdk omitted the shared asset-generation targets.' }
        if ($name -eq 'Lucent.Icons.Lucide') {
            foreach ($required in @('buildTransitive/notices/Lucide-LICENSE.txt', 'buildTransitive/notices/Feather-LICENSE.txt', 'contentFiles/any/any/lucide-icons.json')) {
                if (-not $zip.GetEntry($required)) { throw "Lucide package omitted pinned artwork inventory or notice: $required" }
            }
            $inventoryEntry = $zip.GetEntry('contentFiles/any/any/lucide-icons.json')
            $inventoryReader = [IO.StreamReader]::new($inventoryEntry.Open())
            try { $inventory = $inventoryReader.ReadToEnd() | ConvertFrom-Json } finally { $inventoryReader.Dispose() }
            $selectedBytes = ($inventory.icons | Measure-Object bytes -Sum).Sum
            if ($inventory.revision -cne 'ba95e4c988b1e1b39cf5544e73b25a74b76816ee' -or $inventory.importerRevision -cne 'lucent-lucide-import-v1' -or @($inventory.icons).Count -ne 15 -or $selectedBytes -ne 5032) {
                throw 'Lucide package inventory did not retain its pinned finite selection.'
            }
            if (@($zip.Entries.FullName) -match '(^|/)(node_modules|package\.json|.*\.m?js)$') {
                throw 'Lucide package introduced a Node or JavaScript consumer dependency.'
            }
        }
        $entry = $zip.GetEntry("$name.nuspec")
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($spec.package.metadata.version -ne $Version -or $spec.package.metadata.repository.commit -ne $commit) { throw "Incorrect package identity: $name" }
        if ($name -eq 'Lucent.Core') {
            $runtimeTooling = @($spec.package.metadata.dependencies.group.dependency.id) -match '^Lucent\.Lui\.(Compiler|Generator)$|^Microsoft\.CodeAnalysis'
            $packedTooling = @($zip.Entries.FullName) -match '(^|/)(Lucent\.Lui\.(Compiler|Generator)|Microsoft\.CodeAnalysis).*\.dll$'
            if ($runtimeTooling -or $packedTooling) { throw 'Core package included build-time compiler, generator or Roslyn tooling.' }
        }
        if ($name -eq 'Lucent.Platform.Windows' -and (-not $zip.GetEntry('runtimes/win-x64/native/vcruntime140.dll') -or -not $zip.GetEntry('buildTransitive/notices/SDL3-CS/LICENSE'))) { throw 'Windows package omitted native runtime or notices.' }
        if ($name -eq 'Lucent.Lui.Sdk') {
            foreach ($required in @('Sdk/Sdk.props', 'Sdk/Sdk.targets', 'analyzers/dotnet/cs/Lucent.Lui.Generator.dll', 'analyzers/dotnet/cs/Lucent.Lui.Compiler.dll', 'tools/net10.0/Lucent.Lui.Tooling.dll', 'tools/net10.0/Lucent.Lui.Tooling.runtimeconfig.json')) {
                if (-not $zip.GetEntry($required)) { throw "SDK package omitted build-time asset/compiler tooling: $required" }
            }
            if ($spec.SelectNodes('//*[local-name()="dependencies"]//*[local-name()="dependency"]').Count -ne 0) {
                throw 'SDK package introduced runtime package dependencies; build-time tooling must remain private.'
            }
        }
    } finally { $zip.Dispose() }
}
Write-Output "Lucent package set: PASS ($Version, $commit)"
