using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

public sealed class JobDeleteSettings : JobTargetSettings
{
    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt.")]
    public bool Yes { get; set; }
}

/// <summary>`steadycron jobs delete &lt;job&gt;` — deletes a job (with confirmation).</summary>
public sealed class JobDeleteCommand : SteadyCronCommandBase<JobDeleteSettings>
{
    public JobDeleteCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(JobDeleteSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Identifier, ct);

        if (!settings.Yes)
        {
            if (settings.Json || Console.IsInputRedirected)
            {
                throw new CliException(
                    $"Refusing to delete '{job.Name}' without confirmation. Re-run with --yes.",
                    ExitCodes.Error);
            }

            if (!output.Out.Confirm(PromptFormatting.Marker($"Delete job '{Markup.Escape(job.Name)}' ({job.Kind})?"), defaultValue: false))
            {
                output.Info("Aborted.");
                return ExitCodes.Ok;
            }
        }

        await client.DeleteJobAsync(job.Id, ct);
        output.Success($"Deleted '{job.Name}'.");
        return ExitCodes.Ok;
    }
}
