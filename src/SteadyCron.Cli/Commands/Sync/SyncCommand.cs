using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Sync;

public sealed class SyncSettings : CliSettings
{
    [CommandArgument(0, "[MANIFEST]")]
    [Description("Path to the YAML manifest (default: jobs.yaml).")]
    public string? Manifest { get; set; }

    [CommandOption("--dry-run|--plan")]
    [Description("Show what would change without applying anything.")]
    public bool DryRun { get; set; }

    [CommandOption("--prune")]
    [Description("Delete jobs that exist on the server but are not in the manifest.")]
    public bool Prune { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Apply without the interactive confirmation prompt.")]
    public bool Yes { get; set; }

    public string ManifestPath => string.IsNullOrWhiteSpace(Manifest) ? "jobs.yaml" : Manifest;
}

/// <summary>
/// `steadycron sync [manifest]` — reconciles the account with a YAML manifest: creates new jobs,
/// updates changed ones, reports (or with --prune, deletes) jobs not in the manifest.
/// </summary>
public sealed class SyncCommand : SteadyCronCommandBase<SyncSettings>
{
    private readonly ManifestLoader _loader;
    private readonly SyncPlanner _planner;

    public SyncCommand(
        ConfigResolver resolver,
        SteadyCronClientFactory clientFactory,
        CancellationProvider cancellation,
        ManifestLoader loader,
        SyncPlanner planner)
        : base(resolver, clientFactory, cancellation)
    {
        _loader = loader;
        _planner = planner;
    }

    protected override async Task<int> RunAsync(SyncSettings settings, OutputContext output, CancellationToken ct)
    {
        var manifest = _loader.LoadFromFile(settings.ManifestPath);

        // Validate the manifest before touching the network or requiring credentials.
        var desired = _planner.Normalize(manifest.Jobs ?? []);

        var client = CreateClient(settings);
        var serverJobs = await client.ListAllJobsAsync(ct: ct);
        var plan = _planner.Plan(desired, serverJobs);

        if (output.Json)
        {
            return await RunJsonAsync(settings, output, client, plan, ct);
        }

        output.Markup($"[bold]SteadyCron sync[/] [grey]· {OutputContext.Escape(settings.ManifestPath)} → {OutputContext.Escape(ResolveConfig(settings).ApiUrl)}[/]\n");
        SyncPlanRenderer.Render(output, plan, settings.Prune);

        if (settings.DryRun)
        {
            output.Info("\nDry run — no changes applied.");
            return plan.Conflicts.Count > 0 ? ExitCodes.SyncIncomplete : ExitCodes.Ok;
        }

        var willPrune = settings.Prune && plan.Orphans.Count > 0;
        if (!plan.Creates.Any() && plan.ChangedUpdates.Count == 0 && !willPrune)
        {
            output.Success("\nAlready in sync — nothing to do.");
            return plan.Conflicts.Count > 0 ? ExitCodes.SyncIncomplete : ExitCodes.Ok;
        }

        if (!settings.Yes && IsInteractive())
        {
            output.Line();
            if (!output.Out.Confirm("Apply these changes?", defaultValue: false))
            {
                output.Info("Aborted.");
                return ExitCodes.Ok;
            }
        }

        output.Line();
        var executor = new SyncExecutor(client);
        var result = await executor.ApplyAsync(plan, settings.Prune, e => RenderEvent(output, e), ct);

        return RenderResult(output, plan, result);
    }

    private async Task<int> RunJsonAsync(
        SyncSettings settings, OutputContext output, SteadyCronClient client, SyncPlan plan, CancellationToken ct)
    {
        if (settings.DryRun)
        {
            output.WriteJson(PlanToJson(plan, settings.Prune));
            return plan.Conflicts.Count > 0 ? ExitCodes.SyncIncomplete : ExitCodes.Ok;
        }

        var executor = new SyncExecutor(client);
        var result = await executor.ApplyAsync(plan, settings.Prune, ct: ct);
        output.WriteJson(new
        {
            applied = true,
            created = result.Created,
            updated = result.Updated,
            paused = result.Paused,
            resumed = result.Resumed,
            pruned = result.Pruned,
            orphans = plan.Orphans.Select(o => o.Server.Name).ToArray(),
            conflicts = plan.Conflicts.Select(c => new { name = c.Name, reason = c.Reason }).ToArray(),
            failures = result.Failures.Select(f => new { job = f.JobName, action = f.Action, message = f.Message }).ToArray(),
        });

        return result.Succeeded && plan.Conflicts.Count == 0 ? ExitCodes.Ok : ExitCodes.SyncIncomplete;
    }

    private static void RenderEvent(OutputContext output, SyncEvent e)
    {
        switch (e.Kind)
        {
            case SyncEventKind.Created:
                output.Markup($"[green]✓ created[/] {OutputContext.Escape(e.JobName)}");
                break;
            case SyncEventKind.Updated:
                output.Markup($"[green]✓ updated[/] {OutputContext.Escape(e.JobName)}");
                break;
            case SyncEventKind.Paused:
                output.Markup($"[green]✓ paused[/] {OutputContext.Escape(e.JobName)}");
                break;
            case SyncEventKind.Resumed:
                output.Markup($"[green]✓ resumed[/] {OutputContext.Escape(e.JobName)}");
                break;
            case SyncEventKind.Pruned:
                output.Markup($"[red]✓ deleted[/] {OutputContext.Escape(e.JobName)}");
                break;
            case SyncEventKind.Failed:
                output.Error($"{e.JobName}: {e.Detail}");
                break;
        }
    }

    private static int RenderResult(OutputContext output, SyncPlan plan, SyncApplyResult result)
    {
        var parts = new List<string>
        {
            $"[green]{result.Created} created[/]",
            $"[green]{result.Updated} updated[/]",
        };
        if (result.Paused > 0) { parts.Add($"[grey]{result.Paused} paused[/]"); }
        if (result.Resumed > 0) { parts.Add($"[grey]{result.Resumed} resumed[/]"); }
        if (result.Pruned > 0) { parts.Add($"[red]{result.Pruned} deleted[/]"); }
        if (result.Failures.Count > 0) { parts.Add($"[red]{result.Failures.Count} failed[/]"); }

        output.Markup($"\n[bold]Done:[/] {string.Join(", ", parts)}");

        var incomplete = result.Failures.Count > 0 || plan.Conflicts.Count > 0;
        if (incomplete)
        {
            output.Warn("Sync completed with issues — see above.");
            return ExitCodes.SyncIncomplete;
        }

        output.Success("Sync complete.");
        return ExitCodes.Ok;
    }

    private static object PlanToJson(SyncPlan plan, bool prune) => new
    {
        applied = false,
        creates = plan.Creates.Select(c => new { name = c.Desired.Name, kind = c.Desired.Kind }).ToArray(),
        updates = plan.ChangedUpdates.Select(u => new
        {
            name = u.Desired.Name,
            kind = u.Desired.Kind,
            pause = u.Pause.ToString().ToLowerInvariant(),
            changes = u.Changes.Select(ch => new { field = ch.Field, from = ch.From, to = ch.To }).ToArray(),
        }).ToArray(),
        unchanged = plan.NoOps.Count,
        orphans = plan.Orphans.Select(o => new { name = o.Server.Name, kind = o.Server.Kind, will_delete = prune }).ToArray(),
        conflicts = plan.Conflicts.Select(c => new { name = c.Name, reason = c.Reason }).ToArray(),
    };

    private static bool IsInteractive() => !Console.IsInputRedirected && !Console.IsOutputRedirected;
}
