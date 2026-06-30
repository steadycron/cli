# Changelog

All notable changes to the SteadyCron CLI are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.10.0] - 2026-06-30

### Added

- `steadycron logbook [--flags]` — scrollable account event history covering every event type:
  HTTP executions, heartbeat check-ins, alert deliveries, job lifecycle changes, API key events,
  subscription events, and more. Filters: `--domain` (category slug, repeatable), `--severity`
  (`info`/`warning`/`critical`, repeatable), `--job` (key, name, or id), `--hours` (default 24).
  Pagination via `--page`/`--page-size` or `--all` to fetch every page. `--verbose` shows full
  per-event metadata; `--json` emits machine-readable output. Mirrors the web Logbook page,
  including the same event labels and metadata field names. Available domains: `executions`,
  `heartbeats`, `alerts`, `jobs`, `keys`, `rules`, `channels`, `subscription`.

- `steadycron init` — generates a fully commented boilerplate manifest covering every SteadyCron
  feature: namespace, template variables (`${ENV_VAR}` interpolation), tags, all five alert channel
  kinds (email, Slack, webhook, Discord, Telegram), an HTTP job with every supported field
  (schedule/interval, method, headers, body, timeout, retries, skip-if-running, misfire policy,
  tags, rules), and a heartbeat monitor (grace, stuck-run detection). Every field carries an
  explanatory inline comment. Pass `--terraform` to generate the equivalent Terraform HCL (exact
  resource and attribute names from the live provider). Write to file with `-o`; refuses to
  overwrite existing files. Requires no API key — runs before `steadycron configure`.

- Job key accepted as a universal job identifier across all commands that take a `<JOB>` argument:
  `jobs get`, `jobs logs`, `jobs pause`, `jobs resume`, `jobs run`, `jobs delete`, `rules list`,
  `rules add`, `rules test`, `export --scope job`, and `logbook --job`. Resolution order: GUID →
  job key (exact) → name (exact, with ambiguity detection). Job keys are shown in `jobs list` and
  are stable across renames, making them the preferred way to reference a job in scripts.

### Changed

- `steadycron report` output redesigned to mirror the web Overview dashboard. The header is now
  "Overview", the KPI summary shows Total checks, Successful (+ success-rate percentage), Incidents
  (unified `failed_checks` count covering both HTTP and heartbeat failures), Alerts, and Jobs
  reporting. The "Active issues" section combines failing jobs and silent monitors ranked by
  attention severity (missed → abandoned → failure → late), matching the web's sort order. Success
  rate is calculated as `successful_checks / total_checks` (not execution-only), so CLI and
  dashboard numbers always agree for the same time window.

### Fixed

- Job Key column in `steadycron jobs list` was rendered in grey; it now uses the same white as all
  other columns.

## [1.9.0] - 2026-06-20

### Added
- `runbook_notes` and `runbook_url` fields on jobs in YAML manifests (`description`'s
  neighbors in `ManifestJob`) — optional markdown remediation steps and an external
  runbook link. Both flow through `apply`/`sync` like any other job field and are
  embedded inline in failure alert notifications (Slack, Telegram, email) when the job
  fails or a heartbeat is missed, so on-call sees the fix instead of just "Job X failed."

## [1.8.4] - 2026-06-18

### Fixed
- `action/action.yml`'s Plan step only ever wrote `PLAN_JSON` to `$GITHUB_OUTPUT`. When
  `steadycron plan` exits 5 (real plan errors, not drift), that step failed the job
  immediately with zero diagnostics — the later "Fail on plan errors" step, which prints a
  readable message, never got a chance to run. Now echoes the plan JSON via `::error::`
  before exiting on a real failure. Also fixed the PR-comment script's summary counts,
  which read `creates`/`updates`/`deletes` as top-level lists that the API has never
  returned (the real shape is `summary.{create,update,delete}` counts) — CREATES/UPDATES/
  DELETES in the posted PR comment were always 0 regardless of the actual plan.
  This was already hotfixed directly in `steadycron/action` (and the floating `v1` tag
  moved) so it takes effect immediately; this release just keeps the source of truth in
  sync for the next mirror.

## [1.8.3] - 2026-06-18

### Fixed
- `--json` output (`jobs get`, `jobs list`, `plan --output json`, `apply`, every `--json`
  command) could emit invalid JSON: `OutputContext.WriteJson` wrote through
  `IAnsiConsole.WriteLine`, which renders plain text through Spectre.Console's word-wrap
  pipeline — the same one used for human-readable markup. A long unbroken string with no
  whitespace (a URL, a token) got a literal newline inserted at the detected console width,
  corrupting the JSON wherever that string landed. `--json` output is now written straight
  to `Console.Out`, bypassing Spectre entirely.

## [1.8.2] - 2026-06-18

### Fixed
- The v1.8.1 release regressed `steadycron/action` (and the floating `@v1` tag): the
  `action/action.yml` mirrored from this repo was stale and only supported `tool: yaml`,
  silently wiping out the Terraform tool support (`tool: terraform`, `working-directory`,
  `terraform-version`, `backend-config`, `var-file`) that had been added directly in
  `steadycron/action` and never backported here. `action/action.yml` now matches the
  Terraform-capable version and carries a guardrail comment so future direct edits to
  `steadycron/action` get backported before the next release.

## [1.8.1] - 2026-06-18

### Fixed
- `${VAR:-default}` placeholders no longer corrupt the manifest when multiple defaulted
  placeholders appear without an intervening `{{template_var}}`. The default-value regex
  previously allowed a literal `}` as content whenever it wasn't immediately followed by
  another `}`, so a placeholder's own closing brace could be swallowed and the match would
  run on until the next `}}` pair anywhere later in the file — corrupting everything in
  between. The default value now stops at the first `}` (shell-style semantics).

## [1.6.0] - 2026-06-13

### Added
- `steadycron report [--hours N] [--verbose]` — account-wide activity digest for a rolling time
  window (default 24 h). Shows execution counts, per-job failure detail (HTTP status, error, retry
  count, response body in `--verbose`), alert delivery status, and a "silent jobs" list for
  schedules that fired zero times in the window. Exits non-zero when any failure or undelivered
  alert is detected, enabling scripted alerting. Backed by the new `/api/reports/summary` endpoint.
- New server-side API endpoints: `GET /api/reports/summary` and `GET /api/reports/events`.
  Both accept `from`/`to` timestamps (any `DateTime.Parse`-compatible format), `types`
  (executions/pings/alerts), `job_id`, `kind`, `status`, and repeatable `tag` filters.
  The events endpoint is cursor-paginated (max 500 per page). Maximum query window is plan-gated:
  Free 1 day · Developer 7 days · Team 30 days.

## [1.4.0] - 2026-06-07

### Added
- The GitHub Action is now published to its own [`steadycron/action`](https://github.com/steadycron/action)
  repository and the GitHub Marketplace — reference it as `steadycron/action@v1`. The release workflow
  mirrors `action/action.yml` on each tagged release and moves the floating `v1` major tag.

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

[Unreleased]: https://github.com/steadycron/cli/compare/v1.6.0...HEAD
[1.6.0]: https://github.com/steadycron/cli/compare/v1.4.0...v1.6.0
[1.4.0]: https://github.com/steadycron/cli/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/steadycron/cli/compare/v1.2.2...v1.3.0
[1.2.2]: https://github.com/steadycron/cli/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/steadycron/cli/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/steadycron/cli/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/steadycron/cli/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/steadycron/cli/releases/tag/v1.0.0
