# Contributing

Use the .NET 10 SDK and open `KaraokeToolkit.slnx` in your editor. Windows is
the primary development and desktop test platform. Build and test from the
repository root:

```powershell
dotnet build KaraokeToolkit.slnx
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release
```

For a substantial change, discuss the scope first. Create a short-lived branch
and submit a pull request against `master`. Describe the behavior change and
the checks you ran. Follow the [commit conventions](development/commit-conventions.md)
and see the [development index](README.md) for project-specific guidance.

The maintainer sets release priorities and makes final decisions on changes
and releases. Contributors can propose changes through issues and pull requests
where they have repository access.
