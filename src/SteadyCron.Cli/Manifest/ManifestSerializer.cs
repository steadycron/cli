using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SteadyCron.Cli.Manifest;

/// <summary>
/// Client-side YAML serializer for <see cref="ManifestFile"/>. Emits fields in the canonical
/// v2 key order (version → namespace → channels → tags → variables → shaping → jobs) using the
/// same <c>underscored_naming</c> convention as the loader, so output round-trips cleanly
/// through <see cref="ManifestLoader.Parse"/>.
/// </summary>
public static class ManifestSerializer
{
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    /// <summary>
    /// Serializes <paramref name="manifest"/> to a YAML string.
    /// When <paramref name="requiredEnvVars"/> is non-empty, prepends a
    /// <c># required env vars:</c> comment block listing each variable name.
    /// </summary>
    public static string Serialize(ManifestFile manifest, IReadOnlyList<string>? requiredEnvVars = null)
    {
        var yaml = YamlSerializer.Serialize(new ManifestDto
        {
            Version = manifest.Version,
            Namespace = manifest.Namespace,
            Channels = manifest.Channels is { Count: > 0 } ? manifest.Channels : null,
            Tags = manifest.Tags is { Count: > 0 } ? manifest.Tags : null,
            Variables = manifest.Variables is { Count: > 0 } ? manifest.Variables : null,
            Shaping = manifest.Shaping,
            Jobs = manifest.Jobs is { Count: > 0 } ? manifest.Jobs : null,
        });

        if (requiredEnvVars is not { Count: > 0 })
        {
            return yaml;
        }

        var sb = new StringBuilder();
        sb.AppendLine("# required env vars:");
        foreach (var name in requiredEnvVars)
        {
            sb.AppendLine($"#   {name}");
        }
        sb.AppendLine();
        sb.Append(yaml);
        return sb.ToString();
    }

    // Private DTO with the canonical v2 key order; keeps ManifestFile's declaration order separate
    // from the serialization order the user sees.
    private sealed class ManifestDto
    {
        public int? Version { get; init; }
        public string? Namespace { get; init; }
        public List<ManifestChannel>? Channels { get; init; }
        public List<ManifestTag>? Tags { get; init; }
        public List<ManifestVariable>? Variables { get; init; }
        public ManifestShaping? Shaping { get; init; }
        public List<ManifestJob>? Jobs { get; init; }
    }
}
