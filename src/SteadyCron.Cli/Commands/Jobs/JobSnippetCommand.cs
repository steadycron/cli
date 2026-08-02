using System.ComponentModel;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

public sealed class JobSnippetSettings : CliSettings
{
    [CommandArgument(0, "<JOB>")]
    [Description("Heartbeat or agent monitor key, name, or id.")]
    public string Identifier { get; set; } = "";

    [CommandOption("--lang <LANG>")]
    [Description("Output language: bash (default), python, node, github-actions.")]
    public string Lang { get; set; } = "bash";
}

/// <summary>
/// `steadycron jobs snippet &lt;job&gt; [--lang]` — generates ready-to-paste integration code
/// that wires a monitor's ping URLs into a script or workflow. The snippet is
/// written to stdout so it can be piped or redirected directly into a file.
///
/// <para>An agent monitor gets a different snippet from a heartbeat: its completion call carries a
/// JSON run report, and its <c>/start</c> is mandatory rather than optional. Emitting the heartbeat
/// snippet for one would teach a contract the server rejects.</para>
/// </summary>
public sealed class JobSnippetCommand : SteadyCronCommandBase<JobSnippetSettings>
{
    private static readonly HashSet<string> SupportedLangs =
        new(["bash", "python", "node", "github-actions"], StringComparer.OrdinalIgnoreCase);

