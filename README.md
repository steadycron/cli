# SteadyCron CLI

The official command-line interface for [SteadyCron](https://steadycron.com) — schedule, run, and
monitor cron jobs. SteadyCron is **infrastructure-as-code first**: declare your scheduled HTTP jobs
and heartbeat monitors in a YAML manifest, commit it to your repo, and reconcile your account with a
single command.

```bash
steadycron sync jobs.yaml
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
[Releases](https://github.com/steadycron/cli/releases) page — no .NET runtime required. Put it on
your `PATH`:

```bash
# example: Linux x64
curl -Lo steadycron https://github.com/steadycron/cli/releases/latest/download/steadycron-linux-x64
chmod +x steadycron && sudo mv steadycron /usr/local/bin/
```

## Authenticate

The CLI authenticates with a SteadyCron **API key** (`sc_…`). Create one in the dashboard under
**Settings → API keys**, then provide it via an environment variable so it never lands in your
manifest:

```bash
export STEADYCRON_API_KEY=sc_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Configuration is resolved in this order (first wins):

1. `--api-key` / `--api-url` flags
2. `STEADYCRON_API_KEY` / `STEADYCRON_API_URL` environment variables
3. The config file (`steadycron config set --api-key sc_...`)
4. Built-in defaults (`--api-url` defaults to `https://api.steadycron.com`)

Check what's resolved and verify connectivity:

```bash
steadycron config show --check
```

> A **read-only** API key can run `jobs list/get/logs` and `cron preview`. Mutating commands
> (`sync` without `--dry-run`, `jobs pause/resume/run/delete`) require a **full** key.

## The manifest

A manifest is a declarative list of jobs — HTTP jobs (SteadyCron calls your endpoint) and heartbeat
checks (your job pings SteadyCron). The reconciliation key is each job's **`name`**.

```yaml
version: 1
jobs:
  - name: weekly-digest-email
    kind: http
    method: POST
    url: https://api.myapp.com/jobs/digest
    schedule: "0 9 * * 1"        # cron; or use `interval: 900` (seconds)
    timezone: Europe/Berlin
    timeout: 120
    retries: 3
    headers:
      Authorization: "Bearer {{digest_token}}"
    body: '{"segment":"weekly"}'

  - name: nightly-db-backup
    kind: heartbeat
    schedule: "0 2 * * *"
    grace: 1800
```

See [`examples/jobs.yaml`](https://github.com/steadycron/cli/blob/main/examples/jobs.yaml) for the full field set.

### Manifest fields

| Field | Applies to | Notes |
|---|---|---|
| `name` | all | **Required.** Unique; the reconciliation key. |
| `kind` | all | `http` (default) or `heartbeat`. |
| `description` | all | Optional. |
| `schedule` | all | 5-field cron expression. Mutually exclusive with `interval`. |
| `interval` | all | Seconds (10–86400). Mutually exclusive with `schedule`. |
| `timezone` | all | IANA name (default `UTC`). |
| `paused` | all | Create/keep the job paused. |
| `method` | http | `GET` (default), `POST`, `PUT`, `PATCH`, `DELETE`. |
| `url` | http | **Required for HTTP jobs.** Supports `{{template_variable}}`. |
| `headers` | http | Map of header name → value. |
| `body` | http | Request body. |
| `timeout` | http | Seconds (default 60). |
| `retries` | http | Max retries (default 0). |
| `retry_backoff` | http | Backoff seconds (default 30). |
| `retry_on_timeout` | http | Default `true`. |
| `retry_on_status` | http | List of HTTP status codes to retry on. Omit = any non-2xx. |
| `skip_if_running` | http | Default `false`. |
| `misfire_policy` | http | `do_nothing` (default) or `fire_once_now`. |
| `grace` | heartbeat | Seconds of grace before "missed" (default 60). |
| `stuck_run_detection` | heartbeat | Default `true`. |
| `max_run_duration` | heartbeat | Seconds; omit to let the server compute it. |

> Omitted optional fields are reconciled to their documented defaults — the manifest is the source
> of truth. The one exception is heartbeat `max_run_duration`, which is only managed when you set it.

## Syncing

```bash
# Preview changes without applying (great for CI / pull requests)
steadycron sync jobs.yaml --dry-run

# Apply: create new jobs, update changed ones, report jobs not in the manifest
steadycron sync jobs.yaml

# Also delete jobs that are on the server but not in the manifest
steadycron sync jobs.yaml --prune --yes
```

`sync` is declarative: it **creates** jobs that are new, **updates** jobs whose definition changed,
and **reports** jobs that exist on the server but not in the file. Pass `--prune` to delete those
orphans. Run it from CI on every merge to keep environments identical and eliminate drift. In a
non-interactive shell (CI) it applies without prompting; in an interactive terminal it asks first
unless you pass `--yes`.

## Managing jobs directly

```bash
steadycron jobs list                       # table of all jobs
steadycron jobs list --kind heartbeat --status missed
steadycron jobs get weekly-digest-email    # by name or id
steadycron jobs logs warm-cdn-cache -n 20  # recent executions
steadycron jobs pause weekly-digest-email
steadycron jobs resume weekly-digest-email
steadycron jobs run warm-cdn-cache         # trigger now (HTTP jobs)
steadycron jobs delete old-job --yes

# Create a single job from flags (same validation/defaults as the manifest)
steadycron jobs create --name warm-cache --url https://api.myapp.com/warm \
  --method GET --interval 900 --skip-if-running
steadycron jobs create --name nightly-backup --kind heartbeat \
  --schedule "0 2 * * *" --grace 1800

steadycron cron preview "*/15 9-17 * * 1-5" --timezone Europe/Berlin
```

## Tags, variables, alert channels & rules

```bash
# Tags
steadycron tags list
steadycron tags create env prod --color green
steadycron tags delete env:prod

# Template variables ({{name}} placeholders used in HTTP job URLs/headers/body)
steadycron vars list
steadycron vars set digest_token "sk_live_…"
steadycron vars delete digest_token

# Alert channels
steadycron channels list
steadycron channels create --name "Ops email"   --kind email    --to ops@example.com
steadycron channels create --name "Eng Slack"    --kind slack    --webhook-url https://hooks.slack.com/services/…
steadycron channels create --name "Deploy hook"  --kind webhook  --url https://example.com/hook --header "Authorization: Bearer …"
steadycron channels test "Ops email"
steadycron channels delete "Ops email"

# Per-job alert rules
steadycron rules list nightly-db-backup
steadycron rules add nightly-db-backup --channel "Ops email" --trigger missed_heartbeat --severity p1
steadycron rules add warm-cdn-cache --channel "Eng Slack" --trigger slow_run --factor 3 --min-samples 5
steadycron rules delete <rule-id>
```

Triggers: `failure`, `n_consecutive`, `missed_heartbeat`, `recovery`, `slow_run`, `size_anomaly`.
Channel kinds: `email`, `slack`, `discord`, `webhook`, `telegram`.

Add `--json` to any command for machine-readable output (ideal for scripting).

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Unexpected error |
| `2` | Manifest load/validation error |
| `3` | API error |
| `4` | Missing/invalid credentials (also `401`/`403`) |
| `5` | Sync completed with conflicts or per-job failures |
| `130` | Cancelled (Ctrl+C) |

## Development

```bash
dotnet build
dotnet test
dotnet run --project src/SteadyCron.Cli -- jobs list
```

Built on .NET 10 with [Spectre.Console](https://spectreconsole.net/) and
[YamlDotNet](https://github.com/aaubry/YamlDotNet). The CLI talks to the same REST API that powers
the dashboard; see the [API documentation](https://steadycron.com/docs/api-authentication).

## License

[MIT](https://github.com/steadycron/cli/blob/main/LICENSE) © SteadyCron.
