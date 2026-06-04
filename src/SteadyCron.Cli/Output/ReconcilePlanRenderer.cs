using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Output;

/// <summary>
/// Renders a server <see cref="ReconcileResponse"/> plan as a human-readable, color-coded diff.
/// The server is the authoritative source of the plan; the CLI renders it verbatim.
/// </summary>
public static class ReconcilePlanRenderer
{
    public static void Render(OutputContext output, ReconcileResponse plan)
    {
        foreach (var item in plan.Creates)
        {
            output.Markup($"[green]+ create[/] {OutputContext.Escape(item.Name)} " +
                          RenderMeta(item.ResourceType, item.Kind));
        }

        foreach (var item in plan.Updates)
        {
            output.Markup($"[yellow]~ update[/] {OutputContext.Escape(item.Name)} " +
                          RenderMeta(item.ResourceType, item.Kind));
            foreach (var diff in item.Diffs)
            {
                output.Markup(
                    $"    [grey]{OutputContext.Escape(diff.Field)}:[/] " +
                    $"{OutputContext.Escape(diff.From ?? "(none)")} [grey]→[/] {OutputContext.Escape(diff.To ?? "(none)")}");
            }
        }

        foreach (var item in plan.Deletes)
        {
            output.Markup($"[red]- delete[/] {OutputContext.Escape(item.Name)} " +
                          RenderMeta(item.ResourceType, item.Kind));
        }

        foreach (var error in plan.Errors)
        {
            var resourceHint = error.ResourceName is not null
                ? $" ({OutputContext.Escape(error.ResourceName)})"
                : string.Empty;
            output.Warn($"[{error.Code}]{resourceHint} {error.Message}");
        }

        RenderSummary(output, plan);
    }

    private static void RenderSummary(OutputContext output, ReconcileResponse plan)
    {
        var parts = new List<string>
        {
            $"[green]{plan.Creates.Count} to create[/]",
            $"[yellow]{plan.Updates.Count} to update[/]",
        };

        if (plan.Deletes.Count > 0)
        {
            parts.Add($"[red]{plan.Deletes.Count} to delete[/]");
        }

        if (plan.NoChanges.Count > 0)
        {
            parts.Add($"[grey]{plan.NoChanges.Count} unchanged[/]");
        }

        if (plan.Errors.Count > 0)
        {
            parts.Add($"[red]{plan.Errors.Count} error(s)[/]");
        }

        output.Markup($"\n[bold]Plan:[/] {string.Join(", ", parts)}");
    }

    private static string RenderMeta(string resourceType, string? kind)
    {
        var type = resourceType == "job" && kind is not null
            ? $"{kind} job"
            : resourceType;
        return $"[grey]({OutputContext.Escape(type)})[/]";
    }
}
