# Contributing to the SteadyCron CLI

Thanks for your interest in improving the SteadyCron CLI! This guide covers everything you need to
build, test, and submit changes.

## Prerequisites

- The [.NET 10 SDK](https://dotnet.microsoft.com/download) (the version is pinned in
  [`global.json`](global.json); the build rolls forward to the latest 10.0 feature band).

## Build and test

```bash
dotnet build                                          # build the solution (steadycron.slnx)
dotnet test                                           # run the test suite (xUnit)
dotnet run --project src/SteadyCron.Cli -- jobs list  # run the CLI locally
```

Warnings are treated as errors (`TreatWarningsAsErrors`), so a build that emits a warning will fail —
please keep the tree warning-clean.

## Code style

Style is enforced by [`.editorconfig`](.editorconfig) and checked in the build
(`EnforceCodeStyleInBuild`). A couple of conventions worth calling out:

- **Braces are required on every `if`/`else`** (this is an error, not a suggestion).
- `Nullable` and `ImplicitUsings` are enabled solution-wide.

Run `dotnet format` before opening a PR if your editor doesn't apply the EditorConfig rules
automatically.

## Project layout

- `src/SteadyCron.Cli/` — the CLI. Built on [Spectre.Console.Cli](https://spectreconsole.net/) for
  commands and output, [YamlDotNet](https://github.com/aaubry/YamlDotNet) for the manifest.
  - `Api/` — the HTTP client and request/response models for the SteadyCron API.
  - `Manifest/` — manifest loading, `${env}` interpolation, validation, and reconcile mapping.
  - `Commands/` — one folder per command group (`jobs`, `tags`, `sync`, …).
  - `Output/`, `Configuration/`, `Infrastructure/` — rendering, config resolution, DI plumbing.
- `tests/SteadyCron.Cli.Tests/` — xUnit tests. New behavior should come with tests; contract tests
  lock the API's snake_case JSON shape, so update them deliberately if the API changes.

## Submitting a change

1. Fork the repo and create a branch off `main`.
2. Make your change with tests, and ensure `dotnet build` and `dotnet test` are green.
3. Add a note under the `## [Unreleased]` heading in [`CHANGELOG.md`](CHANGELOG.md) describing your
   change (this project follows [Keep a Changelog](https://keepachangelog.com/)).
4. Open a pull request with a clear description of the problem and the fix. Link any related issue.

Maintainers cut releases by tagging `vX.Y.Z`; see [`docs/PUBLISHING.md`](docs/PUBLISHING.md). You do
not need to bump the version in your PR — just update the changelog's `Unreleased` section.

## Reporting bugs and security issues

- **Bugs / feature requests:** open a [GitHub issue](https://github.com/steadycron/cli/issues) with
  the CLI version (`steadycron --version`), your OS, and steps to reproduce.
- **Security vulnerabilities:** please follow [`SECURITY.md`](SECURITY.md) — do *not* file a public
  issue.
