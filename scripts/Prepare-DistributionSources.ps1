<#
.SYNOPSIS
Assembles verified third-party source evidence. This is not release clearance.
.DESCRIPTION
Offline by default. -Download explicitly permits fetching missing pinned archives.
-CdrdaoOnly assembles the Windows cdrdao and MSYS2 source evidence separately.
-Archive also creates and verifies a ZIP with a SHA-256 sidecar.
Incomplete FFmpeg provenance is recorded in the output; do not present this as a
complete corresponding-source release until the manifest's limitations are closed.
#>
[CmdletBinding()]
param(
    [switch]$Download,
    [switch]$CdrdaoOnly,
    [switch]$Archive
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Get-PinnedRuntimeArchive.ps1')
$manifestPath = Join-Path $repo 'vendor/distribution-sources.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$cdrdaoManifestPath = Join-Path $repo 'vendor/cdrdao/manifest.json'
$cdrdaoRuntimePath = Join-Path $repo 'vendor/cdrdao/runtime-components.json'
$sources = $manifest.sources
if ($CdrdaoOnly) {
    $cdrdao = Get-Content -LiteralPath $cdrdaoManifestPath -Raw | ConvertFrom-Json
    $runtime = Get-Content -LiteralPath $cdrdaoRuntimePath -Raw | ConvertFrom-Json
    $archives = @([IO.Path]::GetFileName($cdrdao.source.archive); $runtime.sourceArchive) | Select-Object -Unique
    $sources = foreach ($sourceArchive in $archives) {
        $matches = @($manifest.sources | Where-Object archive -eq $sourceArchive)
        if ($matches.Count -ne 1) { throw "Expected one source pin for $sourceArchive." }
        $matches[0]
    }
    $manifest.sources = @($sources)
    $manifest.limitations = @(
        'Windows cdrdao and its MSYS2 runtime DLLs only. The upstream cdrdao executable has not been independently rebuilt from its release source.'
    )
}
$cache = Join-Path $repo 'artifacts/checks/licensing'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
function Confirm-SourceArchive($source, [string]$archiveDirectory = '') {
    if ([IO.Path]::GetFileName($source.archive) -ne $source.archive) { throw 'Unsafe source archive name.' }
    $path = if ($source.repositoryPath) { Join-Path $repo $source.repositoryPath } else { Join-Path $cache $source.archive }
    $runtimeArchive = Join-Path $repo "artifacts/runtime/downloads/$($source.sha256)/$($source.archive)"
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -and (Test-Path -LiteralPath $runtimeArchive -PathType Leaf)) {
        $path = $runtimeArchive
    }
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        if (!$Download -or !$source.origin) { throw "Missing source archive: $path. Use -Download to fetch pinned upstream archives." }
        $partial = $path + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
        try {
            Invoke-WebRequest -Uri $source.origin -OutFile $partial -UseBasicParsing
            if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256) {
                throw "Downloaded source checksum mismatch: $($source.component)"
            }
            Move-Item -LiteralPath $partial -Destination $path
        }
        finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256) {
        throw "Source checksum mismatch: $($source.component)"
    }
    [pscustomobject]@{ Path = $path; Archive = $archiveDirectory + $source.archive; Hash = $source.sha256 }
}

