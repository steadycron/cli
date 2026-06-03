# Changelog

All notable changes to the SteadyCron CLI are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-06-03

### Added
- `steadycron jobs create` — create a single job from flags (same validation and defaults as the
  manifest).
- `steadycron tags` — `list`, `create`, `delete`.
- `steadycron vars` — `list`, `set` (upsert), `delete` (account template variables).
- `steadycron channels` — `list`, `create`, `test`, `delete` (email/slack/discord/webhook/telegram).
- `steadycron rules` — `list`, `add`, `delete` (per-job alert rules).

## [1.0.0] - 2026-06-03

### Added
- `steadycron sync [manifest]` — declarative reconciliation of jobs from a YAML manifest, with
  `--dry-run` / `--plan`, `--prune`, and `--yes`.
- `steadycron jobs` — `list`, `get`, `logs`, `pause`, `resume`, `run`, `delete`.
- `steadycron cron preview` — next fire times for a cron expression.
- `steadycron config` — `show`, `set`, `path`; resolution from flags, `STEADYCRON_API_KEY` /
  `STEADYCRON_API_URL`, and a config file.
- Global `--json`, `--quiet`, `--no-color`, `--api-key`, `--api-url`.
- Distribution as a .NET global tool (`dotnet tool install -g steadycron`) and as self-contained
  single-file binaries.
- MIT license, complete NuGet metadata, Source Link, and reproducible builds.

[Unreleased]: https://github.com/steadycron/cli/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/steadycron/cli/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/steadycron/cli/releases/tag/v1.0.0
