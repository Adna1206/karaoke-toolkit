param(
    [Parameter(Mandatory=$true)][string]$RuntimeIdentifier,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$manifestPath = Join-Path $repo 'vendor/ffmpeg/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$platform = $manifest.platforms.PSObject.Properties[$RuntimeIdentifier]
if (!$platform) {
    throw "No vendored FFmpeg build is recorded for '$RuntimeIdentifier'. Add a reviewed archive under vendor/ffmpeg/$RuntimeIdentifier and record its checksum in vendor/ffmpeg/manifest.json."
}
$platform = $platform.Value
$archive = Join-Path (Join-Path $repo ('vendor/ffmpeg/' + $RuntimeIdentifier)) $platform.archive
if (!(Test-Path -LiteralPath $archive -PathType Leaf)) {
    throw "Vendored FFmpeg archive is missing: $archive. Build preparation never downloads it."
}
if ((Get-Sha256 $archive) -ne $platform.sha256) { throw "Vendored FFmpeg archive checksum mismatch: $archive" }
$paired = [bool]$platform.probeArchive
$verifiedHash = $platform.sha256
if ($paired) {
    $probeArchive = Join-Path (Join-Path $repo ('vendor/ffmpeg/' + $RuntimeIdentifier)) $platform.probeArchive
    if (!(Test-Path -LiteralPath $probeArchive -PathType Leaf)) { throw "Vendored FFprobe archive is missing: $probeArchive" }
    if ((Get-Sha256 $probeArchive) -ne $platform.probeSha256) { throw "Vendored FFprobe archive checksum mismatch: $probeArchive" }
    $verifiedHash += ':' + $platform.probeSha256
}

$cache = Join-Path $repo ('artifacts/runtime/ffmpeg/' + $platform.version + '/' + $RuntimeIdentifier)
$mutex = New-Object System.Threading.Mutex($false, ('KaraokeToolkit-FFmpeg-' + $RuntimeIdentifier))
$locked = $false
try {
    try { $locked = $mutex.WaitOne([TimeSpan]::FromMinutes(30)) } catch [System.Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { throw 'Timed out waiting for FFmpeg preparation.' }
    $marker = Join-Path $cache 'verified.sha256'
    $distributionFile = Join-Path $cache 'distribution.txt'
    $cached = (Test-Path -LiteralPath $marker) -and (Test-Path -LiteralPath $distributionFile) -and
        (Get-Content -LiteralPath $marker -Raw).Trim() -eq $verifiedHash
    if ($cached) {
        $distribution = (Get-Content -LiteralPath $distributionFile -Raw).Trim()
        $cached = Test-Path -LiteralPath $distribution -PathType Container
    }
    if (!$cached) {
        New-Item -ItemType Directory -Force -Path $cache | Out-Null
        $unpacked = Join-Path $cache ('unpacked-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $unpacked | Out-Null
        if ($paired) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $distribution = Join-Path $unpacked 'distribution'
            $bin = Join-Path $distribution 'bin'
            New-Item -ItemType Directory -Path $bin | Out-Null
            foreach ($pair in @(@($archive, 'ffmpeg'), @($probeArchive, 'ffprobe'))) {
                $zip = [IO.Compression.ZipFile]::OpenRead($pair[0])
                try {
                    $entry = $zip.GetEntry($pair[1])
                    if (!$entry) { throw "Vendored archive is missing $($pair[1]): $($pair[0])" }
                    $inputStream = $entry.Open()
                    $outputStream = [IO.File]::Create((Join-Path $bin $pair[1]))
                    try { $inputStream.CopyTo($outputStream) }
                    finally { $inputStream.Dispose(); $outputStream.Dispose() }
                } finally { $zip.Dispose() }
            }
            if ($platform.binarySha256 -and (Get-Sha256 (Join-Path $bin 'ffmpeg')) -ne $platform.binarySha256) {
                throw 'Vendored FFmpeg binary checksum mismatch.'
            }
            if ($platform.probeBinarySha256 -and (Get-Sha256 (Join-Path $bin 'ffprobe')) -ne $platform.probeBinarySha256) {
                throw 'Vendored FFprobe binary checksum mismatch.'
            }
            $notice = Join-Path (Join-Path $repo ('vendor/ffmpeg/' + $RuntimeIdentifier)) 'LICENSE.txt'
            if (!(Test-Path -LiteralPath $notice -PathType Leaf)) { throw "Vendored license notice is missing: $notice" }
            Copy-Item -LiteralPath $notice -Destination $distribution
            $gpl = Join-Path (Join-Path $repo ('vendor/ffmpeg/' + $RuntimeIdentifier)) $platform.licenseText
            if (!(Test-Path -LiteralPath $gpl -PathType Leaf)) { throw "Vendored GPL license text is missing: $gpl" }
            Copy-Item -LiteralPath $gpl -Destination $distribution
        } elseif ($archive.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [IO.Compression.ZipFile]::ExtractToDirectory($archive, $unpacked)
        } elseif ($archive.EndsWith('.tar.xz', [StringComparison]::OrdinalIgnoreCase) -or
                  $archive.EndsWith('.tar.gz', [StringComparison]::OrdinalIgnoreCase)) {
            & tar -xf $archive -C $unpacked
            if ($LASTEXITCODE -ne 0) { throw 'FFmpeg archive extraction failed.' }
        } else { throw "Unsupported FFmpeg archive format: $archive" }
        if (!$paired) { $distribution = (Get-ChildItem -LiteralPath $unpacked -Directory | Select-Object -First 1).FullName }
        if (!$distribution) { throw 'FFmpeg archive has no distribution directory.' }
        Set-Content -LiteralPath $distributionFile -Value $distribution
        Set-Content -LiteralPath $marker -Value $verifiedHash
    }

    $suffix = if ($RuntimeIdentifier.StartsWith('win-')) { '.exe' } else { '' }
    foreach ($binary in @('ffmpeg', 'ffprobe')) {
        if (!(Test-Path -LiteralPath (Join-Path $distribution ('bin/' + $binary + $suffix)) -PathType Leaf)) {
            throw "Vendored $RuntimeIdentifier archive is missing bin/$binary$suffix."
        }
    }
    if (!(Test-Path -LiteralPath (Join-Path $distribution 'LICENSE.txt') -PathType Leaf)) {
        throw "Vendored $RuntimeIdentifier archive is missing LICENSE.txt."
    }

    $output = [IO.Path]::GetFullPath($OutputDirectory)
    $repoRoot = [IO.Path]::GetFullPath($repo).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $comparison = if ($env:OS -eq 'Windows_NT') { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (!$output.StartsWith($repoRoot, $comparison) -or (Split-Path $output -Leaf) -ne 'ffmpeg' -or
        !(Test-Path -LiteralPath (Split-Path $output -Parent) -PathType Container)) {
        throw "Refusing to replace an unexpected FFmpeg output directory: $output"
    }
    if (Test-Path -LiteralPath $output) {
        if (((Get-Item -LiteralPath $output).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to replace a linked FFmpeg output directory: $output"
        }
        if (!(Test-Path -LiteralPath (Join-Path $output 'runtime-manifest.json')) -and
            !(Test-Path -LiteralPath (Join-Path $output 'bin/ffmpeg.exe')) -and
            !(Test-Path -LiteralPath (Join-Path $output 'bin/ffmpeg'))) {
            throw "Refusing to replace an unidentified directory: $output"
        }
        Remove-Item -LiteralPath $output -Recurse -Force
    }
    New-Item -ItemType Directory -Path $output | Out-Null
    # Keep the distribution layout, shared libraries and upstream notices intact.
    Get-ChildItem -LiteralPath $distribution | Copy-Item -Destination $output -Recurse -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $output 'runtime-manifest.json')
    if (!$paired) {
        # LGPL v3 incorporates the GPL v3 terms; preserve both texts together.
        Copy-Item -LiteralPath (Join-Path $repo 'licenses/FFmpeg.GPLv3.txt') -Destination (Join-Path $output 'COPYING.GPLv3')
    }
    $licenseDirectory = Join-Path (Split-Path $output -Parent) 'licenses/ffmpeg'
    # Replace this generated component folder so renamed notices cannot linger.
    if (![IO.Path]::GetFullPath($licenseDirectory).StartsWith($repoRoot, $comparison)) { throw 'Unexpected FFmpeg notice directory.' }
    if ([IO.Path]::GetFullPath($licenseDirectory).StartsWith((Join-Path $repoRoot 'licenses') + [IO.Path]::DirectorySeparatorChar, $comparison)) { throw 'Refusing to replace repository source notices.' }
    if (Test-Path -LiteralPath $licenseDirectory) {
        if ((Get-Item -LiteralPath $licenseDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to replace linked FFmpeg notices.' }
        Remove-Item -LiteralPath $licenseDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
    # Preserve upstream files and expose a canonical copy for About and publishing.
    foreach ($notice in Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE)' }) {
        $relative = $notice.FullName.Substring($output.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1)
        $document = if ($notice.Name -match '^COPYING\.(.+)$') { $Matches[1] }
            elseif ($notice.Name -match '^LICENSE(?:\.(?:txt|md))?$') { 'Notices' }
            elseif ($notice.Name -match '^NOTICE(?:\.(?:txt|md))?$') { 'Attributions' }
            else { $notice.Name }
        $relativeFolder = Split-Path $relative -Parent
        $destination = Join-Path (Join-Path $licenseDirectory $relativeFolder) ('FFmpeg.' + $document + '.txt')
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        if ((Test-Path -LiteralPath $destination) -and (Get-Sha256 $destination) -ne (Get-Sha256 $notice.FullName)) {
            throw "FFmpeg notices have conflicting canonical names: $relative"
        }
        Copy-Item -LiteralPath $notice.FullName -Destination $destination -Force
    }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $licenseDirectory 'runtime-manifest.json') -Force
    if ($paired -and $env:OS -ne 'Windows_NT') {
        & chmod +x (Join-Path $output 'bin/ffmpeg') (Join-Path $output 'bin/ffprobe')
        if ($LASTEXITCODE -ne 0) { throw 'Could not set FFmpeg executable permissions.' }
    }
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
