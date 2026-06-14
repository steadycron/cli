using System.ComponentModel;
using System.Text;
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
    [Description("Output format: 'yaml' (default), 'json', or 'terraform'.")]
    public string? Format { get; set; }

    [CommandOption("--namespace|-n")]
    [Description("Stamp this namespace into the exported manifest's 'namespace' field.")]
    public string? Namespace { get; set; }

    [CommandOption("--write-env <PATH>")]
    [Description("Write a .env scaffold listing every ${...} secret the exported manifest references.")]
    public string? WriteEnv { get; set; }

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
        var isTerraform = string.Equals(settings.Format, "terraform", StringComparison.OrdinalIgnoreCase);
        string manifestText;

        if (isTerraform)
        {
            switch (settings.EffectiveScope)
            {
                case "account":
                    manifestText = await client.ExportAccountTerraformAsync(ct);
                    break;

                case "job":
                {
                    if (string.IsNullOrWhiteSpace(settings.Target))
                    {
                        output.Error("--scope job requires a job name or id as the argument.");
                        return ExitCodes.Error;
                    }

                    var job = await JobLookup.ResolveAsync(client, settings.Target, ct);
                    manifestText = await client.ExportJobTerraformAsync(job.Id, ct);
                    break;
                }

                default:
                    output.Error("--format terraform supports --scope account (default) or --scope job <name/id>.");
                    return ExitCodes.Error;
            }

            // Terraform sensitive variables are declared inline as `variable "..." { sensitive = true }`.
            // Remind the user to supply values via tfvars or -var flags.
            var sensitiveVars = CountSensitiveVars(manifestText);
            if (sensitiveVars > 0)
            {
                output.Warn($"The exported configuration declares {sensitiveVars} sensitive variable(s).");
                output.Warn("Supply them via a terraform.tfvars file or -var flags when applying:");
                output.Warn("  terraform apply -var='sc_channel_ops_slack_webhook_url=https://...'");
            }
        }
        else
        {
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

            // Optional .env scaffold for the secret placeholders.
            if (!string.IsNullOrWhiteSpace(settings.WriteEnv))
            {
                if (File.Exists(settings.WriteEnv))
                {
                    output.Error(
                        $"Refusing to overwrite existing env file '{settings.WriteEnv}' " +
                        "(it may already hold secrets). Remove it or choose another path.");
                    return ExitCodes.Error;
                }

                await File.WriteAllTextAsync(settings.WriteEnv, BuildEnvScaffold(placeholders), ct);
                output.Success($"Env scaffold written to {settings.WriteEnv} ({placeholders.Count} variable(s)).");
            }
        }

        // Write to file or stdout (common to all formats)
        if (!string.IsNullOrWhiteSpace(settings.OutputFile))
        {
            await File.WriteAllTextAsync(settings.OutputFile, manifestText, ct);
            output.Success($"Written to {settings.OutputFile}");
        }
        else
        {
            Console.Write(manifestText);
        }

        return ExitCodes.Ok;
    }

    private static int CountSensitiveVars(string hcl)
    {
        var count = 0;
        var search = "sensitive = true";
        var idx = 0;
        while ((idx = hcl.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }
        return count;
    }

    private static string BuildEnvScaffold(IReadOnlyList<string> names)
    {
        var sb = new StringBuilder();
        sb.Append("# SteadyCron secret scaffold — generated by `steadycron export --write-env`.\n");
        sb.Append("# Fill in each value, then apply with: steadycron apply <manifest> --env-file <this file>\n");

        if (names.Count == 0)
        {
            sb.Append("# (the manifest references no ${...} secret placeholders)\n");
            return sb.ToString();
        }

        sb.Append('\n');
        foreach (var name in names)
        {
            sb.Append(name).Append("=\n");
        }

        return sb.ToString();
    }
}
