using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands;

public sealed class ReportSettings : CliSettings
{
    [CommandOption("--hours <N>")]
    [Description("Look back N hours (default 24). Your plan caps the maximum window.")]
    [DefaultValue(24)]
    public int Hours { get; set; } = 24;

    [CommandOption("--verbose")]
    [Description("Include HTTP response bodies and full alert delivery details for failed executions.")]
    public bool Verbose { get; set; }
}

/// <summary>`steadycron report` — account-wide activity digest for a rolling time window.</summary>
public sealed class ReportCommand : SteadyCronCommandBase<ReportSettings>
{
    public ReportCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(ReportSettings settings, OutputContext output, CancellationToken ct)
    {
        if (settings.Hours < 1)
        {
            throw new CliException("--hours must be at least 1.", ExitCodes.Error);
        }

        var client = CreateClient(settings);
        var report = await client.GetReportSummaryAsync(settings.Hours, ct);

        if (output.Json)
        {
            output.WriteJson(report);
            return ExitCodes.Ok;
        }

        RenderReport(report, settings.Verbose, output);
        return ExitCodes.Ok;
    }

    // Matches the web's ATTENTION_SEVERITY: missed=0, abandoned=1, failure=2, late=3.
    private static readonly Dictionary<string, int> AttentionSeverity = new(StringComparer.Ordinal)
    {
        ["missed"] = 0,
        ["abandoned"] = 1,
        ["failure"] = 2,
        ["late"] = 3,
    };

    private record AttentionItem(
        string JobName,
        string Kind,
        string Status,
        int FailureCount,
        ReportLastFailure? LastFailure,
        DateTimeOffset? OccurredAt,
        string? CronExpression,
        int? IntervalSeconds,
        string? Timezone);

