# SteadyCron CLI

[![NuGet](https://img.shields.io/nuget/v/steadycron.svg?logo=nuget)](https://www.nuget.org/packages/steadycron)
[![CI](https://github.com/steadycron/cli/actions/workflows/ci.yml/badge.svg)](https://github.com/steadycron/cli/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/steadycron/cli/blob/main/LICENSE)

The official command-line interface for [SteadyCron](https://steadycron.com) — schedule, run, and
monitor cron jobs as code. Declare your entire account — jobs, heartbeat monitors, alert channels,
tags, variables — in a YAML manifest, commit it to your repo, and reconcile with a single command:

```bash
steadycron sync steadycron.yaml --namespace prod
```

## Install

### As a .NET global tool (recommended)

Requires the [.NET 10 runtime](https://dotnet.microsoft.com/download).

```bash
dotnet tool install -g steadycron
steadycron --version
```

Update with `dotnet tool update -g steadycron`.

### Self-contained binary

Download the single-file binary for your platform from the
[Releases](https://github.com/steadycron/cli/releases) page — no .NET runtime required:

```bash
# example: Linux x64
curl -Lo steadycron https://github.com/steadycron/cli/releases/latest/download/steadycron-linux-x64
chmod +x steadycron && sudo mv steadycron /usr/local/bin/
```

## Authenticate

Create an API key in the dashboard under **Settings → API keys**, then provide it via an
environment variable:

```bash
export STEADYCRON_API_KEY=sc_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Configuration is resolved in this order (first wins):

1. `--api-key` / `--api-url` flags
2. `STEADYCRON_API_KEY` / `STEADYCRON_API_URL` environment variables
3. The config file (`steadycron config set --api-key sc_...`)
4. Built-in defaults (`--api-url` defaults to `https://api.steadycron.com`)

```bash
steadycron config show --check   # verify connectivity
```

> A **read-only** key can run `export`, `validate`, and read-only sub-commands.
> Mutating commands (`apply`, `sync`, `jobs create/pause/delete`, etc.) require a **full** key.

## The v2 manifest

A manifest declares your whole account as code: channels, tags, variables, jobs, and heartbeat
monitors. The server reconciles from this single source of truth — every schedule, alert rule,
and monitoring configuration is version-controlled and reviewable in a pull request.

```yaml
# examples/steadycron.yaml
version: 2
namespace: prod   # required for --prune

channels:
  - id: slack-oncall
    name: Slack #oncall
    kind: slack
    config:
      webhook_url: ${SLACK_WEBHOOK_URL}   # CLI env-var substitution

tags:
  - id: env-prod
    key: env
    value: prod

variables:
  - id: digest-token
    name: digest_token        # used in HTTP fields as {{digest_token}}
    value: ${DIGEST_TOKEN}    # value resolved at load time, never committed

jobs:
  - id: weekly-digest          # stable id — rename the job without re-creating it
    name: weekly-digest-email
    kind: http
    method: POST
    url: ${API_BASE_URL}/jobs/digest
    schedule: "0 9 * * 1"     # Mondays at 09:00
    timezone: Europe/Berlin
    timeout: 120
    retries: 3
    headers:
      Authorization: "Bearer {{digest_token}}"   # server-side template substitution
    tags: ["env:prod"]
    rules:
      - channel: slack-oncall
        trigger: on_failure
        severity: p1

  - id: nightly-db-backup
    name: nightly-db-backup
    kind: heartbeat
    schedule: "0 2 * * *"
    grace: 1800
```

See [`examples/steadycron.yaml`](examples/steadycron.yaml) for the complete field reference.

### Two interpolation mechanisms

| Syntax | Where it runs | Scope |
|---|---|---|
| `${ENV_VAR}` and `${ENV_VAR:-default}` | **CLI, at load time** | Any manifest field |
| `{{template_var}}` | **Server, at execution time** | HTTP job URL / headers / body only |

The CLI resolves `${...}` before sending the manifest to the API. `{{...}}` is passed through
untouched and substituted by the server when the job fires.

### Secrets and `.env` files

Secret fields never leave the server in plaintext: on `export` they come back as `${SC_…}`
placeholders (alert-channel credentials such as `webhook_url`/`bot_token`/`secret`/webhook headers,
and **template-variable values**). To `apply` such a manifest you must supply those values.

Provide them with one or more `--env-file` flags (repeatable; values take precedence over the
process environment so a file prepared for the target account is authoritative):

```bash
steadycron apply production.yaml --namespace prod --env-file secrets.env
```

When a manifest references any required `${...}` placeholder, `apply`/`sync`/`plan` **refuse to run
without an `--env-file`** — pass one, or `--allow-process-env` to source the values from the current
environment instead (e.g. CI that injects secrets as env vars). `validate` supports `--env-file` but
never enforces this (it's a local, read-only lint).

### Restoring to another account

`export` + `apply` move a whole account — jobs, channels, tags, **variable values**, rules — to a
fresh one over the CLI, no UI required:

```bash
# 1. On the source account: export the manifest and a scaffold of the secrets it needs.
steadycron export -o production.yaml --write-env secrets.env

# 2. Fill in secrets.env with the real values (it lists every ${SC_…} the manifest references).

# 3. On the target account (different API key): apply, sourcing the secrets from the file.
steadycron apply production.yaml --namespace prod --prune --env-file secrets.env
```

The server recreates every resource and sets channel credentials and variable values from the
placeholders your `.env` resolves. (Variable-value round-trip requires a server that supports it;
older servers export variable names only.)

### v1 manifests (deprecated)

Version 1 (jobs-only, name-keyed, no namespace/channels/tags/variables) is still accepted. The CLI
prints a deprecation warning and recommends upgrading:

```
⚠ Manifest version 1 is deprecated. Run 'steadycron export' to upgrade to v2.
```

Support will be removed no earlier than two minor releases after this notice.

## Workflow: validate → plan → apply

### `validate` — lint locally, no API call

```bash
steadycron validate steadycron.yaml
steadycron validate ./manifests/
```

Checks schema, cron syntax, cross-references (job tags → declared tags, rule channels → declared
channels), duplicate IDs, and kind-specific field constraints. Fast CI gate — runs in milliseconds.
Exits **0** on success, **2** on errors.

### `plan` — preview what would change

```bash
steadycron plan steadycron.yaml --namespace prod
steadycron plan ./manifests/ --namespace prod --output json   # machine-readable server plan
steadycron plan steadycron.yaml --namespace prod --detailed-exitcode
```

Calls the server's `/api/reconcile` dry-run and renders its authoritative plan. The server is the
single source of truth — the CLI never computes its own diff.

`--detailed-exitcode` exits **2** when drift is detected (Terraform-style), **0** when clean.
Without the flag, any plan exits **0** unless there are errors.

### `sync` — plan + apply

```bash
# Interactive: shows plan, prompts to confirm, then applies
steadycron sync steadycron.yaml --namespace prod

# Non-interactive (CI): applies without prompt
steadycron sync steadycron.yaml --namespace prod --yes

# Include --prune to delete server resources removed from the manifest
steadycron sync ./manifests/ --namespace prod --prune --yes
```

`sync` is declarative: it **creates** new resources, **updates** changed ones, and (with `--prune`)
**deletes** ones missing from the manifest. Without `--prune`, orphaned server resources are reported
but not deleted.

### `apply` — alias for `sync --yes`

```bash
steadycron apply ./manifests/ --namespace prod --prune
```

Applies immediately without prompting. Typical use: CI pipelines on merge to the default branch.

### `export` — pull the current account state as a manifest

```bash
steadycron export -o steadycron.yaml                     # whole account → file
steadycron export -o steadycron.yaml --write-env secrets.env   # + a .env scaffold for its secrets
steadycron export --scope jobs -o jobs.yaml              # jobs only
steadycron export --scope job weekly-digest-email        # single job → stdout
steadycron export --format json                          # JSON instead of YAML
```

Writes the manifest verbatim from the server. Secret fields — alert-channel credentials
(`webhook_url`, `bot_token`, `secret`, webhook headers) **and template-variable values** — are
replaced with `${SC_…}` placeholders, never plaintext. The CLI prints a summary of the required
environment variables to stderr (so piping with `-o` stays clean), and the manifest itself carries a
`# required env vars:` header block.

`--write-env <path>` writes a ready-to-fill `.env` scaffold listing every referenced secret
(refuses to overwrite an existing file). Fill it in, then `apply … --env-file <path>`.

Useful for bootstrapping: export your current account, commit the result, and manage it as code
going forward. To move an account to a new one, see [Restoring to another account](#restoring-to-another-account).

### Multi-file manifests

Separate concerns across files or directories:

```bash
steadycron validate ./manifests/
steadycron plan ./manifests/ --namespace prod
steadycron apply manifests/channels.yaml manifests/jobs.yaml --namespace prod
```

When multiple files are used, they must agree on `version` and `namespace`. Duplicate resource `id`
values across files are an error.

## Importing existing schedules

`import` generates a v2 manifest from a crontab or `vercel.json` file — client-side only, no API
calls. Review the result, then `sync` it.

### `import crontab` — migrate from a crontab

```bash
# Import a crontab file
steadycron import crontab /etc/cron.d/myjobs -o steadycron.yaml

# Import from stdin (e.g. from `crontab -l`)
crontab -l | steadycron import crontab -o steadycron.yaml

# Preview what would be imported without writing anything
steadycron import crontab mycron.txt --dry-run

# System crontab (extra username column): auto-detected, or force with --system
steadycron import crontab /etc/cron.d/myjobs --system -o steadycron.yaml

# Force all entries to a specific kind
steadycron import crontab mycron.txt --as heartbeat -o monitors.yaml
```

**Job kind mapping (`--as auto` default):**

| Command type | Becomes |
|---|---|
| `curl`/`wget`/bare `https://…` URL | `http` job — URL, method, headers, body extracted |
| Anything else | `heartbeat` monitor |

`--as http` forces http for every entry (skips with a warning if no URL can be extracted).
`--as heartbeat` forces heartbeat regardless of the command.

**crontab conventions handled:**

- `MAILTO=`, `PATH=`, and other env-assignment lines are ignored.
- A `# comment` immediately above an entry becomes the job `name` (blank line clears it).
- Macros: `@hourly`, `@daily`/`@midnight`, `@weekly`, `@monthly`, `@yearly`/`@annually` are
  expanded to their 5-field equivalents.
- `@reboot` is skipped with an actionable warning (no equivalent schedule exists).
- System crontab / `cron.d` format (6th field is a username) is auto-detected or set with `--system`.

**Heartbeat monitors — post-sync step:**

When a command is imported as a heartbeat, the CLI prints the ping snippet you must append to that
cron command (the ping token only exists after `sync` creates the monitor):

```
! Heartbeat nightly-backup (id: nightly-backup)
   After sync, append this to your cron command:
   && curl -fsS 'https://ping.steadycron.com/<TOKEN>'
   (<TOKEN> available after: steadycron jobs get nightly-backup)
```

**Note on timezone:** crontab entries run in the server's local timezone. All imported entries get
`timezone: UTC` as a safe default. Edit the `timezone` field in the manifest before `sync` if your
cron server runs in a different timezone.

### `import vercel` — migrate from Vercel cron jobs

Vercel's hobby plan limits crons to once per day in UTC. Migrating to SteadyCron removes both
restrictions.

```bash
# Basic import (--base-url required)
steadycron import vercel --base-url https://app.example.com -o steadycron.yaml

# With a cron secret (added as Authorization header; never inlined)
steadycron import vercel \
  --base-url https://app.example.com \
  --cron-secret-env VERCEL_CRON_SECRET \
  -o steadycron.yaml

# Preview without writing
steadycron import vercel --base-url https://app.example.com --dry-run
```

The importer reads `vercel.json` in the current directory (or pass an explicit path as the first
argument). For each cron entry, it emits an `http GET` job with the full URL (`--base-url` + path)
and `timezone: UTC`.

**`--cron-secret-env NAME`** — instead of inlining the secret value, the manifest emits:

```yaml
headers:
  Authorization: Bearer ${VERCEL_CRON_SECRET}
```

The `${…}` placeholder is resolved at `sync` time via `--env-file` or the process environment.
The manifest itself never contains the secret.

**End-to-end flow:**

```bash
# 1. Generate the manifest
steadycron import vercel \
  --base-url https://app.example.com \
  --cron-secret-env VERCEL_CRON_SECRET \
  -o steadycron.yaml

# 2. Review the manifest
steadycron validate steadycron.yaml

# 3. Set the secret and sync
echo "VERCEL_CRON_SECRET=your-secret-here" > secrets.env
steadycron apply steadycron.yaml --env-file secrets.env --namespace prod
```

After migrating, you can tighten schedules and set per-job timezones — capabilities Vercel doesn't
expose.

## Cron and monitoring as code in CI

Add the SteadyCron GitHub Action to plan on pull requests and apply on merge:

```yaml
# .github/workflows/steadycron.yml
name: SteadyCron

on:
  pull_request:
    paths: ["steadycron/**"]
  push:
    branches: [main]
    paths: ["steadycron/**"]

permissions:
  contents: read
  pull-requests: write

jobs:
  steadycron:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: steadycron/action@v1
        with:
          # plan on PRs, apply on push to main
          command: ${{ github.event_name == 'pull_request' && 'plan' || 'apply' }}
          manifest: steadycron/
          namespace: prod
          prune: "true"
          comment-on-pr: "true"
        env:
          STEADYCRON_API_KEY: ${{ secrets.STEADYCRON_API_KEY }}
```

The API key is read from the `STEADYCRON_API_KEY` environment variable (a repository secret), not a
`with:` input — this keeps it out of the action's logged inputs.

The action:
1. Installs the CLI (pinned version).
2. On `pull_request` — runs `steadycron plan --output json`, formats the plan as Markdown, and
   **posts/updates a sticky PR comment** (find-and-replace so re-runs update in place). Fails the
   check if the plan has errors (limit violations, conflicts, etc.).
3. On `push` to the default branch — runs `steadycron apply --yes`.

See [`examples/ci/`](examples/ci/) for standalone `pull_request` and `push` workflow files.

## Activity reports

```bash
steadycron report                  # digest of the last 24 h
steadycron report --hours 6        # shorter window
steadycron report --hours 168      # last 7 d (Developer plan and above)
steadycron report --hours 720      # last 30 d (Team plan only)
steadycron report --verbose        # adds HTTP response bodies and full alert delivery list
steadycron report --json           # machine-readable (CI alerting, dashboards)
```

The report command calls `/api/reports/summary` and shows:

| Section | What you learn |
|---|---|
| **Summary** | Execution counts (total / success / failed), ping count, alert delivery counts |
| **Failures** | Per-job block: last HTTP status, error message, duration, retry count, whether alerts fired |
| **Alert deliveries** | Trigger → channel → delivered/failed/suppressed (full table in `--verbose`, problems-only otherwise) |
| **Silent jobs** | Jobs that had zero activity in the window — schedule drift or misconfiguration |
| **Footer** | One-line health verdict and exit code (non-zero on failures or undelivered alerts) |

Plan limits cap how far back you can query (Free: 1 day, Developer: 7 days, Team: 30 days).
The server returns a `range_exceeds_plan` error with a clear message if the requested `--hours` exceeds your limit.

## Managing resources directly

```bash
steadycron jobs list                       # table of all jobs
steadycron jobs list --kind heartbeat --status missed
steadycron jobs get weekly-digest-email    # by name or id
steadycron jobs logs warm-cdn-cache -n 20
steadycron jobs pause weekly-digest-email
steadycron jobs resume weekly-digest-email
steadycron jobs run warm-cdn-cache
steadycron jobs delete old-job --yes

steadycron jobs create --name warm-cache --url https://api.myapp.com/warm \
  --method GET --interval 900 --skip-if-running

steadycron cron preview "*/15 9-17 * * 1-5" --timezone Europe/Berlin

steadycron tags list
steadycron tags create env prod --color green

steadycron vars list
steadycron vars set digest_token "sk_live_…"

steadycron channels list
steadycron channels create --name "Ops email" --kind email --to ops@example.com

# `jobs list` includes a job ID column — copy the id to use with rules commands
steadycron rules list nightly-db-backup        # shows trigger, severity, channel kind and target
steadycron rules add nightly-db-backup \
  --channel "Ops email" --trigger missed_heartbeat --severity p1
steadycron rules test nightly-db-backup        # fires a test alert on every channel for the job
steadycron rules delete <rule-id>
```

`rules test` sends one notification per unique channel (even if multiple triggers point at the
same channel) and exits non-zero if any delivery fails — useful for verifying a new channel
configuration from CI or a script.

Add `--json` to any command for machine-readable output.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (or `plan` with no drift when `--detailed-exitcode` is not set) |
| `1` | Unexpected error |
| `2` | Manifest load/validation error; also `plan --detailed-exitcode` when drift is detected |
| `3` | API error |
| `4` | Missing/invalid credentials (`401`/`403`) |
| `5` | Plan/apply has `errors[]` (limit violations, conflicts) or per-resource failures |
| `130` | Cancelled (Ctrl+C) |

> `--detailed-exitcode` overloads code `2` for `plan`: exit `2` = drift detected,
> exit `0` = clean. This matches `terraform plan` behaviour and is documented as an opt-in
> so that CI scripts branching on exit code `2` for validation errors are unaffected.

## Development

```bash
dotnet build
dotnet test
dotnet run --project src/SteadyCron.Cli -- jobs list
```

Built on .NET 10 with [Spectre.Console](https://spectreconsole.net/) and
[YamlDotNet](https://github.com/aaubry/YamlDotNet).

## License

[MIT](https://github.com/steadycron/cli/blob/main/LICENSE) © SteadyCron.
