[CmdletBinding()]
param(
    [string]$Filter,
    [switch]$KeepFixtures,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$runs = Join-Path $repo 'artifacts/tests/runs'
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
$run = Join-Path $runs $runId
$previousRun = $env:KARAOKE_TOOLKIT_TEST_RUN_ID
Push-Location $repo
try {
    $env:KARAOKE_TOOLKIT_TEST_RUN_ID = $runId
    $arguments = @('test', 'KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj', '-c', 'Release',
        '-m:1', '-p:UsedAvaloniaProducts=', '-p:ArtifactBuildName=StandaloneTests',
        '-v', 'minimal', '--logger', "trx;LogFilePrefix=$runId", '--results-directory', 'artifacts/tests/results')
    if ($Filter) { $arguments += @('--filter', $Filter) }
    if ($NoRestore) { $arguments += '--no-restore' }
    & dotnet @arguments
    $result = $LASTEXITCODE
    if (Test-Path -LiteralPath $run -PathType Container) {
        if ($result -eq 0 -and !$KeepFixtures) {
            & (Join-Path $PSScriptRoot 'Remove-ArtifactRun.ps1') -Root $runs -Path $run
        }
        else {
            Write-Host "Test fixtures: $run"
            if (!$KeepFixtures) { New-Item -Path (Join-Path $run '.cleanup-eligible') -ItemType File -Force | Out-Null }
        }
    }
    & (Join-Path $PSScriptRoot 'Clean-Artifacts.ps1')
    exit $result
}
finally {
    $env:KARAOKE_TOOLKIT_TEST_RUN_ID = $previousRun
    Pop-Location
}
