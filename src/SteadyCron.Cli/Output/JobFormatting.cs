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

    /// <summary>
    /// Prints the "append this to your cron command" block for a newly-created heartbeat
    /// monitor: the ping URL plus a ready-to-paste <c>&amp;&amp; curl -fsS ...</c> snippet and an
    /// example crontab line. Shared by <c>jobs create</c> and the <c>init</c> wizard so both
    /// print byte-identical guidance.
    /// </summary>
    public static void RenderPingSnippet(OutputContext output, PingUrls urls, string? cronExpression)
    {
        output.Line();
        output.Markup("Add this to the END of your cron command:");
        output.Line();
        output.Markup($"    [grey]&& curl -fsS {Markup.Escape(urls.Success)}[/]");
        output.Line();
        output.Markup("Example crontab line:");
        var schedule = cronExpression ?? "* * * * *";
        output.Markup($"    [grey]{Markup.Escape(schedule)}  /usr/local/bin/your-script.sh && curl -fsS {Markup.Escape(urls.Success)}[/]");
    }
}
