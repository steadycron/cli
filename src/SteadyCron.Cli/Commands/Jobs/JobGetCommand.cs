using Spectre.Console;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

/// <summary>`steadycron jobs get &lt;job&gt;` — shows a single job's full definition.</summary>
public sealed class JobGetCommand : SteadyCronCommandBase<JobTargetSettings>
{
    public JobGetCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(JobTargetSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Identifier, ct);

        if (output.Json)
        {
            output.WriteJson(job);
            return ExitCodes.Ok;
        }

        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn();

        void Row(string key, string? value) =>
            grid.AddRow($"[grey]{key}[/]", Markup.Escape(value ?? "—"));

        Row("id", job.Id.ToString());
        Row("name", job.Name);
        Row("kind", job.Kind);
        grid.AddRow("[grey]status[/]", JobFormatting.StatusMarkup(job.Status));
        if (job.PausedReason is not null)
        {
            Row("paused_reason", job.PausedReason);
        }

        Row("description", job.Description);
        Row("schedule", JobFormatting.ScheduleWithTz(job));

        if (job.Kind == "http")
        {
            Row("method", job.HttpMethod);
            Row("url", job.HttpUrl);
            Row("headers", DescribeHeaders(job.HttpHeaders));
            Row("body", Truncate(job.HttpBody, 200));
            Row("timeout", $"{job.TimeoutSeconds}s");
            Row("retries", job.MaxRetries?.ToString());
            Row("retry_backoff", job.RetryBackoffSeconds is { } b ? $"{b}s" : null);
            Row("retry_on_timeout", job.RetryOnTimeout.ToString());
            Row("retry_on_status", job.RetryOnStatusCodes is { Length: > 0 } c ? string.Join(",", c) : "any non-2xx");
            Row("skip_if_running", job.SkipIfRunning.ToString());
            Row("misfire_policy", job.MisfirePolicy);
            Row("next run", JobFormatting.When(job.NextFireAt));
        }
        else
        {
            Row("grace", $"{job.GraceSeconds}s");
            Row("stuck_run_detection", job.StuckRunDetection.ToString());
            Row("max_run_duration", $"{job.MaxRunDurationSeconds}s");
            if (job.PingUrls is { } ping)
            {
                Row("ping (success)", ping.Success);
                Row("ping (start)", ping.Start);
                Row("ping (fail)", ping.Fail);
            }
        }

        Row("last run", JobFormatting.When(job.LastFireAt));
        Row("badge", job.BadgeUrl);

        output.Render(new Panel(grid)
            .Header($"[bold]{Markup.Escape(job.Name)}[/]")
            .Border(BoxBorder.Rounded));

        if (job.PausedReason == JobFormatting.UnverifiedEmailPauseReason)
        {
            output.Warn(JobFormatting.UnverifiedEmailWarning);
        }

        return ExitCodes.Ok;
    }

    private static string DescribeHeaders(Dictionary<string, string>? headers) =>
        headers is null || headers.Count == 0
            ? "—"
            : string.Join("\n", headers.Select(h => $"{h.Key}: {h.Value}"));

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= max ? value : value[..max] + "…";
    }
}
