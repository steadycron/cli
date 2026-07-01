using Spectre.Console;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Sync;

/// <summary>
/// Core reconcile flow shared by <c>sync</c>, <c>plan</c>, and <c>apply</c>.
/// Loads the manifest, calls <c>POST /api/reconcile</c>, renders the server's plan,
/// and optionally applies.
/// </summary>
public sealed class ReconcileEngine
{
    private readonly ManifestLoader _loader;

    public ReconcileEngine(ManifestLoader loader)
    {
        _loader = loader;
    }

    public async Task<int> RunAsync(
        SyncSettings settings,
        OutputContext output,
        SteadyCronClient client,
        string apiUrl,
        bool forceDryRun,
        bool forceYes,
        CancellationToken ct)
    {
        // Load + env-interpolate. The env-file guardrail (mandatory when the manifest references
        // required ${...} placeholders) is enforced here, before any API call.
        ManifestFile manifest;
        try
        {
            var getVar = ManifestEnvironment.Build(
                settings.EffectivePaths, settings.EnvFileList,
                allowProcessEnv: settings.AllowProcessEnv, enforceEnvFile: true);
            manifest = _loader.LoadFromPaths(settings.EffectivePaths, getVar);
        }
        catch (ManifestException ex)
        {
            output.Error(ex.Message);
            return ExitCodes.ManifestError;
        }

        if (!settings.NoWarnV1 && ManifestLoader.IsV1(manifest))
        {
            output.Warn("Manifest version 1 is deprecated. Run 'steadycron export' to upgrade to v2.");
        }

        // Resolve namespace: CLI flag overrides manifest field
        var ns = settings.Namespace ?? manifest.Namespace;

        var isDryRun = forceDryRun || settings.DryRun;
        var isYes = forceYes || settings.Yes;

        if (settings.Prune && ns is null)
        {
            output.Error("--prune requires --namespace (or a 'namespace' field in the manifest).");
            return ExitCodes.ManifestError;
        }

        // Phase 1: dry-run to get the server's plan
        var planResponse = await client.ReconcileAsync(
            new ReconcileRequest
            {
                Manifest = manifest,
                Namespace = ns,
                DryRun = true,
                Prune = settings.Prune,
            }, ct);

        if (output.Json)
        {
            return await RunJsonAsync(settings, output, client, manifest, ns, planResponse, isDryRun, ct);
        }

        var paths = string.Join(", ", settings.EffectivePaths);
        output.Markup(
            $"[bold]SteadyCron sync[/] [grey]· {OutputContext.Escape(paths)} → " +
            $"{OutputContext.Escape(apiUrl)}[/]\n");

        ReconcilePlanRenderer.Render(output, planResponse);

        // Errors in plan → no apply; exit 5
        if (planResponse.Errors.Count > 0)
        {
            output.Error("\nPlan has errors — apply blocked. Fix the above and retry.");
            return ExitCodes.PlanErrors;
        }

        // Dry-run only
        if (isDryRun)
        {
            output.Info("\nDry run — no changes applied.");
            return ResolveDryRunExit(planResponse, settings.DetailedExitCode);
        }

        // Nothing to do
        if (!planResponse.HasWork)
        {
            output.Success("\nAlready in sync — nothing to do.");
            return ExitCodes.Ok;
        }

        // Prompt before apply (interactive TTY only, unless --yes)
        if (!isYes && IsInteractive())
        {
            output.Line();
            if (!output.Out.Confirm("Apply these changes?", defaultValue: false))
            {
                output.Info("Aborted.");
                return ExitCodes.Ok;
            }
        }

        output.Line();

        // Phase 2: apply
        var applyResponse = await client.ReconcileAsync(
            new ReconcileRequest
            {
                Manifest = manifest,
                Namespace = ns,
                DryRun = false,
                Prune = settings.Prune,
            }, ct);

        var exitCode = RenderApplyResult(output, applyResponse);
        await RenderCreatedHeartbeatPingUrlsAsync(output, applyResponse, client, ct);
        return exitCode;
    }

    // ── JSON path ─────────────────────────────────────────────────────────────────

    private static async Task<int> RunJsonAsync(
        SyncSettings settings,
        OutputContext output,
        SteadyCronClient client,
        ManifestFile manifest,
        string? ns,
        ReconcileResponse planResponse,
        bool isDryRun,
        CancellationToken ct)
    {
        if (isDryRun || planResponse.Errors.Count > 0)
        {
            output.WriteJson(planResponse);
            return planResponse.Errors.Count > 0
                ? ExitCodes.PlanErrors
                : ResolveDryRunExit(planResponse, settings.DetailedExitCode);
        }

        var applyResponse = await client.ReconcileAsync(
            new ReconcileRequest
            {
                Manifest = manifest,
                Namespace = ns,
                DryRun = false,
                Prune = settings.Prune,
            }, ct);

        output.WriteJson(applyResponse);
        return applyResponse.Errors.Count > 0 ? ExitCodes.PlanErrors : ExitCodes.Ok;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static int RenderApplyResult(OutputContext output, ReconcileResponse result)
    {
        var parts = new List<string>
        {
            $"[green]{result.Summary.Create} created[/]",
            $"[yellow]{result.Summary.Update} updated[/]",
        };

        if (result.Summary.Delete > 0) { parts.Add($"[red]{result.Summary.Delete} deleted[/]"); }

        output.Markup($"\n[bold]Done:[/] {string.Join(", ", parts)}");

        if (result.Errors.Count > 0)
        {
            foreach (var e in result.Errors)
            {
                output.Error($"{e.Code}: {e.Message}");
            }

            output.Warn("Apply completed with errors — see above.");
            return ExitCodes.PlanErrors;
        }

        output.Success("Sync complete.");
        return ExitCodes.Ok;
    }

    private static int ResolveDryRunExit(ReconcileResponse plan, bool detailedExitCode)
    {
        if (!detailedExitCode) { return ExitCodes.Ok; }

        return plan.HasWork ? ExitCodes.ManifestError : ExitCodes.Ok;
    }

    private static bool IsInteractive() =>
        !Console.IsInputRedirected && !Console.IsOutputRedirected;

    /// <summary>
    /// After a successful apply, fetches all heartbeat jobs whose keys appear in the
    /// create-action list and prints their ping URLs. This saves users a follow-up
    /// `jobs ping-urls` call when setting up new monitors.
    /// </summary>
    private static async Task RenderCreatedHeartbeatPingUrlsAsync(
        OutputContext output,
        ReconcileResponse result,
        SteadyCronClient client,
        CancellationToken ct)
    {
        var createdJobKeys = result.Changes
            .Where(c => c.Action is "create" && c.Resource is "job")
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (createdJobKeys.Count == 0) { return; }

        var heartbeatJobs = await client.ListAllJobsAsync(kind: "heartbeat", ct: ct);
        var newMonitors = heartbeatJobs
            .Where(j => j.JobKey is not null
                        && createdJobKeys.Contains(j.JobKey)
                        && j.PingUrls is not null)
            .ToList();

        if (newMonitors.Count == 0) { return; }

        output.Line();
        output.Markup(newMonitors.Count == 1
            ? "[grey]Heartbeat ping URLs for the new monitor:[/]"
            : $"[grey]Heartbeat ping URLs for {newMonitors.Count} new monitors:[/]");
        output.Line();

        foreach (var job in newMonitors)
        {
            JobFormatting.RenderPingUrls(output, job.Name, job.PingUrls!);
            output.Line();
        }
    }
}
