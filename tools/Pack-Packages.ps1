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
$names = @('Lucent.Core', 'Lucent.Renderer.Skia', 'Lucent.Platform.Windows', 'Lucent.Hosting', 'Lucent.Lui.Sdk', 'Lucent.Reactive.R3')
foreach ($name in $names) {
    $project = Join-Path $root "src/$name/$name.csproj"
    & dotnet restore $project --locked-mode
    if ($LASTEXITCODE) { throw "Restore failed: $name" }
    & dotnet pack $project -c Release --no-restore -warnaserror "-p:LucentPackageVersion=$Version" "-p:RepositoryCommit=$commit" -o $OutputDirectory
    if ($LASTEXITCODE) { throw "Pack failed: $name" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($name in $names) {
    $path = Join-Path $OutputDirectory "$name.$Version.nupkg"
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        if (-not $zip.GetEntry('LICENSE') -or -not $zip.GetEntry('README.md')) { throw "Missing package attribution: $name" }
        if ($name -eq 'Lucent.Reactive.R3' -and -not $zip.GetEntry('buildTransitive/notices/R3-LICENSE.txt')) { throw 'R3 package omitted upstream license notice.' }
        $entry = $zip.GetEntry("$name.nuspec")
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($spec.package.metadata.version -ne $Version -or $spec.package.metadata.repository.commit -ne $commit) { throw "Incorrect package identity: $name" }
        if ($name -eq 'Lucent.Platform.Windows' -and (-not $zip.GetEntry('runtimes/win-x64/native/vcruntime140.dll') -or -not $zip.GetEntry('buildTransitive/notices/SDL3-CS/LICENSE'))) { throw 'Windows package omitted native runtime or notices.' }
        if ($name -eq 'Lucent.Lui.Sdk' -and (-not $zip.GetEntry('analyzers/dotnet/cs/Lucent.Lui.Generator.dll') -or -not $zip.GetEntry('tools/net10.0/Lucent.Lui.Tooling.dll'))) { throw 'SDK package omitted generator or formatter.' }
    } finally { $zip.Dispose() }
}
Write-Output "Lucent package set: PASS ($Version, $commit)"
