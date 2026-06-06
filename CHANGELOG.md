# Changelog

All notable changes to the SteadyCron CLI are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.3.0] - 2026-06-06

### Added
- `--env-file <path>` (repeatable) on `sync`/`plan`/`apply`/`validate` — loads `${...}` secret values
  from a `.env` file. File values take precedence over the process environment.
- `steadycron export --write-env <path>` — writes a ready-to-fill `.env` scaffold listing every
  `${SC_…}` secret the exported manifest references (refuses to overwrite an existing file).
- `--allow-process-env` on `sync`/`plan`/`apply` — opt back in to sourcing secrets from the process
  environment (e.g. CI), bypassing the new env-file requirement.
- Template-variable **values** now round-trip through `export`→`apply`: `export` emits each value as a
  `${SC_VAR_<NAME>}` placeholder and reconcile sets/updates the value (masked in plans). This makes
  restoring an account to a different one fully CLI-driven (see the README restore runbook). Requires a
  server that supports it; older servers export variable names only.

### Changed
- When a manifest references any required `${...}` placeholder, `sync`/`plan`/`apply` now refuse to run
  unless an `--env-file` is supplied (or `--allow-process-env` is passed). This is a guardrail against
  accidentally applying with secrets sourced from an ambient environment. CI that injects secrets as
  env vars should add `--allow-process-env`.

## [1.2.2] - 2026-06-06

### Fixed
- Job status output now renders `abandoned` in amber (warning) instead of red, aligning the CLI
  with the shared SteadyCron status-color guide — `abandoned` (a run that started but never
  completed) is an attention state, not a confirmed failure.

## [1.2.1] - 2026-06-04

### Fixed
- `sync` / `plan` / `apply` always reported an empty plan (`0 to create, 0 to update`) and never
  created or updated resources. The CLI deserialized the `/api/reconcile` response into
  non-existent `creates`/`updates`/`deletes` arrays; the server's actual `summary` + action-keyed
  `changes` payload was silently dropped. The response model now matches the documented contract,
  so plans, applies, and `--prune` work. Added a JSON-contract regression test that locks the
  reconcile response shape.

## [1.2.0] - 2026-06-04

### Added
- Manifest v2: `namespace`, `channels`, `tags`, `variables`, `shaping`, `id`/`tags`/`rules` on jobs.
- `steadycron validate [paths...]` — local schema + cross-reference lint, no API call (exit 0/2).
- `steadycron plan [paths...]` — dry-run via `/api/reconcile`; renders the server's authoritative plan.
  `--output json` emits the raw plan for CI tooling. `--detailed-exitcode` exits 2 on drift.
- `steadycron apply [paths...]` — alias for `sync --yes`.
- `steadycron export` — exports the account (or jobs/single job) as a v2 manifest.
- `${ENV_VAR}` and `${ENV_VAR:-default}` interpolation in any manifest field at load time.
- Multi-file / directory manifest loading; duplicate-id and version/namespace agreement checks.
- `--namespace` flag on `sync`/`plan`/`apply`; required by `--prune`.
- GitHub Action (`action/action.yml`): plan on PRs with sticky comment, apply on merge.

### Changed
- `sync` now calls `POST /api/reconcile`; the server is the authoritative plan source.
- Exit code 5 renamed from `SyncIncomplete` to `PlanErrors` (same value, broader meaning).
- v1 manifests (jobs-only, no namespace) load with a deprecation warning.

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

[Unreleased]: https://github.com/steadycron/cli/compare/v1.3.0...HEAD
[1.3.0]: https://github.com/steadycron/cli/compare/v1.2.2...v1.3.0
[1.1.0]: https://github.com/steadycron/cli/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/steadycron/cli/releases/tag/v1.0.0
