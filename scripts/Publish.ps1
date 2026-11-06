<#
.SYNOPSIS
Publishes self-contained Windows/Linux x64 builds and creates local Velopack packages.
.EXAMPLE
.\scripts\Publish.ps1
.EXAMPLE
.\scripts\Publish.ps1 -RuntimeIdentifier win-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string[]]$RuntimeIdentifier = @('win-x64', 'linux-x64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'KaraokeToolkit.App/KaraokeToolkit.App.csproj'
$cliProject = Join-Path $repo 'KaraokeToolkit.Cli/KaraokeToolkit.Cli.csproj'
$mcpProject = Join-Path $repo 'KaraokeToolkit.Mcp/KaraokeToolkit.Mcp.csproj'

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}

Push-Location $repo
try {
    # Read evaluated MSBuild properties, including Directory.Build.props. Do not
    # duplicate the application version or visible product name in the script.
    $metadataText = & dotnet msbuild $project -nologo '-getProperty:Version,Product'
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the application version.' }
    $metadata = ($metadataText -join [Environment]::NewLine) | ConvertFrom-Json
    $version = $metadata.Properties.Version
    $product = $metadata.Properties.Product
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Expected YEAR.MONTH.BUILD version, found: $version" }
    if ([string]::IsNullOrWhiteSpace($product)) { throw 'The Product property is missing.' }

    # Keep runtime and packager versions in sync; tool restore is local to this
    # repository and does not install/change a global vpk tool.
    [xml]$appProject = Get-Content -LiteralPath $project
    $runtimeVersion = $appProject.SelectSingleNode('/Project/ItemGroup/PackageReference[@Include="Velopack"]').GetAttribute('Version')
    $tools = Get-Content -LiteralPath (Join-Path $repo '.config/dotnet-tools.json') -Raw | ConvertFrom-Json
    if ($runtimeVersion -ne $tools.tools.vpk.version) { throw 'Velopack runtime and vpk tool versions must match.' }
    Invoke-DotNet @('tool', 'restore')

    # A new output directory per invocation avoids stale native files or accidental
    # overwrites of previous packages. Everything stays under ignored artifacts/.
    $runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
    $releaseRoot = Join-Path $repo "artifacts/releases/$version/$runId"
    foreach ($rid in ($RuntimeIdentifier | Select-Object -Unique)) {
        $publish = Join-Path $repo "artifacts/publish/$version/$runId/$rid"
        $packages = Join-Path $releaseRoot $rid
        Write-Host "Publishing $product $version for $rid..."
        Invoke-DotNet @('publish', $project, '-c', 'Release', '-r', $rid, '--self-contained', 'true',
            '-o', $publish, '-p:PublishSingleFile=false', '-p:PublishTrimmed=false',
            '-p:ArtifactBuildName=Publish', '-p:UsedAvaloniaProducts=')
        # The CLI shares the runtime, Core and native files with the desktop build.
        # Distinct executable/deps/runtimeconfig names allow both entry points in
        # the same package without a second copy of the .NET/FFmpeg distributions.
        Invoke-DotNet @('publish', $cliProject, '-c', 'Release', '-r', $rid, '--self-contained', 'true',
            '-o', $publish, '-p:PublishSingleFile=false', '-p:PublishTrimmed=false',
            '-p:ArtifactBuildName=Publish', '-p:UsedAvaloniaProducts=')

        # MCP is an additional inactive stdio entry point in the same app package.
        Invoke-DotNet @('publish', $mcpProject, '-c', 'Release', '-r', $rid, '--self-contained', 'true',
            '-o', $publish, '-p:PublishSingleFile=false', '-p:PublishTrimmed=false',
            '-p:ArtifactBuildName=Publish', '-p:UsedAvaloniaProducts=')

        $isWindowsTarget = $rid.StartsWith('win-')
        $mainExe = if ($isWindowsTarget) { 'KaraokeToolkit.App.exe' } else { 'KaraokeToolkit.App' }
        $ffmpegExe = if ($isWindowsTarget) { 'ffmpeg.exe' } else { 'ffmpeg' }
        $ffprobeExe = if ($isWindowsTarget) { 'ffprobe.exe' } else { 'ffprobe' }
        $cliExe = if ($isWindowsTarget) { 'karaoke-toolkit.exe' } else { 'karaoke-toolkit' }
        $mcpExe = if ($isWindowsTarget) { 'karaoke-toolkit-mcp.exe' } else { 'karaoke-toolkit-mcp' }
        foreach ($file in @($mainExe, $cliExe, $mcpExe, 'karaoke-toolkit-mcp.deps.json', 'karaoke-toolkit-mcp.runtimeconfig.json', 'ModelContextProtocol.dll', 'licenses/ModelContextProtocol.txt', 'karaoke-toolkit.deps.json', 'karaoke-toolkit.runtimeconfig.json', 'Velopack.dll', 'KaraokeToolkit.Core.dll',
            "ffmpeg/bin/$ffmpegExe", "ffmpeg/bin/$ffprobeExe", 'ffmpeg/runtime-manifest.json',
            'licenses/application/LICENSE', 'licenses/application/NOTICE.txt', 'licenses/ffmpeg/FFmpeg.Notices.txt', 'licenses/ffmpeg/runtime-manifest.json',
            'licenses/README.md', 'licenses/Velopack.txt', 'licenses/TagLibSharp.txt')) {
            if (!(Test-Path -LiteralPath (Join-Path $publish $file) -PathType Leaf)) { throw "Published $rid build is missing $file" }
        }
        if ($isWindowsTarget -and !(Test-Path -LiteralPath (Join-Path $publish 'cdrdao/cdrdao.exe') -PathType Leaf)) {
            throw 'Published Windows build is missing cdrdao.'
        }
        if ($isWindowsTarget) {
            foreach ($notice in @('cdrdao.GPLv2.txt') + @(Get-ChildItem -LiteralPath (Join-Path $repo 'licenses/cdrdao') -File | Select-Object -ExpandProperty Name)) {
                if (!(Test-Path -LiteralPath (Join-Path $publish ('licenses/cdrdao/' + $notice)) -PathType Leaf)) {
                    throw "Published Windows build is missing cdrdao notice: $notice"
                }
            }
        }

        $directive = if ($isWindowsTarget) { '[win]' } else { '[linux]' }
        $icon = Join-Path $repo ('KaraokeToolkit.App/Assets/karaoke-toolkit.' + $(if ($isWindowsTarget) { 'ico' } else { 'png' }))
        $packArguments = @('tool', 'run', 'vpk', '--', $directive, 'pack', '--packId', 'KaraokeToolkit',
            '--packVersion', $version, '--packDir', $publish, '--mainExe', $mainExe,
            '--packTitle', $product, '--packAuthors', 'ADNA', '--runtime', $rid,
            '--icon', $icon, '--outputDir', $packages)
        if ($isWindowsTarget) { $packArguments += @('--shortcuts', 'StartMenuRoot') }
        else { $packArguments += @('--categories', 'AudioVideo') }
        Invoke-DotNet $packArguments
        $expectedPattern = if ($isWindowsTarget) { '*-Setup.exe' } else { '*.AppImage' }
        if (@(Get-ChildItem -LiteralPath $packages -Filter $expectedPattern -File).Count -eq 0) {
            throw "Velopack did not produce the expected $rid package."
        }
        # Append the evaluated app version before checksums record final names.
        $assetManifest = Join-Path $packages $(if ($isWindowsTarget) { 'assets.win.json' } else { 'assets.linux.json' })
        foreach ($package in Get-ChildItem -LiteralPath $packages -Filter $expectedPattern -File) {
            $versionedName = $package.BaseName + '-' + $version + $package.Extension
            Rename-Item -LiteralPath $package.FullName -NewName $versionedName
            if (Test-Path -LiteralPath $assetManifest) {
                $assets = @([IO.File]::ReadAllText($assetManifest) | ConvertFrom-Json)
                foreach ($asset in $assets) {
                    if ($asset.RelativeFileName -ceq $package.Name) { $asset.RelativeFileName = $versionedName }
                }
                [IO.File]::WriteAllText($assetManifest, (ConvertTo-Json -InputObject $assets -Compress))
            }
        }
        $hashes = Get-ChildItem -LiteralPath $packages -File | Get-FileHash -Algorithm SHA256 |
            ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_.Path) }
        [IO.File]::WriteAllLines((Join-Path $packages 'SHA256SUMS.txt'), [string[]]$hashes)
        Write-Host "Packages: $packages"
    }
    Write-Host "Publish complete: $releaseRoot"
    Write-Host 'No packages were installed or uploaded. Update checks remain disabled.'
}
finally { Pop-Location }
