namespace SteadyCron.Cli.Api.Models;

public sealed record ReportSummaryResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    int PlanMaxWindowDays,
    ReportFilters AppliedFilters,
    ReportSummaryStats Summary,
    IReadOnlyList<ReportJobSummary> Jobs,
    IReadOnlyList<ReportSilentJob> SilentJobs,
    IReadOnlyList<ReportAlertDelivery> AccountAlerts);

public sealed record ReportFilters(
    IReadOnlyList<string> Types,
    string? JobId,
    string? Kind,
    string? Status,
    IReadOnlyList<string> Tags);

public sealed record ReportSummaryStats(
    int TotalExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    int TotalPings,
    int TotalAlerts,
    int AlertsDelivered,
    int AlertsFailed,
    int AlertsSuppressed,
    int TotalJobsActive,
    int TotalJobsWithActivity,
    int TotalJobsSilent);

public sealed record ReportJobSummary(
    Guid JobId,
    string JobName,
    string Kind,
    string CurrentStatus,
    IReadOnlyList<ReportTagInfo> Tags,
    int ExecutionCount,
    int SuccessCount,
    int FailureCount,
    int PingCount,
    DateTimeOffset? LastExecutionAt,
    ReportLastFailure? LastFailure,
    IReadOnlyList<ReportAlertDelivery> AlertDeliveries);

public sealed record ReportLastFailure(
    Guid ExecutionId,
    DateTimeOffset OccurredAt,
    int? HttpStatusCode,
    int? DurationMs,
    string? ErrorKind,
    string? ErrorMessage,
    string? ResponseBody,
    bool ResponseBodyTruncated,
    string? ResponseHeaders,
    int Attempt,
    bool IsFinalAttempt);

public sealed record ReportAlertDelivery(
    Guid DeliveryId,
    string? Trigger,
    string? NoticeKind,
    Guid ChannelId,
    string? ChannelName,
    string? ChannelKind,
    string Status,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? DeliveredAt,
    string? LastError,
    string? SuppressedReason);

public sealed record ReportSilentJob(
    Guid JobId,
    string JobName,
    string Kind,
    IReadOnlyList<ReportTagInfo> Tags,
    string CurrentStatus,
    bool IsPaused,
    string? PausedReason,
    DateTimeOffset? NextFireAt);

public sealed record ReportTagInfo(Guid Id, string Key, string Value, string? Color);
