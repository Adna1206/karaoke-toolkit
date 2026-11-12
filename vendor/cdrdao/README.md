# CDRDAO vendor references

The official 1.2.6 source archive and Windows x64 ZIP are pinned in
`manifest.json`. Both SHA256 hashes match the release assets. The ZIP contains
`cdrdao.exe`, companion tools and four MSYS runtime DLLs. The executable was
identified as PE x64 and printed version 1.2.6 during a no-drive command check.

The official release does not provide Linux or macOS binaries. Build those from
the pinned source on the target platform and review the resulting runtime
dependencies before adding them here. No CDRDAO binary is wired into the app
yet; the current experimental Windows disc reader still uses BASSCD.

CDRDAO's source archive includes GPL v2 `COPYING`. Review the licenses and
source obligations of the bundled MSYS DLLs before shipping the Windows ZIP
with a public release. A drive and CD+G disc are required to establish ripping
compatibility; the version check did not access a drive.
