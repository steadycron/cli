using System.ComponentModel;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

public sealed class JobPingUrlsSettings : CliSettings
{
    [CommandArgument(0, "[JOB]")]
    [Description("Heartbeat or agent monitor key, name, or id. Omit to list every monitor.")]
    public string? Identifier { get; set; }
}

/// <summary>
/// `steadycron jobs ping-urls [job]` — prints the success/start/fail ping URLs for the
/// ping-driven kinds (heartbeat and agent monitors). Omitting the job argument lists them all at
/// once, which is useful right after a manifest apply.
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

            if (!job.IsPingDriven)
            {
                throw new CliException(
                    $"'{job.Name}' is an HTTP job — ping URLs only exist for heartbeat and agent monitors.",
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

        // No identifier → list every monitor that has ping URLs. The kind filter is one value per
        // request, so both ping-driven kinds are fetched rather than filtering on !http.
        var jobs = (await client.ListAllJobsAsync(kind: JobKinds.Heartbeat, ct: ct))
            .Concat(await client.ListAllJobsAsync(kind: JobKinds.Agent, ct: ct))
            .Where(j => j.PingUrls is not null)
            .OrderBy(j => j.Name, StringComparer.OrdinalIgnoreCase)
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
            output.Info("No heartbeat or agent monitors found.");
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
