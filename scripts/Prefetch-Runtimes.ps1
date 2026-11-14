[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string[]]$RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Get-PinnedRuntimeArchive.ps1')
$ffmpeg = Get-Content -LiteralPath (Join-Path $repo 'vendor/ffmpeg/manifest.json') -Raw | ConvertFrom-Json
$cdrdao = Get-Content -LiteralPath (Join-Path $repo 'vendor/cdrdao/manifest.json') -Raw | ConvertFrom-Json
foreach ($rid in ($RuntimeIdentifier | Select-Object -Unique)) {
    $platform = $ffmpeg.platforms.PSObject.Properties[$rid].Value
    Get-PinnedRuntimeArchive $repo $platform.archive $platform.sha256 $platform.origin
    if ($platform.probeArchive) {
        Get-PinnedRuntimeArchive $repo $platform.probeArchive $platform.probeSha256 $platform.probeOrigin
    }
    if ($rid -eq 'win-x64') {
        $reader = $cdrdao.platforms.PSObject.Properties[$rid].Value
        Get-PinnedRuntimeArchive $repo $reader.archive $reader.sha256 $reader.origin
        Get-PinnedRuntimeArchive $repo $cdrdao.source.archive $cdrdao.source.sha256 $cdrdao.source.origin
    }
}
