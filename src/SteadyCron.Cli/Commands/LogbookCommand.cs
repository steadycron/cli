using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Commands.Jobs;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands;

public sealed class LogbookSettings : CliSettings
{
    [CommandOption("--hours <N>")]
    [Description("Look back N hours (default 24). Your plan caps the maximum window.")]
    [DefaultValue(24)]
    public int Hours { get; set; } = 24;

    [CommandOption("--severity <SEVERITY>")]
    [Description("Filter by severity: info, warning, or critical. Repeat for multiple values.")]
    public string[]? Severity { get; set; }

    [CommandOption("--domain <DOMAIN>")]
    [Description("Filter by category: executions, heartbeats, alerts, jobs, keys, rules, channels, subscription. Repeat for multiple.")]
    public string[]? Domain { get; set; }

    [CommandOption("--job <JOB>")]
    [Description("Filter to a specific job (job key, name, or id).")]
    public string? Job { get; set; }

    [CommandOption("--page <N>")]
    [Description("Page number (default 1). Ignored with --all.")]
    [DefaultValue(1)]
    public int Page { get; set; } = 1;

    [CommandOption("--page-size <N>")]
    [Description("Items per page, 1–100 (default 50). Ignored with --all.")]
    [DefaultValue(50)]
    public int PageSize { get; set; } = 50;

    [CommandOption("--all")]
    [Description("Fetch every page of results.")]
    public bool All { get; set; }

    [CommandOption("--verbose")]
    [Description("Show full event metadata for each entry.")]
    public bool Verbose { get; set; }
}

/// <summary>`steadycron logbook` — scrollable account event history with optional filtering.</summary>
public sealed class LogbookCommand : SteadyCronCommandBase<LogbookSettings>
{
    public LogbookCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(LogbookSettings settings, OutputContext output, CancellationToken ct)
    {
        if (settings.Hours < 1)
        {
            throw new CliException("--hours must be at least 1.", ExitCodes.Error);
        }

        var client = CreateClient(settings);
        var to = DateTimeOffset.UtcNow;
        var from = to.AddHours(-settings.Hours);

        // Resolve --job to a UUID via the same lookup used by all other job commands.
        Guid? jobId = null;
        if (settings.Job is not null)
        {
            var job = await JobLookup.ResolveAsync(client, settings.Job, ct);
            jobId = job.Id;
        }

        // Resolve --domain names to the underlying event_type values the API understands.
        IReadOnlyList<string>? eventTypes = null;
        if (settings.Domain is { Length: > 0 })
        {
            var types = new List<string>();
            foreach (var d in settings.Domain)
            {
                var resolved = ResolveDomain(d);
                if (resolved is null)
                {
                    throw new CliException(
                        $"Unknown domain '{d}'. Valid values: executions, heartbeats, alerts, jobs, keys, rules, channels, subscription.",
                        ExitCodes.Error);
                }

                types.AddRange(resolved);
            }

            eventTypes = types;
        }

        // Validate severity values up-front for a clear error message.
        if (settings.Severity is { Length: > 0 })
        {
            var invalid = settings.Severity.FirstOrDefault(s => s is not ("info" or "warning" or "critical"));
            if (invalid is not null)
            {
                throw new CliException(
                    $"Unknown severity '{invalid}'. Valid values: info, warning, critical.",
                    ExitCodes.Error);
            }
        }

        IReadOnlyList<string>? severities = settings.Severity is { Length: > 0 } ? settings.Severity : null;

        List<LogbookEntry> items;
        int totalCount;

        if (settings.All)
        {
            items = await FetchAllAsync(client, from, to, eventTypes, severities, jobId, settings.PageSize, ct);
            totalCount = items.Count;
        }
        else
        {
            var response = await client.ListLogbookEventsAsync(
                from, to, eventTypes, severities, jobId, settings.Page, settings.PageSize, ct);
            items = [.. response.Items];
            totalCount = response.TotalCount;
        }

        if (output.Json)
        {
            output.WriteJson(items);
            return ExitCodes.Ok;
        }

        RenderLogbook(items, totalCount, settings, from, to, output);
        return ExitCodes.Ok;
    }

