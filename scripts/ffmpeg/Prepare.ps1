param(
    [Parameter(Mandatory=$true)][string]$RuntimeIdentifier,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'


$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$manifestPath = Join-Path $repo 'vendor/ffmpeg/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$platform = $manifest.platforms.PSObject.Properties[$RuntimeIdentifier]
if (!$platform) {
    throw "No pinned FFmpeg build is recorded for '$RuntimeIdentifier'."
}
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'Get-PinnedRuntimeArchive.ps1')
$platform = $platform.Value
$lockPath = Join-Path $repo ('vendor/ffmpeg/' + $platform.condaLock)
$condaLock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
if ($condaLock.runtimeIdentifier -cne $RuntimeIdentifier -or $condaLock.provider -cne 'conda-forge' -or
    $condaLock.version -cne $platform.version -or $condaLock.version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
    !$condaLock.packages.Count) {
    throw 'Invalid Conda runtime lock.'
}
$expander = Join-Path $PSScriptRoot 'Expand-CondaRuntime.ps1'
$verifiedHash = (Get-Sha256 $lockPath) + ':' + (Get-Sha256 $expander)
. $expander
$cacheVersion = 'conda-' + $condaLock.version
$cache = Join-Path $repo ('artifacts/runtime/ffmpeg/' + $cacheVersion + '/' + $RuntimeIdentifier)
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
        $distribution = Join-Path $unpacked 'distribution'
        Expand-CondaRuntime $repo $condaLock $distribution
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
    Copy-Item -LiteralPath $lockPath -Destination (Join-Path $output 'runtime-manifest.json')
    # LGPL v3 incorporates the GPL v3 terms.
    Copy-Item -LiteralPath (Join-Path $repo 'licenses/FFmpeg.GPLv3.txt') -Destination (Join-Path $output 'COPYING.GPLv3')
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
    Copy-Item -LiteralPath (Join-Path $output 'conda-notices') -Destination $licenseDirectory -Recurse
    Copy-Item -LiteralPath $lockPath -Destination (Join-Path $licenseDirectory 'runtime-manifest.json')
    if (!$suffix -and $env:OS -ne 'Windows_NT') {
        & chmod +x (Join-Path $output 'bin/ffmpeg') (Join-Path $output 'bin/ffprobe')
        if ($LASTEXITCODE -ne 0) { throw 'Could not set FFmpeg executable permissions.' }
    }
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
