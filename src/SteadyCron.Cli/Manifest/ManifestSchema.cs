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

    public static readonly IReadOnlyList<string> ChannelKinds = ["email", "slack", "discord", "webhook", "telegram"];

    public static readonly IReadOnlyList<string> MisfirePolicies = ["do_nothing", "fire_once_now"];

    public static readonly IReadOnlyList<string> TagColors =
    [
        "red", "orange", "amber", "yellow", "lime", "green", "teal", "cyan", "blue",
        "indigo", "violet", "purple", "pink", "rose", "slate", "gray", "zinc",
    ];
}