    private static async Task<List<LogbookEntry>> FetchAllAsync(
        Api.SteadyCronClient client,
        DateTimeOffset from, DateTimeOffset to,
        IReadOnlyList<string>? eventTypes,
        IReadOnlyList<string>? severities,
        Guid? jobId,
        int pageSize,
        CancellationToken ct)
    {
        var all = new List<LogbookEntry>();
        var page = 1;
        while (true)
        {
            var response = await client.ListLogbookEventsAsync(
                from, to, eventTypes, severities, jobId, page, pageSize, ct);
            all.AddRange(response.Items);
            if (all.Count >= response.TotalCount || response.Items.Count == 0) { break; }

            page++;
        }

        return all;
    }

    private static void RenderLogbook(
        List<LogbookEntry> items,
        int totalCount,
        LogbookSettings settings,
        DateTimeOffset from,
        DateTimeOffset to,
        OutputContext output)
    {
        var f = from.ToLocalTime();
        var t = to.ToLocalTime();
        var offset = f.ToString("zzz");
        var windowLabel = f.Date == t.Date
            ? $"{f:yyyy-MM-dd HH:mm} → {t:HH:mm} ({offset})"
            : $"{f:yyyy-MM-dd HH:mm} → {t:yyyy-MM-dd HH:mm} ({offset})";

        output.Line($"Logbook  {windowLabel}");
        output.Line(new string('─', 60));
        output.Line();

        if (items.Count == 0)
        {
            output.Info("No logbook events match the current filters and time range.");
            return;
        }

        if (settings.Verbose)
        {
            RenderVerbose(items, output);
        }
        else
        {
            RenderTable(items, output);
        }

        // Pagination footer
        if (!settings.All && totalCount > items.Count)
        {
            var start = (settings.Page - 1) * settings.PageSize + 1;
            var end = start + items.Count - 1;
            output.Info($"Showing {start}–{end} of {totalCount} event(s). Use --page {settings.Page + 1} or --all to see everything.");
        }
        else
        {
            output.Info($"{totalCount} event(s).");
        }
    }

