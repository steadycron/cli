# Changelog

All notable changes to the SteadyCron CLI are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.17.0] - 2026-08-02

### Added

- **AI agent monitors (`kind: agent`)** — SteadyCron's third job kind is now a first-class CLI
  citizen. An agent monitor is pinged like a heartbeat, but each run posts a JSON report that the
  server judges against per-job outcome rules, so an agent that exits 0 having produced nothing is
  a failure rather than a green check.
  - **Manifest**: `kind: agent` plus `items_label`, `report_required`,
    `rule_empty_result_enabled`, `rule_max_cost_usd_per_run`, `rule_max_cost_usd_per_period`,
    `rule_cost_period`, `rule_max_steps`, `rule_max_tool_calls`, and `rule_max_duration_ms`.
    Validated locally by `validate`, `plan`, and `apply`, with bounds matching the API's, so a bad
    manifest fails with the field named instead of as a 400 halfway through an apply.
  - **`jobs create`**: `--kind agent` with `--items-label`, `--no-report-required`,
    `--allow-empty-result`, `--max-cost-per-run`, `--max-cost-per-period`, `--cost-period`,
    `--max-steps`, `--max-tool-calls`, and `--max-duration-ms`.
  - **`manifest add job --kind agent`** and **`manifest scaffold`** both cover the kind, including
    the two separate clocks (`grace` for when the run must start, `max_run_duration_seconds` for
    how long it may then take) and every outcome rule.
  - **`init`** offers "Monitor an AI agent" and wires up the empty-result alert rule alongside the
    missed-run one — without it the server's finding would alert against nothing.
  - **`jobs snippet`** emits the agent reporting contract (an ordered `/start` → `/success` pair
    carrying a JSON body) for bash, Python, Node, and GitHub Actions, rather than the heartbeat's
    single bare `curl`.
  - **`rules add --trigger`** accepts `empty_result`, `cost_exceeded`, `no_progress`, and
    `unverified_run`.
  - **`logbook` / `logstream`**: new `agents` domain covering the seven `agent_run_*` event types.

### Fixed

- **`jobs ping-urls`, `jobs snippet`, and the post-apply ping-URL summary skipped agent monitors**
  entirely, and `jobs logs` / `report` labelled them as heartbeats. Every one of these tested
  `kind == "heartbeat"` or `kind != "http"`, which stopped being a correct stand-in for
  "ping-driven" / "HTTP-executed" once a third kind existed.
- **`max_run_duration` was silently dropped on `apply`.** The manifest field is now
  `max_run_duration_seconds`, matching what the API actually reads; the old spelling is still
  accepted and normalised on load, so existing manifests keep working.

## [1.16.0] - 2026-07-05

### Fixed

- **Status glyphs (`✓`/`✗`/`●`/`→` and friends) rendered as `?` on legacy Windows consoles**
  (`cmd.exe` with the default raster font) across every command, most visibly `logstream`'s success
  dot. All such glyphs now resolve through `Output/Glyphs.cs` against the console's detected Unicode
  support, falling back to ASCII (`v`/`x`/`*`/`->`/…) automatically.
- **Low-contrast grey text on content the user needs to read**: job names, failure details, plan
  diffs, and field labels across `logstream`, `report`, `logbook`, `sync`/`plan`/`apply`, and several
  other commands were dimmed to grey with no real reason. Output color now follows a single style
  guide (`Output/Styles.cs`, documented in `docs/output-style-guide.md`) — grey is reserved for
  optional hints, footnotes, and table borders; everything else the user needs to read is rendered
  at full contrast. Interactive prompts also gained a consistent yellow `?` marker.

## [1.15.0] - 2026-07-04

### Fixed

- **`steadycron manifest add <resource>` could corrupt a manifest** whose sequence items sit at
  the same column as their section key (`jobs:\n- id: x`) rather than indented under it (`jobs:\n
  \  - id: x`) — exactly the style this CLI's own `export` produces. The new block was inserted in
  the wrong place (before existing items, not after) with mismatched indentation, and YamlDotNet
  correctly rejected the result. Nothing was lost — the validated-write already refused to persist
  a broken candidate — but the command was unusable against `init`-generated files. Both the
  section-boundary detection and the indent-matching logic now handle either style correctly.
- **`sync`/`plan`/`apply`/`validate` defaulted to `jobs.yaml`** when no manifest path was given,
  left over from before `init`/`manifest scaffold`/`manifest add` standardized on
  `steadycron.yaml` — so `steadycron apply` right after `init` failed with "Manifest file not
  found: jobs.yaml". All four now default to `steadycron.yaml`.

