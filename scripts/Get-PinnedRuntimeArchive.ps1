function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}

function Get-PinnedRuntimeArchive {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Archive,
        [Parameter(Mandatory)][string]$Sha256,
        [Parameter(Mandatory)][uri]$Origin
    )

    if ($Sha256 -cnotmatch '^[0-9a-f]{64}$' -or $Origin.Scheme -ne 'https') {
        throw 'Runtime archive pins require a SHA-256 digest and an HTTPS origin.'
    }
    $name = [IO.Path]::GetFileName($Archive)
    if ([string]::IsNullOrWhiteSpace($name) -or $name -in '.', '..') {
        throw 'Invalid pinned runtime archive name.'
    }
    $directory = Join-Path $Repository "artifacts/runtime/downloads/$Sha256"
    $path = Join-Path $directory $name
    $mutex = New-Object System.Threading.Mutex($false, "KaraokeToolkit-Runtime-$Sha256")
    $locked = $false
    try {
        try { $locked = $mutex.WaitOne([TimeSpan]::FromMinutes(30)) }
        catch [System.Threading.AbandonedMutexException] { $locked = $true }
        if (!$locked) { throw "Timed out waiting for runtime archive $name." }
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            if ((Get-Sha256 $path) -ine $Sha256) {
                throw "Cached runtime archive checksum mismatch: $path. Remove this file to fetch a fresh copy."
            }
            return $path
        }

        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        $partial = $path + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
        try {
            Write-Host "Fetching pinned runtime archive $name..."
            Add-Type -AssemblyName System.Net.Http
            $client = New-Object System.Net.Http.HttpClient
            try {
                $client.Timeout = [TimeSpan]::FromMinutes(5)
                $response = $client.GetAsync($Origin, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
                try {
                    $response.EnsureSuccessStatusCode() | Out-Null
                    $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                    $file = [IO.File]::Create($partial)
                    try { $stream.CopyTo($file) }
                    finally { $file.Dispose() }
                }
                finally { $response.Dispose() }
            }
            finally { $client.Dispose() }
            if ((Get-Sha256 $partial) -ine $Sha256) {
                throw 'Downloaded archive checksum mismatch.'
            }
            Move-Item -LiteralPath $partial -Destination $path
        }
        catch {
            throw "Could not fetch pinned runtime archive $name from $Origin. Expected SHA-256 $Sha256. $($_.Exception.Message)"
        }
        finally {
            if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
        }
        return $path
    }
    finally {
        if ($locked) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}
