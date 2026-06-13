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

    private static void RenderReport(ReportSummaryResponse r, bool verbose, OutputContext output)
    {
        var s = r.Summary;
        var windowLabel = FormatWindow(r.From, r.To);

        // ── Header ────────────────────────────────────────────────────────────────

        output.Line($"Report  {windowLabel}");
        output.Line(new string('─', 60));
        output.Line();

        // ── Summary panel ─────────────────────────────────────────────────────────

        var summaryTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").Width(22))
            .AddColumn("");

        summaryTable.AddRow("[bold]Executions[/]",
            $"[white]{s.TotalExecutions}[/]" +
            (s.TotalExecutions > 0
                ? $"  ([green]{s.SuccessfulExecutions} ✓[/]  [red]{s.FailedExecutions} ✗[/])"
                : ""));

        summaryTable.AddRow("[bold]Pings (heartbeat)[/]",
            $"[white]{s.TotalPings}[/]");

        summaryTable.AddRow("[bold]Alerts[/]",
            $"[white]{s.TotalAlerts}[/]" +
            (s.TotalAlerts > 0
                ? $"  ([green]{s.AlertsDelivered} delivered[/]" +
                  $"  [red]{s.AlertsFailed} failed[/]" +
                  $"  [grey]{s.AlertsSuppressed} suppressed[/])"
                : ""));

        summaryTable.AddRow("[bold]Jobs[/]",
            $"[white]{s.TotalJobsActive}[/]  " +
            $"([grey]{s.TotalJobsWithActivity} active[/]  " +
            $"[yellow]{s.TotalJobsSilent} silent[/])");

        output.Render(summaryTable);
        output.Line();

        // ── Failures ──────────────────────────────────────────────────────────────

        var failures = r.Jobs.Where(j => j.FailureCount > 0).ToList();
        if (failures.Count > 0)
        {
            output.Markup($"[red bold]FAILURES ({failures.Count})[/]");
            output.Line();

            foreach (var job in failures)
            {
                RenderFailedJob(job, verbose, output);
                output.Line();
            }
        }

        // ── Jobs with activity (no failures) ──────────────────────────────────────

        var healthy = r.Jobs.Where(j => j.FailureCount == 0).ToList();
        if (healthy.Count > 0 && verbose)
        {
            output.Markup($"[green bold]HEALTHY JOBS ({healthy.Count})[/]");
            output.Line();
            var tbl = new Table().Border(TableBorder.Rounded).Expand();
            tbl.AddColumn("Job");
            tbl.AddColumn("Kind");
            tbl.AddColumn("Executions / Pings");
            tbl.AddColumn("Last activity");
            foreach (var job in healthy)
            {
                tbl.AddRow(
                    Markup.Escape(job.JobName),
                    Markup.Escape(job.Kind),
                    job.Kind == "http"
                        ? $"[green]{job.ExecutionCount}[/]"
                        : $"[green]{job.PingCount}[/]",
                    Markup.Escape(job.LastExecutionAt is not null
                        ? FormatTs(job.LastExecutionAt.Value)
                        : "—"));
            }
            output.Render(tbl);
            output.Line();
        }

        // ── Alert deliveries ──────────────────────────────────────────────────────

        var allJobAlerts = r.Jobs.SelectMany(j => j.AlertDeliveries).Concat(r.AccountAlerts).ToList();
        if (allJobAlerts.Count > 0)
        {
            var failedAlerts = allJobAlerts.Where(a => a.Status is "failed" or "suppressed").ToList();
            var headerLabel = failedAlerts.Count > 0
                ? $"[yellow bold]ALERT DELIVERIES ({allJobAlerts.Count} total, {failedAlerts.Count} problem(s))[/]"
                : $"[bold]ALERT DELIVERIES ({allJobAlerts.Count})[/]";

            output.Markup(headerLabel);
            output.Line();

            var showAll = verbose || failedAlerts.Count > 0;
            var alertsToShow = showAll ? allJobAlerts : failedAlerts;

            if (!showAll && alertsToShow.Count == 0)
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
                    var alerts = showAll ? jAlert.AlertDeliveries : jAlert.AlertDeliveries.Where(a => a.Status is "failed" or "suppressed").ToList();
                    foreach (var a in alerts)
                    {
                        tbl.AddRow(
                            Markup.Escape(jAlert.JobName),
                            Markup.Escape(a.Trigger ?? a.NoticeKind ?? "—"),
                            Markup.Escape(a.ChannelName ?? a.ChannelId.ToString()),
                            AlertStatusMarkup(a.Status),
                            Markup.Escape(a.DeliveredAt is not null ? FormatTs(a.DeliveredAt.Value) : "—"));
                    }
                }

                foreach (var a in r.AccountAlerts)
                {
                    if (!showAll && a.Status is not ("failed" or "suppressed")) { continue; }
                    tbl.AddRow(
                        "[grey](account)[/]",
                        Markup.Escape(a.Trigger ?? a.NoticeKind ?? "—"),
                        Markup.Escape(a.ChannelName ?? a.ChannelId.ToString()),
                        AlertStatusMarkup(a.Status),
                        Markup.Escape(a.DeliveredAt is not null ? FormatTs(a.DeliveredAt.Value) : "—"));
                }

                output.Render(tbl);
            }

            output.Line();
        }

        // ── Silent jobs ───────────────────────────────────────────────────────────

        if (r.SilentJobs.Count > 0)
        {
            output.Markup($"[yellow bold]SILENT JOBS — no activity in window ({r.SilentJobs.Count})[/]");
            output.Line();

            var tbl = new Table().Border(TableBorder.Rounded).Expand();
            tbl.AddColumn("Job");
            tbl.AddColumn("Kind");
            tbl.AddColumn("Status");
            tbl.AddColumn(new TableColumn("Next fire").NoWrap());

            foreach (var j in r.SilentJobs)
            {
                tbl.AddRow(
                    Markup.Escape(j.JobName),
                    Markup.Escape(j.Kind),
                    JobFormatting.StatusMarkup(j.CurrentStatus),
                    Markup.Escape(j.NextFireAt is not null ? FormatTs(j.NextFireAt.Value) : "—"));
            }

            output.Render(tbl);
            output.Line();
        }

        // ── Footer ────────────────────────────────────────────────────────────────

        if (s.FailedExecutions == 0 && s.AlertsFailed == 0)
        {
            output.Success($"All {s.TotalJobsWithActivity} active job(s) healthy in this window.");
        }
        else
        {
            var parts = new List<string>();
            if (s.FailedExecutions > 0) { parts.Add($"{s.FailedExecutions} execution failure(s)"); }
            if (s.AlertsFailed > 0) { parts.Add($"{s.AlertsFailed} undelivered alert(s)"); }
            output.Warn($"{string.Join(", ", parts)} — see details above.");
        }

        if (r.SilentJobs.Count > 0)
        {
            output.Warn($"{r.SilentJobs.Count} job(s) had no activity in this window. Check their schedules or pause them if retired.");
        }
    }

    private static void RenderFailedJob(ReportJobSummary job, bool verbose, OutputContext output)
    {
        var tags = job.Tags.Count > 0
            ? "  [grey]" + string.Join(" ", job.Tags.Select(t => Markup.Escape($"[{t.Key}:{t.Value}]"))) + "[/]"
            : "";

        output.Markup($"  [red bold]{OutputContext.Escape(job.JobName)}[/]{tags}  [grey]{job.Kind}[/]");

        if (job.LastFailure is { } f)
        {
            var httpPart = f.HttpStatusCode is not null ? $" · HTTP {f.HttpStatusCode}" : "";
            var durationPart = f.DurationMs is not null ? $" · {f.DurationMs}ms" : "";
            var attemptPart = $" · attempt {f.Attempt}" + (!f.IsFinalAttempt ? " (retrying)" : "");
            output.Markup($"  [grey]Last failure:[/] [white]{OutputContext.Escape(FormatTs(f.OccurredAt))}[/]{httpPart}{durationPart}{attemptPart}");

            if (f.ErrorKind is not null || f.ErrorMessage is not null)
            {
                var err = string.IsNullOrWhiteSpace(f.ErrorMessage) ? f.ErrorKind : f.ErrorMessage;
                output.Markup($"  [grey]Error:[/]        [red]{OutputContext.Escape(err ?? "")}[/]" +
                              (f.ErrorKind is not null && f.ErrorMessage is not null
                                  ? $" [grey]({OutputContext.Escape(f.ErrorKind)})[/]"
                                  : ""));
            }

            if (verbose && f.ResponseBody is not null)
            {
                var body = f.ResponseBody.Length > 500 ? f.ResponseBody[..500] + "…" : f.ResponseBody;
                output.Markup($"  [grey]Response:[/]     {OutputContext.Escape(body)}" +
                              (f.ResponseBodyTruncated ? " [grey](truncated)[/]" : ""));
            }
        }

        if (job.AlertDeliveries.Count > 0)
        {
            var delivered = job.AlertDeliveries.Where(a => a.Status == "delivered").ToList();
            var failed = job.AlertDeliveries.Where(a => a.Status is "failed" or "suppressed").ToList();

            if (delivered.Count > 0)
            {
                var channels = string.Join(", ", delivered.Select(a =>
                    $"{OutputContext.Escape(a.ChannelName ?? "?")} [grey]({OutputContext.Escape(a.Trigger ?? "?")})[/]"));
                output.Markup($"  [grey]Alerts:[/]       [green]delivered → {channels}[/]");
            }

            if (failed.Count > 0)
            {
                var channels = string.Join(", ", failed.Select(a =>
                    $"{OutputContext.Escape(a.ChannelName ?? "?")} [grey]({OutputContext.Escape(a.Status)})[/]"));
                output.Markup($"  [grey]Alerts:[/]       [red]problem → {channels}[/]");
            }
        }
        else
        {
            output.Markup($"  [grey]Alerts:[/]       [yellow]none configured for this job[/]");
        }

        if (job.FailureCount > 1)
        {
            output.Markup($"  [grey]Total failures in window:[/] [red]{job.FailureCount}[/]  [grey](success: {job.SuccessCount})[/]");
        }
    }

    private static string FormatWindow(DateTimeOffset from, DateTimeOffset to)
    {
        var f = from.ToLocalTime();
        var t = to.ToLocalTime();
        var offset = f.ToString("zzz"); // e.g. "+02:00" or "+00:00"
        if (f.Date == t.Date)
        {
            return $"{f:yyyy-MM-dd HH:mm} → {t:HH:mm} ({offset})";
        }

        return $"{f:yyyy-MM-dd HH:mm} → {t:yyyy-MM-dd HH:mm} ({offset})";
    }

    private static string FormatTs(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

    private static string AlertStatusMarkup(string status) => status switch
    {
        "delivered" => "[green]delivered[/]",
        "failed" => "[red]failed[/]",
        "suppressed" => "[yellow]suppressed[/]",
        "pending" or "delivering" => "[blue]pending[/]",
        _ => $"[grey]{Markup.Escape(status)}[/]",
    };
}
