using System.ComponentModel;
using Spectre.Console.Cli;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

public sealed class JobPingUrlsSettings : CliSettings
{
    [CommandArgument(0, "[JOB]")]
    [Description("Heartbeat job key, name, or id. Omit to list every heartbeat monitor.")]
    public string? Identifier { get; set; }
}

/// <summary>
/// `steadycron jobs ping-urls [job]` — prints the success/start/fail ping URLs for
/// heartbeat monitors. Omitting the job argument lists all heartbeat jobs at once,
/// which is useful right after a manifest apply.
/// </summary>
public sealed class JobPingUrlsCommand : SteadyCronCommandBase<JobPingUrlsSettings>
{
    public JobPingUrlsCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c)
        : base(r, f, c) { }

    protected override async Task<int> RunAsync(JobPingUrlsSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);

        if (settings.Identifier is not null)
        {
            var job = await JobLookup.ResolveAsync(client, settings.Identifier, ct);

            if (job.Kind != "heartbeat")
            {
                throw new CliException(
                    $"'{job.Name}' is an HTTP job — ping URLs only exist for heartbeat monitors.",
                    ExitCodes.Error);
            }

            if (output.Json)
            {
                output.WriteJson(new { job_key = job.JobKey, job_name = job.Name, ping_urls = job.PingUrls });
                return ExitCodes.Ok;
            }

            JobFormatting.RenderPingUrls(output, job.Name, job.PingUrls!);
            return ExitCodes.Ok;
        }

        // No identifier → list all heartbeat monitors.
        var jobs = (await client.ListAllJobsAsync(kind: "heartbeat", ct: ct))
            .Where(j => j.PingUrls is not null)
            .ToList();

        if (output.Json)
        {
            output.WriteJson(jobs.Select(j => new
            {
                job_key = j.JobKey,
                job_name = j.Name,
                ping_urls = j.PingUrls,
            }));
            return ExitCodes.Ok;
        }

        if (jobs.Count == 0)
        {
            output.Info("No heartbeat monitors found.");
            return ExitCodes.Ok;
        }

        var first = true;
        foreach (var job in jobs)
        {
            if (!first) { output.Line(); }

            first = false;
            JobFormatting.RenderPingUrls(output, job.Name, job.PingUrls!);
        }

        return ExitCodes.Ok;
    }
}
