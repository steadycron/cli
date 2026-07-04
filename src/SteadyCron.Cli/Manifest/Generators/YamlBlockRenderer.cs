using System.Text;
using System.Text.RegularExpressions;

namespace SteadyCron.Cli.Manifest.Generators;

/// <summary>
/// YAML implementation of <see cref="IManifestBlockRenderer"/>. Blocks are brief by design (§3.1:
/// "shorter than the cheat sheet; one line per non-obvious field") — <c>manifest scaffold</c>
/// remains the fully-documented reference; these blocks are what a user actually wants inserted
/// into a real, evolving manifest. Every secret-bearing field is emitted as a <c>${ENV}</c>
/// placeholder — this renderer never receives (and so can never leak) a real secret value.
/// </summary>
public sealed class YamlBlockRenderer : IManifestBlockRenderer
{
    private static readonly Regex NonEnvChar = new(@"[^A-Z0-9]+", RegexOptions.Compiled);

    public string RenderJob(NewJobSpec spec)
    {
        var sb = new StringBuilder();
        sb.Append("  - id: ").Append(spec.Id).Append('\n');
        sb.Append("    name: ").Append(spec.Name).Append('\n');
        sb.Append("    kind: ").Append(spec.Kind).Append('\n');

        if (spec.Schedule is not null)
        {
            sb.Append("    schedule: \"").Append(spec.Schedule).Append("\"       # or use `interval: <seconds>` instead\n");
        }
        else
        {
            sb.Append("    interval: ").Append(spec.Interval).Append("         # seconds; or use `schedule: \"<cron>\"` instead\n");
        }

        sb.Append("    timezone: ").Append(spec.Timezone).Append('\n');

        if (spec.IsHttp)
        {
            sb.Append("    method: ").Append(spec.Method).Append('\n');
            sb.Append("    url: ").Append(spec.Url).Append('\n');
            sb.Append("    # rules:\n");
            sb.Append("    #   - channel: ops-email       # channel name — see `manifest add channel`\n");
            sb.Append("    #     trigger: on_failure\n");
        }
        else
        {
            sb.Append("    grace: ").Append(spec.Grace)
                .Append("               # seconds of slack after the expected ping before alerting\n");
            sb.Append("    # rules:\n");
            sb.Append("    #   - channel: ops-email       # channel name — see `manifest add channel`\n");
            sb.Append("    #     trigger: on_missed_heartbeat\n");
        }

        return sb.ToString();
    }

    public string RenderChannel(NewChannelSpec spec)
    {
        var sb = new StringBuilder();
        sb.Append("  - name: ").Append(spec.Name).Append('\n');
        sb.Append("    kind: ").Append(spec.Kind).Append('\n');
        sb.Append("    config:\n");

        var env = ToEnvName(spec.Name);
        switch (spec.Kind)
        {
            case "email":
                sb.Append("      to: ").Append(spec.To).Append('\n');
                break;

            case "slack":
            case "discord":
                sb.Append("      webhook_url: ").Append(EnvPlaceholder($"{env}_WEBHOOK_URL"))
                    .Append("   # set via --env-file or the environment at apply-time\n");
                break;

            case "webhook":
                sb.Append("      url: ").Append(EnvPlaceholder($"{env}_URL"))
                    .Append("   # set via --env-file or the environment at apply-time\n");
                break;

            case "telegram":
                sb.Append("      bot_token: ").Append(EnvPlaceholder($"{env}_BOT_TOKEN"))
                    .Append("   # set via --env-file or the environment at apply-time\n");
                sb.Append("      chat_id: ").Append(EnvPlaceholder($"{env}_CHAT_ID"))
                    .Append("    # set via --env-file or the environment at apply-time\n");
                break;
        }

        return sb.ToString();
    }

    public string RenderTag(NewTagSpec spec)
    {
        var sb = new StringBuilder();
        sb.Append("  - key: ").Append(spec.Key).Append('\n');
        sb.Append("    value: ").Append(spec.Value).Append('\n');
        if (spec.Color is not null)
        {
            sb.Append("    color: ").Append(spec.Color)
                .Append("   # valid colors: ").Append(string.Join(", ", ManifestSchema.TagColors)).Append('\n');
        }

        return sb.ToString();
    }

    public string RenderVariable(NewVariableSpec spec)
    {
        var env = ToEnvName(spec.Name);
        var sb = new StringBuilder();
        sb.Append("  - name: ").Append(spec.Name).Append('\n');
        sb.Append("    value: ").Append(EnvPlaceholder(env))
            .Append("     # reads the ").Append(env).Append(" environment variable at apply-time\n");

        return sb.ToString();
    }

    private static string EnvPlaceholder(string name) => "${" + name + "}";

    /// <summary>Converts a resource name into an uppercase, underscore-separated env var name,
    /// e.g. "ops-slack" -> "OPS_SLACK".</summary>
    private static string ToEnvName(string name)
    {
        var slug = NonEnvChar.Replace(name.ToUpperInvariant().Trim(), "_").Trim('_');
        return slug.Length == 0 ? "CHANNEL" : slug;
    }
}
