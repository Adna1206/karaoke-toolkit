[CmdletBinding(SupportsShouldProcess)]
param([ValidateRange(1, 3650)][int]$RetentionDays = 14)

$ErrorActionPreference = 'Stop'
$artifacts = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'
$cutoff = [DateTime]::UtcNow.AddDays(-$RetentionDays)
$roots = @()
$tests = Join-Path $artifacts 'tests/runs'
if (Test-Path -LiteralPath $tests -PathType Container) { $roots += $tests }
$publish = Join-Path $artifacts 'publish'
if (Test-Path -LiteralPath $publish -PathType Container) {
    $roots += @(Get-ChildItem -LiteralPath $publish -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' -and !($_.Attributes -band [IO.FileAttributes]::ReparsePoint) } |
        Select-Object -ExpandProperty FullName)
}
foreach ($root in $roots) {
    foreach ($run in Get-ChildItem -LiteralPath $root -Directory) {
        if ($run.Name -notmatch '^\d{8}-\d{6}-[0-9a-f]{6,32}$' -or
            ($run.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
        $marker = Get-Item -LiteralPath (Join-Path $run.FullName '.cleanup-eligible') -ErrorAction SilentlyContinue
        if ($marker -and $marker.LastWriteTimeUtc -lt $cutoff -and $PSCmdlet.ShouldProcess($run.FullName, 'Remove expired artifact run')) {
            & (Join-Path $PSScriptRoot 'Remove-ArtifactRun.ps1') -Root $root -Path $run.FullName
        }
    }
}