    private static void RenderReport(ReportSummaryResponse r, bool verbose, OutputContext output)
    {
        var s = r.Summary;

        // ── Header ─────────────────────────────────────────────────────────────────

        output.Line($"Overview  {FormatWindow(r.From, r.To, output.Glyphs)}");
        output.Line(new string(output.Glyphs.Rule, 60));
        output.Line();

        // ── KPI summary ────────────────────────────────────────────────────────────
        // Calculations mirror the web Overview page exactly.
        // total_checks = total_executions (HTTP) + inbound check-ins (success/fail pings).
        // The check-in count is derived as total_checks − total_executions, same as the web, and
        // so covers both ping-driven kinds — it is labelled "monitor", not "heartbeat", because an
        // account with agent monitors would otherwise see their runs counted as heartbeats.

        var httpChecks = s.TotalExecutions;
        var monitorChecks = Math.Max(0, s.TotalChecks - s.TotalExecutions);

        double? successRate = s.TotalChecks > 0
            ? (double)s.SuccessfulChecks / s.TotalChecks * 100
            : null;

        // Mirrors web: toFixed(0) when rate ≥ 99.95 or rate == 0, else toFixed(1).
        string FormatRate(double rate) =>
            rate >= 99.95 || rate == 0 ? $"{rate:F0}%" : $"{rate:F1}%";

        var bullet = output.Glyphs.Bullet;
        var alertsSub = $"{s.AlertsDelivered} delivered {bullet} {s.AlertsFailed} failed" +
            (s.AlertsSuppressed > 0 ? $" {bullet} {s.AlertsSuppressed} suppressed" : "");

        var kpi = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").Width(18))
            .AddColumn(new TableColumn("").Width(8))
            .AddColumn("");

        kpi.AddRow(
            "[bold]Total checks[/]",
            $"[white]{s.TotalChecks}[/]",
            $"{httpChecks} HTTP {bullet} {monitorChecks} monitor");

        kpi.AddRow(
            "[bold]Successful[/]",
            $"[green]{s.SuccessfulChecks}[/]",
            successRate.HasValue ? $"{Markup.Escape(FormatRate(successRate.Value))} success rate" : "");

        kpi.AddRow(
            "[bold]Incidents[/]",
            s.FailedChecks > 0 ? $"[red]{s.FailedChecks}[/]" : $"{s.FailedChecks}",
            s.FailedChecks == 0 ? "all clear" : "");

        kpi.AddRow(
            "[bold]Alerts[/]",
            $"[white]{s.TotalAlerts}[/]",
            s.TotalAlerts > 0 ? Markup.Escape(alertsSub) : "");

        kpi.AddEmptyRow();

        kpi.AddRow(
            "[bold]Jobs reporting[/]",
            $"[white]{s.TotalJobsWithActivity} / {s.TotalJobsActive}[/]",
            s.TotalJobsSilent > 0 ? $"[yellow]{s.TotalJobsSilent} silent[/]" : "all reporting");

        output.Render(kpi);
        output.Line();

        // ── Active issues ──────────────────────────────────────────────────────────
        // Mirrors the web's "Active issues" card: jobs whose current_status is in
        // {missed, abandoned, failure, late}, drawn from both jobs and silent_jobs,
        // sorted by the same severity order as the web (missed → abandoned → failure → late).

        var attention = BuildAttention(r);

        output.Render(new Rule(
            attention.Count > 0
                ? $"[bold]Active issues ({attention.Count})[/]"
                : "[bold]Active issues[/]").LeftJustified());

        if (attention.Count == 0)
        {
            output.Markup("  All systems healthy — no active issues.");
            output.Line();
        }
        else
        {
            var tbl = new Table().Border(TableBorder.Rounded).Expand();
            tbl.AddColumn("Job");
            tbl.AddColumn("Kind");
            tbl.AddColumn("Issue");
            tbl.AddColumn("Detail");
            tbl.AddColumn("Schedule");
            tbl.AddColumn(new TableColumn("Occurred at").NoWrap());

            foreach (var item in attention)
            {
                tbl.AddRow(
                    Markup.Escape(item.JobName),
                    Markup.Escape(KindLabel(item.Kind)),
                    IssueMarkup(item.Status),
                    Markup.Escape(DetailText(item.Status, item.Kind, item.LastFailure, item.FailureCount, output.Glyphs)),
                    Markup.Escape(HumanSchedule(item.CronExpression, item.IntervalSeconds, item.Timezone, output.Glyphs)),
                    Markup.Escape(item.OccurredAt.HasValue ? FormatTs(item.OccurredAt.Value) : output.Glyphs.NoValue));
            }

            output.Render(tbl);
        }

        output.Line();

        // ── Incidents ─────────────────────────────────────────────────────────────
        // Per-job failure detail for every job that failed in the window.

        var failures = r.Jobs.Where(j => j.FailureCount > 0).ToList();
        if (failures.Count > 0)
        {
            output.Render(new Rule($"[red bold]Incidents ({failures.Count})[/]").LeftJustified());
            output.Line();

            foreach (var job in failures)
            {
                RenderFailedJob(job, verbose, output);
                output.Line();
            }
        }

        // ── Healthy jobs (verbose only) ────────────────────────────────────────────

        var healthy = r.Jobs.Where(j => j.FailureCount == 0).ToList();
        if (healthy.Count > 0 && verbose)
        {
            output.Render(new Rule($"[green bold]Healthy ({healthy.Count})[/]").LeftJustified());
            output.Line();

            var tbl = new Table().Border(TableBorder.Rounded).Expand();
            tbl.AddColumn("Job");
            tbl.AddColumn("Kind");
            tbl.AddColumn("Checks");
            tbl.AddColumn("Last activity");

            foreach (var job in healthy)
            {
                tbl.AddRow(
                    Markup.Escape(job.JobName),
                    Markup.Escape(KindLabel(job.Kind)),
                    job.Kind == "http"
                        ? $"[green]{job.ExecutionCount}[/]"
                        : $"[green]{job.PingCount}[/]",
                    Markup.Escape(job.LastSeenAt.HasValue
                        ? FormatTs(job.LastSeenAt.Value)
                        : job.LastExecutionAt.HasValue
                            ? FormatTs(job.LastExecutionAt.Value)
                            : output.Glyphs.NoValue));
            }

            output.Render(tbl);
            output.Line();
        }

        // ── Alert deliveries ──────────────────────────────────────────────────────

        var allJobAlerts = r.Jobs.SelectMany(j => j.AlertDeliveries).Concat(r.AccountAlerts).ToList();
        if (allJobAlerts.Count > 0)
        {
            var failedAlerts = allJobAlerts.Where(a => a.Status is "failed" or "suppressed").ToList();
            var showAll = verbose || failedAlerts.Count > 0;

            var headerLabel = failedAlerts.Count > 0
                ? $"[yellow bold]Alert deliveries ({allJobAlerts.Count} total, {failedAlerts.Count} problem(s))[/]"
                : $"[bold]Alert deliveries ({allJobAlerts.Count})[/]";

            output.Render(new Rule(headerLabel).LeftJustified());
            output.Line();

            if (!showAll)
            {
                output.Info("All alerts delivered successfully.");
            }
            else
            {
                var tbl = new Table().Border(TableBorder.Rounded).Expand();
                tbl.AddColumn("Job");
                tbl.AddColumn("Trigger");
                tbl.AddColumn("Channel");
                tbl.AddColumn("Status");
                tbl.AddColumn(new TableColumn("Delivered at").NoWrap());

                foreach (var jAlert in r.Jobs)
                {
                    var alerts = showAll
                        ? jAlert.AlertDeliveries
                        : (IEnumerable<ReportAlertDelivery>)jAlert.AlertDeliveries
                            .Where(a => a.Status is "failed" or "suppressed");
                    foreach (var a in alerts)
                    {
                        tbl.AddRow(
                            Markup.Escape(jAlert.JobName),
                            Markup.Escape(a.Trigger ?? a.NoticeKind ?? output.Glyphs.NoValue),
                            Markup.Escape(a.ChannelName ?? a.ChannelId.ToString()),
                            AlertStatusMarkup(a.Status),
                            Markup.Escape(a.DeliveredAt is not null ? FormatTs(a.DeliveredAt.Value) : output.Glyphs.NoValue));
                    }
                }

                foreach (var a in r.AccountAlerts)
                {
                    if (!showAll && a.Status is not ("failed" or "suppressed")) { continue; }
                    tbl.AddRow(
                        "(account)",
                        Markup.Escape(a.Trigger ?? a.NoticeKind ?? output.Glyphs.NoValue),
                        Markup.Escape(a.ChannelName ?? a.ChannelId.ToString()),
                        AlertStatusMarkup(a.Status),
                        Markup.Escape(a.DeliveredAt is not null ? FormatTs(a.DeliveredAt.Value) : output.Glyphs.NoValue));
                }

                output.Render(tbl);
            }

            output.Line();
        }

        // ── Silent jobs ───────────────────────────────────────────────────────────

        if (r.SilentJobs.Count > 0)
        {
            output.Render(new Rule($"[yellow bold]Silent jobs ({r.SilentJobs.Count})[/]").LeftJustified());

            var tbl = new Table().Border(TableBorder.Rounded).Expand().BorderColor(Color.Grey);
            tbl.AddColumn(new TableColumn("Job"));
            tbl.AddColumn(new TableColumn("Kind"));
            tbl.AddColumn(new TableColumn("Status"));
            tbl.AddColumn(new TableColumn("Next fire").NoWrap());

            foreach (var j in r.SilentJobs)
            {
                tbl.AddRow(
                    Markup.Escape(j.JobName),
                    Markup.Escape(KindLabel(j.Kind)),
                    JobFormatting.StatusMarkup(j.CurrentStatus),
                    Markup.Escape(j.NextFireAt is not null ? FormatTs(j.NextFireAt.Value) : output.Glyphs.NoValue));
            }

            output.Render(tbl);
            output.Line();
        }

        // ── Footer ────────────────────────────────────────────────────────────────

        if (s.FailedChecks == 0 && s.AlertsFailed == 0)
        {
            output.Success($"All {s.TotalJobsWithActivity} active job(s) healthy in this window.");
        }
        else
        {
            var parts = new List<string>();
            if (s.FailedChecks > 0) { parts.Add($"{s.FailedChecks} incident(s)"); }
            if (s.AlertsFailed > 0) { parts.Add($"{s.AlertsFailed} undelivered alert(s)"); }
            output.Warn($"{string.Join(", ", parts)} — see details above.");
        }

        if (r.SilentJobs.Count > 0)
        {
            output.Warn($"{r.SilentJobs.Count} job(s) had no activity in this window — check their schedules or pause them if retired.");
        }
    }

