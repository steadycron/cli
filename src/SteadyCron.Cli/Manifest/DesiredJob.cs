using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Manifest;

/// <summary>
/// The normalized, defaults-applied desired state for one job, derived from a
/// <see cref="ManifestJob"/>. This is the canonical shape compared against the server's
/// <see cref="JobResponse"/> during sync. <c>name</c> is the reconciliation key.
/// </summary>
public sealed record DesiredJob
{
    public required string Name { get; init; }

    /// <summary><c>http</c> or <c>heartbeat</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Stable unique key for this job; maps to <c>job_key</c> on the API. Null = auto-generated from name.</summary>
    public string? JobKey { get; init; }

    /// <summary>Normalized: null when empty.</summary>
    public string? Description { get; init; }

    public ScheduleKind ScheduleKind { get; init; }
    public string? CronExpression { get; init; }
    public int? IntervalSeconds { get; init; }
    public string Timezone { get; init; } = JobDefaults.Timezone;
    public bool Paused { get; init; }

    // ── HTTP (null for heartbeat) ────────────────────────────────────────────────
    public string? HttpMethod { get; init; }
    public string? HttpUrl { get; init; }

    /// <summary>Never null; empty when no headers.</summary>
    public IReadOnlyDictionary<string, string> HttpHeaders { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Normalized: null when empty.</summary>
    public string? HttpBody { get; init; }
    public int TimeoutSeconds { get; init; } = JobDefaults.TimeoutSeconds;
    public int MaxRetries { get; init; } = JobDefaults.MaxRetries;
    public int RetryBackoffSeconds { get; init; } = JobDefaults.RetryBackoffSeconds;
    public bool RetryOnTimeout { get; init; } = JobDefaults.RetryOnTimeout;

    /// <summary>null = retry on any non-2xx; otherwise a sorted, distinct set of codes.</summary>
    public IReadOnlyList<int>? RetryOnStatusCodes { get; init; }
    public bool SkipIfRunning { get; init; }
    public string MisfirePolicyValue { get; init; } = JobDefaults.MisfirePolicy;

    // ── Heartbeat ────────────────────────────────────────────────────────────────
    public int GraceSeconds { get; init; } = JobDefaults.GraceSeconds;
    public bool StuckRunDetection { get; init; } = JobDefaults.StuckRunDetection;

    /// <summary>Only managed (created/diffed) when explicitly set; otherwise the server computes it.</summary>
    public int? MaxRunDurationSeconds { get; init; }

    public bool IsHttp => string.Equals(Kind, "http", StringComparison.Ordinal);
}

/// <summary>API-matching defaults applied to omitted manifest fields (see JobsController).</summary>
public static class JobDefaults
{
    public const string Timezone = "UTC";
    public const string Method = "GET";
    public const int TimeoutSeconds = 60;
    public const int MaxRetries = 0;
    public const int RetryBackoffSeconds = 30;
    public const bool RetryOnTimeout = true;
    public const string MisfirePolicy = "do_nothing";
    public const int GraceSeconds = 60;
    public const bool StuckRunDetection = true;
}

/// <summary>A single field that differs between desired and server state, for plan display.</summary>
public sealed record FieldChange(string Field, string From, string To);
