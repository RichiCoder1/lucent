#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-LuiReleasePolicy {
    Get-Content -LiteralPath (Join-Path $PSScriptRoot '../eng/lui-release-policy.json') -Raw | ConvertFrom-Json -AsHashtable
}

function ConvertTo-LuiCanonicalNode($Value) {
    if ($Value -is [Collections.IDictionary]) {
        $result = [ordered]@{}
        $keys = [string[]]@($Value.Keys)
        [Array]::Sort($keys, [StringComparer]::Ordinal)
        foreach ($key in $keys) { $result[$key] = ConvertTo-LuiCanonicalNode $Value[$key] }
        return $result
    }
    if ($Value -is [Collections.IList]) {
        return ,@($Value | ForEach-Object { ConvertTo-LuiCanonicalNode $_ })
    }
    return $Value
}

function ConvertTo-LuiCanonicalJson($Value) {
    ConvertTo-Json -InputObject (ConvertTo-LuiCanonicalNode $Value) -Depth 64 -Compress
}

function Get-LuiBytesHash([byte[]] $Bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Assert-LuiRelativePath([string] $Path) {
    if (!$Path -or $Path.Contains('\') -or $Path.Contains(':') -or $Path.StartsWith('/') -or $Path -match '[\x00-\x1f]' -or @($Path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
        throw "Unsafe artifact path: $Path"
    }
}

function Resolve-LuiArtifactPath([string] $Directory, [string] $Path) {
    Assert-LuiRelativePath $Path
    $root = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $root $Path))
    if (!$full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact escaped its directory.' }
    for ($current = $full; $current -ne $root; $current = [IO.Path]::GetDirectoryName($current)) {
        if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Artifact path contains a link: $Path" }
    }
    return $full
}

function Get-LuiArtifact([string] $Directory, [string] $Path) {
    $full = Resolve-LuiArtifactPath $Directory $Path
    $file = Get-Item -LiteralPath $full
    if ($file.PSIsContainer -or $file.Length -le 0 -or $file.Length -gt 512MB) { throw "Invalid artifact size: $Path" }
    return [ordered]@{ fileName = $Path; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant() }
}

function Open-LuiArchive([string] $Path) {
    if ((Get-Item -LiteralPath $Path).Length -gt 512MB) { throw 'Archive exceeds the encoded size limit.' }
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        [long]$total = 0
        if ($zip.Entries.Count -gt 10000) { throw 'Archive exceeds the entry limit.' }
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.TrimEnd('/')
            Assert-LuiRelativePath $name
            if (!$names.Add($name)) { throw "Duplicate archive path: $name" }
            $kind = ($entry.ExternalAttributes -shr 16) -band 0xf000
            if ($kind -notin @(0, 0x4000, 0x8000)) { throw "Unsupported archive entry: $name" }
            if ($entry.FullName.EndsWith('/')) { continue }
            if ($kind -eq 0x4000) { throw "Invalid archive directory: $name" }
            $total += $entry.Length
            if ($entry.Length -gt 512MB -or $total -gt 1GB) { throw 'Archive exceeds the expanded size limit.' }
            $entries.Add($entry.FullName, $entry)
        }
        foreach ($name in $entries.Keys) {
            $parent = $name
            while (($slash = $parent.LastIndexOf('/')) -ge 0) {
                $parent = $parent.Substring(0, $slash)
                if ($entries.ContainsKey($parent)) { throw "Archive file is also a parent directory: $parent" }
            }
        }
        return @{ Zip = $zip; Entries = $entries }
    }
    catch { $zip.Dispose(); throw }
}

function Read-LuiArchiveBytes($Archive, [string] $Name) {
    if (!$Archive.Entries.ContainsKey($Name)) { throw "Missing archive entry: $Name" }
    $entry = $Archive.Entries[$Name]
    $stream = $entry.Open()
    $buffer = [byte[]]::new([int]$entry.Length)
    try {
        $stream.ReadExactly($buffer, 0, $buffer.Length)
        if ($stream.ReadByte() -ne -1) { throw "Invalid expanded length: $Name" }
        return ,$buffer
    }
    finally { $stream.Dispose() }
}

function Read-LuiArchiveJson($Archive, [string] $Name) {
    [Text.Encoding]::UTF8.GetString((Read-LuiArchiveBytes $Archive $Name)) | ConvertFrom-Json -AsHashtable
}

function Read-LuiArchiveXml($Archive, [string] $Name) {
    $text = [IO.StringReader]::new([Text.Encoding]::UTF8.GetString((Read-LuiArchiveBytes $Archive $Name)))
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($text, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    }
    finally { $reader.Dispose(); $text.Dispose() }
}