### Added

- **`steadycron_secrets.env`**: `init` now writes this file by default (alongside
  `steadycron.yaml`/`steadycron_example.yaml`, never overwriting an existing one), scaffolded from
  the real `${...}` secret placeholders your account's manifest actually references. Naming the
  file predictably (rather than trying to guess whatever name a user might pick) closes a real gap
  in the `.gitignore` guard added in 1.14.0, which could only protect a filename it actually knew
  about.
  - `apply`/`sync`/`plan`/`validate` now auto-detect `steadycron_secrets.env` in the current
    directory with **no `--env-file` flag needed** (an explicit `--env-file` always overrides it
    entirely; CI behavior — `--allow-process-env` for injected secrets — is unchanged).
  - `export --write-env` can now be passed with no path (`--write-env` alone) and defaults to the
    same filename; an explicit path still works exactly as before.
  - The `.gitignore` guard now writes `*.env` + `.env.*` instead of a hardcoded `secrets.env` —
    `*.env` alone already covers `steadycron_secrets.env` (and bare `.env`); `.env.*` additionally
    covers the `.env.local`/`.env.production` dotenv convention.

## [1.14.0] - 2026-07-04

### Added

- **`steadycron manifest add <resource>`** (alias `manifest g`) — Angular-CLI-style generators
  that append a single `job`, `channel`, `tag`, or `variable` to an existing manifest, instead of
  hand-editing YAML or creating resources imperatively. Fully non-interactive when every value is
  passed as a flag (safe for scripts); missing values are prompted for. Client-side only — no API
  calls, no API key required, same contract as `manifest scaffold`/`import`.
  - Comments and formatting outside the inserted block are never touched: the new section-append
    editor locates the right section (creating it, or the file itself, if absent) and appends
    after the last existing item, re-indenting to match the file's own style. Refuses to edit a
    file with tabs or a flow-style section (`jobs: []`) rather than risk corrupting it.
  - The candidate result is validated before anything is written; a failure (or a duplicate
    `id`/`name`/`key:value`) leaves the original file byte-identical and exits non-zero.
  - Secret-bearing fields (channel webhook URLs/bot tokens, variable values) are always emitted as
    `${ENV_VAR}` placeholders with an explanatory comment — this command never prompts for a
    secret value.
  - `--dry-run` prints the generated block without writing; `-f/--file` targets a manifest other
    than `steadycron.yaml`; `--terraform` is reserved for a future release and currently errors.
  - Schedule/timezone/grace-period prompts work fully offline (no `init` API dependency) via the
    new Cronos-backed local cron evaluator — same defaults and derivation as `init`'s wizard.
- **`steadycron init` now sets up your repo, not just your account**:
  - Adds a `.gitignore` guard: when the current directory is a git repo, appends `secrets.env` and
    `*.env` so a stray secrets file can never be committed by accident.
  - Offers to install CI: when the repo looks GitHub-hosted (a `.github/` folder, or a
    `github.com` remote), prompts `Set up CI? Plan on PRs, apply on merge` and writes
    `.github/workflows/steadycron.yml` — the same plan-on-PR/apply-on-merge workflow this README
    documents by hand — plus a reminder to add `STEADYCRON_API_KEY` as a repository secret.
  - Prints a copy-pasteable README badge snippet for the job that was just created.

### Fixed

- **`steadycron init` could crash** creating a heartbeat or HTTP job whose badge markdown snippet
  was rendered through the markup parser — the snippet's own `[`/`]` characters were parsed as an
  (invalid) style tag (`Could not find color or style 'nightly-backup'`). Long unbroken lines
  meant for copy-paste (URLs, markdown) now bypass both markup parsing and word-wrapping.

## [1.13.0] - 2026-07-04

### Added

- **`steadycron init` kind selector gained a `Skip` option** — sets up `steadycron.yaml` /
  `steadycron_example.yaml` and prints the "manage as code" workflow block without creating a
  job, for anyone who wants to hand-write their manifest from the start.
- **`steadycron init` timezone prompt is now a selector**: `UTC` (default), `Local` (your
  machine's detected IANA zone, hidden if it can't be detected reliably), or `Other…` (free text,
  validated against the same timezone list the server accepts).
- **`steadycron init`'s heartbeat schedule prompt now defaults to `*/15 * * * *`** (Enter
  accepts it), and the grace-period default is now derived from the schedule itself — 300s for
  sub-hourly schedules, 1800s for sub-daily, 3600s for daily-or-slower — instead of a flat 1800s
  for everything.
