using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands;

/// <summary>
/// Base for all commands: resolves configuration, creates the API client on demand, and turns the
/// expected exception types into friendly messages and stable exit codes.
/// </summary>
public abstract class SteadyCronCommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CliSettings
{
    private readonly CancellationProvider _cancellation;

    protected SteadyCronCommandBase(
        ConfigResolver resolver,
        SteadyCronClientFactory clientFactory,
        CancellationProvider cancellation)
    {
        Resolver = resolver;
        ClientFactory = clientFactory;
        _cancellation = cancellation;
    }

    protected ConfigResolver Resolver { get; }

    protected SteadyCronClientFactory ClientFactory { get; }

    public sealed override async Task<int> ExecuteAsync(CommandContext context, TSettings settings)
    {
        var output = new OutputContext(settings.Json, settings.Quiet, settings.NoColor);
        try
        {
            return await RunAsync(settings, output, _cancellation.Token);
        }
        catch (CliException ex)
        {
            output.Error(ex.Message);
            return ex.ExitCode;
        }
        catch (ManifestException ex)
        {
            output.Error(ex.Message);
            return ExitCodes.ManifestError;
        }
        catch (SteadyCronApiException ex)
        {
            output.ApiError(ex);
            return ex.IsAuthError ? ExitCodes.AuthError : ExitCodes.ApiError;
        }
        catch (HttpRequestException ex)
        {
            output.Error($"Could not reach the API: {ex.Message}");
            return ExitCodes.ApiError;
        }
        catch (OperationCanceledException)
        {
            output.Warn("Cancelled.");
            return ExitCodes.Cancelled;
        }
    }

    protected abstract Task<int> RunAsync(TSettings settings, OutputContext output, CancellationToken ct);

    protected CliConfiguration ResolveConfig(TSettings settings) =>
        Resolver.Resolve(settings.ApiKey, settings.ApiUrl);

    /// <summary>Resolves config and builds a client, or throws a friendly auth error if no key is set.</summary>
    protected SteadyCronClient CreateClient(TSettings settings)
    {
        var config = ResolveConfig(settings);
        if (!config.HasApiKey)
        {
            throw new CliException(
                "No API key configured. Set the STEADYCRON_API_KEY environment variable, pass --api-key, " +
                "or run `steadycron config set --api-key sc_...`.",
                ExitCodes.AuthError);
        }

        return ClientFactory.Create(config);
    }
}