    public JobSnippetCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c)
        : base(r, f, c) { }

    protected override async Task<int> RunAsync(JobSnippetSettings settings, OutputContext output, CancellationToken ct)
    {
        if (!SupportedLangs.Contains(settings.Lang))
        {
            throw new CliException(
                $"Unknown language '{settings.Lang}'. Supported: bash, python, node, github-actions.",
                ExitCodes.Error);
        }

        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Identifier, ct);

        if (!job.IsPingDriven)
        {
            throw new CliException(
                $"'{job.Name}' is an HTTP job — snippets are only generated for heartbeat and agent monitors.",
                ExitCodes.Error);
        }

        if (job.PingUrls is null)
        {
            throw new CliException(
                $"Ping URLs not available for '{job.Name}'. Try 'steadycron jobs get {job.JobKey}'.",
                ExitCodes.Error);
        }

        if (output.Json)
        {
            output.WriteJson(new
            {
                job_key = job.JobKey,
                job_name = job.Name,
                lang = settings.Lang.ToLowerInvariant(),
                snippet = GenerateSnippet(settings.Lang, job),
            });
            return ExitCodes.Ok;
        }

        Console.Write(GenerateSnippet(settings.Lang, job));
        return ExitCodes.Ok;
    }

    private static string GenerateSnippet(string lang, JobResponse job)
    {
        var urls = job.PingUrls!;
        var itemsLabel = string.IsNullOrWhiteSpace(job.ItemsLabel) ? "items" : job.ItemsLabel.Trim();

        return lang.ToLowerInvariant() switch
        {
            "python" => job.IsAgent ? AgentPythonSnippet(job.Name, itemsLabel, urls) : PythonSnippet(job.Name, urls),
            "node" => job.IsAgent ? AgentNodeSnippet(job.Name, itemsLabel, urls) : NodeSnippet(job.Name, urls),
            "github-actions" => job.IsAgent
                ? AgentGitHubActionsSnippet(job.Name, job.CronExpression, urls)
                : GitHubActionsSnippet(job.Name, job.CronExpression, urls),
            _ => job.IsAgent ? AgentBashSnippet(job.Name, itemsLabel, urls) : BashSnippet(job.Name, urls),
        };
    }

    // $""" is fine here: no literal { needed in the bash output
    private static string BashSnippet(string name, PingUrls urls) =>
        $"""
        #!/usr/bin/env bash
        # SteadyCron heartbeat: {name}
        # Wrap your script body with these curl calls so SteadyCron knows it ran.
        set -euo pipefail

        # Signal run started (optional — enables stuck-run detection on the dashboard)
        curl -fsS --retry 3 "{urls.Start}" > /dev/null

        # On any error, signal failure before the script exits.
        trap 'curl -fsS --retry 3 "{urls.Fail}" > /dev/null' ERR

        # ── Your script here ──────────────────────────────────────────────────────────
        echo "Running {name}..."
        # ... your commands ...
        # ─────────────────────────────────────────────────────────────────────────────

        # Signal success (remove the trap so it doesn't fire on the curl itself)
        trap - ERR
        curl -fsS --retry 3 "{urls.Success}" > /dev/null

        """;

    // $$""" so single { is literal (needed for Python f-string {PING_BASE} syntax)
    private static string PythonSnippet(string name, PingUrls urls) =>
        $$"""
        # SteadyCron heartbeat: {{name}}
        # Wrap your script body with these calls so SteadyCron knows it ran.
        import urllib.request

        PING_BASE = "{{urls.Success[..urls.Success.LastIndexOf('/')]}}"

        # Signal run started (optional — enables stuck-run detection on the dashboard)
        urllib.request.urlopen(f"{PING_BASE}/start")

        try:
            # ── Your code here ────────────────────────────────────────────────────────
            print("Running {{name}}...")
            # ... your code ...
            # ─────────────────────────────────────────────────────────────────────────

            urllib.request.urlopen(f"{PING_BASE}/success")
        except Exception:
            urllib.request.urlopen(f"{PING_BASE}/fail")
            raise

        """;

    // $$""" so single { is literal (needed for JS template literal ${PING_BASE} syntax)
    private static string NodeSnippet(string name, PingUrls urls) =>
        $$"""
        // SteadyCron heartbeat: {{name}}
        // Wrap your script body with these calls so SteadyCron knows it ran.
        const PING_BASE = "{{urls.Success[..urls.Success.LastIndexOf('/')]}}";

        async function run() {
          // Signal run started (optional — enables stuck-run detection on the dashboard)
          await fetch(`${PING_BASE}/start`);

          try {
            // ── Your code here ──────────────────────────────────────────────────────
            console.log("Running {{name}}...");
            // ... your code ...
            // ────────────────────────────────────────────────────────────────────────

            await fetch(`${PING_BASE}/success`);
          } catch (err) {
            await fetch(`${PING_BASE}/fail`);
            throw err;
          }
        }

        run().catch((err) => {
          console.error(err);
          process.exit(1);
        });

        """;

    // ── Agent monitors ───────────────────────────────────────────────────────────
    // Two things separate these from the heartbeat snippets: /start is mandatory (a /success with
    // no run open is refused with 422), and the completion call carries a JSON run report.
    // itemsProduced is the field that matters — it is what the empty-result rule reads.

    // $$""" so a single { is literal — the run report body is JSON, which the heartbeat
    // snippets never had to carry.
    private static string AgentBashSnippet(string name, string itemsLabel, PingUrls urls) =>
        $$"""
        #!/usr/bin/env bash
        # SteadyCron agent monitor: {{name}}
        # A run is an ordered PAIR of calls — /start is required, not optional.
        set -euo pipefail

        # 1. Signal the run started. Without this the completion call is rejected.
        curl -fsS --retry 3 "{{urls.Start}}" > /dev/null

        # On any error, report the failure before the script exits.
        trap 'curl -fsS --retry 3 -X POST "{{urls.Fail}}" \
                -H "Content-Type: application/json" \
                -d "{\"summary\": \"agent run failed\"}" > /dev/null' ERR

        # ── Your agent here ───────────────────────────────────────────────────────────
        # Capture what it produced — this is what SteadyCron judges the run on.
        ITEMS_PRODUCED=0   # TODO: set from your agent's real output ({{itemsLabel}})
        # ─────────────────────────────────────────────────────────────────────────────

        # 2. Report the completed run. Every field is optional except, in practice,
        #    itemsProduced: a run reporting 0 is treated as a failed run.
        trap - ERR
        curl -fsS --retry 3 -X POST "{{urls.Success}}" \
          -H "Content-Type: application/json" \
          -d "{\"itemsProduced\": $ITEMS_PRODUCED, \"model\": \"claude-opus-5\", \"summary\": \"run complete\"}" \
          > /dev/null

        """;

    private static string AgentPythonSnippet(string name, string itemsLabel, PingUrls urls) =>
        $$"""
        # SteadyCron agent monitor: {{name}}
        # A run is an ordered PAIR of calls — /start is required, not optional.
        import json
        import urllib.request

        PING_BASE = "{{urls.Success[..urls.Success.LastIndexOf('/')]}}"


        def report(kind: str, payload: dict | None = None) -> None:
            data = json.dumps(payload).encode() if payload is not None else None
            req = urllib.request.Request(
                f"{PING_BASE}/{kind}",
                data=data,
                headers={"Content-Type": "application/json"} if data else {},
            )
            urllib.request.urlopen(req)


        # 1. Signal the run started. Without this the completion call is rejected.
        report("start")

        try:
            # ── Your agent here ───────────────────────────────────────────────────────
            # Capture what it produced ({{itemsLabel}}) — this is what the run is judged on.
            items_produced = 0
            # ─────────────────────────────────────────────────────────────────────────

            # 2. Report the completed run. A run reporting 0 items is treated as failed.
            report("success", {
                "itemsProduced": items_produced,
                "model": "claude-opus-5",
                "tokensIn": 0,
                "tokensOut": 0,
                "summary": "run complete",
            })
        except Exception as err:
            report("fail", {"summary": str(err)})
            raise

        """;

    private static string AgentNodeSnippet(string name, string itemsLabel, PingUrls urls) =>
        $$"""
        // SteadyCron agent monitor: {{name}}
        // A run is an ordered PAIR of calls — /start is required, not optional.
        const PING_BASE = "{{urls.Success[..urls.Success.LastIndexOf('/')]}}";

        const report = (kind, payload) =>
          fetch(`${PING_BASE}/${kind}`, {
            method: payload ? "POST" : "GET",
            headers: payload ? { "Content-Type": "application/json" } : undefined,
            body: payload ? JSON.stringify(payload) : undefined,
          });

        async function run() {
          // 1. Signal the run started. Without this the completion call is rejected.
          await report("start");

          try {
            // ── Your agent here ─────────────────────────────────────────────────────
            // Capture what it produced ({{itemsLabel}}) — this is what the run is judged on.
            const itemsProduced = 0;
            // ────────────────────────────────────────────────────────────────────────

            // 2. Report the completed run. A run reporting 0 items is treated as failed.
            await report("success", {
              itemsProduced,
              model: "claude-opus-5",
              tokensIn: 0,
              tokensOut: 0,
              summary: "run complete",
            });
          } catch (err) {
            await report("fail", { summary: String(err) });
            throw err;
          }
        }

        run().catch((err) => {
          console.error(err);
          process.exit(1);
        });

        """;

    // $$$""" so {{ is two literal braces (needed for GitHub Actions ${{ secrets.X }} syntax)
    private static string AgentGitHubActionsSnippet(string name, string? cron, PingUrls urls)
    {
        var scheduleComment = cron is not null
            ? $"    - cron: \"{cron}\"   # matches the SteadyCron schedule"
            : "    - cron: \"0 3 * * *\"   # TODO: set this to match your SteadyCron schedule";
        var baseUrl = urls.Success[..urls.Success.LastIndexOf('/')];
        var jobSlug = name.ToLowerInvariant().Replace(' ', '-');

        return $$$"""
            # SteadyCron agent monitor: {{{name}}}
            # A run is an ordered PAIR of calls — /start is required, not optional, and the
            # completion call carries a JSON run report.
            # Store the base ping URL as a repository secret named STEADYCRON_PING_URL:
            #   Settings → Secrets and variables → Actions → New repository secret
            #   Value: {{{baseUrl}}}
            on:
              schedule:
            {{{scheduleComment}}}

            jobs:
              {{{jobSlug}}}:
                runs-on: ubuntu-latest
                steps:
                  - name: SteadyCron — run started
                    run: curl -fsS "${{ secrets.STEADYCRON_PING_URL }}/start" > /dev/null

                  # ── Your agent here ─────────────────────────────────────────────────
                  # Write what it produced to $GITHUB_ENV so the report step can read it.
                  - name: Run {{{name}}}
                    run: |
                      echo "TODO: replace with your actual agent step"
                      echo "ITEMS_PRODUCED=0" >> "$GITHUB_ENV"
                  # ────────────────────────────────────────────────────────────────────

                  - name: SteadyCron — report run
                    if: success()
                    run: |
                      curl -fsS -X POST "${{ secrets.STEADYCRON_PING_URL }}/success" \
                        -H "Content-Type: application/json" \
                        -d "{\"itemsProduced\": ${ITEMS_PRODUCED:-0}, \"summary\": \"run complete\"}" \
                        > /dev/null

                  - name: SteadyCron — report failure
                    if: failure()
                    run: |
                      curl -fsS -X POST "${{ secrets.STEADYCRON_PING_URL }}/fail" \
                        -H "Content-Type: application/json" \
                        -d '{"summary": "workflow failed"}' > /dev/null

            """;
    }

    // $$$""" so {{ is two literal braces (needed for GitHub Actions ${{ secrets.X }} syntax)
    private static string GitHubActionsSnippet(string name, string? cron, PingUrls urls)
    {
        var scheduleComment = cron is not null
            ? $"    - cron: \"{cron}\"   # matches the SteadyCron schedule"
            : "    - cron: \"0 * * * *\"   # TODO: set this to match your SteadyCron schedule";
        var baseUrl = urls.Success[..urls.Success.LastIndexOf('/')];
        var jobSlug = name.ToLowerInvariant().Replace(' ', '-');

        return $$$"""
            # SteadyCron heartbeat: {{{name}}}
            # Add the steps below to your workflow job so SteadyCron knows when it ran.
            # Store the base ping URL as a repository secret named STEADYCRON_PING_URL:
            #   Settings → Secrets and variables → Actions → New repository secret
            #   Value: {{{baseUrl}}}
            on:
              schedule:
            {{{scheduleComment}}}

            jobs:
              {{{jobSlug}}}:
                runs-on: ubuntu-latest
                steps:
                  - name: Ping SteadyCron — started
                    run: curl -fsS "${{ secrets.STEADYCRON_PING_URL }}/start" > /dev/null

                  # ── Your steps here ─────────────────────────────────────────────────
                  - name: Run {{{name}}}
                    run: echo "TODO: replace with your actual step"
                  # ────────────────────────────────────────────────────────────────────

                  - name: Ping SteadyCron — success
                    if: success()
                    run: curl -fsS "${{ secrets.STEADYCRON_PING_URL }}/success" > /dev/null

                  - name: Ping SteadyCron — failed
                    if: failure()
                    run: curl -fsS "${{ secrets.STEADYCRON_PING_URL }}/fail" > /dev/null

            """;
    }
}
