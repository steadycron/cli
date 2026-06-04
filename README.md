# SteadyCron CLI

The official command-line interface for [SteadyCron](https://steadycron.com) — schedule, run, and
monitor cron jobs as code. Declare your entire account — jobs, alert channels, tags, variables —
in a YAML manifest, commit it to your repo, and reconcile with a single command:

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

A manifest declares your whole account: channels, tags, variables, and jobs. The server reconciles
from this single source of truth.

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
steadycron export --scope jobs -o jobs.yaml              # jobs only
steadycron export --scope job weekly-digest-email        # single job → stdout
steadycron export --namespace prod                       # stamp namespace in the output
steadycron export --format json                          # JSON instead of YAML
```

Writes the manifest verbatim from the server. Secret fields are replaced with `${PLACEHOLDER}`
references; the CLI prints a summary of required environment variables to stderr so piping stays
clean.

Useful for bootstrapping: export your current account, commit the result, and manage it as code
going forward.

### Multi-file manifests

Separate concerns across files or directories:

```bash
steadycron validate ./manifests/
steadycron plan ./manifests/ --namespace prod
steadycron apply manifests/channels.yaml manifests/jobs.yaml --namespace prod
```

When multiple files are used, they must agree on `version` and `namespace`. Duplicate resource `id`
values across files are an error.

## Cron as Code in CI

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
  sync:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: ./action   # or: steadycron/sync-action@v1
        with:
          manifest-path: steadycron/
          namespace: prod
          prune: "true"
          api-key: ${{ secrets.STEADYCRON_API_KEY }}
          mode: auto   # plan on PRs, apply on push to main
```

The action:
1. Installs the CLI (pinned version).
2. On `pull_request` — runs `steadycron plan --output json`, formats the plan as Markdown, and
   **posts/updates a sticky PR comment** (find-and-replace so re-runs update in place). Fails the
   check if the plan has errors (limit violations, conflicts, etc.).
3. On `push` to the default branch — runs `steadycron apply --yes`.

See [`examples/ci/`](examples/ci/) for standalone `pull_request` and `push` workflow files.

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

steadycron rules list nightly-db-backup
steadycron rules add nightly-db-backup \
  --channel "Ops email" --trigger missed_heartbeat --severity p1
```

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
