using Spectre.Console;
using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Output;


/// <summary>Shared formatting for jobs and executions (status colors, schedule/relative-time text).</summary>
public static class JobFormatting
{
    public const string UnverifiedEmailPauseReason = "unverified_email";

    public const string UnverifiedEmailWarning =
        "This job is paused because your email address is not yet verified. " +
        "Check your inbox and click the verification link — the job will activate automatically once verified.";
    public static string StatusMarkup(string? status)
    {
        var s = status ?? "new";
        var color = s.ToLowerInvariant() switch
        {
            "success" => "green",
            "running" => "blue",
            "late" or "abandoned" => "yellow",
            "skipped" => "grey",
            "paused" => "grey",
            "missed" or "failure" => "red",
            _ => "grey",
        };
        return $"[{color}]{Markup.Escape(s)}[/]";
    }

    /// <summary>
    /// Job-kind-aware status display: a job that has never fired/pinged shows a specific amber
    /// label rather than a generic grey "new" (which reads as inactive rather than pending).
    /// Display-only — the underlying <see cref="JobResponse.Status"/> value is unchanged.
    /// </summary>
    public static string StatusMarkup(JobResponse job)
    {
        if (job.Status is not null)
        {
            return StatusMarkup(job.Status);
        }

        var label = string.Equals(job.Kind, "heartbeat", StringComparison.OrdinalIgnoreCase)
            ? "waiting for ping"
            : "awaiting first run";
        return $"[yellow]{Markup.Escape(label)}[/]";
    }

    /// <summary>"1 job" / "3 jobs" — never "job(s)".</summary>
    public static string Pluralize(int count, string singular) =>
        count == 1 ? $"{count} {singular}" : $"{count} {singular}s";

    /// <summary>
    /// Builds the jobs table shared by <c>jobs list</c> and the <c>init</c> wizard's post-create
    /// summary, so both render byte-identical columns.
    /// </summary>
    public static Table BuildJobsTable(IReadOnlyList<JobResponse> jobs)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("Name");
        table.AddColumn("Kind");
        table.AddColumn("Status");
        table.AddColumn("Schedule");
        table.AddColumn("Next run");
        table.AddColumn("Last run");
        table.AddColumn(new TableColumn("Job key").NoWrap());

        foreach (var job in jobs)
        {
            table.AddRow(
                Markup.Escape(job.Name),
                Markup.Escape(job.Kind),
                StatusMarkup(job),
                Markup.Escape(ScheduleWithTz(job)),
                Markup.Escape(job.Kind == "http" ? When(job.NextFireAt) : "—"),
                Markup.Escape(When(job.LastFireAt)),
                Markup.Escape(job.JobKey ?? "—"));
        }

        return table;
    }

    /// <summary>Renders the shared jobs table plus a correctly-pluralized count footer.</summary>
    public static void RenderJobsTable(OutputContext output, IReadOnlyList<JobResponse> jobs)
    {
        output.Render(BuildJobsTable(jobs));
        output.Info($"{Pluralize(jobs.Count, "job")}.");
    }

    public static string ScheduleText(JobResponse job)
    {
        if (string.Equals(job.ScheduleKind, "interval", StringComparison.OrdinalIgnoreCase))
        {
            return $"every {job.IntervalSeconds}s";
        }

        return job.CronExpression ?? "—";
    }

    public static string ScheduleWithTz(JobResponse job) =>
        $"{ScheduleText(job)} ({job.Timezone})";

    /// <summary>A compact, human-friendly absolute+relative timestamp, or "—" when null.</summary>
    public static string When(DateTimeOffset? value)
    {
        if (value is null)
        {
            return "—";
        }

        var local = value.Value.ToLocalTime();
        return $"{local:yyyy-MM-dd HH:mm} ({Relative(value.Value)})";
    }

    public static string Relative(DateTimeOffset value)
    {
        var delta = value - DateTimeOffset.UtcNow;
        var future = delta > TimeSpan.Zero;
        var abs = future ? delta : -delta;

        string magnitude;
        if (abs.TotalSeconds < 60)
        {
            magnitude = $"{(int)abs.TotalSeconds}s";
        }
        else if (abs.TotalMinutes < 60)
        {
            magnitude = $"{(int)abs.TotalMinutes}m";
        }
        else if (abs.TotalHours < 24)
        {
            magnitude = $"{(int)abs.TotalHours}h";
        }
        else
        {
            magnitude = $"{(int)abs.TotalDays}d";
        }

        return future ? $"in {magnitude}" : $"{magnitude} ago";
    }

    /// <summary>
    /// Renders the three ping URLs for a heartbeat monitor to the output.
    /// Optionally prefixes with a bold job-name header.
    /// </summary>
    public static void RenderPingUrls(OutputContext output, string? name, PingUrls urls)
    {
        if (name is not null)
        {
            output.Markup($"  [bold]{Markup.Escape(name)}[/]");
        }

        output.Markup($"  [grey]success[/]  {Markup.Escape(urls.Success)}");
        output.Markup($"  [grey]start  [/]  {Markup.Escape(urls.Start)}");
        output.Markup($"  [grey]fail   [/]  {Markup.Escape(urls.Fail)}");
    }

    private const string PingRecipesUrl = "https://steadycron.com/docs/ping-recipes";

    /// <summary>
    /// Builds the "append this to your job" block for a newly-created heartbeat monitor: one
    /// platform-detected ready-to-paste snippet (crontab append on Linux/macOS, a PowerShell-friendly
    /// line on Windows) plus a pointer to the docs for every other scheduler — one snippet variant,
    /// never all of them. This is the single most action-critical line in the flow, so it renders
    /// bold green, never dim. A blank string marks a blank output line.
    /// </summary>
    internal static IReadOnlyList<string> BuildPingSnippetLines(PingUrls urls, string? cronExpression)
    {
        var lines = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            lines.Add("Append this to your scheduled task/script:");
            lines.Add(string.Empty);
            lines.Add($"    [bold green]your-script.ps1; if ($?) {{ curl.exe -fsS {Markup.Escape(urls.Success)} }}[/]");
        }
        else
        {
            lines.Add("Add this to the END of your cron command:");
            lines.Add(string.Empty);
            lines.Add($"    [bold green]&& curl -fsS {Markup.Escape(urls.Success)}[/]");
            lines.Add(string.Empty);
            lines.Add("Example crontab line:");
            var schedule = cronExpression ?? "* * * * *";
            lines.Add($"    [bold green]{Markup.Escape(schedule)}  /usr/local/bin/your-script.sh && curl -fsS {Markup.Escape(urls.Success)}[/]");
        }

        lines.Add(string.Empty);
        lines.Add($"Other schedulers (systemd, Docker, Kubernetes, Windows): [cyan]{PingRecipesUrl}[/]");

        return lines;
    }

    /// <summary>Shared by <c>jobs create</c> and the <c>init</c> wizard so both print byte-identical guidance.</summary>
    public static void RenderPingSnippet(OutputContext output, PingUrls urls, string? cronExpression)
    {
        output.Line();

        foreach (var line in BuildPingSnippetLines(urls, cronExpression))
        {
            if (line.Length == 0)
            {
                output.Line();
            }
            else
            {
                output.Markup(line);
            }
        }
    }
}