function Assert-LuiSame($Actual, $Expected, [string] $Context) {
    if ((ConvertTo-LuiCanonicalJson $Actual) -cne (ConvertTo-LuiCanonicalJson $Expected)) { throw "Mismatch: $Context" }
}

function Get-LuiAssemblyVersion([byte[]] $Bytes) {
    $stream = [IO.MemoryStream]::new($Bytes, $false)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($handle in $metadata.GetAssemblyDefinition().GetCustomAttributes()) {
            $attribute = $metadata.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $member = $metadata.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $type = $metadata.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
            if ($metadata.GetString($type.Namespace) -ceq 'System.Reflection' -and $metadata.GetString($type.Name) -ceq 'AssemblyInformationalVersionAttribute') {
                $blob = $metadata.GetBlobReader($attribute.Value)
                if ($blob.ReadUInt16() -ne 1) { throw 'Invalid assembly version attribute.' }
                return $blob.ReadSerializedString()
            }
        }
        throw 'Assembly has no informational version.'
    }
    finally { $pe.Dispose(); $stream.Dispose() }
}

function Assert-LuiDependencyFiles($Archive, [string] $DepsPath) {
    $deps = Read-LuiArchiveJson $Archive $DepsPath
    $target = $deps['targets'][$deps['runtimeTarget']['name']]
    if (!$target) { throw "Missing dependency target: $DepsPath" }
    $prefix = if ($DepsPath.Contains('/')) { $DepsPath.Substring(0, $DepsPath.LastIndexOf('/') + 1) } else { '' }
    foreach ($library in $target.Values) {
        # Satellite resources are optional: Roslyn's shipped BuildHost deps declares
        # translations absent from its NuGet payload. The runtime falls back to the
        # neutral assembly; the inventory still hashes every resource actually shipped.
        foreach ($section in @('runtime', 'native', 'runtimeTargets')) {
            if (!$library.ContainsKey($section)) { continue }
            foreach ($entry in $library[$section].GetEnumerator()) {
                if ($entry.Key.EndsWith('/_._')) { continue }
                $name = if ($section -eq 'runtimeTargets') { $entry.Key }
                    else { [IO.Path]::GetFileName($entry.Key) }
                if (!$Archive.Entries.ContainsKey($prefix + $name)) { throw "Missing server dependency: $prefix$name" }
            }
        }
    }
}

function Get-LuiServerArchive([string] $Path) {
    $archive = Open-LuiArchive $Path
    try {
        $policy = Get-LuiReleasePolicy
        $identity = Read-LuiArchiveJson $archive 'lucent-server.json'
        $manifest = Read-LuiArchiveJson $archive 'lucent-server-files.json'
        if ($identity['schemaVersion'] -ne 1 -or $manifest['schemaVersion'] -ne 1) { throw 'Unsupported server identity or inventory schema.' }
        Assert-LuiSame $identity['language'] $policy['language'] 'server language'
        Assert-LuiSame $identity['protocol'] $policy['protocol'] 'server protocol'
        $listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $manifest['files']) {
            $name = [string]$file['fileName']
            Assert-LuiRelativePath $name
            if ($name -eq 'lucent-server-files.json' -or !$listed.Add($name)) { throw 'Invalid server inventory path.' }
            $bytes = Read-LuiArchiveBytes $archive $name
            if ($file['bytes'] -ne $bytes.Length -or $file['sha256'] -cne (Get-LuiBytesHash $bytes)) { throw "Server inventory mismatch: $name" }
        }
        if ($listed.Count -ne $archive.Entries.Count - 1) { throw 'Server inventory is incomplete.' }
        foreach ($name in @($policy['serverFiles']) + @($policy['serverNotices'])) {
            if (!$listed.Contains($name)) { throw "Missing server deployment file: $name" }
        }
        foreach ($pair in @(@('server', 'Lucent.Lui.LanguageServer.dll'), @('compiler', 'Lucent.Lui.Compiler.dll'))) {
            $bytes = Read-LuiArchiveBytes $archive $pair[1]
            if ($identity[$pair[0]]['sha256'] -cne (Get-LuiBytesHash $bytes) -or $identity[$pair[0]]['informationalVersion'] -cne (Get-LuiAssemblyVersion $bytes)) { throw "Server assembly identity mismatch: $($pair[0])" }
            if ($identity[$pair[0]]['informationalVersion'] -notmatch ('\+' + [regex]::Escape($identity['sourceCommit']) + '(\.|$)')) { throw 'Server assembly source identity mismatch.' }
        }
        $runtime = Read-LuiArchiveJson $archive 'Lucent.Lui.LanguageServer.runtimeconfig.json'
        Assert-LuiSame $identity['runtime'] $runtime['runtimeOptions'] 'server runtime configuration'
        if ($identity['runtime']['tfm'] -cne 'net10.0' -or $identity['runtime']['framework']['name'] -cne 'Microsoft.NETCore.App') { throw 'Unsupported server runtime.' }
        Assert-LuiDependencyFiles $archive 'Lucent.Lui.LanguageServer.deps.json'
        Assert-LuiDependencyFiles $archive 'BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.deps.json'
        return $identity
    }
    finally { $archive.Zip.Dispose() }
}

