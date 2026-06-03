namespace SteadyCron.Cli.Api.Models;

// All enums below serialize via the global JsonStringEnumConverter(SnakeCaseLower) configured
// in SteadyCronJson.Options, matching the API (e.g. ScheduleKind.Cron -> "cron",
// MisfirePolicy.FireOnceNow -> "fire_once_now", ExecutionStatus.Paused -> "paused").

/// <summary>How a job is scheduled.</summary>
public enum ScheduleKind
{
    Cron,
    Interval,
}

/// <summary>What happens when a fire is missed (e.g. after downtime).</summary>
public enum MisfirePolicy
{
    DoNothing,
    FireOnceNow,
}

/// <summary>
/// The <c>status</c> value accepted by the create endpoint. Only <c>Paused</c> is meaningful for
/// creation (create a job already paused); the others exist for contract completeness.
/// </summary>
public enum ExecutionStatus
{
    Running,
    Success,
    Failure,
    Skipped,
    Missed,
    Late,
    Paused,
}
