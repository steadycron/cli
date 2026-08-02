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

/// <summary>
/// The three job kinds, as the API spells them on the wire, plus the two predicates every caller
/// should reach for instead of comparing strings.
///
/// <para>Ask <b>"is it HTTP-executed?"</b> and <b>"is it ping-driven?"</b> separately — never derive
/// one from the negation of the other. Before the agent kind existed <c>!= "heartbeat"</c> was a
/// correct stand-in for "this is an HTTP job"; a third kind broke that reading everywhere at once
/// (see <c>docs/AGENTS.md § 12</c>), which is why the negation lives here and nowhere else.</para>
/// </summary>
public static class JobKinds
{
    public const string Http = "http";
    public const string Heartbeat = "heartbeat";
    public const string Agent = "agent";

    /// <summary>Every accepted kind, in the order they are offered to users.</summary>
    public static readonly IReadOnlyList<string> All = [Http, Heartbeat, Agent];

    public static bool IsHttp(string? kind) => Is(kind, Http);

    public static bool IsHeartbeat(string? kind) => Is(kind, Heartbeat);

    public static bool IsAgent(string? kind) => Is(kind, Agent);

    /// <summary>True for the kinds SteadyCron watches for inbound pings rather than calling out to.</summary>
    public static bool IsPingDriven(string? kind) => IsHeartbeat(kind) || IsAgent(kind);

    /// <summary>The noun to use for this kind in user-facing text.</summary>
    public static string Label(string? kind) =>
        IsAgent(kind) ? "agent monitor" : IsHeartbeat(kind) ? "heartbeat monitor" : "HTTP job";

    private static bool Is(string? kind, string expected) =>
        string.Equals(kind, expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What happens when a fire is missed (e.g. after downtime).</summary>
public enum MisfirePolicy
{
    DoNothing,
    FireOnceNow,
}

/// <summary>
/// The window a per-period agent spend ceiling sums over. Buckets are evaluated in the job's own
/// timezone, so "this month" means what the owner thinks it means.
/// </summary>
public enum AgentCostPeriod
{
    Day,
    Month,
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