    private static List<AttentionItem> BuildAttention(ReportSummaryResponse r)
    {
        var items = new List<AttentionItem>();

        foreach (var j in r.Jobs)
        {
            if (!AttentionSeverity.ContainsKey(j.CurrentStatus)) { continue; }

            items.Add(new AttentionItem(
                j.JobName, j.Kind, j.CurrentStatus,
                j.FailureCount, j.LastFailure,
                j.LastFailure?.OccurredAt ?? j.LastSeenAt,
                j.CronExpression, j.IntervalSeconds, j.Timezone));
        }

        foreach (var j in r.SilentJobs)
        {
            if (!AttentionSeverity.ContainsKey(j.CurrentStatus)) { continue; }

            items.Add(new AttentionItem(
                j.JobName, j.Kind, j.CurrentStatus,
                0, null,
                j.LastSeenAt,
                j.CronExpression, j.IntervalSeconds, j.Timezone));
        }

        return items
            .OrderBy(i => AttentionSeverity.TryGetValue(i.Status, out var v) ? v : 50)
            .ToList();
    }

    // Mirrors web's issueLabel().
    private static string IssueMarkup(string status) => status switch
    {
        "missed" => "[red]Missed[/]",
        "abandoned" => "[yellow]Abandoned[/]",
        "failure" => "[red]Failed[/]",
        "late" => "[yellow]Late[/]",
        _ => Markup.Escape(status),
    };

