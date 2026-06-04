using System.ComponentModel;
using Spectre.Console.Cli;
using SteadyCron.Cli.Commands.Jobs;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Export;

public sealed class ExportSettings : CliSettings
{
    [CommandArgument(0, "[TARGET]")]
    [Description("For --scope job: job name or id to export.")]
    public string? Target { get; set; }

    [CommandOption("--scope")]
    [Description("What to export: 'account' (default), 'jobs', or 'job'.")]
    public string? Scope { get; set; }

    [CommandOption("-o|--output-file")]
    [Description("Write the manifest to this file instead of stdout.")]
    public string? OutputFile { get; set; }

    [CommandOption("--format")]
    [Description("Output format: 'yaml' (default) or 'json'.")]
    public string? Format { get; set; }

    [CommandOption("--namespace|-n")]
    [Description("Stamp this namespace into the exported manifest's 'namespace' field.")]
    public string? Namespace { get; set; }

    public string EffectiveScope =>
        string.IsNullOrWhiteSpace(Scope) ? "account" : Scope.Trim().ToLowerInvariant();
}

/// <summary>
/// `steadycron export` — exports the account (or a subset) as a v2 manifest.
/// The manifest is written verbatim from the server; secret fields arrive as
/// <c>${PLACEHOLDER}</c> references. Required env vars are summarised on stderr
/// so that piping with <c>-o</c> stays clean.
/// </summary>
public sealed class ExportCommand : SteadyCronCommandBase<ExportSettings>
{
    public ExportCommand(
        ConfigResolver resolver,
        SteadyCronClientFactory clientFactory,
        CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(
        ExportSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        string manifestText;

        switch (settings.EffectiveScope)
        {
            case "account":
                manifestText = await client.ExportAccountAsync(settings.Format, settings.Namespace, ct);
                break;

            case "jobs":
                manifestText = await client.ExportJobsAsync(settings.Format, settings.Namespace, ct);
                break;

            case "job":
            {
                if (string.IsNullOrWhiteSpace(settings.Target))
                {
                    output.Error("--scope job requires a job name or id as the argument.");
                    return ExitCodes.Error;
                }

                var job = await JobLookup.ResolveAsync(client, settings.Target, ct);
                manifestText = await client.ExportJobAsync(job.Id, settings.Format, settings.Namespace, ct);
                break;
            }

            default:
                output.Error($"Unknown --scope '{settings.Scope}'. Valid values: account, jobs, job.");
                return ExitCodes.Error;
        }

        // Report required-env placeholders to stderr so stdout (the manifest body) stays clean
        var placeholders = EnvInterpolator.FindPlaceholders(manifestText);
        if (placeholders.Count > 0)
        {
            output.Warn($"The exported manifest references {placeholders.Count} environment variable(s):");
            foreach (var name in placeholders)
            {
                output.Warn($"  ${{{name}}}");
            }
        }

        // Write manifest to file or stdout
        if (!string.IsNullOrWhiteSpace(settings.OutputFile))
        {
            await File.WriteAllTextAsync(settings.OutputFile, manifestText, ct);
            output.Success($"Manifest written to {settings.OutputFile}");
        }
        else
        {
            Console.Write(manifestText);
        }

        return ExitCodes.Ok;
    }
}
