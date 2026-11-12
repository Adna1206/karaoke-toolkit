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
$manifest = Get-Content -LiteralPath (Join-Path $repo 'vendor/cdrdao/manifest.json') -Raw | ConvertFrom-Json
$platform = $manifest.platforms.PSObject.Properties[$RuntimeIdentifier]
if (!$platform) {
    Write-Output "No bundled cdrdao for $RuntimeIdentifier. Disc ripping requires an installed cdrdao or KARAOKETOOLKIT_CDRDAO."
    exit 0
}
$platform = $platform.Value
$archive = Join-Path (Join-Path $repo 'vendor/cdrdao') $platform.archive
if ((Get-Sha256 $archive) -ne $platform.sha256) {
    throw 'Vendored cdrdao archive checksum mismatch.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($name in @($platform.binary) + @($platform.bundledRuntime)) {
        if ([IO.Path]::GetFileName($name) -ne $name) { throw 'Unsafe cdrdao manifest filename.' }
        $entry = $zip.GetEntry($name)
        if (!$entry) { throw "Vendored cdrdao archive is missing $name" }
        $inputStream = $entry.Open()
        $outputStream = [IO.File]::Create((Join-Path $OutputDirectory $name))
        try { $inputStream.CopyTo($outputStream) }
        finally { $inputStream.Dispose(); $outputStream.Dispose() }
    }
} finally { $zip.Dispose() }
Copy-Item -LiteralPath (Join-Path $repo 'licenses/CDRDAO.GPLv2.txt') -Destination $OutputDirectory -Force
# Keep the exact cdrdao source alongside the distributed executable.
$source = Join-Path (Join-Path $repo 'vendor/cdrdao') $manifest.source.archive
if ((Get-Sha256 $source) -ne $manifest.source.sha256) { throw 'cdrdao source checksum mismatch.' }
Copy-Item -LiteralPath $source -Destination $OutputDirectory -Force
Write-Output "Prepared cdrdao $($manifest.version) for $RuntimeIdentifier."
