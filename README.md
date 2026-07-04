# SteadyCron CLI

[![NuGet](https://img.shields.io/nuget/v/steadycron.svg?logo=nuget)](https://www.nuget.org/packages/steadycron)
[![CI](https://github.com/steadycron/cli/actions/workflows/ci.yml/badge.svg)](https://github.com/steadycron/cli/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/steadycron/cli/blob/main/LICENSE)

The official command-line interface for [SteadyCron](https://steadycron.com) — schedule, run, and
monitor cron jobs as code. Everything works from the terminal: account creation, your first
monitored job, day-to-day management, and a full manifest-as-code workflow. No dashboard required.

```bash
dotnet tool install -g steadycron
steadycron signup
steadycron init
```

## Contents

- [Quickstart](#quickstart)
- [Install](#install)
- [Authenticate](#authenticate)
- [Your first job: `init`](#your-first-job-init)
- [Day-to-day commands](#day-to-day-commands)
- [Cron as code](#cron-as-code)
  - [The v2 manifest](#the-v2-manifest)
  - [Interpolation, secrets, and `.env` files](#two-interpolation-mechanisms)
  - [Workflow: validate → plan → apply](#workflow-validate--plan--apply)
  - [`export` and restoring to another account](#export--pull-the-current-account-state-as-a-manifest)
  - [`manifest scaffold`](#manifest-scaffold--boilerplate-manifest-generator)
  - [`manifest add`](#manifest-add--append-a-resource-to-an-existing-manifest)
  - [Importing existing schedules](#importing-existing-schedules)
  - [CI: plan on PRs, apply on merge](#cron-and-monitoring-as-code-in-ci)
- [Activity reports](#activity-reports)
- [Logbook](#logbook)
- [Exit codes](#exit-codes)
- [Development](#development)

## Quickstart

From nothing to a protected cron job, entirely in your terminal:

```
$ dotnet tool install -g steadycron

$ steadycron signup
Email: dan@example.com
Password: ********
✓ Account created. Verification code sent to dan@example.com.
Enter 6-digit code: 482913
✓ Email verified.
✓ API key created (cli-dans-laptop, scope: full) → saved to ~/.config/steadycron/config.json
✓ Default email alert channel set up for dan@example.com.

Next: steadycron init — create your first monitored job.

$ steadycron init
? What do you want to do?
  › Monitor an existing cron job (heartbeat)
    Schedule a new HTTP job (we call your URL)
    Skip — just set up the manifest workflow

? Job name: nightly-backup
? Expected schedule (cron) [*/15 * * * *]: 0 2 * * *
? Timezone:
  › UTC
    Local (Europe/Berlin)
    Other…
  Next fires: 2026-07-05 02:00, 2026-07-06 02:00, 2026-07-07 02:00 (UTC)
? Grace period seconds [3600]:

✓ Created heartbeat monitor nightly-backup.
✓ If a ping doesn't arrive on time, we'll email dan@example.com.

┌────────────────┬───────────┬──────────────────┬────────────────┬──────────┐
│ Name           │ Kind      │ Status           │ Schedule       │ Next run │
├────────────────┼───────────┼──────────────────┼────────────────┼──────────┤
│ nightly-backup │ heartbeat │ waiting for ping │ 0 2 * * * UTC  │ —        │
└────────────────┴───────────┴──────────────────┴────────────────┴──────────┘
1 job. Check status anytime: steadycron jobs get nightly-backup

Add this to the end of your cron command:

    && curl -fsS https://ping.steadycron.com/<token>

Example crontab line:
    0 2 * * *  /usr/local/bin/backup.sh && curl -fsS https://ping.steadycron.com/<token>
Other schedulers (systemd, Docker, Kubernetes, Windows): https://steadycron.com/docs/ping-recipes

✓ Wrote steadycron.yaml — your account as code (1 job, 1 channel, 1 rule)
✓ Wrote steadycron_example.yaml — reference for every manifest feature

Manage everything as code:
  1. Add or edit jobs in steadycron.yaml
  2. steadycron validate steadycron.yaml    check locally, no API
  3. steadycron plan steadycron.yaml        preview changes
  4. steadycron apply steadycron.yaml       sync to your account
```

That's the whole product in one session: a monitored job, alerting to your inbox, and your
account exported as a version-controllable manifest.

Already have an account? `steadycron login` mints a fresh key for this machine without touching
any other machine's key — see [Authenticate](#authenticate).

From here, either keep managing resources one-off ([Day-to-day commands](#day-to-day-commands))
or commit `steadycron.yaml` and manage everything through `plan`/`apply`
([Cron as code](#cron-as-code)). Both operate on the same account — mix them freely.

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

Binaries are available for Linux (x64, arm64), macOS (x64, arm64), and Windows (x64).

## Authenticate

New to SteadyCron? Create an account, verify your email, and provision an API key — entirely
in-terminal:

```bash
steadycron signup
```

The full transcript is in the [Quickstart](#quickstart). What it does: creates the account,
verifies your email with a 6-digit code, mints a full-scope API key named `cli-<hostname>`, saves
it to the config file, and sets up a default email alert channel pointing at your address.

**Config file location:** `~/.config/steadycron/config.json` on Linux/macOS,
`%APPDATA%\steadycron\config.json` on Windows.

Already have an account? Sign in on a new machine:

```bash
steadycron login
```

`login` mints a fresh key for this machine and never touches or reveals any other machine's key.
Stale `cli-*` keys can be revoked from the dashboard if you ever want to clean up.

Prefer to create a key from the dashboard instead? Create one under **Settings → API keys**, then
provide it via an environment variable:

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

## Your first job: `init`

`steadycron init` is an interactive wizard that creates your first monitored job — a heartbeat
monitor for an existing cron job, or a new HTTP job SteadyCron calls on a schedule — plus a
default alert rule. The full transcript is in the [Quickstart](#quickstart).

What the wizard gives you:

- **Sensible defaults everywhere** — schedule prefills `*/15 * * * *`, the grace period is derived
  from your schedule, and timezone is a selector (UTC / your local timezone / manual entry).
  Enter-through works for a quick test job.
- **A next-fires preview** in your chosen timezone before anything is created.
- **Plain-language alerting** — the default rule emails your verified address on a missed ping
  (heartbeat) or failed run (HTTP). Add more channels later with `channels create` + `rules add`.
- **The ping snippet** for heartbeats, with a platform-matched scheduler example and a pointer to
  [ping recipes](https://steadycron.com/docs/ping-recipes) for systemd, Docker, Kubernetes, and
  Windows Task Scheduler.
- **Two manifest files** written to the current directory (never overwriting existing files):
  - `steadycron.yaml` — a live export of your account, immediately usable with `plan`/`apply`
  - `steadycron_example.yaml` — a fully commented reference covering every manifest feature
- **A README badge snippet** for the job — markdown you can paste straight into your repo's README.
- **A `.gitignore` guard**, when the current directory is a git repo: appends `secrets.env` and
  `*.env` so a stray secrets file can never be committed by accident.
- **A GitHub Actions opt-in**, when the current directory looks like a GitHub-hosted repo (a
  `.github/` folder, or a `github.com` remote): offers to write
  `.github/workflows/steadycron.yml` — the same plan-on-PR/apply-on-merge workflow documented
  [below](#cron-and-monitoring-as-code-in-ci), pre-wired to `steadycron.yaml`.

The third wizard option, **Skip**, creates nothing but still writes both manifest files (and offers
the `.gitignore`/CI setup) — the fastest way to start manifest-first on an empty account.

`init` requires a configured API key (`signup`/`login`/`config set` first) and an interactive
terminal. Run it again anytime to add another job, or use
[`steadycron manifest add job`](#manifest-add--append-a-resource-to-an-existing-manifest) to add
the next one manifest-first, without touching the API at all.

## Day-to-day commands

Commands that accept a `<JOB>` argument resolve it in order: GUID → job key → exact name. Job
keys are shown in `jobs list` (Key column) and are stable across renames — prefer them over
names in scripts.

```bash
steadycron jobs list                          # table of all jobs with their job keys
steadycron jobs list --kind heartbeat --status missed
steadycron jobs get my-job-key                # by job key (preferred), name, or id
steadycron jobs logs my-job-key -n 20
steadycron jobs pause my-job-key
steadycron jobs resume my-job-key
steadycron jobs run my-job-key
steadycron jobs delete old-job-key --yes

steadycron jobs create --name warm-cache --url https://api.myapp.com/warm \
  --method GET --interval 900 --skip-if-running

steadycron cron preview "*/15 9-17 * * 1-5" --timezone Europe/Berlin

steadycron tags list
steadycron tags create env prod --color green

steadycron vars list
steadycron vars set digest_token "sk_live_…"

steadycron channels list
steadycron channels create --name "Ops email" --kind email --to ops@example.com

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

## Cron as code

One-off commands are fine for getting started; the manifest is how you run SteadyCron for real.
Declare your entire account — jobs, heartbeat monitors, alert channels, tags, variables — in a
YAML file, commit it, and reconcile with a single command:

```bash
steadycron sync steadycron.yaml --namespace prod
```

You already have a starting manifest: `init` wrote `steadycron.yaml` (your live account) and
`steadycron_example.yaml` (annotated reference). Or generate either from scratch — see
[`export`](#export--pull-the-current-account-state-as-a-manifest) and
[`manifest scaffold`](#manifest-scaffold--boilerplate-manifest-generator).

### The v2 manifest

A manifest declares your whole account as code. The server reconciles from this single source of
truth — every schedule, alert rule, and monitoring configuration is version-controlled and
reviewable in a pull request.

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

See [`examples/steadycron.yaml`](examples/steadycron.yaml) for the complete field reference, or
run `steadycron manifest scaffold` for a fully commented boilerplate.

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

### `manifest scaffold` — boilerplate manifest generator

`steadycron manifest scaffold` generates a fully documented boilerplate manifest (or Terraform
HCL) that covers every SteadyCron feature. Use it to get started without reading the docs or
creating resources in the dashboard first.

```bash
steadycron manifest scaffold                          # print documented YAML to stdout
steadycron manifest scaffold -o steadycron.yaml       # write to file (refuses to overwrite)
steadycron manifest scaffold --terraform              # Terraform HCL instead of YAML
steadycron manifest scaffold --terraform -o main.tf   # write Terraform boilerplate to file
```

The generated manifest includes a namespace, template variables with `${ENV_VAR}` explanation,
tags, all five alert channel kinds (email, Slack, webhook, Discord, Telegram), a fully annotated
HTTP job (every field), a heartbeat monitor, and inline alert rules. Every field has a comment
explaining what it does and listing valid values. Delete the sections you don't need, then run
`steadycron plan steadycron.yaml` to preview.

The Terraform boilerplate uses the exact resource and attribute names from the live
`steadycron/steadycron` provider — `steadycron_http_job`, `steadycron_heartbeat_monitor`,
`steadycron_alert_channel`, `steadycron_alert_rule`, `steadycron_tag`, and
`steadycron_template_variable` — so it applies out of the box with `terraform init && terraform apply`.

`manifest scaffold` requires no API key and no configuration — run it any time.

### `manifest add` — append a resource to an existing manifest

`steadycron manifest add <resource>` appends a single job, channel, tag, or template variable to
an existing manifest — the manifest-first way to grow `steadycron.yaml` over time, instead of
hand-editing YAML or creating resources imperatively with `jobs create`/`channels create`. Like
`manifest scaffold`, it never calls the API and never requires a key: it edits the file directly,
additively, and only after validating the result.

```bash
# Fully via flags — no prompts, safe for scripts
steadycron manifest add job --kind heartbeat --name nightly-backup --schedule "0 2 * * *"
steadycron manifest add channel --kind slack --name ops-slack
steadycron manifest add tag env staging --color yellow
steadycron manifest add variable api_token
```

Missing values are prompted for interactively — schedule prefills `*/15 * * * *`, timezone is the
same UTC/local/manual selector `init` uses, and the grace period default is derived from the
schedule, all computed locally (no API call):

```
$ steadycron manifest add job
? Kind:
  › heartbeat
    http
Job name: nightly-backup
Schedule (cron) [*/15 * * * *]: ⏎
? Timezone:
  › UTC
    Local (Europe/Berlin)
    Other…
Grace period seconds [1800]: ⏎
✓ Added job nightly-backup to steadycron.yaml
Preview: steadycron plan steadycron.yaml
```

Comments and formatting outside the inserted block are never touched — `add` locates the right
section (creating it if absent, or the file itself if it doesn't exist yet) and appends after the
last existing item. The candidate result is validated before anything is written; a failure (or a
duplicate `id`/`name`) leaves the original file byte-identical and exits non-zero.

Every secret-bearing field — channel webhook URLs, bot tokens, template variable values — is
always emitted as an `${ENV_VAR}` placeholder with an explanatory comment. `add` never prompts for
a secret value, so nothing you type ever ends up committed to the manifest in plaintext.

`--dry-run` prints the generated block without writing anything; `-f/--file <path>` targets a
manifest other than `steadycron.yaml`; `steadycron manifest g` is the short alias. `--terraform` is
reserved for a future release and currently exits with an error.

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
steadycron report                  # Overview of the last 24 h
steadycron report --hours 6        # shorter window
steadycron report --hours 168      # last 7 d (Developer plan and above)
steadycron report --hours 720      # last 30 d (Team plan only)
steadycron report --verbose        # adds HTTP response bodies and full alert delivery list
steadycron report --json           # machine-readable (CI alerting, dashboards)
```

The report mirrors the web Overview dashboard — same calculations, same terminology, so the
numbers always agree for an identical time window. It calls `/api/reports/summary` and shows:

| Section | What you see |
|---|---|
| **KPI row** | Total checks (HTTP + heartbeat), Successful + success-rate %, Incidents (failed checks), Alerts delivered/failed/suppressed, Jobs reporting |
| **Active issues** | Jobs with failures or missed check-ins, ranked by attention severity: missed → abandoned → failure → late |
| **Silent monitors** | Jobs with zero activity in the window — possible schedule drift or misconfiguration |
| **Footer** | One-line health verdict; exits non-zero when failures or undelivered alerts are detected |

Plan limits cap how far back you can query (Free: 1 day, Developer: 7 days, Team: 30 days).
The server returns a `range_exceeds_plan` error with a clear message if the requested `--hours` exceeds your limit.

## Logbook

```bash
steadycron logbook                                       # last 24 h of all event types
steadycron logbook --hours 168                           # last 7 days
steadycron logbook --domain executions                   # filter by category
steadycron logbook --domain executions --domain alerts   # multiple categories
steadycron logbook --severity critical                   # critical events only
steadycron logbook --job my-job-key                      # events for a specific job
steadycron logbook --page 2 --page-size 100             # paginate
steadycron logbook --all                                 # fetch all pages
steadycron logbook --verbose                             # full metadata per event
steadycron logbook --all --json                          # machine-readable output
```

Mirrors the web Logbook page. Available `--domain` values:

| Domain | Event types included |
|---|---|
| `executions` | HTTP execution succeeded / failed |
| `heartbeats` | Heartbeat missed / recovered, ping received, run started / abandoned |
| `alerts` | Alert delivered / failed / suppressed / pending |
| `jobs` | Job created / deleted / paused / resumed |
| `keys` | API key created / revoked |
| `rules` | Alert rule created / deleted |
| `channels` | Alert channel created / updated / deleted |
| `subscription` | Plan upgrade / downgrade / cancellation / past-due / paused |

`--severity` accepts `info`, `warning`, or `critical` (repeatable). `--job` accepts a job key,
name, or id. `--all` pages through all results; omitting it returns the first `--page-size`
events (default 50, max 100). `--verbose` shows every metadata field per event (HTTP status,
error type, response excerpt, source IP, etc.).

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (or `plan` with no drift when `--detailed-exitcode` is not set) |
| `1` | Unexpected error |
| `2` | Manifest load/validation error; also `plan --detailed-exitcode` when drift is detected |
| `3` | API error |
| `4` | Missing/invalid credentials (`401`/`403`) — run `steadycron signup` or `steadycron login` |
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
