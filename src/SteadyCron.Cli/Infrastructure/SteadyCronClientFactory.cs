using SteadyCron.Cli.Api;
using SteadyCron.Cli.Configuration;

namespace SteadyCron.Cli.Infrastructure;

/// <summary>Builds a <see cref="SteadyCronClient"/> for a resolved configuration.</summary>
public sealed class SteadyCronClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SteadyCronClientFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Creates a client. Requires a resolved API key; callers should check
    /// <see cref="CliConfiguration.HasApiKey"/> first and surface a friendly message otherwise.
    /// </summary>
    public SteadyCronClient Create(CliConfiguration config)
    {
        if (!config.HasApiKey)
        {
            throw new InvalidOperationException("No API key configured.");
        }

        var http = _httpClientFactory.CreateClient("steadycron");
        return new SteadyCronClient(http, config.ApiUrl, config.ApiKey!);
    }

    /// <summary>
    /// Creates a client with no Authorization header, for the CLI-native signup/login flow,
    /// which calls anonymous <c>/api/auth/cli/*</c> endpoints before any key exists.
    /// </summary>
    public SteadyCronClient CreateUnauthenticated(CliConfiguration config)
    {
        var http = _httpClientFactory.CreateClient("steadycron");
        return new SteadyCronClient(http, config.ApiUrl, apiKey: null);
    }
}
