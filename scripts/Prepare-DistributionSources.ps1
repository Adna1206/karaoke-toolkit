<#
.SYNOPSIS
Assembles verified third-party source evidence. This is not release clearance.
.DESCRIPTION
Offline by default. -Download explicitly permits fetching missing pinned archives.
Incomplete FFmpeg provenance is recorded in the output; do not present this as a
complete corresponding-source release until the manifest's limitations are closed.
#>
[CmdletBinding()]
param([switch]$Download)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $repo 'vendor/distribution-sources.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$cache = Join-Path $repo 'artifacts/checks/licensing'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$verified = @()
foreach ($source in $manifest.sources) {
    if ([IO.Path]::GetFileName($source.archive) -ne $source.archive) { throw 'Unsafe source archive name.' }
    $path = if ($source.repositoryPath) { Join-Path $repo $source.repositoryPath } else { Join-Path $cache $source.archive }
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
Copy-Item -LiteralPath $manifestPath -Destination $output
$applicationLicenses = Join-Path $output 'licenses/application'
New-Item -ItemType Directory -Path $applicationLicenses -Force | Out-Null
foreach ($file in @('LICENSE', 'NOTICE.txt')) { Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $applicationLicenses }
Copy-Item -LiteralPath (Join-Path $repo 'docs/distribution-licensing.md') -Destination (Join-Path $output 'README.md')
$verified | ForEach-Object { $_.Hash + '  ' + $_.Archive } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
if (!$manifest.completeForPublicDistribution) { Write-Warning 'Source evidence is incomplete for public distribution. Read README.md and the manifest limitations.' }
Write-Output "Verified source evidence: $output"
