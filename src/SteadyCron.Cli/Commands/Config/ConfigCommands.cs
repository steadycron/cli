using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Config;

public sealed class ConfigShowSettings : CliSettings
{
    [CommandOption("--check")]
    [Description("Verify the resolved credentials by making a test API call.")]
    public bool Check { get; set; }
}

/// <summary>`steadycron config` / `config show` — shows the resolved configuration and its sources.</summary>
public sealed class ConfigShowCommand : SteadyCronCommandBase<ConfigShowSettings>
{
    public ConfigShowCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(ConfigShowSettings settings, OutputContext output, CancellationToken ct)
    {
        var config = ResolveConfig(settings);

        if (output.Json)
        {
            output.WriteJson(new
            {
                api_url = config.ApiUrl,
                api_url_source = config.ApiUrlSource.ToString().ToLowerInvariant(),
                api_key = config.MaskedApiKey,
                api_key_source = config.HasApiKey ? config.ApiKeySource.ToString().ToLowerInvariant() : null,
                config_file = config.ConfigFilePath,
                config_file_exists = config.ConfigFilePath is not null && File.Exists(config.ConfigFilePath),
            });
        }
        else
        {
            var grid = new Grid();
            grid.AddColumn(new GridColumn().PadRight(2));
            grid.AddColumn();
            grid.AddRow("api_url", $"{Markup.Escape(config.ApiUrl)} ({config.ApiUrlSource.ToString().ToLowerInvariant()})");
            grid.AddRow(
                "api_key",
                config.HasApiKey
                    ? $"{Markup.Escape(config.MaskedApiKey!)} ({config.ApiKeySource.ToString().ToLowerInvariant()})"
                    : "[red]not set[/]");
            grid.AddRow("config file", Markup.Escape(config.ConfigFilePath ?? output.Glyphs.NoValue));
            output.Render(new Panel(grid).Header("[bold]SteadyCron configuration[/]").Border(BoxBorder.Rounded));
        }

        if (!config.HasApiKey)
        {
            output.Warn("No API key configured. Set STEADYCRON_API_KEY, pass --api-key, or run `steadycron config set --api-key sc_...`.");
            return ExitCodes.AuthError;
        }

        if (settings.Check)
        {
            var client = ClientFactory.Create(config);
            await client.ListJobsAsync(page: 1, pageSize: 1, ct: ct);
            output.Success($"Credentials OK — reached {config.ApiUrl}.");
        }

        return ExitCodes.Ok;
    }
}

/// <summary>`steadycron config set` — persists api_url / api_key to the config file.</summary>
public sealed class ConfigSetCommand : SteadyCronCommandBase<CliSettings>
{
    public ConfigSetCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override Task<int> RunAsync(CliSettings settings, OutputContext output, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) && string.IsNullOrWhiteSpace(settings.ApiUrl))
        {
            throw new CliException("Nothing to set. Pass --api-key and/or --api-url.", ExitCodes.Error);
        }

        var path = ConfigFile.ResolvePath();
        var existing = ConfigFile.Load(path);

        var merged = new ConfigFile
        {
            ApiUrl = string.IsNullOrWhiteSpace(settings.ApiUrl) ? existing?.ApiUrl : settings.ApiUrl.Trim().TrimEnd('/'),
            ApiKey = string.IsNullOrWhiteSpace(settings.ApiKey) ? existing?.ApiKey : settings.ApiKey.Trim(),
        };
        merged.Save(path);

        output.Success($"Saved configuration to {path}");
        return Task.FromResult(ExitCodes.Ok);
    }
}

/// <summary>`steadycron config path` — prints the config file path.</summary>
public sealed class ConfigPathCommand : SteadyCronCommandBase<CliSettings>
{
    public ConfigPathCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override Task<int> RunAsync(CliSettings settings, OutputContext output, CancellationToken ct)
    {
        output.Out.WriteLine(ConfigFile.ResolvePath());
        return Task.FromResult(ExitCodes.Ok);
    }
}
