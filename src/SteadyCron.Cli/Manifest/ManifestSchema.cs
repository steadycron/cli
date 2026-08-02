using SteadyCron.Cli.Api.Models;

namespace SteadyCron.Cli.Manifest;

/// <summary>
/// The single source of truth for manifest enum-like value sets (valid HTTP methods, channel
/// kinds, misfire policies, tag colors). Consumed by validation (<see cref="JobMapper"/>,
/// <see cref="ManifestValidator"/>), imperative commands (<c>channels create</c>), the
/// documentation generators (<c>manifest scaffold</c>, <c>manifest add</c>), and their tests —
/// so the list of valid values can never drift between what's documented and what's accepted.
/// </summary>
public static class ManifestSchema
{
    public static readonly IReadOnlyList<string> HttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    /// <summary>The job kinds a manifest may declare. Delegates to the wire contract so the
    /// manifest and the API client can never disagree about what a kind is called.</summary>
    public static readonly IReadOnlyList<string> JobKinds = Api.Models.JobKinds.All;

    /// <summary>The window an agent's per-period spend ceiling sums over.</summary>
    public static readonly IReadOnlyList<string> AgentCostPeriods = ["day", "month"];

    public static readonly IReadOnlyList<string> ChannelKinds = ["email", "slack", "discord", "webhook", "telegram"];

    public static readonly IReadOnlyList<string> MisfirePolicies = ["do_nothing", "fire_once_now"];

    public static readonly IReadOnlyList<string> TagColors =
    [
        "red", "orange", "amber", "yellow", "lime", "green", "teal", "cyan", "blue",
        "indigo", "violet", "purple", "pink", "rose", "slate", "gray", "zinc",
    ];
}