    // Mirrors web's detailText().
    private static string DetailText(string status, string kind, ReportLastFailure? f, int failureCount, Glyphs glyphs)
    {
        string reason;
        if (f?.HttpStatusCode is { } code)
        {
            reason = $"HTTP {code}" + (f.ErrorMessage is not null ? $" {glyphs.Bullet} {f.ErrorMessage}" : "");
        }
        else if (f?.ErrorMessage is not null)
        {
            reason = f.ErrorMessage;
        }
        else if (f?.ErrorKind is not null)
        {
            reason = f.ErrorKind;
        }
        else
        {
            // An agent user never configured a "heartbeat", so the wording branches on kind
            // rather than sorting every non-HTTP job under the heartbeat vocabulary.
            reason = status switch
            {
                "missed" => JobKinds.IsAgent(kind) ? "No run reported"
                    : JobKinds.IsHeartbeat(kind) ? "Missed check-in"
                    : "Missed scheduled run",
                "abandoned" => "Started but never completed",
                "late" => JobKinds.IsAgent(kind) ? "Run overdue"
                    : JobKinds.IsHeartbeat(kind) ? "Check-in overdue"
                    : "Run overdue",
                "unverified" => "Ran, but reported no results",
                "failure" => "Execution failed",
                _ => glyphs.NoValue,
            };
        }

        return failureCount > 1 ? $"{reason} {glyphs.Bullet} {failureCount}{glyphs.Times} failed" : reason;
    }

    // Mirrors web's humanSchedule().
    private static string HumanSchedule(string? cron, int? intervalSeconds, string? timezone, Glyphs glyphs)
    {
        if (cron is not null)
        {
            var tz = timezone ?? "";
            var tzAbbrev = tz.Contains('/') ? tz[(tz.LastIndexOf('/') + 1)..] : tz;
            return $"{cron} {glyphs.Bullet} {tzAbbrev}";
        }

        if (intervalSeconds is { } s)
        {
            if (s < 60) { return $"Every {s}s"; }
            if (s < 3600) { return $"Every {s / 60}m"; }
            return $"Every {s / 3600}h";
        }

        return glyphs.NoValue;
    }

    private static string KindLabel(string kind) =>
        JobKinds.IsAgent(kind) ? "Agent" : JobKinds.IsHeartbeat(kind) ? "Heartbeat" : "HTTP";

