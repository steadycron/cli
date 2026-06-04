using System.ComponentModel;
using Spectre.Console.Cli;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Sync;

public sealed class ApplySettings : SyncSettings { }

/// <summary>
/// `steadycron apply [paths...]` — applies a manifest without a confirmation prompt.
/// Alias for <c>sync --yes</c>. Typical use: CI pipelines on merge to the default branch.
/// </summary>
public sealed class ApplyCommand : SteadyCronCommandBase<ApplySettings>
{
    private readonly ReconcileEngine _engine;

    public ApplyCommand(
        ConfigResolver resolver,
        SteadyCronClientFactory clientFactory,
        CancellationProvider cancellation,
        ManifestLoader loader)
        : base(resolver, clientFactory, cancellation)
    {
        _engine = new ReconcileEngine(loader);
    }

    protected override Task<int> RunAsync(ApplySettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var apiUrl = ResolveConfig(settings).ApiUrl;
        return _engine.RunAsync(settings, output, client, apiUrl, forceDryRun: false, forceYes: true, ct);
    }
}
