param(
    [Parameter(Mandatory=$true)][string]$RuntimeIdentifier,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$manifest = Get-Content -LiteralPath (Join-Path $repo 'vendor/cdrdao/manifest.json') -Raw | ConvertFrom-Json
$platform = $manifest.platforms.PSObject.Properties[$RuntimeIdentifier]
if (!$platform) {
    Write-Output "No bundled cdrdao for $RuntimeIdentifier. Disc ripping requires an installed cdrdao or KARAOKETOOLKIT_CDRDAO."
    exit 0
}
$platform = $platform.Value
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'Get-PinnedRuntimeArchive.ps1')
$archive = Get-PinnedRuntimeArchive $repo $platform.archive $platform.sha256 $platform.origin
$source = Get-PinnedRuntimeArchive $repo $manifest.source.archive $manifest.source.sha256 $manifest.source.origin
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
$runtimeComponents = Get-Content -LiteralPath (Join-Path $repo 'vendor/cdrdao/runtime-components.json') -Raw | ConvertFrom-Json
foreach ($component in $runtimeComponents) {
    if ((Get-Sha256 (Join-Path $OutputDirectory $component.file)) -ne $component.sha256) {
        throw "cdrdao runtime no longer matches its reviewed source package: $($component.file)"
    }
}
Copy-Item -LiteralPath (Join-Path $repo 'vendor/cdrdao/runtime-components.json') -Destination $OutputDirectory -Force
$licenseDirectory = Join-Path (Split-Path ([IO.Path]::GetFullPath($OutputDirectory)) -Parent) 'licenses/cdrdao'
$repoRoot = [IO.Path]::GetFullPath($repo).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (![IO.Path]::GetFullPath($licenseDirectory).StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to replace cdrdao notices outside the repository.'
}
if ([IO.Path]::GetFullPath($licenseDirectory).StartsWith((Join-Path $repoRoot 'licenses') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to replace repository source notices.'
}
if (Test-Path -LiteralPath $licenseDirectory) {
    if ((Get-Item -LiteralPath $licenseDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to replace linked cdrdao notices.' }
    Remove-Item -LiteralPath $licenseDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'vendor/cdrdao/runtime-components.json') -Destination $licenseDirectory -Force
Copy-Item -LiteralPath (Join-Path $repo 'licenses/CDRDAO.GPLv2.txt') -Destination (Join-Path $licenseDirectory 'cdrdao.GPLv2.txt') -Force
# Preserve the licences for the exact runtime DLLs shipped in the reader ZIP.
Get-ChildItem -LiteralPath (Join-Path $repo 'licenses/cdrdao') -Filter '*.txt' -File |
    Copy-Item -Destination $licenseDirectory -Force
# Keep the exact cdrdao source alongside the distributed executable.
Copy-Item -LiteralPath $source -Destination $OutputDirectory -Force
Write-Output "Prepared cdrdao $($manifest.version) for $RuntimeIdentifier."