function Get-LuiVsix([string] $Path) {
    $archive = Open-LuiArchive $Path
    try {
        $manifest = Read-LuiArchiveJson $archive 'extension/package.json'
        $vsix = Read-LuiArchiveXml $archive 'extension.vsixmanifest'
        $identity = $vsix.SelectSingleNode('//*[local-name()="Identity"]')
        $id = $manifest['publisher'] + '.' + $manifest['name']
        if (!$identity -or $identity.GetAttribute('Id') -cne $manifest['name'] -or $identity.GetAttribute('Publisher') -cne $manifest['publisher'] -or $identity.GetAttribute('Version') -cne $manifest['version']) { throw 'VSIX identity mismatch.' }
        $policy = Get-LuiReleasePolicy
        if ($id -cne $policy['distribution']['extensionId']) { throw 'Unsupported extension publisher or ID.' }
        $compatibility = $manifest['lucentRelease']
        if (!$compatibility -or $compatibility['schemaVersion'] -ne 1 -or $compatibility['serverDelivery'] -cne 'external-path') { throw 'Unsupported extension delivery policy.' }
        $protocol = $policy['protocol']
        $version = @{ major = $protocol['major']; minor = $protocol['minor'] }
        Assert-LuiSame $compatibility['protocol'] @{ id = $protocol['id']; minimum = $version; maximumInclusive = $version } 'extension protocol compatibility'
        $declared = @(@{ path = $manifest['main']; json = $false })
        foreach ($language in $manifest['contributes']['languages']) {
            $declared += @{ path = $language['configuration']; json = $true }
        }
        foreach ($grammar in $manifest['contributes']['grammars']) {
            $declared += @{ path = $grammar['path']; json = $true }
        }
        foreach ($file in $declared) {
            $relative = [string]$file['path']
            if ($relative.StartsWith('./')) { $relative = $relative.Substring(2) }
            Assert-LuiRelativePath $relative
            $entry = 'extension/' + $relative
            if (!$archive.Entries.ContainsKey($entry)) { throw "Missing declared VSIX entry: $entry" }
            if ($file['json']) { $null = Read-LuiArchiveJson $archive $entry }
        }
        if (@($archive.Entries.Keys | Where-Object { $_ -match '(^|/)Lucent\.Lui\.LanguageServer\.dll$' }).Count) { throw 'External-path extension contains an undeclared bundled server.' }
        if (!$archive.Entries.ContainsKey('extension/LICENSE.txt')) { throw 'VSIX omitted its license.' }
        return [ordered]@{ id = $id; version = $manifest['version']; vscodeEngine = $manifest['engines']['vscode']; protocol = $compatibility['protocol']; serverDelivery = 'external-path'; bundledServer = $null }
    }
    finally { $archive.Zip.Dispose() }
}

