using System.ComponentModel;
using System.Net;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Commands.Channels;
using SteadyCron.Cli.Commands.Jobs;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Rules;

internal static class AlertParsing
{
    public static AlertTrigger ParseTrigger(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant().Replace('-', '_');
        if (normalized.StartsWith("on_", StringComparison.Ordinal))
        {
            normalized = normalized[3..];
        }

        return normalized switch
        {
            "failure" or "fail" => AlertTrigger.OnFailure,
            "n_consecutive" or "consecutive" => AlertTrigger.OnNConsecutive,
            "missed_heartbeat" or "missed" => AlertTrigger.OnMissedHeartbeat,
            "recovery" or "recover" => AlertTrigger.OnRecovery,
            "slow_run" or "slow" => AlertTrigger.OnSlowRun,
            "size_anomaly" or "size" => AlertTrigger.OnSizeAnomaly,
            _ => throw new CliException(
                $"Unknown trigger '{raw}'. Expected one of: failure, n_consecutive, missed_heartbeat, recovery, slow_run, size_anomaly.",
                ExitCodes.Error),
        };
    }

    public static AlertSeverity ParseSeverity(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "p1" => AlertSeverity.P1,
        "p2" => AlertSeverity.P2,
        "p3" => AlertSeverity.P3,
        _ => throw new CliException($"Unknown severity '{raw}'. Expected p1, p2, or p3.", ExitCodes.Error),
    };

    public static bool IsSmart(AlertTrigger t) => t is AlertTrigger.OnSlowRun or AlertTrigger.OnSizeAnomaly;
}

public sealed class RulesListSettings : CliSettings
{
    [CommandArgument(0, "<JOB>")]
    [Description("Job key, exact name, or id (GUID).")]
    public string Job { get; set; } = string.Empty;
}

public sealed class RulesListCommand : SteadyCronCommandBase<RulesListSettings>
{
    public RulesListCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(RulesListSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Job, ct);
        var rules = await client.ListRulesAsync(job.Id, ct);

        if (output.Json)
        {
            output.WriteJson(rules);
            return ExitCodes.Ok;
        }

        if (rules.Count == 0)
        {
            output.Info($"No alert rules on '{job.Name}'.");
            return ExitCodes.Ok;
        }

        var channels = (await client.ListChannelsAsync(ct)).ToDictionary(c => c.Id);

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("Trigger");
        table.AddColumn("Severity");
        table.AddColumn("Channel");
        table.AddColumn("Kind");
        table.AddColumn("Target");
        table.AddColumn("Dedup");
        table.AddColumn(new TableColumn("Rule id").NoWrap());
        foreach (var r in rules)
        {
            channels.TryGetValue(r.ChannelId, out var ch);
            table.AddRow(
                Markup.Escape(r.Trigger),
                Markup.Escape(r.Severity),
                Markup.Escape(ch?.Name ?? r.ChannelId.ToString()),
                Markup.Escape(ch?.Kind ?? output.Glyphs.NoValue),
                Markup.Escape(ch is not null ? ChannelLookup.DescribeConfig(ch) : output.Glyphs.NoValue),
                Markup.Escape($"{r.DedupWindowSeconds}s"),
                r.Id.ToString());
        }

        output.Render(table);
        return ExitCodes.Ok;
    }
}

public sealed class RuleAddSettings : CliSettings
{
    [CommandArgument(0, "<JOB>")]
    [Description("Job key, exact name, or id (GUID).")]
    public string Job { get; set; } = string.Empty;

    [CommandOption("--channel <CHANNEL>")]
    [Description("Alert channel id or exact name.")]
    public string? Channel { get; set; }

    [CommandOption("--trigger <TRIGGER>")]
    [Description("failure | n_consecutive | missed_heartbeat | recovery | slow_run | size_anomaly.")]
    public string? Trigger { get; set; }

    [CommandOption("--severity <SEVERITY>")]
    [Description("p1 | p2 (default) | p3.")]
    public string? Severity { get; set; }

    [CommandOption("--dedup <SECONDS>")]
    [Description("Deduplication window in seconds.")]
    public int? DedupSeconds { get; set; }

    [CommandOption("--factor <N>")]
    [Description("slow_run/size_anomaly: anomaly factor (1.5–100).")]
    public double? Factor { get; set; }

    [CommandOption("--min-samples <N>")]
    [Description("slow_run/size_anomaly: minimum baseline samples (3–100).")]
    public int? MinSamples { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Job))
        {
            return ValidationResult.Error("A job key, name, or id is required.");
        }

        if (string.IsNullOrWhiteSpace(Channel))
        {
            return ValidationResult.Error("--channel is required.");
        }

        return string.IsNullOrWhiteSpace(Trigger)
            ? ValidationResult.Error("--trigger is required.")
            : ValidationResult.Success();
    }
}