$verified = @($sources | ForEach-Object { Confirm-SourceArchive $_ })
if (!$CdrdaoOnly) {
    # The release's own lockfile identifies the exact Rust inputs to librsvg.
    $svg = $sources | Where-Object windowsRuntimePackage -CEQ 'librsvg'
    $svgArchive = $verified | Where-Object Archive -CEQ $svg.archive
    $svgFolder = $svg.archive -replace '\.tar\.xz$', ''
    $cargoLock = & tar -xOf $svgArchive.Path -- "$svgFolder/Cargo.lock"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read librsvg Cargo.lock.' }
    $packageBlocks = ($cargoLock -join "`n") -split '(?m)^\[\[package\]\]\s*$'
    $crates = foreach ($package in $packageBlocks) {
        if ($package -notmatch '(?m)^source = "([^"]+)"') { continue }
        if ($Matches[1] -cne 'registry+https://github.com/rust-lang/crates.io-index' -or
            $package -cnotmatch '(?m)^name = "([a-zA-Z0-9_-]+)"\s+version = "([0-9a-zA-Z.+-]+)"\s+source = "[^"]+"\s+checksum = "([0-9a-f]{64})"') {
            throw 'Unsupported dependency in librsvg Cargo.lock.'
        }
        $crateName, $version, $hash = $Matches[1], $Matches[2], $Matches[3]
        $crateArchive = "$crateName-$version.crate"
        Confirm-SourceArchive @{ archive = $crateArchive; origin = "https://static.crates.io/crates/$crateName/$crateArchive"; sha256 = $hash; component = $crateArchive } 'ffmpeg/win-x64/librsvg-crates/'
    }
    if (!$crates) { throw 'No registry dependencies found in librsvg Cargo.lock.' }
    $verified += $crates
}
$output = Join-Path $repo ('artifacts/distribution-sources/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($source in $verified) {
    $destination = Join-Path $output $source.Archive
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source.Path -Destination $destination
}
$outputManifest = Join-Path $output 'distribution-sources.json'
if ($CdrdaoOnly) {
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputManifest -Encoding utf8
} else {
    Copy-Item -LiteralPath $manifestPath -Destination $outputManifest
}
$cdrdaoProvenance = Join-Path $output 'cdrdao'
New-Item -ItemType Directory -Path $cdrdaoProvenance | Out-Null
Copy-Item -LiteralPath $cdrdaoManifestPath, $cdrdaoRuntimePath -Destination $cdrdaoProvenance
$notices = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $notices | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'licenses/cdrdao') -Destination $notices -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'licenses/CDRDAO.GPLv2.txt') -Destination (Join-Path $notices 'cdrdao')
if (!$CdrdaoOnly) {
    Copy-Item -LiteralPath (Join-Path $repo 'licenses/TagLibSharp.txt') -Destination $notices
    $ffmpegNotices = Join-Path $notices 'ffmpeg'
    New-Item -ItemType Directory -Path $ffmpegNotices | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'licenses/FFmpeg.GPLv3.txt'), (Join-Path $repo 'licenses/FFmpeg.LGPLv3.txt') -Destination $ffmpegNotices
    $ffmpegProvenance = Join-Path $output 'ffmpeg'
    New-Item -ItemType Directory -Path $ffmpegProvenance -Force | Out-Null
    $ffmpegVendor = Join-Path $repo 'vendor/ffmpeg'
    $runtimeManifestPath = Join-Path $ffmpegVendor 'manifest.json'
    $runtimeManifest = Get-Content -LiteralPath $runtimeManifestPath -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $runtimeManifestPath -Destination $ffmpegProvenance
    foreach ($platform in $runtimeManifest.platforms.PSObject.Properties) {
        $lockPath = Join-Path $ffmpegVendor $platform.Value.condaLock
        Copy-Item -LiteralPath $lockPath -Destination $ffmpegProvenance
        $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
        $recipePackages = @('ffmpeg')
        if ($platform.Name -ceq 'win-x64') {
            $recipePackages += @($sources | Where-Object windowsRuntimePackage | ForEach-Object windowsRuntimePackage)
        }
        foreach ($packageName in $recipePackages) {
            $package = $lock.packages | Where-Object name -CEQ $packageName
            if (!$package) { throw "Cannot match $packageName to the $($platform.Name) runtime lock." }
            $origin = [uri]$package.origin
            $name = [IO.Path]::GetFileName($origin.AbsolutePath)
            $cached = Join-Path $repo "artifacts/runtime/downloads/$($package.sha256)/$name"
            if (!$Download -and !(Test-Path -LiteralPath $cached -PathType Leaf)) {
                throw "Missing recipe package: $cached. Use -Download to fetch it."
            }
            $archivePath = Get-PinnedRuntimeArchive $repo $name $package.sha256 $origin
            $recipeDirectory = Join-Path $ffmpegProvenance "$($platform.Name)/$packageName"
            New-Item -ItemType Directory -Path $recipeDirectory -Force | Out-Null
            $metadataMember = 'info-' + [IO.Path]::GetFileNameWithoutExtension($name) + '.tar.zst'
            & tar -xf $archivePath -C $recipeDirectory -- $metadataMember
            if ($LASTEXITCODE -ne 0) { throw "Cannot extract $name metadata." }
            $metadataArchive = Join-Path $recipeDirectory $metadataMember
            $members = @('info/recipe')
            if ($packageName -ceq 'librsvg') { $members += 'info/licenses/THIRDPARTY.yml' }
            & tar -xf $metadataArchive -C $recipeDirectory -- @members
            if ($LASTEXITCODE -ne 0) { throw "Cannot extract the recipe for $name." }
            Remove-Item -LiteralPath $metadataArchive
        }
    }
}
$applicationLicenses = Join-Path $output 'licenses/application'
New-Item -ItemType Directory -Path $applicationLicenses -Force | Out-Null
foreach ($file in @('LICENSE', 'NOTICE.txt')) { Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $applicationLicenses }
Copy-Item -LiteralPath (Join-Path $repo 'docs/distribution/distribution-licensing.md') -Destination (Join-Path $output 'README.md')
Copy-Item -LiteralPath (Join-Path $repo 'docs/distribution/source-availability.md') -Destination (Join-Path $output 'source-availability.md')
$verified | ForEach-Object { $_.Hash + '  ' + $_.Archive } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
if ($CdrdaoOnly) {
    Write-Warning 'This bundle covers only Windows cdrdao and its runtime DLLs. Review the manifest limitations before publication.'
} elseif (!$manifest.completeForPublicDistribution) {
    Write-Warning 'Source evidence is incomplete for public distribution. Read README.md and the manifest limitations.'
}
Write-Output "Verified source evidence: $output"
if ($Archive) {
    # Archives are already compressed; store them without recompression.
    # The sibling ZIP cannot accidentally include itself in the source tree.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
    $scope = if ($CdrdaoOnly) { 'cdrdao' } else { 'third-party' }
    $zipPath = "$output-$scope-sources-$version-incomplete.zip"
    $partialZip = "$zipPath.partial"
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($output, $partialZip, [IO.Compression.CompressionLevel]::NoCompression, $false)
        $zip = [IO.Compression.ZipFile]::OpenRead($partialZip)
        try {
            $files = @(Get-ChildItem -LiteralPath $output -File -Recurse)
            if ($zip.Entries.Count -ne $files.Count) { throw 'Source ZIP entry count mismatch.' }
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($output.Length + 1).Replace('\', '/')
                $entry = $zip.GetEntry($relative)
                if (!$entry -or $entry.Length -ne $file.Length) { throw "Source ZIP entry mismatch: $relative" }
                $stream = $entry.Open()
                $sha = [Security.Cryptography.SHA256]::Create()
                try {
                    $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
                } finally { $sha.Dispose(); $stream.Dispose() }
                if ($actual -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) {
                    throw "Source ZIP checksum mismatch: $relative"
                }
            }
        } finally { $zip.Dispose() }
        Move-Item -LiteralPath $partialZip -Destination $zipPath
        $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii
        Write-Output "Verified source ZIP ($($files.Count) files): $zipPath"
        Write-Output "SHA256: $hash"
    } finally {
        if (Test-Path -LiteralPath $partialZip) { Remove-Item -LiteralPath $partialZip }
    }
}
