# Versioning

The repository-root Directory.Build.props sets the version for the app, Core and
tests. SDK projects import it automatically; keep the version in this one place.

Use YEAR.MONTH.BUILD. MONTH has no leading zero.
BUILD counts releases within the month, not local compilations. For example:
2026.9.1, 2026.9.2, then 2026.10.1.

Before releasing, update the single `<Version>` value and rebuild/publish. Nothing
automatically increments it. Keep BUILD below 65535 for Windows version metadata.

Tag the exact release commit with a `v` prefix, such as `v2026.11.17`, and
publish packages from that commit. Mark beta builds as GitHub prereleases.
See [publishing](publishing.md#sharing-a-windows-test-build) for release assets.

When bumping the version for a release, add `docs/changelogs/<version>.md` and
link it at the top of the [changelog index](../changelogs/README.md). Summarize the
changes users will notice and include any required upgrade steps.

About, report headers and CUE/TXT headers read ApplicationInfo.Version from the
compiled assembly. Windows file/assembly versions use the four-part equivalent
(2026.9.1.0); the product version stays 2026.9.1. Vendor files are unaffected.