function Get-LuiPackageInventory([string] $Directory, [string] $Version, [string] $SourceCommit, [string] $CompilerHash) {
    $packages = @(& (Join-Path $PSScriptRoot 'Get-PackageSet.ps1') -Directory $Directory -Version $Version)
    $result = @()
    foreach ($file in ($packages | Sort-Object Name -CaseSensitive)) {
        $archive = Open-LuiArchive $file.FullName
        try {
            $id = $file.Name.Substring(0, $file.Name.Length - $Version.Length - 7)
            $xml = Read-LuiArchiveXml $archive "$id.nuspec"
            $metadata = $xml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
            if (!$metadata) { throw 'Package has no nuspec metadata.' }
            $actualId = $metadata.SelectSingleNode('*[local-name()="id"]').InnerText
            $actualVersion = $metadata.SelectSingleNode('*[local-name()="version"]').InnerText
            $repository = $metadata.SelectSingleNode('*[local-name()="repository"]')
            if ($actualId -cne $id -or $actualVersion -cne $Version -or !$repository -or $repository.GetAttribute('commit') -cne $SourceCommit) { throw "Package identity mismatch: $id" }
            foreach ($dependency in $metadata.SelectNodes('.//*[local-name()="dependency"]')) {
                if ($dependency.GetAttribute('id').StartsWith('Lucent.', [StringComparison]::Ordinal) -and $dependency.GetAttribute('version') -cnotin @($Version, "[$Version]", "[$Version, )")) { throw "Mixed Lucent package dependency: $id" }
            }
            if ($id -eq 'Lucent.Lui.Sdk') {
                $copies = @($archive.Entries.Keys | Where-Object { $_ -match '(^|/)Lucent\.Lui\.Compiler\.dll$' })
                if ($copies.Count -eq 0 -or !$archive.Entries.ContainsKey('analyzers/dotnet/cs/Lucent.Lui.Compiler.dll')) { throw 'SDK omitted its compiler.' }
                foreach ($copy in $copies) {
                    if ((Get-LuiBytesHash (Read-LuiArchiveBytes $archive $copy)) -cne $CompilerHash) { throw "SDK/server compiler mismatch: $copy" }
                }
            }
            $result += [ordered]@{ id = $id; version = $Version; repositoryCommit = $SourceCommit; artifact = Get-LuiArtifact $Directory $file.Name }
        }
        finally { $archive.Zip.Dispose() }
    }
    return ,$result
}

function Get-LuiInputHash($Descriptor) {
    $inputs = [ordered]@{}
    foreach ($key in @('releaseSet', 'sdk', 'language', 'protocol', 'server', 'extension', 'packages', 'targets')) { $inputs[$key] = $Descriptor[$key] }
    Get-LuiBytesHash ([Text.Encoding]::UTF8.GetBytes((ConvertTo-LuiCanonicalJson $inputs)))
}

function Assert-LuiReleaseSet($Descriptor, [string] $Directory) {
    $json = ConvertTo-LuiCanonicalJson $Descriptor
    if (!(Test-Json -Json $json -SchemaFile (Join-Path $PSScriptRoot 'release-set.schema.json'))) { throw 'Invalid release descriptor schema.' }
    $policy = Get-LuiReleasePolicy
    Assert-LuiSame $Descriptor['distribution']['policy'] $policy['distribution'] 'distribution policy'
    Assert-LuiSame $Descriptor['language'] $policy['language'] 'language policy'
    Assert-LuiSame $Descriptor['protocol'] $policy['protocol'] 'protocol policy'
    Assert-LuiSame $Descriptor['targets'] $policy['supportedRids'] 'supported targets'
    $actualSdk = (Get-Content (Join-Path $PSScriptRoot '../global.json') -Raw | ConvertFrom-Json -AsHashtable)['sdk']
    Assert-LuiSame $Descriptor['sdk'] $actualSdk 'SDK selection policy'
    $server = $Descriptor['server']
    $serverFile = $server['artifact']['fileName']
    Assert-LuiSame $server['artifact'] (Get-LuiArtifact $Directory $serverFile) 'server archive bytes'
    $actualServer = Get-LuiServerArchive (Resolve-LuiArtifactPath $Directory $serverFile)
    Assert-LuiSame $server['identity'] $actualServer 'server identity'
    if ($actualServer['sourceCommit'] -cne $Descriptor['releaseSet']['sourceCommit']) { throw 'Mixed server/package source commits.' }
    $extension = $Descriptor['extension']
    $vsixFile = $extension['artifact']['fileName']
    Assert-LuiSame $extension['artifact'] (Get-LuiArtifact $Directory $vsixFile) 'VSIX bytes'
    Assert-LuiSame $extension['identity'] (Get-LuiVsix (Resolve-LuiArtifactPath $Directory $vsixFile)) 'VSIX identity'
    $packages = Get-LuiPackageInventory $Directory $Descriptor['releaseSet']['version'] $Descriptor['releaseSet']['sourceCommit'] $actualServer['compiler']['sha256']
    Assert-LuiSame $Descriptor['packages'] $packages 'package set'
    if ($Descriptor['inputManifestSha256'] -cne (Get-LuiInputHash $Descriptor)) { throw 'Release input identity mismatch.' }
    if ($Descriptor['status'] -eq 'complete') {
        if ($Descriptor['releaseSet']['sourceState'] -cne 'clean' -or $Descriptor['distribution']['kind'] -cne 'authenticated-ci-artifact') { throw 'Only a clean authenticated CI artifact can be complete.' }
        if ($Descriptor['provenance']['sourceCommit'] -cne $Descriptor['releaseSet']['sourceCommit']) { throw 'CI provenance belongs to a different source commit.' }
        $required = @('managed', 'native', 'packages', 'server', 'extension')
        Assert-LuiSame @($Descriptor['evidence'] | ForEach-Object { $_['kind'] } | Sort-Object -CaseSensitive) @($required | Sort-Object -CaseSensitive) 'complete-set evidence kinds'
        foreach ($evidence in $Descriptor['evidence']) {
            if ($evidence['sourceCommit'] -cne $Descriptor['releaseSet']['sourceCommit'] -or $evidence['inputManifestSha256'] -cne $Descriptor['inputManifestSha256']) { throw 'Evidence belongs to different release inputs.' }
            Assert-LuiSame $evidence['artifact'] (Get-LuiArtifact $Directory $evidence['artifact']['fileName']) 'evidence bytes'
            $receipt = Get-Content -LiteralPath (Resolve-LuiArtifactPath $Directory $evidence['artifact']['fileName']) -Raw | ConvertFrom-Json -AsHashtable
            foreach ($key in @('kind', 'sourceCommit', 'inputManifestSha256', 'status')) { Assert-LuiSame $receipt[$key] $evidence[$key] "evidence $key" }
            Assert-LuiSame $receipt['provenance'] $Descriptor['provenance'] 'evidence provenance'
        }
    }
    elseif ($Descriptor['evidence'].Count -ne 0) { throw 'Candidate descriptor cannot claim complete-set evidence.' }
}