    private static void RenderFailedJob(ReportJobSummary job, bool verbose, OutputContext output)
    {
        var bullet = output.Glyphs.Bullet;
        var arrow = output.Glyphs.Arrow;

        var tags = job.Tags.Count > 0
            ? "  " + string.Join(" ", job.Tags.Select(t => Markup.Escape($"[{t.Key}:{t.Value}]")))
            : "";

        output.Markup($"  [red bold]{OutputContext.Escape(job.JobName)}[/]{tags}  {job.Kind}");

        if (job.LastFailure is { } f)
        {
            var httpPart = f.HttpStatusCode is not null ? $" {bullet} HTTP {f.HttpStatusCode}" : "";
            var durationPart = f.DurationMs is not null ? $" {bullet} {f.DurationMs}ms" : "";
            var attemptPart = $" {bullet} attempt {f.Attempt}" + (!f.IsFinalAttempt ? " (retrying)" : "");
            output.Markup($"  Last failure: [white]{OutputContext.Escape(FormatTs(f.OccurredAt))}[/]{httpPart}{durationPart}{attemptPart}");

            if (f.ErrorKind is not null || f.ErrorMessage is not null)
            {
                var err = string.IsNullOrWhiteSpace(f.ErrorMessage) ? f.ErrorKind : f.ErrorMessage;
                output.Markup($"  Error:        [red]{OutputContext.Escape(err ?? "")}[/]" +
                              (f.ErrorKind is not null && f.ErrorMessage is not null
                                  ? $" ({OutputContext.Escape(f.ErrorKind)})"
                                  : ""));
            }

            if (verbose && f.ResponseBody is not null)
            {
                var body = f.ResponseBody.Length > 500 ? f.ResponseBody[..500] + "…" : f.ResponseBody;
                output.Markup($"  Response:     {OutputContext.Escape(body)}" +
                              (f.ResponseBodyTruncated ? " (truncated)" : ""));
            }
        }

        if (job.AlertDeliveries.Count > 0)
        {
            var delivered = job.AlertDeliveries.Where(a => a.Status == "delivered").ToList();
            var failed = job.AlertDeliveries.Where(a => a.Status is "failed" or "suppressed").ToList();

            if (delivered.Count > 0)
            {
                var channels = string.Join(", ", delivered.Select(a =>
                    $"{OutputContext.Escape(a.ChannelName ?? "?")} ({OutputContext.Escape(a.Trigger ?? "?")})"));
                output.Markup($"  Alerts:       [green]delivered {arrow} {channels}[/]");
            }

            if (failed.Count > 0)
            {
                var channels = string.Join(", ", failed.Select(a =>
                    $"{OutputContext.Escape(a.ChannelName ?? "?")} ({OutputContext.Escape(a.Status)})"));
                output.Markup($"  Alerts:       [red]problem {arrow} {channels}[/]");
            }
        }
        else
        {
            output.Markup($"  Alerts:       [yellow]none configured for this job[/]");
        }

        if (job.FailureCount > 1)
        {
            output.Markup($"  Total failures in window: [red]{job.FailureCount}[/]  (success: {job.SuccessCount})");
        }
    }

    private static string FormatWindow(DateTimeOffset from, DateTimeOffset to, Glyphs glyphs)
    {
        var f = from.ToLocalTime();
        var t = to.ToLocalTime();
        var offset = f.ToString("zzz");
        if (f.Date == t.Date)
        {
            return $"{f:yyyy-MM-dd HH:mm} {glyphs.Arrow} {t:HH:mm} ({offset})";
        }

        return $"{f:yyyy-MM-dd HH:mm} {glyphs.Arrow} {t:yyyy-MM-dd HH:mm} ({offset})";
    }

    private static string FormatTs(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

    private static string AlertStatusMarkup(string status) => status switch
    {
        "delivered" => "[green]delivered[/]",
        "failed" => "[red]failed[/]",
        "suppressed" => "[yellow]suppressed[/]",
        "pending" or "delivering" => "[blue]pending[/]",
        _ => Markup.Escape(status),
    };
}
