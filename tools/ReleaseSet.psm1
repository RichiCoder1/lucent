#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-LuiReleasePolicy {
    Get-Content -LiteralPath (Join-Path $PSScriptRoot '../eng/lui-release-policy.json') -Raw | ConvertFrom-Json -AsHashtable
}

function Get-LuiNuGetDoctorNoticeNames {
    $prefix = 'tools/net10.0/any/nuget/'
    $required = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-notices.json') -Raw | ConvertFrom-Json | Where-Object package -eq 'Lucent.Tools')
    if ($required.Count -eq 0) { throw 'NuGet doctor notice policy is missing.' }
    foreach ($notice in $required) {
        if (!$notice.entry.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Invalid NuGet doctor notice policy path.' }
        $name = $notice.entry.Substring($prefix.Length)
        $name
    }
}

function Assert-LuiNuGetDoctorNotices([string[]] $Names) {
    foreach ($name in Get-LuiNuGetDoctorNoticeNames) {
        if ($name -cnotin $Names) { throw "Required NuGet doctor notice is missing: $name" }
    }
}

function Assert-LuiNuGetDoctorPayload([string[]] $Names, $Dependencies) {
    Assert-LuiNuGetDoctorNotices $Names
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in @('Lucent.Tools.NuGet.dll', 'Lucent.Tools.NuGet.deps.json', 'Lucent.Tools.NuGet.runtimeconfig.json', 'notices/provenance.json') + @(Get-LuiNuGetDoctorNoticeNames)) { $null = $expected.Add($name) }
    $target = $Dependencies['targets'][$Dependencies['runtimeTarget']['name']]
    if (!$target) { throw 'NuGet doctor dependency target is missing.' }
    foreach ($library in $target.Values) {
        foreach ($section in @('runtime', 'native', 'runtimeTargets', 'resources')) {
            if (!$library.ContainsKey($section)) { continue }
            foreach ($entry in $library[$section].GetEnumerator()) {
                if ($entry.Key.EndsWith('/_._')) { continue }
                $name = if ($section -eq 'runtimeTargets') { $entry.Key }
                    elseif ($section -eq 'resources') { $entry.Value['locale'] + '/' + [IO.Path]::GetFileName($entry.Key) }
                    else { [IO.Path]::GetFileName($entry.Key) }
                Assert-LuiRelativePath $name
                $null = $expected.Add($name)
            }
        }
    }
    Assert-LuiSame @($Names | Sort-Object -CaseSensitive) @($expected | Sort-Object -CaseSensitive) 'NuGet doctor payload declared runtime and notice closure'
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
    $stream = [IO.MemoryStream]::new((Read-LuiArchiveBytes $Archive $Name), $false)
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($stream, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    }
    finally { $reader.Dispose(); $stream.Dispose() }
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

function Get-LuiServerContent($archive, [string] $prefix) {
        $policy = Get-LuiReleasePolicy
        $identity = Read-LuiArchiveJson $archive ($prefix + 'lucent-server.json')
        $manifest = Read-LuiArchiveJson $archive ($prefix + 'lucent-server-files.json')
        if ($identity['schemaVersion'] -ne 1 -or $manifest['schemaVersion'] -ne 1) { throw 'Unsupported server identity or inventory schema.' }
        Assert-LuiSame $identity['language'] $policy['language'] 'server language'
        Assert-LuiSame $identity['protocol'] $policy['protocol'] 'server protocol'
        $listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $manifest['files']) {
            $name = [string]$file['fileName']
            Assert-LuiRelativePath $name
            if ($name -eq 'lucent-server-files.json' -or !$listed.Add($name)) { throw 'Invalid server inventory path.' }
            $bytes = Read-LuiArchiveBytes $archive ($prefix + $name)
            if ($file['bytes'] -ne $bytes.Length -or $file['sha256'] -cne (Get-LuiBytesHash $bytes)) { throw "Server inventory mismatch: $name" }
        }
        $serverEntries = @($archive.Entries.Keys | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
        if ($listed.Count -ne $serverEntries.Count - 1) { throw 'Server inventory is incomplete.' }
        foreach ($name in @($policy['serverFiles']) + @($policy['serverNotices'])) {
            if (!$listed.Contains($name)) { throw "Missing server deployment file: $name" }
        }
        foreach ($pair in @(@('server', 'Lucent.Lui.LanguageServer.dll'), @('compiler', 'Lucent.Lui.Compiler.dll'))) {
            $bytes = Read-LuiArchiveBytes $archive ($prefix + $pair[1])
            if ($identity[$pair[0]]['sha256'] -cne (Get-LuiBytesHash $bytes) -or $identity[$pair[0]]['informationalVersion'] -cne (Get-LuiAssemblyVersion $bytes)) { throw "Server assembly identity mismatch: $($pair[0])" }
            if ($identity[$pair[0]]['informationalVersion'] -notmatch ('\+' + [regex]::Escape($identity['sourceCommit']) + '(\.|$)')) { throw 'Server assembly source identity mismatch.' }
        }
        $runtime = Read-LuiArchiveJson $archive ($prefix + 'Lucent.Lui.LanguageServer.runtimeconfig.json')
        Assert-LuiSame $identity['runtime'] $runtime['runtimeOptions'] 'server runtime configuration'
        if ($identity['runtime']['tfm'] -cne 'net10.0' -or $identity['runtime']['framework']['name'] -cne 'Microsoft.NETCore.App') { throw 'Unsupported server runtime.' }
        Assert-LuiDependencyFiles $archive ($prefix + 'Lucent.Lui.LanguageServer.deps.json')
        Assert-LuiDependencyFiles $archive ($prefix + 'BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.deps.json')
        return [ordered]@{
            identity = $identity
            filesSha256 = Get-LuiBytesHash ([Text.Encoding]::UTF8.GetBytes((ConvertTo-LuiCanonicalJson $manifest)))
        }
}

function Get-LuiServerBundle([string] $Path) {
    $archive = Open-LuiArchive $Path
    try { return Get-LuiServerContent $archive '' }
    finally { $archive.Zip.Dispose() }
}

function Get-LuiServerArchive([string] $Path) { (Get-LuiServerBundle $Path).identity }

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
        if (!$compatibility -or $compatibility['schemaVersion'] -ne 1 -or $compatibility['serverDelivery'] -cnotin @('external-path', 'bundled')) { throw 'Unsupported extension delivery policy.' }
        $protocol = $policy['protocol']
        $version = @{ major = $protocol['major']; minor = $protocol['minor'] }
        Assert-LuiSame $compatibility['protocol'] @{ id = $protocol['id']; minimum = $version; maximumInclusive = $version } 'extension protocol compatibility'
        if ($compatibility['serverDelivery'] -ceq 'bundled') {
            Assert-LuiSame $compatibility['language'] $policy['language'] 'extension language compatibility'
            $bundle = Get-LuiServerContent $archive 'extension/server/'
            Assert-LuiSame $compatibility['bundledServer'] $bundle 'bundled server manifest'
            if ($compatibility['sourceCommit'] -cne $bundle['identity']['sourceCommit']) { throw 'Extension and bundled server source commits differ.' }
        }
        else { $bundle = $null }
        if ($manifest['files'] -contains 'server-cache.js' -and !$manifest['lucentCacheHelper']) { throw 'Extension cache module has no bundled helper.' }
        if ($manifest['lucentCacheHelper']) {
            $helper = $manifest['lucentCacheHelper']
            if ($helper['schemaVersion'] -ne 1 -or $helper['entryPoint'] -cne 'Lucent.Tooling.Cache.dll' -or $helper['sourceCommit'] -cne $compatibility['sourceCommit']) { throw 'Unsupported or mismatched cache helper identity.' }
            $helperNames = @('Lucent.Tooling.Cache.dll', 'Lucent.Tooling.Cache.deps.json', 'Lucent.Tooling.Cache.runtimeconfig.json')
            Assert-LuiSame @($helper['files'] | ForEach-Object { $_['fileName'] } | Sort-Object -CaseSensitive) @($helperNames | Sort-Object -CaseSensitive) 'cache helper inventory'
            $prefix = 'extension/tooling-cache/'
            Assert-LuiSame @($archive.Entries.Keys | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } | Sort-Object -CaseSensitive) @($helperNames | ForEach-Object { $prefix + $_ } | Sort-Object -CaseSensitive) 'cache helper payload'
            foreach ($file in $helper['files']) {
                $bytes = Read-LuiArchiveBytes $archive ($prefix + $file['fileName'])
                if ($bytes.Length -ne $file['bytes'] -or (Get-LuiBytesHash $bytes) -cne $file['sha256']) { throw 'Cache helper bytes differ from the packaged inventory.' }
            }
            $helperVersion = Get-LuiAssemblyVersion (Read-LuiArchiveBytes $archive ($prefix + 'Lucent.Tooling.Cache.dll'))
            if ($helperVersion -notmatch ('\+' + [regex]::Escape($helper['sourceCommit']) + '(\.|$)')) { throw 'Cache helper assembly source identity mismatch.' }
            $runtime = Read-LuiArchiveJson $archive ($prefix + 'Lucent.Tooling.Cache.runtimeconfig.json')
            if ($runtime['runtimeOptions']['tfm'] -cne 'net10.0' -or $runtime['runtimeOptions']['framework']['name'] -cne 'Microsoft.NETCore.App') { throw 'Unsupported cache helper runtime.' }
            Assert-LuiDependencyFiles $archive ($prefix + 'Lucent.Tooling.Cache.deps.json')
        }
        elseif (@($archive.Entries.Keys | Where-Object { $_.StartsWith('extension/tooling-cache/', [StringComparison]::OrdinalIgnoreCase) }).Count) { throw 'Extension contains an undeclared cache helper.' }
        if ($manifest['files'] -contains 'managed-tool.js' -and !$manifest['lucentDoctor']) { throw 'Extension managed tool module has no bundled doctor.' }
        if ($manifest['lucentDoctor']) {
            $doctor = $manifest['lucentDoctor']
            if ($doctor['schemaVersion'] -ne 1 -or $doctor['entryPoint'] -cne 'Lucent.Tools.dll' -or $doctor['sourceCommit'] -cne $compatibility['sourceCommit']) { throw 'Unsupported or mismatched doctor identity.' }
            $doctorNames = @('Lucent.Tools.dll', 'Lucent.Tools.deps.json', 'Lucent.Tools.runtimeconfig.json')
            Assert-LuiSame @($doctor['files'] | ForEach-Object { $_['fileName'] } | Sort-Object -CaseSensitive) @($doctorNames | Sort-Object -CaseSensitive) 'doctor inventory'
            $doctorPrefix = 'extension/doctor/'
            Assert-LuiSame @($archive.Entries.Keys | Where-Object { $_.StartsWith($doctorPrefix, [StringComparison]::OrdinalIgnoreCase) } | Sort-Object -CaseSensitive) @($doctorNames | ForEach-Object { $doctorPrefix + $_ } | Sort-Object -CaseSensitive) 'doctor payload'
            foreach ($file in $doctor['files']) {
                $bytes = Read-LuiArchiveBytes $archive ($doctorPrefix + $file['fileName'])
                if ($bytes.Length -ne $file['bytes'] -or (Get-LuiBytesHash $bytes) -cne $file['sha256']) { throw 'Doctor bytes differ from the packaged inventory.' }
            }
            $doctorVersion = Get-LuiAssemblyVersion (Read-LuiArchiveBytes $archive ($doctorPrefix + 'Lucent.Tools.dll'))
            if ($doctorVersion -notmatch ('\+' + [regex]::Escape($doctor['sourceCommit']) + '(\.|$)')) { throw 'Doctor assembly source identity mismatch.' }
            $doctorRuntime = Read-LuiArchiveJson $archive ($doctorPrefix + 'Lucent.Tools.runtimeconfig.json')
            if ($doctorRuntime['runtimeOptions']['tfm'] -cne 'net10.0' -or $doctorRuntime['runtimeOptions']['framework']['name'] -cne 'Microsoft.NETCore.App') { throw 'Unsupported doctor runtime.' }
            Assert-LuiDependencyFiles $archive ($doctorPrefix + 'Lucent.Tools.deps.json')
        }
        elseif (@($archive.Entries.Keys | Where-Object { $_.StartsWith('extension/doctor/', [StringComparison]::OrdinalIgnoreCase) }).Count) { throw 'Extension contains an undeclared doctor.' }
        if ($manifest['files'] -contains 'nuget-doctor/**' -and !$manifest['lucentNuGetDoctor']) { throw 'Extension has no bundled NuGet doctor.' }
        if ($manifest['lucentNuGetDoctor']) {
            $nugetDoctor = $manifest['lucentNuGetDoctor']
            if ($nugetDoctor['schemaVersion'] -ne 1 -or $nugetDoctor['entryPoint'] -cne 'Lucent.Tools.NuGet.dll' -or $nugetDoctor['sourceCommit'] -cne $compatibility['sourceCommit']) { throw 'Unsupported or mismatched NuGet doctor identity.' }
            $prefix = 'extension/nuget-doctor/'
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            $total = 0L
            foreach ($file in $nugetDoctor['files']) {
                $name = [string]$file['fileName']
                Assert-LuiRelativePath $name
                if (!$names.Add($name)) { throw 'Duplicate NuGet doctor inventory path.' }
                $bytes = Read-LuiArchiveBytes $archive ($prefix + $name)
                $total += $bytes.Length
                if ($bytes.Length -le 0 -or $file['bytes'] -ne $bytes.Length -or $file['sha256'] -cne (Get-LuiBytesHash $bytes)) { throw 'NuGet doctor bytes differ from the packaged inventory.' }
            }
            if ($names.Count -gt 128 -or $total -gt 128MB) { throw 'NuGet doctor payload exceeds its bound.' }
            foreach ($required in @('Lucent.Tools.NuGet.dll', 'Lucent.Tools.NuGet.deps.json', 'Lucent.Tools.NuGet.runtimeconfig.json')) {
                if (!$names.Contains($required)) { throw "NuGet doctor inventory omitted $required" }
            }
            Assert-LuiNuGetDoctorPayload @($names) (Read-LuiArchiveJson $archive ($prefix + 'Lucent.Tools.NuGet.deps.json'))
            Assert-LuiSame @($archive.Entries.Keys | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } | Sort-Object -CaseSensitive) @($names | ForEach-Object { $prefix + $_ } | Sort-Object -CaseSensitive) 'NuGet doctor payload'
            $version = Get-LuiAssemblyVersion (Read-LuiArchiveBytes $archive ($prefix + 'Lucent.Tools.NuGet.dll'))
            if ($version -notmatch ('\+' + [regex]::Escape($nugetDoctor['sourceCommit']) + '(\.|$)')) { throw 'NuGet doctor assembly source identity mismatch.' }
            $runtime = Read-LuiArchiveJson $archive ($prefix + 'Lucent.Tools.NuGet.runtimeconfig.json')
            if ($runtime['runtimeOptions']['tfm'] -cne 'net10.0' -or $runtime['runtimeOptions']['framework']['name'] -cne 'Microsoft.NETCore.App') { throw 'Unsupported NuGet doctor runtime.' }
            Assert-LuiDependencyFiles $archive ($prefix + 'Lucent.Tools.NuGet.deps.json')
        }
        elseif (@($archive.Entries.Keys | Where-Object { $_.StartsWith('extension/nuget-doctor/', [StringComparison]::OrdinalIgnoreCase) }).Count) { throw 'Extension contains an undeclared NuGet doctor.' }
        $declared = @(@{ path = $manifest['main']; json = $false })
        if ($null -ne $bundle) { $declared += @{ path = './server-bundle.js'; json = $false } }
        foreach ($file in @('project-requirements.js', 'server-cache.js', 'server-acquisition.js', 'managed-tool.js', 'doctor-client.js', 'onboarding-ui.js', 'environment-ui.js', 'preview-coordinator.js', 'preview-protocol.js', 'preview-process.js', 'preview-runtime.js', 'preview-ui.js', 'preview-panel.js', 'preview-diagnostics.js', 'release-catalog.json')) {
            if ($manifest['files'] -contains $file) { $declared += @{ path = $file; json = $file.EndsWith('.json') } }
        }
        if ($manifest['lucentDoctor']) {
            foreach ($file in @('managed-tool.js', 'doctor-client.js', 'onboarding-ui.js', 'environment-ui.js', 'onboarding/setup.md', 'onboarding/environment.md')) {
                $declared += @{ path = $file; json = $false }
            }
        }
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
        if ($null -eq $bundle -and @($archive.Entries.Keys | Where-Object { $_.StartsWith('extension/server/', [StringComparison]::OrdinalIgnoreCase) }).Count) { throw 'External-path extension contains an undeclared bundled server.' }
        if (!$archive.Entries.ContainsKey('extension/LICENSE.txt')) { throw 'VSIX omitted its license.' }
        return [ordered]@{ id = $id; version = $manifest['version']; vscodeEngine = $manifest['engines']['vscode']; protocol = $compatibility['protocol']; serverDelivery = $compatibility['serverDelivery']; bundledServer = $bundle }
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
    if ($extension['identity']['serverDelivery'] -ceq 'bundled') {
        Assert-LuiSame $extension['identity']['bundledServer'] (Get-LuiServerBundle (Resolve-LuiArtifactPath $Directory $serverFile)) 'bundled/standalone server bytes'
    }
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
    if ($extension['serverDelivery'] -ceq 'bundled') {
        Assert-LuiSame $extension['bundledServer'] (Get-LuiServerBundle (Resolve-LuiArtifactPath $Directory $ServerArchive)) 'bundled/standalone server bytes'
    }
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

Export-ModuleMember -Function Get-LuiReleasePolicy, Assert-LuiNuGetDoctorPayload, ConvertTo-LuiCanonicalJson, Get-LuiBytesHash, Get-LuiAssemblyVersion, Assert-LuiRelativePath, Resolve-LuiArtifactPath, Get-LuiArtifact, Open-LuiArchive, Read-LuiArchiveBytes, Read-LuiArchiveJson, Get-LuiServerArchive, Get-LuiServerBundle, Get-LuiVsix, Get-LuiPackageInventory, Assert-LuiReleaseSet, Write-LuiImmutableJson, New-LuiReleaseSet
