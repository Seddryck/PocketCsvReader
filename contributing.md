# Contributing

Discuss substantial changes in an issue before implementation. Use a descriptive natural-language issue title, not a Conventional Commit prefix. Every issue has exactly one change-type label: `bug`, `new-feature`, or `enhancement`; documentation work may also use `docs`.

## Branches and worktrees

Read [AGENTS.md](AGENTS.md) and applicable workflows under `.github/skills/`. Fetch origin, create a task branch from the latest `origin/main`, and work in a dedicated worktree. Use `fix/` for defective behavior, `feat/` for features/enhancements, `docs/` for documentation, and the other repository prefixes for refactoring, tests, performance, and maintenance. Describe the change in plain text; do not append an issue number or use a tooling prefix.

For repositories under `D:\users\seddr\Projects`, create worktrees manually under `<repository-root>/.worktrees/<task-name>` and exclude `.worktrees/` locally through `.git/info/exclude`, not the shared `.gitignore`.

## Validation and pull requests

Run focused tests first. Build the solution and run relevant tests; avoid full suites unless the change requires them. Report exactly what ran and what did not. Use Conventional Commit messages, commit all intended changes, push the branch, and open a pull request targeting `main` with a Conventional Commit-style title. Describe resulting behavior, validation and limitations. Link an existing issue using `close #number`. Do not add change-type labels to the PR unless requested. Leave a clean worktree.

GitHub Actions runs CI. Check the applicable workflow results before merging. New parsers and substantial record-source changes must follow the [file-parser-design skill](.github/skills/file-parser-design/SKILL.md), including memory ownership and allocation checks.

## Documentation changes

The [documentation maintenance guide](docs/MAINTAINING.md) describes the navigation, compatibility routes, runnable example markers, link checks, Jekyll build, and visual review. Keep shared behavior in one authoritative guide and link format-specific differences from format pages.

Ask before machine-wide configuration changes. Never commit secrets or print credentials. For GitHub issue/PR management use `gh`, reusing the existing Windows keyring authentication; do not start a new login just because sandbox access fails.

## Local Git hooks

Enable the repository hooks after cloning (the setting applies only to this repository):

```sh
git config --local core.hooksPath .githooks
```

The hooks require the .NET SDK and the npm `@commitlint/cli` and
`@commitlint/config-conventional` packages, with `commitlint` on `PATH`. On Windows they run
with Bash bundled in Git for Windows. The `commit-msg` hook retains the existing
Conventional Commit validation using `commitlint.config.cjs`.

Before pushing, restore dependencies and build the full solution:

```sh
dotnet restore PocketCsvReader.full.sln
dotnet build PocketCsvReader.full.sln --no-restore
```

The `pre-push` hook checks all reader, test, benchmark, and profiler projects with
`dotnet format PocketCsvReader.full.sln analyzers --verify-no-changes --no-restore`.
To apply available fixes, run
`dotnet format PocketCsvReader.full.sln analyzers --no-restore`, then review the
changes and fix any remaining diagnostics before pushing.
