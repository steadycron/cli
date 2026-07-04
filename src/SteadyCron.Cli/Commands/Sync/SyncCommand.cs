using System.ComponentModel;
using Spectre.Console.Cli;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Sync;

/// <summary>Shared settings for sync / plan / apply.</summary>
public class SyncSettings : CliSettings
{
    [CommandArgument(0, "[PATHS...]")]
    [Description("Manifest files or directories (default: steadycron.yaml).")]
    public string[]? Paths { get; set; }

    [CommandOption("--namespace|-n")]
    [Description("Account namespace. Required when using --prune.")]
    public string? Namespace { get; set; }

    [CommandOption("--dry-run|--plan")]
    [Description("Show what would change without applying anything.")]
    public bool DryRun { get; set; }

    [CommandOption("--prune")]
    [Description("Delete server resources not declared in the manifest. Requires --namespace.")]
    public bool Prune { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Apply without the interactive confirmation prompt.")]
    public bool Yes { get; set; }

    [CommandOption("--detailed-exitcode")]
    [Description("Exit with code 2 when the plan finds drift (Terraform-style). Without this flag a plan with drift exits 0.")]
    public bool DetailedExitCode { get; set; }

    [CommandOption("--no-warn-v1")]
    [Description("Suppress the v1 manifest deprecation warning.")]
    public bool NoWarnV1 { get; set; }

    [CommandOption("--env-file <PATH>")]
    [Description("Load ${...} secret values from a .env file (repeatable). Takes precedence over the process environment.")]
    public string[]? EnvFiles { get; set; }

    [CommandOption("--allow-process-env")]
    [Description("Resolve ${...} placeholders from the process environment without requiring an --env-file.")]
    public bool AllowProcessEnv { get; set; }

    public IEnumerable<string> EffectivePaths =>
        Paths is { Length: > 0 } ? Paths : ["steadycron.yaml"];

    public IReadOnlyList<string> EnvFileList => EnvFiles ?? [];
}

/// <summary>
/// `steadycron sync [paths...]` — reconciles the account with a YAML manifest via
/// <c>POST /api/reconcile</c>. The server computes and applies the plan atomically.
/// Use <c>--dry-run</c> (or the <c>plan</c> alias) to preview without applying.
/// </summary>
public sealed class SyncCommand : SteadyCronCommandBase<SyncSettings>
{
    private readonly ReconcileEngine _engine;

    public SyncCommand(
        ConfigResolver resolver,
        SteadyCronClientFactory clientFactory,
        CancellationProvider cancellation,
        ManifestLoader loader)
        : base(resolver, clientFactory, cancellation)
    {
        _engine = new ReconcileEngine(loader);
    }

    protected override Task<int> RunAsync(SyncSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var apiUrl = ResolveConfig(settings).ApiUrl;
        return _engine.RunAsync(settings, output, client, apiUrl, forceDryRun: false, forceYes: false, ct);
    }
}
