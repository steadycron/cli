using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteadyCron.Cli.Configuration;

/// <summary>
/// On-disk config: <c>{ "api_url": "…", "api_key": "sc_…" }</c>. Resolved (in order) from
/// <c>$STEADYCRON_CONFIG</c>, then <c>&lt;app-config&gt;/steadycron/config.json</c>.
/// </summary>
public sealed record ConfigFile
{
    [JsonPropertyName("api_url")]
    public string? ApiUrl { get; init; }

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Resolves the config file path, honouring <c>$STEADYCRON_CONFIG</c>.</summary>
    public static string ResolvePath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("STEADYCRON_CONFIG");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var configRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(configRoot))
        {
            configRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(configRoot, "steadycron", "config.json");
    }

    /// <summary>Loads the config file if it exists; returns null when absent. Throws on malformed JSON.</summary>
    public static ConfigFile? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ConfigFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Config file '{path}' is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Writes the config file, creating parent directories. Restricts permissions on Unix.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

        if (!OperatingSystem.IsWindows())
        {
            // The file holds an API key — make it owner-readable only (0600).
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
