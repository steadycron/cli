using System.ComponentModel;
using Spectre.Console.Cli;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Sync;

public sealed class PlanSettings : SyncSettings
{
    private string? _outputFormat;

    [CommandOption("--output")]
    [Description("Output format: 'text' (default) or 'json' (machine-readable server plan, used by CI).")]
    public string? OutputFormat
    {
        get => _outputFormat;
        set
        {
            _outputFormat = value;
            if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
            {
                Json = true;
            }
        }
    }
}

/// <summary>
/// `steadycron plan [paths...]` — preview what sync would change.
/// Alias for <c>sync --dry-run</c>. Exits 0 when no drift;
/// exits 2 with <c>--detailed-exitcode</c> when drift is detected.
/// </summary>
public sealed class PlanCommand : SteadyCronCommandBase<PlanSettings>
{
    private readonly ReconcileEngine _engine;

    public PlanCommand(
        ConfigResolver resolver,
        SteadyCronClientFactory clientFactory,
        CancellationProvider cancellation,
        ManifestLoader loader)
        : base(resolver, clientFactory, cancellation)
    {
        _engine = new ReconcileEngine(loader);
    }

    protected override Task<int> RunAsync(PlanSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var apiUrl = ResolveConfig(settings).ApiUrl;
        return _engine.RunAsync(settings, output, client, apiUrl, forceDryRun: true, forceYes: false, ct);
    }
}
