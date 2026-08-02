using System.Text.Json;

namespace SteadyCron.Cli.Api.Models;

// ── Tags (/api/tags) ────────────────────────────────────────────────────────────

public sealed record TagResponse(
    Guid Id,
    Guid AccountId,
    string Key,
    string Value,
    string? Color,
    int JobCount,
    DateTimeOffset CreatedAt)
{
    public string Display => $"{Key}:{Value}";
}

public sealed record CreateTagRequest(string Key, string Value, string? Color = null);

// ── Template variables (/api/variables) ─────────────────────────────────────────

public sealed record TemplateVariableResponse(
    Guid Id,
    Guid AccountId,
    string Name,
    string Value,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateTemplateVariableRequest(string Name, string? Value = null);

public sealed record UpdateTemplateVariableRequest(string? Name = null, string? Value = null);

// ── Alert channels (/api/alert-channels) ────────────────────────────────────────

public sealed record AlertChannelResponse(
    Guid Id,
    Guid AccountId,
    string Name,
    string Kind,
    Dictionary<string, string>? Config,
    DateTimeOffset CreatedAt);

public sealed record CreateAlertChannelRequest(
    string Name,
    string Kind,
    Dictionary<string, string> Config);

// ── Alert rules (/api/jobs/{id}/alert-rules, /api/alert-rules/{id}) ──────────────

/// <summary>What triggers an alert. Serialized snake_case (e.g. <c>on_failure</c>).</summary>
public enum AlertTrigger
{
    OnFailure,
    OnNConsecutive,
    OnMissedHeartbeat,
    OnRecovery,

    // Smart / anomaly triggers (HTTP jobs) — per-execution response checks.
    OnSlowRun,
    OnSizeAnomaly,

    // Agent monitor triggers, evaluated server-side against each run report. OnSlowRun is reused
    // for an agent's duration ceiling rather than adding a fourth.
    OnEmptyResult,
    OnCostExceeded,
    OnNoProgress,
    OnUnverifiedRun,
}

/// <summary>Alert severity. Serialized snake_case for requests (<c>p1</c>/<c>p2</c>/<c>p3</c>).</summary>
public enum AlertSeverity
{
    P1,
    P2,
    P3,
}

public sealed record CreateAlertRuleRequest
{
    public required Guid ChannelId { get; init; }
    public required AlertTrigger Trigger { get; init; }
    public AlertSeverity? Severity { get; init; }
    public Dictionary<string, object>? Params { get; init; }
    public int? DedupWindowSeconds { get; init; }
}

public sealed record AlertRuleResponse(
    Guid Id,
    Guid JobId,
    Guid ChannelId,
    string Trigger,
    string Severity,
    Dictionary<string, JsonElement>? Params,
    int DedupWindowSeconds,
    DateTimeOffset CreatedAt);
