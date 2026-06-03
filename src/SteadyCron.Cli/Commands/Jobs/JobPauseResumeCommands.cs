using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

/// <summary>`steadycron jobs pause &lt;job&gt;` — pauses scheduling for a job.</summary>
public sealed class JobPauseCommand : SteadyCronCommandBase<JobTargetSettings>
{
    public JobPauseCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(JobTargetSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Identifier, ct);
        var updated = await client.PauseJobAsync(job.Id, ct);

        if (output.Json)
        {
            output.WriteJson(updated);
            return ExitCodes.Ok;
        }

        output.Success($"Paused '{job.Name}'.");
        return ExitCodes.Ok;
    }
}

/// <summary>`steadycron jobs resume &lt;job&gt;` — resumes a paused job.</summary>
public sealed class JobResumeCommand : SteadyCronCommandBase<JobTargetSettings>
{
    public JobResumeCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(JobTargetSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Identifier, ct);
        var updated = await client.ResumeJobAsync(job.Id, ct);

        if (output.Json)
        {
            output.WriteJson(updated);
            return ExitCodes.Ok;
        }

        output.Success($"Resumed '{job.Name}'.");
        return ExitCodes.Ok;
    }
}

/// <summary>`steadycron jobs run &lt;job&gt;` — triggers an immediate run (HTTP jobs only).</summary>
public sealed class JobRunNowCommand : SteadyCronCommandBase<JobTargetSettings>
{
    public JobRunNowCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(JobTargetSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Identifier, ct);

        if (job.Kind != "http")
        {
            throw new CliException("run is only available for HTTP jobs.", ExitCodes.Error);
        }

        await client.RunNowAsync(job.Id, ct);
        output.Success($"Triggered '{job.Name}'. It will fire within a few seconds.");
        return ExitCodes.Ok;
    }
}