function Write-LuiImmutableJson([string] $Path, $Value) {
    $full = [IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $full) { throw "Immutable output already exists: $full" }
    $parent = [IO.Path]::GetDirectoryName($full)
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $temporary = Join-Path $parent ('.lucent-release-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, (ConvertTo-LuiCanonicalJson $Value) + "`n", [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $full, $false)
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}

function New-LuiReleaseSet {
    param([string] $Directory, [string] $Version, [string] $SourceCommit, [string] $SourceState,
        [string] $ServerArchive, [string] $Vsix, [string] $OutputPath, [hashtable] $Completion)
    $policy = Get-LuiReleasePolicy
    $identity = Get-LuiServerArchive (Resolve-LuiArtifactPath $Directory $ServerArchive)
    $extension = Get-LuiVsix (Resolve-LuiArtifactPath $Directory $Vsix)
    $descriptor = [ordered]@{
        schemaVersion = 1
        releaseSet = [ordered]@{ version = $Version; sourceCommit = $SourceCommit; sourceState = $SourceState }
        status = 'candidate'
        distribution = [ordered]@{ kind = 'local-candidate'; policy = $policy['distribution'] }
        sdk = (Get-Content (Join-Path $PSScriptRoot '../global.json') -Raw | ConvertFrom-Json -AsHashtable)['sdk']
        language = $policy['language']; protocol = $policy['protocol']; targets = $policy['supportedRids']
        server = [ordered]@{ identity = $identity; rid = 'win-x64'; artifact = Get-LuiArtifact $Directory $ServerArchive }
        extension = [ordered]@{ identity = $extension; artifact = Get-LuiArtifact $Directory $Vsix }
        packages = Get-LuiPackageInventory $Directory $Version $SourceCommit $identity['compiler']['sha256']
        inputManifestSha256 = ''; evidence = @(); provenance = $null
    }
    $descriptor['inputManifestSha256'] = Get-LuiInputHash $descriptor
    if ($Completion) {
        $descriptor['status'] = 'complete'
        $descriptor['distribution']['kind'] = 'authenticated-ci-artifact'
        $descriptor['evidence'] = $Completion['evidence']
        $descriptor['provenance'] = $Completion['provenance']
    }
    Assert-LuiReleaseSet $descriptor $Directory
    Write-LuiImmutableJson $OutputPath $descriptor
    return $descriptor
}

Export-ModuleMember -Function Get-LuiReleasePolicy, ConvertTo-LuiCanonicalJson, Get-LuiBytesHash, Assert-LuiRelativePath, Resolve-LuiArtifactPath, Get-LuiArtifact, Open-LuiArchive, Read-LuiArchiveBytes, Read-LuiArchiveJson, Get-LuiServerArchive, Get-LuiVsix, Get-LuiPackageInventory, Assert-LuiReleaseSet, Write-LuiImmutableJson, New-LuiReleaseSet
