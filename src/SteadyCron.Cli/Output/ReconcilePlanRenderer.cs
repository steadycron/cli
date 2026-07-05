using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Output;

/// <summary>
/// Renders a server <see cref="ReconcileResponse"/> plan as a human-readable, color-coded diff.
/// The server is the authoritative source of the plan; the CLI renders it verbatim. Changes arrive
/// as a single flat list keyed by <see cref="ReconcileChange.Action"/>; counts come from the
/// server's <see cref="ReconcileSummary"/>.
/// </summary>
public static class ReconcilePlanRenderer
{
    public static void Render(OutputContext output, ReconcileResponse plan)
    {
        var arrow = output.Glyphs.Arrow;

        foreach (var change in plan.Changes)
        {
            switch (change.Action)
            {
                case "create":
                    output.Markup($"[green]+ create[/] {OutputContext.Escape(change.Key)} " +
                                  RenderMeta(change.Resource));
                    break;

                case "update":
                    output.Markup($"[yellow]~ update[/] {OutputContext.Escape(change.Key)} " +
                                  RenderMeta(change.Resource));
                    foreach (var diff in change.Diff ?? [])
                    {
                        output.Markup(
                            $"    {OutputContext.Escape(diff.Field)}: " +
                            $"{OutputContext.Escape(diff.From ?? "(none)")} {arrow} {OutputContext.Escape(diff.To ?? "(none)")}");
                    }
                    break;

                case "delete":
                    output.Markup($"[red]- delete[/] {OutputContext.Escape(change.Key)} " +
                                  RenderMeta(change.Resource));
                    break;

                // "no_change" is reflected in the summary line only.
            }
        }

        foreach (var error in plan.Errors)
        {
            var resourceHint = error.Key is not null
                ? $" ({OutputContext.Escape(error.Key)})"
                : string.Empty;
            output.Warn($"[{error.Code}]{resourceHint} {error.Message}");
        }

        RenderSummary(output, plan);
    }

    private static void RenderSummary(OutputContext output, ReconcileResponse plan)
    {
        var summary = plan.Summary;
        var parts = new List<string>
        {
            $"[green]{summary.Create} to create[/]",
            $"[yellow]{summary.Update} to update[/]",
        };

        if (summary.Delete > 0)
        {
            parts.Add($"[red]{summary.Delete} to delete[/]");
        }

        if (summary.NoChange > 0)
        {
            parts.Add($"{summary.NoChange} unchanged");
        }

        if (plan.Errors.Count > 0)
        {
            parts.Add($"[red]{plan.Errors.Count} error(s)[/]");
        }

        output.Markup($"\n[bold]Plan:[/] {string.Join(", ", parts)}");
    }

    private static string RenderMeta(string resource) =>
        $"({OutputContext.Escape(resource)})";
}
