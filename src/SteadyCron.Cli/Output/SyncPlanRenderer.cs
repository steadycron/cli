using Spectre.Console;
using SteadyCron.Cli.Manifest;

namespace SteadyCron.Cli.Output;

/// <summary>Renders a <see cref="SyncPlan"/> as a human-readable, color-coded diff.</summary>
public static class SyncPlanRenderer
{
    public static void Render(OutputContext output, SyncPlan plan, bool prune)
    {
        foreach (var create in plan.Creates)
        {
            output.Markup($"[green]+ create[/] {OutputContext.Escape(create.Desired.Name)} [grey]({create.Desired.Kind})[/]");
        }

        foreach (var update in plan.ChangedUpdates)
        {
            output.Markup($"[yellow]~ update[/] {OutputContext.Escape(update.Desired.Name)} [grey]({update.Desired.Kind})[/]");
            foreach (var change in update.Changes)
            {
                output.Markup($"    [grey]{OutputContext.Escape(change.Field)}:[/] {OutputContext.Escape(change.From)} [grey]→[/] {OutputContext.Escape(change.To)}");
            }

            switch (update.Pause)
            {
                case PauseTransition.Pause:
                    output.Markup("    [grey]state:[/] active [grey]→[/] paused");
                    break;
                case PauseTransition.Resume:
                    output.Markup("    [grey]state:[/] paused [grey]→[/] active");
                    break;
            }
        }

        foreach (var orphan in plan.Orphans)
        {
            var suffix = prune ? "[red](will be deleted)[/]" : "[grey](not in manifest; use --prune to delete)[/]";
            var glyph = prune ? "[red]- delete[/]" : "[grey]- orphan[/]";
            output.Markup($"{glyph} {OutputContext.Escape(orphan.Server.Name)} [grey]({orphan.Server.Kind})[/] {suffix}");
        }

        foreach (var conflict in plan.Conflicts)
        {
            output.Warn($"conflict: {conflict.Name}: {conflict.Reason}");
        }

        RenderSummary(output, plan, prune);
    }

    private static void RenderSummary(OutputContext output, SyncPlan plan, bool prune)
    {
        var parts = new List<string>
        {
            $"[green]{plan.Creates.Count} to create[/]",
            $"[yellow]{plan.ChangedUpdates.Count} to update[/]",
        };

        if (prune)
        {
            parts.Add($"[red]{plan.Orphans.Count} to delete[/]");
        }
        else if (plan.Orphans.Count > 0)
        {
            parts.Add($"[grey]{plan.Orphans.Count} orphaned[/]");
        }

        if (plan.NoOps.Count > 0)
        {
            parts.Add($"[grey]{plan.NoOps.Count} unchanged[/]");
        }

        if (plan.Conflicts.Count > 0)
        {
            parts.Add($"[red]{plan.Conflicts.Count} conflict(s)[/]");
        }

        output.Markup($"\n[bold]Plan:[/] {string.Join(", ", parts)}");
    }
}
