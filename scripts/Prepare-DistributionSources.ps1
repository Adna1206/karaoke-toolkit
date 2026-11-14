<#
.SYNOPSIS
Assembles verified third-party source evidence. This is not release clearance.
.DESCRIPTION
Offline by default. -Download explicitly permits fetching missing pinned archives.
-CdrdaoOnly assembles the Windows cdrdao and MSYS2 source evidence separately.
Incomplete FFmpeg provenance is recorded in the output; do not present this as a
complete corresponding-source release until the manifest's limitations are closed.
#>
[CmdletBinding()]
param(
    [switch]$Download,
    [switch]$CdrdaoOnly
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $repo 'vendor/distribution-sources.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$cdrdaoManifestPath = Join-Path $repo 'vendor/cdrdao/manifest.json'
$cdrdaoRuntimePath = Join-Path $repo 'vendor/cdrdao/runtime-components.json'
$sources = $manifest.sources
if ($CdrdaoOnly) {
    $cdrdao = Get-Content -LiteralPath $cdrdaoManifestPath -Raw | ConvertFrom-Json
    $runtime = Get-Content -LiteralPath $cdrdaoRuntimePath -Raw | ConvertFrom-Json
    $archives = @([IO.Path]::GetFileName($cdrdao.source.archive); $runtime.sourceArchive) | Select-Object -Unique
    $sources = foreach ($archive in $archives) {
        $matches = @($manifest.sources | Where-Object archive -eq $archive)
        if ($matches.Count -ne 1) { throw "Expected one source pin for $archive." }
        $matches[0]
    }
    $manifest.sources = @($sources)
    $manifest.limitations = @(
        'Windows cdrdao and its MSYS2 runtime DLLs only. The upstream cdrdao executable has not been independently rebuilt from its release source.'
    )
}
$cache = Join-Path $repo 'artifacts/checks/licensing'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$verified = @()
foreach ($source in $sources) {
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
    $verified += [pscustomobject]@{ Path = $path; Archive = $source.archive; Hash = $source.sha256 }
}
$output = Join-Path $repo ('artifacts/distribution-sources/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($source in $verified) { Copy-Item -LiteralPath $source.Path -Destination (Join-Path $output $source.Archive) }
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
    Copy-Item -LiteralPath (Join-Path $repo 'licenses/ffmpeg') -Destination $notices -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'licenses/FFmpeg.GPLv3.txt') -Destination (Join-Path $notices 'ffmpeg')
    Copy-Item -LiteralPath (Join-Path $repo 'vendor/ffmpeg/manifest.json') -Destination (Join-Path $output 'ffmpeg-runtime-manifest.json')
}
$applicationLicenses = Join-Path $output 'licenses/application'
New-Item -ItemType Directory -Path $applicationLicenses -Force | Out-Null
foreach ($file in @('LICENSE', 'NOTICE.txt')) { Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $applicationLicenses }
Copy-Item -LiteralPath (Join-Path $repo 'docs/distribution-licensing.md') -Destination (Join-Path $output 'README.md')
$verified | ForEach-Object { $_.Hash + '  ' + $_.Archive } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
if ($CdrdaoOnly) {
    Write-Warning 'This bundle covers only Windows cdrdao and its runtime DLLs. Review the manifest limitations before publication.'
} elseif (!$manifest.completeForPublicDistribution) {
    Write-Warning 'Source evidence is incomplete for public distribution. Read README.md and the manifest limitations.'
}
Write-Output "Verified source evidence: $output"
