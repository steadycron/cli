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
    [Description("Heartbeat job key, name, or id.")]
    public string Identifier { get; set; } = "";

    [CommandOption("--lang <LANG>")]
    [Description("Output language: bash (default), python, node, github-actions.")]
    public string Lang { get; set; } = "bash";
}

/// <summary>
/// `steadycron jobs snippet &lt;job&gt; [--lang]` — generates ready-to-paste integration code
/// that wires a heartbeat monitor's ping URLs into a script or workflow. The snippet is
/// written to stdout so it can be piped or redirected directly into a file.
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

        if (job.Kind != "heartbeat")
        {
            throw new CliException(
                $"'{job.Name}' is an HTTP job — snippets are only generated for heartbeat monitors.",
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
                snippet = GenerateSnippet(settings.Lang, job.Name, job.CronExpression, job.PingUrls),
            });
            return ExitCodes.Ok;
        }

        Console.Write(GenerateSnippet(settings.Lang, job.Name, job.CronExpression, job.PingUrls));
        return ExitCodes.Ok;
    }

    private static string GenerateSnippet(string lang, string jobName, string? cron, PingUrls urls) =>
        lang.ToLowerInvariant() switch
        {
            "python" => PythonSnippet(jobName, urls),
            "node" => NodeSnippet(jobName, urls),
            "github-actions" => GitHubActionsSnippet(jobName, cron, urls),
            _ => BashSnippet(jobName, urls),
        };

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
