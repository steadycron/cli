namespace SteadyCron.Cli.Configuration;

/// <summary>
/// Merges configuration from (highest precedence first) explicit flags, environment variables
/// (<c>STEADYCRON_API_KEY</c> / <c>STEADYCRON_API_URL</c>), the config file, and built-in defaults.
/// </summary>
public sealed class ConfigResolver
{
    public const string ApiKeyEnvVar = "STEADYCRON_API_KEY";
    public const string ApiUrlEnvVar = "STEADYCRON_API_URL";

    private readonly Func<string, string?> _getEnv;
    private readonly Func<string?> _resolveConfigPath;
    private readonly Func<string, ConfigFile?> _loadConfig;

    public ConfigResolver()
        : this(Environment.GetEnvironmentVariable, ConfigFile.ResolvePath, ConfigFile.Load)
    {
    }

    // Test seam: inject env/config sources.
    public ConfigResolver(
        Func<string, string?> getEnv,
        Func<string?> resolveConfigPath,
        Func<string, ConfigFile?> loadConfig)
    {
        _getEnv = getEnv;
        _resolveConfigPath = resolveConfigPath;
        _loadConfig = loadConfig;
    }

    public CliConfiguration Resolve(string? flagApiKey, string? flagApiUrl)
    {
        var configPath = _resolveConfigPath();
        ConfigFile? file = null;
        if (!string.IsNullOrEmpty(configPath))
        {
            file = _loadConfig(configPath);
        }

        var (apiUrl, apiUrlSource) = ResolveValue(
            flagApiUrl,
            _getEnv(ApiUrlEnvVar),
            file?.ApiUrl,
            CliConfiguration.DefaultApiUrl);

        var (apiKey, apiKeySource) = ResolveValue(
            flagApiKey,
            _getEnv(ApiKeyEnvVar),
            file?.ApiKey,
            fallback: null);

        return new CliConfiguration(
            ApiUrl: NormalizeUrl(apiUrl!),
            ApiUrlSource: apiUrlSource,
            ApiKey: string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
            ApiKeySource: apiKeySource,
            ConfigFilePath: configPath);
    }

    private static (string? Value, ConfigSource Source) ResolveValue(
        string? flag, string? env, string? file, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(flag))
        {
            return (flag, ConfigSource.Flag);
        }

        if (!string.IsNullOrWhiteSpace(env))
        {
            return (env, ConfigSource.Environment);
        }

        if (!string.IsNullOrWhiteSpace(file))
        {
            return (file, ConfigSource.ConfigFile);
        }

        return (fallback, ConfigSource.Default);
    }

    private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/');
}
