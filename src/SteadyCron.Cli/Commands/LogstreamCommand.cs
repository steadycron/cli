using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Commands.Jobs;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;
using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Commands;

public sealed class LogstreamSettings : CliSettings
{
    [CommandOption("--since <N>")]
    [Description("Show events from the last N seconds on startup before going live (default: 60). Use 0 for live-only.")]
    [DefaultValue(60)]
    public int Since { get; set; } = 60;

    [CommandOption("--severity <SEVERITY>")]
    [Description("Filter by severity: info, warning, or critical. Repeat for multiple.")]
    public string[]? Severity { get; set; }

    [CommandOption("--domain <DOMAIN>")]
    [Description("Filter by category: executions, heartbeats, alerts, jobs, keys, rules, channels, subscription. Repeat for multiple.")]
    public string[]? Domain { get; set; }

    [CommandOption("--job <JOB>")]
    [Description("Filter to a specific job (key, name, or id).")]
    public string? Job { get; set; }

    [CommandOption("--interval <N>")]
    [Description("Poll interval in seconds (default: 2, min: 1).")]
    [DefaultValue(2)]
    public int Interval { get; set; } = 2;
}

/// <summary>
/// `steadycron logstream` — tails the logbook in real time by polling GET /api/logbook on a
/// short interval and printing new events as they arrive. Similar look and feel to
/// `az webapp log tail`. Ctrl+C exits cleanly. Supports --json for NDJSON output.
/// </summary>
public sealed class LogstreamCommand : SteadyCronCommandBase<LogstreamSettings>
{
    // Longest event label is "Heartbeat ping received" (23 chars).
    private const int EventLabelWidth = 25;
    // Print an idle marker when no events have appeared for this many seconds.
    private const int IdleThresholdSeconds = 30;

    public LogstreamCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c)
        : base(r, f, c) { }

    protected override async Task<int> RunAsync(LogstreamSettings settings, OutputContext output, CancellationToken ct)
    {
        if (settings.Since < 0)
        {
            throw new CliException("--since must be 0 or greater.", ExitCodes.Error);
        }

        if (settings.Interval < 1)
        {
            throw new CliException("--interval must be at least 1.", ExitCodes.Error);
        }

        var client = CreateClient(settings);

        Guid? jobId = null;
        if (settings.Job is not null)
        {
            var job = await JobLookup.ResolveAsync(client, settings.Job, ct);
            jobId = job.Id;
        }

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

        // ── Header ──────────────────────────────────────────────────────────────────

        if (!output.Json)
        {
            var hints = new List<string>();
            if (settings.Domain is { Length: > 0 }) { hints.Add($"domain: {string.Join(", ", settings.Domain)}"); }
            if (settings.Severity is { Length: > 0 }) { hints.Add($"severity: {string.Join(", ", settings.Severity)}"); }
            if (settings.Job is not null) { hints.Add($"job: {settings.Job}"); }
            hints.Add($"polling every {settings.Interval} s");
            hints.Add("Ctrl+C to stop");

            output.Out.MarkupLine(
                $"[bold]Logstream[/]  [grey]{string.Join("  ·  ", hints.Select(OutputContext.Escape))}[/]");
            output.Out.Write(new Rule().RuleStyle(Style.Parse("grey")));
        }

        // ── Initial tail ────────────────────────────────────────────────────────────

        var now = DateTimeOffset.UtcNow;
        List<LogbookEntry> tailItems = [];

        if (settings.Since > 0)
        {
            var tailResponse = await client.ListLogbookEventsAsync(
                now.AddSeconds(-settings.Since), now, eventTypes, severities, jobId, 1, 100, ct);

            // API returns newest-first; reverse for chronological display.
            tailItems = [.. tailResponse.Items.Reverse()];

            if (tailItems.Count > 0)
            {
                if (!output.Json)
                {
                    output.Out.Write(new Rule($"[grey]last {settings.Since} s[/]")
                        .LeftJustified().RuleStyle(Style.Parse("grey")));
                }

                foreach (var e in tailItems)
                {
                    Emit(e, output);
                }
            }
        }

        if (!output.Json)
        {
            output.Out.Write(new Rule("[grey]live[/]").LeftJustified().RuleStyle(Style.Parse("grey")));
        }

        // ── Polling loop ────────────────────────────────────────────────────────────

        // Start tracking from the newest event we've seen, or from now.
        var lastSeenAt = tailItems.Count > 0
            ? tailItems.Max(e => e.OccurredAt)
            : now;

        // Track IDs to dedup events that land exactly on the lastSeenAt boundary.
        var seenIds = new HashSet<Guid>(tailItems.Select(e => e.Id));

        var lastOutputAt = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.Interval), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var pollNow = DateTimeOffset.UtcNow;
                var response = await client.ListLogbookEventsAsync(
                    lastSeenAt, pollNow, eventTypes, severities, jobId, 1, 100, ct);

                var newEvents = response.Items
                    .Where(e => !seenIds.Contains(e.Id))
                    .OrderBy(e => e.OccurredAt)
                    .ToList();

                foreach (var e in newEvents)
                {
                    Emit(e, output);
                    seenIds.Add(e.Id);
                    if (e.OccurredAt > lastSeenAt) { lastSeenAt = e.OccurredAt; }
                    lastOutputAt = DateTimeOffset.UtcNow;
                }

                // Bound memory: once we've advanced past the boundary, the old IDs
                // can never appear again. Replace with just the IDs at the current boundary.
                if (seenIds.Count > 500)
                {
                    seenIds = response.Items
                        .Where(e => e.OccurredAt == lastSeenAt)
                        .Select(e => e.Id)
                        .ToHashSet();
                }

                if (!output.Json
                    && newEvents.Count == 0
                    && DateTimeOffset.UtcNow - lastOutputAt >= TimeSpan.FromSeconds(IdleThresholdSeconds))
                {
                    var idleTs = DateTimeOffset.Now.ToString("HH:mm:ss");
                    output.Out.Write(new Rule($"[grey]{idleTs}[/]")
                        .LeftJustified().RuleStyle(Style.Parse("grey")));
                    lastOutputAt = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is not CliException)
            {
                if (!output.Json)
                {
                    output.Warn($"Poll error: {ex.Message}");
                }
            }
        }

        if (!output.Json)
        {
            output.Out.WriteLine();
            output.Info("Stopped.");
        }

        return ExitCodes.Ok;
    }

    private static void Emit(LogbookEntry e, OutputContext output)
    {
        if (output.Json)
        {
            output.WriteJson(e);
            return;
        }

        var ts = e.OccurredAt.ToLocalTime().ToString("HH:mm:ss");
        var dot = LogbookFormatting.SeverityDot(e.Severity);

        // Pad plain text BEFORE escaping so markup chars don't disturb the width.
        var labelPlain = LogbookFormatting.EventLabel(e.EventType).PadRight(EventLabelWidth);
        var label = OutputContext.Escape(labelPlain);

        var job = e.JobName is not null
            ? $"  [grey]{OutputContext.Escape(e.JobName)}[/]"
            : "";

        var detail = e.Detail is not null
            ? $"    [grey]{OutputContext.Escape(Clip(e.Detail, 50))}[/]"
            : "";

        output.Out.MarkupLine($"{ts}  {dot}  {label}{job}{detail}");
    }

    private static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
