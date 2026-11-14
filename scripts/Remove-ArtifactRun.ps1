param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$Path
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$artifacts = (Get-Item -LiteralPath (Join-Path $repo 'artifacts') -Force).FullName
$rootPath = [IO.Path]::GetFullPath($Root)
$target = (Get-Item -LiteralPath $Path -Force).FullName
$comparison = if ([Environment]::OSVersion.Platform -eq 'Win32NT') { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (![IO.Path]::GetDirectoryName($target).Equals($rootPath, $comparison) -or
    !$rootPath.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "Cleanup target is outside the expected artifact run directory: $target"
}
$entry = Get-Item -LiteralPath $target -Force
while ($null -ne $entry -and ($entry.FullName.Equals($artifacts, $comparison) -or
    $entry.FullName.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, $comparison))) {
    if (!($entry.Attributes -band [IO.FileAttributes]::Directory) -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Cleanup refuses linked or non-directory paths: $($entry.FullName)"
    }
    $entry = $entry.Parent
}
Remove-Item -LiteralPath $target -Recurse -Force
