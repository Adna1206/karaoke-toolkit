function Expand-CondaRuntime($Repository, $Lock, $Destination) {
    $bin = Join-Path $Destination 'bin'
    $notices = Join-Path $Destination 'conda-notices'
    New-Item -ItemType Directory -Force -Path $bin, $notices | Out-Null
    $windows = $Lock.runtimeIdentifier -eq 'win-x64'
    $unixMember = '^(bin/(ffmpeg|ffprobe)|lib/(pulseaudio/)?[A-Za-z0-9_.+-]+\.(so(\.[0-9]+)*|dylib))$'
    foreach ($package in $Lock.packages) {
        if ($package.name -cnotmatch '^[a-z0-9][a-z0-9_.-]*$') { throw 'Invalid Conda package name.' }
        $uri = [uri]$package.origin
        $archive = Get-PinnedRuntimeArchive $Repository ([IO.Path]::GetFileName($uri.AbsolutePath)) $package.sha256 $uri
        $work = Join-Path (Split-Path $Destination -Parent) $package.name
        New-Item -ItemType Directory -Path $work | Out-Null
        & tar -xf $archive -C $work
        if ($LASTEXITCODE -ne 0) { throw "Could not unpack Conda package $($package.name)." }
        $payload = @(Get-ChildItem -LiteralPath $work -File -Filter 'pkg-*.tar.zst')
        $metadata = @(Get-ChildItem -LiteralPath $work -File -Filter 'info-*.tar.zst')
        if ($payload.Count -ne 1 -or $metadata.Count -ne 1) { throw "Unexpected Conda package layout: $($package.name)." }
        $paths = @($package.files | ForEach-Object {
            $pattern = if ($windows) { '^(Library/bin/)?[A-Za-z0-9_.+-]+\.(exe|dll)$' } else { $unixMember }
            if ($_.path -cnotmatch $pattern) { throw "Invalid runtime member: $($_.path)." }
            $_.path
        } | Select-Object -Unique)
        & tar -xf $payload[0].FullName -C $work -- @paths
        if ($LASTEXITCODE -ne 0) { throw "Missing pinned runtime members: $($package.name)." }
        & tar -xf $metadata[0].FullName -C $work
        if ($LASTEXITCODE -ne 0) { throw "Could not unpack Conda metadata: $($package.name)." }
        foreach ($file in $package.files) {
            $source = Join-Path $work $file.path
            if ((Get-Sha256 $source) -cne $file.sha256) { throw "Runtime member checksum mismatch: $($file.path)." }
            # Unix loader aliases are regular copies under their original import names.
            $relativeTarget = if ($windows) { 'bin/' + [IO.Path]::GetFileName($file.path) }
                elseif ($file.target) { $file.target }
                else { $file.path }
            if (!$windows -and $relativeTarget -cnotmatch $unixMember) { throw "Invalid runtime destination: $relativeTarget." }
            $target = Join-Path $Destination $relativeTarget
            if (Test-Path -LiteralPath $target) { throw "Duplicate runtime member: $target." }
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath $source -Destination $target
        }
        $licenseRoot = Join-Path $work 'info/licenses'
        if (!(Test-Path -LiteralPath $licenseRoot) -and !$package.noticePackage -and !$package.noticeFiles) {
            # Some packages store their licence files in the payload tar.
            $payloadMembers = & tar -tf $payload[0].FullName
            if ($LASTEXITCODE -ne 0) { throw "Could not list Conda payload: $($package.name)." }
            $licences = @($payloadMembers | Where-Object { $_ -cmatch '^info/licenses/(?:[A-Za-z0-9_.+-]+/)*[A-Za-z0-9_.+-]+$' -and $_ -notmatch '(?:^|/)\.\.?(/|$)' })
            if ($licences.Count) {
                & tar -xf $payload[0].FullName -C $work -- @licences
                if ($LASTEXITCODE -ne 0) { throw "Could not unpack Conda payload licences: $($package.name)." }
            }
        }
        if ($package.noticePackage) {
            $noticeUri = [uri]$package.noticePackage.origin
            $noticeArchive = Get-PinnedRuntimeArchive $Repository ([IO.Path]::GetFileName($noticeUri.AbsolutePath)) $package.noticePackage.sha256 $noticeUri
            $noticeWork = Join-Path $work 'notice-package'
            New-Item -ItemType Directory -Path $noticeWork | Out-Null
            & tar -xf $noticeArchive -C $noticeWork
            if ($LASTEXITCODE -ne 0) { throw "Could not unpack Conda notice package: $($package.name)." }
            $noticeMetadata = @(Get-ChildItem -LiteralPath $noticeWork -File -Filter 'info-*.tar.zst')
            if ($noticeMetadata.Count -ne 1) { throw "Unexpected Conda notice package layout: $($package.name)." }
            & tar -xf $noticeMetadata[0].FullName -C $noticeWork
            if ($LASTEXITCODE -ne 0) { throw "Could not unpack Conda notice metadata: $($package.name)." }
            $licenseRoot = Join-Path $noticeWork 'info/licenses'
        }
        $licenseFiles = @(Get-ChildItem -LiteralPath $licenseRoot -File -Recurse -ErrorAction SilentlyContinue)
        if (!$licenseFiles.Count -and !$package.noticeFiles) { throw "Missing package licence: $($package.name)." }
        $noticeDirectory = Join-Path $notices $package.name
        New-Item -ItemType Directory -Path $noticeDirectory | Out-Null
        foreach ($licenseFile in $licenseFiles) {
            $document = $licenseFile.FullName.Substring($licenseRoot.Length + 1) -replace '[\\/]', '-'
            $noticePath = Join-Path $noticeDirectory ($package.name + '.' + $document + '.txt')
            if (Test-Path -LiteralPath $noticePath) { throw "Duplicate Conda notice name: $noticePath." }
            Copy-Item -LiteralPath $licenseFile.FullName -Destination $noticePath
        }
        foreach ($pin in $package.noticeFiles) {
            $noticeUri = [uri]$pin.origin
            $name = [IO.Path]::GetFileName($noticeUri.AbsolutePath)
            $source = Get-PinnedRuntimeArchive $Repository $name $pin.sha256 $noticeUri
            Copy-Item -LiteralPath $source -Destination (Join-Path $noticeDirectory ($package.name + '.' + $name + '.txt'))
        }
        # The staging directory was created above from a validated package name.
        $resolved = (Get-Item -LiteralPath $work).FullName
        if ($resolved -cne [IO.Path]::GetFullPath($work) -or (Split-Path $resolved -Parent) -cne (Split-Path $Destination -Parent)) {
            throw 'Unexpected Conda staging path.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    'Conda-forge FFmpeg. See conda-notices/ for the licences of the pinned runtime packages.' |
        Set-Content -LiteralPath (Join-Path $Destination 'LICENSE.txt') -Encoding ascii
}
