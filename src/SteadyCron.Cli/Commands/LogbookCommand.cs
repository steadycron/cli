using System.ComponentModel;
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
                var resolved = LogbookFormatting.ResolveDomain(d);
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
        var arrow = output.Glyphs.Arrow;
        var windowLabel = f.Date == t.Date
            ? $"{f:yyyy-MM-dd HH:mm} {arrow} {t:HH:mm} ({offset})"
            : $"{f:yyyy-MM-dd HH:mm} {arrow} {t:yyyy-MM-dd HH:mm} ({offset})";

        output.Line($"Logbook  {windowLabel}");
        output.Line(new string(output.Glyphs.Rule, 60));
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
                LogbookFormatting.SeverityDot(item.Severity, output.Glyphs),
                Markup.Escape(item.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Markup.Escape(LogbookFormatting.EventLabel(item.EventType)),
                item.JobName is not null ? Markup.Escape(item.JobName) : output.Glyphs.NoValue,
                item.Detail is not null ? Markup.Escape(item.Detail) : output.Glyphs.NoValue);
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
            output.Markup($"  {LogbookFormatting.SeverityLabel(item.Severity, output.Glyphs)}  {Markup.Escape(ts)}");

            // Event label + job
            var jobPart = item.JobName is not null ? $"  {output.Glyphs.Bullet}  {Markup.Escape(item.JobName)}" : "";
            output.Markup($"  [bold]{Markup.Escape(LogbookFormatting.EventLabel(item.EventType))}[/]{jobPart}");

            // Detail
            if (item.Detail is not null)
            {
                output.Markup($"  Detail:  {Markup.Escape(item.Detail)}");
            }

            // Metadata key-value pairs
            if (item.Metadata is { Count: > 0 })
            {
                foreach (var (key, value) in item.Metadata)
                {
                    var label = LogbookFormatting.MetadataLabel(key).PadRight(20);
                    output.Markup($"  {Markup.Escape(label)}  {Markup.Escape(LogbookFormatting.FormatMetadataValue(value))}");
                }
            }

            output.Line();
        }
    }

}
