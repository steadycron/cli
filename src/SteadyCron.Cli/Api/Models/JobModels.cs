using System.Text.Json.Serialization;

namespace SteadyCron.Cli.Api.Models;

/// <summary>
/// Body for <c>POST /api/jobs</c>. Nullable members are omitted from the payload when null
/// (see <see cref="SteadyCronJson"/>), so the server applies its own defaults for anything the
/// manifest left unspecified.
/// </summary>
public sealed record CreateJobRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Kind { get; init; }

    public required ScheduleKind ScheduleKind { get; init; }
    public string? CronExpression { get; init; }
    public int? IntervalSeconds { get; init; }
    public string? Timezone { get; init; }
    public int? GraceSeconds { get; init; }

    // HTTP
    public string? HttpMethod { get; init; }
    public string? HttpUrl { get; init; }
    public Dictionary<string, string>? HttpHeaders { get; init; }
    public string? HttpBody { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxRetries { get; init; }
    public int? RetryBackoffSeconds { get; init; }
    public bool? RetryOnTimeout { get; init; }
    public int[]? RetryOnStatusCodes { get; init; }
    public bool? SkipIfRunning { get; init; }
    public MisfirePolicy? MisfirePolicy { get; init; }

    // Heartbeat
    public bool? StuckRunDetection { get; init; }
    public int? MaxRunDurationSeconds { get; init; }

    public ExecutionStatus? Status { get; init; }
}

/// <summary>
/// Body for <c>PATCH /api/jobs/{id}</c>. Every member is nullable; only the changed fields are
/// sent. The server merges them onto the existing job.
/// </summary>
public sealed record UpdateJobRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? CronExpression { get; init; }
    public int? IntervalSeconds { get; init; }
    public string? Timezone { get; init; }
    public int? GraceSeconds { get; init; }
    public bool? StuckRunDetection { get; init; }
    public int? MaxRunDurationSeconds { get; init; }
    public string? HttpMethod { get; init; }
    public string? HttpUrl { get; init; }
    public Dictionary<string, string>? HttpHeaders { get; init; }
    public string? HttpBody { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxRetries { get; init; }
    public int? RetryBackoffSeconds { get; init; }
    public bool? RetryOnTimeout { get; init; }
    public int[]? RetryOnStatusCodes { get; init; }
    public bool? SkipIfRunning { get; init; }
    public MisfirePolicy? MisfirePolicy { get; init; }
}

/// <summary>The three ping URLs returned for heartbeat jobs.</summary>
public sealed record PingUrls(string Success, string Start, string Fail);

public sealed record JobTagInfo(Guid Id, string Key, string Value, string? Color);

/// <summary>A job as returned by the API (<c>GET /api/jobs</c> and friends).</summary>
public sealed record JobResponse
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public string Kind { get; init; } = "http";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? Status { get; init; }
    public string ScheduleKind { get; init; } = "cron";
    public string? CronExpression { get; init; }
    public int? IntervalSeconds { get; init; }
    public string Timezone { get; init; } = "UTC";
    public int GraceSeconds { get; init; }
    public bool StuckRunDetection { get; init; }
    public int MaxRunDurationSeconds { get; init; }
    public string? HttpMethod { get; init; }
    public string? HttpUrl { get; init; }
    public Dictionary<string, string>? HttpHeaders { get; init; }
    public string? HttpBody { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxRetries { get; init; }
    public int? RetryBackoffSeconds { get; init; }
    public bool RetryOnTimeout { get; init; }
    public int[]? RetryOnStatusCodes { get; init; }
    public bool SkipIfRunning { get; init; }
    public string MisfirePolicy { get; init; } = "do_nothing";
    public DateTimeOffset? NextFireAt { get; init; }
    public DateTimeOffset? LastFireAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public PingUrls? PingUrls { get; init; }
    public IReadOnlyList<JobTagInfo>? Tags { get; init; }
    public string? HealthState { get; init; }
    public string? BadgeUrl { get; init; }
    public string? SigningSecret { get; init; }
}

public sealed record JobListResponse(
    IReadOnlyList<JobResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record ExecutionResponse
{
    public Guid Id { get; init; }
    public Guid JobId { get; init; }
    public string Status { get; init; } = "";
    public DateTimeOffset ScheduledAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int? DurationMs { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }
    public int Attempt { get; init; }
    public bool IsFinalAttempt { get; init; }
}

public sealed record ExecutionListResponse(
    IReadOnlyList<ExecutionResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record BulkDeleteJobsRequest(IReadOnlyList<Guid> Ids);

public sealed record CronPreviewRequest(string Expression, string? Timezone, int? Count);

public sealed record CronPreviewResponse([property: JsonPropertyName("next_fires")] IReadOnlyList<DateTimeOffset> NextFires);
