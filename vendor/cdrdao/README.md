# CDRDAO vendor references

The official 1.2.6 source archive and Windows x64 ZIP are pinned in
`manifest.json`. Both SHA256 hashes match the release assets. The ZIP contains
`cdrdao.exe`, companion tools and four MSYS runtime DLLs. The executable was
identified as PE x64 and printed version 1.2.6 during a no-drive command check.

The official release does not provide Linux or macOS binaries. Build those from
the pinned source on the target platform and review the resulting runtime
dependencies before adding them here. Windows builds bundle this reader for the
experimental CD+G operation; Linux/macOS use an installed executable or configured
path. See [disc ripping](../../docs/user/disc-ripping.md) for integration and limits.

CDRDAO's source archive includes GPL v2 `COPYING`. All four runtime DLLs
match archived MSYS2 packages byte-for-byte; `runtime-components.json` records
their identities and source archives. Repository `licenses/cdrdao/` retains
their licence texts, which build preparation copies to output `licenses/cdrdao/`.
Source packages, patches and
build recipes can be assembled with `scripts/Prepare-DistributionSources.ps1`;
see [distribution licensing](../../docs/distribution/distribution-licensing.md). Public source
access still needs arranging alongside any binary release. A drive and CD+G disc are required to establish ripping
compatibility; the version check did not access a drive.
