using System.Text.Json;

namespace SteadyCron.Cli.Output;

internal static class LogbookFormatting
{
    internal static IReadOnlyList<string>? ResolveDomain(string domain) =>
        domain.Trim().ToLowerInvariant().Replace("-", "") switch
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

    internal static string EventLabel(string eventType) =>
        EventLabels.TryGetValue(eventType, out var label) ? label : eventType.Replace("_", " ");

    internal static string MetadataLabel(string key) =>
        MetadataLabels.TryGetValue(key, out var label) ? label : key.Replace("_", " ");

    internal static string FormatMetadataValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "—",
        _ => element.ToString(),
    };

    internal static string SeverityDot(string severity) => severity switch
    {
        "critical" => "[red]●[/]",
        "warning" => "[yellow]●[/]",
        _ => "[grey]●[/]",
    };

    internal static string SeverityLabel(string severity) => severity switch
    {
        "critical" => "[red bold]● critical[/]",
        "warning" => "[yellow]● warning[/]",
        _ => "[grey]● info[/]",
    };

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
}
