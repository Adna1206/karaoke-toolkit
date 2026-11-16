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
    $lock = Get-Content -LiteralPath (Join-Path $repo ('vendor/ffmpeg/' + $platform.condaLock)) -Raw | ConvertFrom-Json
    foreach ($package in $lock.packages) {
        foreach ($pin in (@($package, $package.noticePackage) + $package.noticeFiles) | Where-Object { $_ }) {
            $uri = [uri]$pin.origin
            Get-PinnedRuntimeArchive $repo ([IO.Path]::GetFileName($uri.AbsolutePath)) $pin.sha256 $uri
        }
    }
    if ($rid -eq 'win-x64') {
        $reader = $cdrdao.platforms.PSObject.Properties[$rid].Value
        Get-PinnedRuntimeArchive $repo $reader.archive $reader.sha256 $reader.origin
        Get-PinnedRuntimeArchive $repo $cdrdao.source.archive $cdrdao.source.sha256 $cdrdao.source.origin
    }
}
