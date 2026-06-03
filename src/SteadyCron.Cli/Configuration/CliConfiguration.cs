namespace SteadyCron.Cli.Configuration;

/// <summary>Where a resolved configuration value came from (for diagnostics in `steadycron config`).</summary>
public enum ConfigSource
{
    Default,
    ConfigFile,
    Environment,
    Flag,
}

/// <summary>A fully resolved configuration: the API base URL and key, plus where each came from.</summary>
public sealed record CliConfiguration(
    string ApiUrl,
    ConfigSource ApiUrlSource,
    string? ApiKey,
    ConfigSource ApiKeySource,
    string? ConfigFilePath)
{
    public const string DefaultApiUrl = "https://api.steadycron.com";

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>API keys are shown only as their 11-char prefix (<c>sc_</c> + 8) anywhere we print them.</summary>
    public string? MaskedApiKey =>
        string.IsNullOrEmpty(ApiKey)
            ? null
            : ApiKey.Length <= 11 ? ApiKey : ApiKey[..11] + "…";
}
