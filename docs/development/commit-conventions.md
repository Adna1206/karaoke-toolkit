# Commit message conventions

Use the [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/)
format for new commits:

```text
<type>(optional scope): short description
```

Write the description in the imperative mood, such as
`fix: correct graph range typing`. A scope can identify the affected area, such as
`feat(controller): show average inference latency`.

| Type | Use for |
| --- | --- |
| `feat` | New functionality |
| `fix` | Bug fixes |
| `docs` | Documentation changes |
| `test` | Test changes |
| `refactor` | Code changes that preserve behaviour |
| `perf` | Performance improvements |
| `build` | Build tooling or dependency changes |
| `ci` | Continuous integration changes |
| `chore` | Routine maintenance, including a version-only bump |

Use `!` after the type or scope for a breaking change, and explain the change
in the commit body. For example: `feat(controller)!: change telemetry format`.

For a version-only commit, use `chore: bump version to <new-version>`, with
the number from [`Directory.Build.props`](../../Directory.Build.props). This repository uses
`YEAR.MONTH.BUILD` for its version, so the version number is maintained
independently of Conventional Commits' Semantic Versioning associations.
