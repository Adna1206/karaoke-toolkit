# Versioning

The repository-root Directory.Build.props sets the version for the app, Core and
tests. SDK projects import it automatically; keep the version in this one place.

Use YEAR.MONTH.BUILD. MONTH has no leading zero.
BUILD counts releases within the month, not local compilations. For example:
2026.9.1, 2026.9.2, then 2026.10.1.

Before releasing, update the single `<Version>` value and rebuild/publish. Nothing
automatically increments it. Keep BUILD below 65535 for Windows version metadata.

About, report headers and CUE/TXT headers read ApplicationInfo.Version from the
compiled assembly. Windows file/assembly versions use the four-part equivalent
(2026.9.1.0); the product version stays 2026.9.1. Vendor files are unaffected.
