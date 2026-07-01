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
    // How far behind "now" the live poll window trails. The logbook API mutates execution
    // events in-place: the event is created as execution_failure when the HTTP request is
    // dispatched, then updated to execution_success when the response arrives. Polling with
    // to=now could catch events mid-flight and show a transient failure that resolves
    // seconds later — but our ID-based dedup would never re-show the corrected state.
    // Trailing 5 s gives the API enough time to finalise every event before we display it.
    private const int LiveLagSeconds = 5;

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

        var lastSeenAt = tailItems.Count > 0
            ? tailItems.Max(e => e.OccurredAt)
            : now;

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
                // Trail LiveLagSeconds behind now so execution events have time to reach their
                // final state before we display them (see LiveLagSeconds comment above).
                var pollTo = pollNow.AddSeconds(-LiveLagSeconds);

                var newEventCount = 0;

                if (pollTo > lastSeenAt)
                {
                    var response = await client.ListLogbookEventsAsync(
                        lastSeenAt, pollTo, eventTypes, severities, jobId, 1, 100, ct);

                    var newEvents = response.Items
                        .Where(e => !seenIds.Contains(e.Id))
                        .OrderBy(e => e.OccurredAt)
                        .ToList();

                    newEventCount = newEvents.Count;

                    foreach (var e in newEvents)
                    {
                        Emit(e, output);
                        seenIds.Add(e.Id);
                        if (e.OccurredAt > lastSeenAt) { lastSeenAt = e.OccurredAt; }
                        lastOutputAt = DateTimeOffset.UtcNow;
                    }

                    if (seenIds.Count > 500)
                    {
                        seenIds = response.Items
                            .Where(e => e.OccurredAt == lastSeenAt)
                            .Select(e => e.Id)
                            .ToHashSet();
                    }
                }

                if (!output.Json
                    && newEventCount == 0
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
        var labelPlain = LogbookFormatting.EventLabel(e.EventType).PadRight(EventLabelWidth);
        var detail = BuildDetail(e);

        var lineColor = LineColor(e);

        if (lineColor is not null)
        {
            // Entire line in a single color — no inner markup so all components are plain-escaped.
            var jobPart = e.JobName is not null ? $"  {OutputContext.Escape(e.JobName)}" : "";
            var detailPart = detail.Length > 0 ? $"    {OutputContext.Escape(Clip(detail, 70))}" : "";
            output.Out.MarkupLine(
                $"[{lineColor}]{ts}  ●  {OutputContext.Escape(labelPlain)}{jobPart}{detailPart}[/]");
        }
        else
        {
            // Normal line — dot reflects event type, job and detail are greyed.
            var dot = SuccessEvent(e.EventType) ? "[green]●[/]" : "[grey]●[/]";
            var jobPart = e.JobName is not null ? $"  [grey]{OutputContext.Escape(e.JobName)}[/]" : "";
            var detailPart = detail.Length > 0 ? $"    [grey]{OutputContext.Escape(Clip(detail, 70))}[/]" : "";
            output.Out.MarkupLine($"{ts}  {dot}  {OutputContext.Escape(labelPlain)}{jobPart}{detailPart}");
        }
    }

    // Returns the color to apply to the entire line, or null for normal formatting.
    // execution_failure is red for final (or unknown) attempts and yellow for intermediate
    // retries. The API sets is_final_attempt=false only when the job has retries configured
    // and this was not the last attempt; absent or true → treat as final (red).
    private static string? LineColor(LogbookEntry e) => e.EventType switch
    {
        "execution_failure" => IsFinalAttempt(e) ? "red" : "yellow",
        "heartbeat_missed" or "run_abandoned" or "ping_fail" or "alert_failed" => "red",
        _ => null,
    };

    private static bool SuccessEvent(string eventType) => eventType is
        "execution_success" or "heartbeat_recovered" or "ping_success" or "alert_delivered";

    // Returns true when is_final_attempt is explicitly true, or when the metadata key is
    // absent (older API versions; treat unknown as final to avoid under-reporting).
    private static bool IsFinalAttempt(LogbookEntry e)
    {
        if (e.Metadata is null || !e.Metadata.TryGetValue("is_final_attempt", out var el))
        {
            return true;
        }

        return el.ValueKind != JsonValueKind.False;
    }

    // Builds the detail text shown after the job name. For execution failures and heartbeat
    // events the API-provided detail field just mirrors the event label ("Execution failed"),
    // so we extract the actually useful signal from metadata instead.
    private static string BuildDetail(LogbookEntry e)
    {
        switch (e.EventType)
        {
            case "execution_failure":
            case "ping_fail":
                return BuildFailureDetail(e);
            case "heartbeat_missed":
            case "run_abandoned":
                return BuildHeartbeatDetail(e);
            default:
                return e.Detail ?? "";
        }
    }

    private static string BuildFailureDetail(LogbookEntry e)
    {
        var parts = new List<string>();

        if (e.Metadata is { Count: > 0 })
        {
            if (e.Metadata.TryGetValue("http_status_code", out var statusEl))
            {
                var s = MetaStr(statusEl);
                if (s.Length > 0) { parts.Add($"HTTP {s}"); }
            }

            // error_message is the runtime message (e.g. "connection refused");
            // error_kind is the category (e.g. "timeout"). Prefer message, fall back to kind.
            if (e.Metadata.TryGetValue("error_message", out var errMsgEl))
            {
                var msg = MetaStr(errMsgEl);
                if (msg.Length > 0) { parts.Add(msg); }
            }
            else if (e.Metadata.TryGetValue("error_kind", out var errKindEl))
            {
                var kind = MetaStr(errKindEl);
                if (kind.Length > 0) { parts.Add(kind); }
            }

            // Show attempt context so users know whether this is a transient retry or the final verdict.
            if (e.Metadata.TryGetValue("attempt", out var attemptEl))
            {
                var attemptStr = MetaStr(attemptEl);
                var isFinal = IsFinalAttempt(e);
                if (attemptStr.Length > 0)
                {
                    parts.Add(isFinal ? $"attempt {attemptStr} · final" : $"attempt {attemptStr} · retrying");
                }
            }

            // Response body: only include if it fits and wasn't truncated (truncated bodies
            // are usually noise — the status code and error message already tell the story).
            if (parts.Count < 3
                && e.Metadata.TryGetValue("response_body_excerpt", out var bodyEl)
                && (!e.Metadata.TryGetValue("response_body_truncated", out var truncEl)
                    || truncEl.ValueKind == JsonValueKind.False))
            {
                var body = MetaStr(bodyEl);
                if (body.Length > 0 && body.Length <= 60) { parts.Add(body); }
            }
        }

        // Don't fall back to e.Detail — for execution_failure it contains "Execution failed",
        // which is identical to the event label and adds no information.
        return string.Join(" · ", parts);
    }

    private static string BuildHeartbeatDetail(LogbookEntry e)
    {
        if (e.Metadata is not null && e.Metadata.TryGetValue("last_error", out var errEl))
        {
            var lastErr = MetaStr(errEl);
            if (lastErr.Length > 0) { return lastErr; }
        }

        return e.Detail ?? "";
    }

    private static string MetaStr(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "") : el.ToString();

    private static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