    private static void RenderTable(List<LogbookEntry> items, OutputContext output)
    {
        var tbl = new Table().Border(TableBorder.Rounded).Expand();
        tbl.AddColumn(new TableColumn("").NoWrap().Width(1));  // severity dot
        tbl.AddColumn(new TableColumn("Time").NoWrap());
        tbl.AddColumn(new TableColumn("Event").NoWrap());
        tbl.AddColumn(new TableColumn("Job"));
        tbl.AddColumn(new TableColumn("Detail"));

        foreach (var item in items)
        {
            tbl.AddRow(
                SeverityDot(item.Severity),
                Markup.Escape(item.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Markup.Escape(EventLabel(item.EventType)),
                item.JobName is not null ? Markup.Escape(item.JobName) : "[grey]—[/]",
                item.Detail is not null ? Markup.Escape(item.Detail) : "[grey]—[/]");
        }

        output.Render(tbl);
        output.Line();
    }

    private static void RenderVerbose(List<LogbookEntry> items, OutputContext output)
    {
        foreach (var item in items)
        {
            // Timestamp + severity on one line
            var ts = item.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
            output.Markup($"  {SeverityLabel(item.Severity)}  [grey]{Markup.Escape(ts)}[/]");

            // Event label + job
            var jobPart = item.JobName is not null ? $"  [grey]·[/]  {Markup.Escape(item.JobName)}" : "";
            output.Markup($"  [bold]{Markup.Escape(EventLabel(item.EventType))}[/]{jobPart}");

            // Detail
            if (item.Detail is not null)
            {
                output.Markup($"  [grey]Detail:[/]  {Markup.Escape(item.Detail)}");
            }

            // Metadata key-value pairs
            if (item.Metadata is { Count: > 0 })
            {
                foreach (var (key, value) in item.Metadata)
                {
                    var label = MetadataLabel(key).PadRight(20);
                    output.Markup($"  [grey]{Markup.Escape(label)}[/]  {Markup.Escape(FormatMetadataValue(value))}");
                }
            }

            output.Line();
        }
    }

    // Maps domain names (web's LOGBOOK_DOMAINS) to the underlying event_type strings.
    // Accepts lowercase slugs; dashes are stripped so "alert-rules" == "alertrules".
    private static IReadOnlyList<string>? ResolveDomain(string domain)
    {
        return domain.Trim().ToLowerInvariant().Replace("-", "") switch
        {
            "executions" => ["execution_success", "execution_failure"],
            "heartbeats" => ["heartbeat_missed", "heartbeat_recovered", "run_abandoned", "ping_success", "ping_fail", "ping_start"],
            "alerts" => ["alert_delivered", "alert_failed", "alert_suppressed", "alert_pending"],
            "jobs" => ["job_created", "job_deleted", "job_paused", "job_resumed"],
            "keys" or "apikeys" => ["api_key_created", "api_key_revoked"],
            "rules" or "alertrules" => ["alert_rule_created", "alert_rule_deleted"],
            "channels" or "alertchannels" => ["alert_channel_created", "alert_channel_updated", "alert_channel_deleted"],
            "subscription" => ["subscription_plan_upgraded", "subscription_plan_downgraded", "subscription_canceled", "subscription_past_due", "subscription_paused"],
            _ => null,
        };
    }

    // Mirrors logbook-events.ts: logbookEventLabel().
    private static readonly Dictionary<string, string> EventLabels = new(StringComparer.Ordinal)
    {
        ["execution_success"] = "Execution succeeded",
        ["execution_failure"] = "Execution failed",
        ["heartbeat_missed"] = "Heartbeat missed",
        ["heartbeat_recovered"] = "Heartbeat recovered",
        ["run_abandoned"] = "Run abandoned",
        ["ping_success"] = "Heartbeat ping received",
        ["ping_fail"] = "Heartbeat ping failed",
        ["ping_start"] = "Heartbeat run started",
        ["alert_delivered"] = "Alert delivered",
        ["alert_failed"] = "Alert delivery failed",
        ["alert_suppressed"] = "Alert suppressed",
        ["alert_pending"] = "Alert pending delivery",
        ["job_created"] = "Job created",
        ["job_deleted"] = "Job deleted",
        ["job_paused"] = "Job paused",
        ["job_resumed"] = "Job resumed",
        ["api_key_created"] = "API key created",
        ["api_key_revoked"] = "API key revoked",
        ["alert_rule_created"] = "Alert rule created",
        ["alert_rule_deleted"] = "Alert rule deleted",
        ["alert_channel_created"] = "Alert channel created",
        ["alert_channel_updated"] = "Alert channel updated",
        ["alert_channel_deleted"] = "Alert channel deleted",
        ["subscription_plan_upgraded"] = "Plan upgraded",
        ["subscription_plan_downgraded"] = "Plan downgraded",
        ["subscription_canceled"] = "Subscription canceled",
        ["subscription_past_due"] = "Payment past due",
        ["subscription_paused"] = "Subscription paused",
    };

    private static string EventLabel(string eventType) =>
        EventLabels.TryGetValue(eventType, out var label)
            ? label
            : eventType.Replace("_", " ");

    // Mirrors web's METADATA_LABELS.
    private static readonly Dictionary<string, string> MetadataLabels = new(StringComparer.Ordinal)
    {
        ["http_status_code"] = "HTTP status",
        ["error_kind"] = "Error type",
        ["error_message"] = "Error message",
        ["attempt"] = "Attempt",
        ["is_final_attempt"] = "Final attempt",
        ["response_body_excerpt"] = "Response body",
        ["response_body_truncated"] = "Response truncated",
        ["source_ip"] = "Source IP",
        ["user_agent"] = "User agent",
        ["payload_excerpt"] = "Payload",
        ["last_error"] = "Last error",
        ["attempts"] = "Attempts",
        ["suppressed_reason"] = "Suppressed reason",
        ["notice_kind"] = "Notice kind",
        ["trigger"] = "Trigger",
    };

    private static string MetadataLabel(string key) =>
        MetadataLabels.TryGetValue(key, out var label) ? label : key.Replace("_", " ");

    private static string FormatMetadataValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "—",
        _ => element.ToString(),
    };

    private static string SeverityDot(string severity) => severity switch
    {
        "critical" => "[red]●[/]",
        "warning" => "[yellow]●[/]",
        _ => "[grey]●[/]",
    };

    private static string SeverityLabel(string severity) => severity switch
    {
        "critical" => "[red bold]● critical[/]",
        "warning" => "[yellow]● warning[/]",
        _ => "[grey]● info[/]",
    };
}