public sealed class RuleAddCommand : SteadyCronCommandBase<RuleAddSettings>
{
    public RuleAddCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(RuleAddSettings settings, OutputContext output, CancellationToken ct)
    {
        var trigger = AlertParsing.ParseTrigger(settings.Trigger!);
        var severity = settings.Severity is null ? (AlertSeverity?)null : AlertParsing.ParseSeverity(settings.Severity);

        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Job, ct);
        var channel = await ChannelLookup.ResolveAsync(client, settings.Channel!, ct);

        Dictionary<string, object>? prms = null;
        if (AlertParsing.IsSmart(trigger))
        {
            if (settings.Factor.HasValue || settings.MinSamples.HasValue)
            {
                prms = new Dictionary<string, object>(StringComparer.Ordinal);
                if (settings.Factor.HasValue) { prms["factor"] = settings.Factor.Value; }
                if (settings.MinSamples.HasValue) { prms["min_baseline_samples"] = settings.MinSamples.Value; }
            }
        }
        else if (settings.Factor.HasValue || settings.MinSamples.HasValue)
        {
            output.Warn($"--factor/--min-samples apply only to slow_run/size_anomaly triggers; ignoring for {settings.Trigger}.");
        }

        var rule = await client.CreateRuleAsync(job.Id, new CreateAlertRuleRequest
        {
            ChannelId = channel.Id,
            Trigger = trigger,
            Severity = severity,
            Params = prms,
            DedupWindowSeconds = settings.DedupSeconds,
        }, ct);

        if (output.Json)
        {
            output.WriteJson(rule);
            return ExitCodes.Ok;
        }

        output.Success($"Added {rule.Trigger} {output.Glyphs.Arrow} '{channel.Name}' on '{job.Name}' ({rule.Id}).");
        return ExitCodes.Ok;
    }
}

public sealed class RuleDeleteSettings : CliSettings
{
    [CommandArgument(0, "<RULE_ID>")]
    [Description("Alert rule id (from `rules list`).")]
    public string RuleId { get; set; } = string.Empty;

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt.")]
    public bool Yes { get; set; }

    public override ValidationResult Validate() =>
        Guid.TryParse(RuleId, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error("A valid rule id (GUID) is required. Find it with `rules list <job>`.");
}

public sealed class RuleDeleteCommand : SteadyCronCommandBase<RuleDeleteSettings>
{
    public RuleDeleteCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(RuleDeleteSettings settings, OutputContext output, CancellationToken ct)
    {
        var id = Guid.Parse(settings.RuleId);

        if (!settings.Yes && !settings.Json && !Console.IsInputRedirected)
        {
            if (!output.Out.Confirm(PromptFormatting.Marker($"Delete alert rule {id}?"), false))
            {
                output.Info("Aborted.");
                return ExitCodes.Ok;
            }
        }

        var client = CreateClient(settings);
        await client.DeleteRuleAsync(id, ct);
        output.Success($"Deleted alert rule {id}.");
        return ExitCodes.Ok;
    }
}

public sealed class RulesTestSettings : CliSettings
{
    [CommandArgument(0, "<JOB>")]
    [Description("Job key, exact name, or id (GUID).")]
    public string Job { get; set; } = string.Empty;
}

/// <summary>`steadycron rules test &lt;job&gt;` — sends a test notification on every channel
/// that has at least one alert rule configured for the given job.</summary>
public sealed class RulesTestCommand : SteadyCronCommandBase<RulesTestSettings>
{
    public RulesTestCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(RulesTestSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Job, ct);
        var rules = await client.ListRulesAsync(job.Id, ct);

        if (rules.Count == 0)
        {
            output.Info($"No alert rules configured for '{job.Name}'. Add one with 'rules add'.");
            return ExitCodes.Ok;
        }

        // One test per unique channel — multiple rules pointing at the same channel don't multiply tests.
        var uniqueChannelIds = rules.Select(r => r.ChannelId).Distinct().ToList();
        var channels = (await client.ListChannelsAsync(ct)).ToDictionary(c => c.Id);

        var anyFailed = false;
        var arrow = output.Glyphs.Arrow;
        foreach (var channelId in uniqueChannelIds)
        {
            channels.TryGetValue(channelId, out var ch);
            var label = ch is not null
                ? $"{ch.Name} ({ch.Kind} {arrow} {ChannelLookup.DescribeConfig(ch)})"
                : channelId.ToString();

            try
            {
                await client.TestChannelAsync(channelId, ct);
                output.Success($"Test alert sent {arrow} {label}");
            }
            catch (SteadyCronApiException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                output.Warn($"Rate limited   {arrow} {label}: email send limit reached, try again later.");
                anyFailed = true;
            }
            catch (SteadyCronApiException ex)
            {
                output.Error($"Failed         {arrow} {label}: {ex.Message}");
                anyFailed = true;
            }
        }

        output.Line();
        if (!anyFailed)
        {
            output.Info($"{uniqueChannelIds.Count} channel(s) tested. Check each destination for delivery.");
        }
        else
        {
            output.Warn($"{uniqueChannelIds.Count} channel(s) tested — one or more failed (see above).");
        }

        return anyFailed ? ExitCodes.Error : ExitCodes.Ok;
    }
}