- **`steadycron init` now writes `steadycron.yaml` and `steadycron_example.yaml`** to the current
  directory at the end of every run (including `Skip`): `steadycron.yaml` is your live account
  exported as a manifest (immediately reconcilable — it already contains the job you just
  created); `steadycron_example.yaml` is the annotated cheat sheet. Neither file is ever
  overwritten if it already exists. A closing block shows the four-command "manage everything as
  code" workflow (`validate` / `plan` / `apply`).
- **`steadycron init`'s post-create summary** now shows the job in the same table `jobs list`
  uses, confirms the actual alert destination (e.g. "If a ping doesn't arrive on time, we'll
  email ops@example.com.") instead of generic wording, and — for heartbeats — prints one
  platform-detected ping snippet (crontab append on Linux/macOS, a PowerShell one-liner on
  Windows) plus a link to the new [ping recipes](https://steadycron.com/docs/ping-recipes) docs
  page for every other scheduler.
- **`steadycron signup`** now validates the email format client-side before calling the API, and
  prints the saved config path abbreviated to `~/...` on Linux/macOS.

### Changed

- **Output styling pass across `signup`/`init`/`jobs create`**: the ping snippet, next-fires
  preview, and IaC commands no longer render grey/dim — they were previously easy to miss even
  though they're the most important lines in the flow. Commands render cyan; the ping snippet
  renders bold green.
- **`jobs list` / `jobs get` / `init`'s post-create table**: a job that has never fired now shows
  "waiting for ping" (heartbeat) or "awaiting first run" (HTTP) in amber instead of a plain grey
  "new" — the underlying API status is unchanged, this is display-only. The job-count footer now
  reads "1 job." / "3 jobs." instead of "N job(s)."
- **`manifest scaffold`'s generated cheat sheet** had three schema bugs, now fixed: the Slack
  channel example used `{{template_var}}` substitution, which channel config never supports (now
  `${SLACK_WEBHOOK_URL}`, matching real `--env-file` usage); the `shaping:` example used fields
  that don't exist (`max_concurrent_jobs`/`max_jobs_per_minute`) instead of the real schema
  (`quiet_hours`/`aggregation`/`escalation`/`flapping`); `HEAD` was listed as a valid HTTP method
  but isn't. The triggers list now also documents `on_slow_run` and `on_size_anomaly`. A new test
  runs the generated cheat sheet through `steadycron validate` in CI so it can't drift again.

## [1.12.0] - 2026-07-03

### Added

- **`steadycron signup`** — creates an account, verifies your email via a 6-digit code, and
  provisions a Full-scope API key, entirely in-terminal. No dashboard required. Wrong-code
  entries show remaining attempts; after 5 wrong guesses, offers `[r]esend`/`[q]uit`. Warns and
  requires `--force` if a working key is already configured. The key is written straight to the
  config file and never echoed in full to stdout.
- **`steadycron login`** — signs an existing account into a new machine: mints a fresh API key
  and saves it locally, without reading or reusing any other machine's key. Routes an unverified
  account into the same code-entry flow as `signup`.
- **`steadycron init`** is now an interactive first-job wizard: pick a heartbeat monitor (for an
  existing cron job) or a new HTTP job, answer a few prompts, and get a monitored job plus an
  alert rule in one command. Cron expressions are validated against the live `cron preview`
  endpoint with a retry loop. Prints the ping-URL snippet inline (see below) for heartbeats.
- **`steadycron jobs create`** now prints the `&& curl -fsS ...` ping snippet inline for
  heartbeat monitors (previously only the bare ping URLs), matching `init`'s output.

### Changed

- **`steadycron init`'s previous behavior — generating a boilerplate manifest/Terraform
  scaffold — has moved to `steadycron manifest scaffold`.** Same flags (`--terraform`, `-o`),
  same output, new command path. This is a breaking change if you scripted the old `init`.

## [1.11.1] - 2026-07-01

### Fixed

- **`logstream` phantom failure events** — logstream could display transient
  `execution_failure` events that immediately resolved to `execution_success` in the final
  logbook record. The root cause: the backend mutates the logbook event in-place as an
  execution progresses (failure → success, same event ID). Polling with `to = now` could
  return an event in its intermediate failure state; the ID-based deduplication then
  filtered out the later success update, leaving the wrong state on screen permanently. The
  live poll window now trails 5 seconds behind the current time so every event has settled
  to its final state before it is displayed. The effective stream latency (poll interval +
  5 s) is imperceptible for a monitoring tool.

## [1.11.0] - 2026-07-01

### Added

- **`steadycron logstream`** — streams live logbook events to the terminal by polling
  `GET /api/logbook` on a short interval and printing each new event as a plain scrolling line
  (Azure log-stream style — no table). Ctrl+C exits cleanly. Options:
  - `--since N` (default 60) — tail the last N seconds before going live; `--since 0` for
    live-only.
  - `--domain`, `--severity`, `--job` — same filter surface as `logbook`.
  - `--interval N` (default 2) — poll cadence in seconds.
  - `--json` — outputs NDJSON (one JSON object per event, no headers) for piping to `jq`.
  - An idle marker (`── HH:mm:ss ──`) is printed every 30 seconds of silence so it is clear
    the command is still connected.

- **`steadycron jobs ping-urls [JOB]`** — prints the success/start/fail ping URLs for
  heartbeat monitors. Omit the argument to list every monitor at once (useful right after
  `apply`). Supports `--json`.

- **`steadycron jobs snippet <JOB> [--lang LANG]`** — generates ready-to-paste integration
  code that wires a heartbeat monitor's ping URLs into a script or workflow. The snippet is
  written to stdout so it can be piped or redirected into a file. Supported languages:
  - `bash` (default) — `set -euo pipefail` with `trap '…fail…' ERR` and a final success ping.
  - `python` — `urllib.request` with try/except and a `PING_BASE` constant.
  - `node` — `fetch` with async/await try/catch and `process.exit(1)` on error.
  - `github-actions` — full workflow YAML with `on.schedule` (pre-filled from the job's cron
    if available), `${{ secrets.STEADYCRON_PING_URL }}`, and `if: success()` / `if: failure()`
    steps. Supports `--json` for structured output.

- **`jobs pause` and `jobs resume` now support bulk operations** via `--tag <key:value>`
  (repeatable, AND logic) and `--all`. Both flags show the matched job list, prompt for
  confirmation (bypass with `--yes`), then execute per job and print a `✓` / `skip` / `✗`
  result for each, followed by a summary line. Single-job behaviour is unchanged.

- **Ping URLs displayed after `apply`/`sync`** — when a manifest apply creates new heartbeat
  monitors, their ping URLs are automatically fetched and printed at the end of the apply
  output so users don't need a follow-up `jobs ping-urls` call.

### Fixed

- **Report total-checks mismatch** — the CLI's `report` window now applies the same 60-second
  safety margin as the dashboard (`from = now − hours + 1 min`) so the total-checks and
  successful-checks counts agree with what the web Overview page shows for the same period.

- **Silent jobs table styling** — the table in the "Silent jobs" section now renders in grey
  (border, column headers, and cell text), visually de-emphasising it relative to the "Active
  issues" section above. The section heading rule stays yellow.

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

[Unreleased]: https://github.com/steadycron/cli/compare/v1.15.0...HEAD
[1.15.0]: https://github.com/steadycron/cli/compare/v1.14.0...v1.15.0
[1.14.0]: https://github.com/steadycron/cli/compare/v1.13.0...v1.14.0
[1.13.0]: https://github.com/steadycron/cli/compare/v1.12.0...v1.13.0
[1.12.0]: https://github.com/steadycron/cli/compare/v1.11.1...v1.12.0
[1.11.1]: https://github.com/steadycron/cli/compare/v1.11.0...v1.11.1
[1.11.0]: https://github.com/steadycron/cli/compare/v1.10.0...v1.11.0
[1.10.0]: https://github.com/steadycron/cli/compare/v1.9.0...v1.10.0
[1.9.0]: https://github.com/steadycron/cli/compare/v1.8.4...v1.9.0
[1.8.4]: https://github.com/steadycron/cli/compare/v1.8.3...v1.8.4
[1.8.3]: https://github.com/steadycron/cli/compare/v1.8.2...v1.8.3
[1.8.2]: https://github.com/steadycron/cli/compare/v1.8.1...v1.8.2
[1.8.1]: https://github.com/steadycron/cli/compare/v1.6.0...v1.8.1
[1.6.0]: https://github.com/steadycron/cli/compare/v1.4.0...v1.6.0
[1.4.0]: https://github.com/steadycron/cli/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/steadycron/cli/compare/v1.2.2...v1.3.0
[1.2.2]: https://github.com/steadycron/cli/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/steadycron/cli/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/steadycron/cli/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/steadycron/cli/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/steadycron/cli/releases/tag/v1.0.0
