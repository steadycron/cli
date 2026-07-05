using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

/// <summary>
/// Settings for pause and resume: accepts a single job by key/name/id OR a tag filter OR --all.
/// </summary>
public sealed class JobBulkActionSettings : CliSettings
{
    [CommandArgument(0, "[JOB]")]
    [Description("Job key, name, or id. Omit when using --tag or --all.")]
    public string? Identifier { get; set; }

    [CommandOption("--tag <TAG>")]
    [Description("Filter by tag key:value (repeatable; AND logic). e.g. --tag env:staging")]
    public string[] Tags { get; set; } = [];

    [CommandOption("--all")]
    [Description("Apply to every job in the account. Use with care.")]
    public bool All { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt for bulk operations.")]
    public bool Yes { get; set; }

    public bool IsBulk => Tags.Length > 0 || All;

    public override ValidationResult Validate()
    {
        if (Identifier is not null && IsBulk)
        {
            return ValidationResult.Error("Cannot combine a positional job argument with --tag or --all.");
        }

        if (All && Tags.Length > 0)
        {
            return ValidationResult.Error("--all and --tag are mutually exclusive.");
        }

        if (Identifier is null && !IsBulk)
        {
            return ValidationResult.Error("Specify a job key/name/id, --tag <key:value>, or --all.");
        }

        return ValidationResult.Success();
    }
}

/// <summary>`steadycron jobs pause` — pauses a single job, all jobs matching a tag, or every job.</summary>
public sealed class JobPauseCommand : SteadyCronCommandBase<JobBulkActionSettings>
{
    public JobPauseCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation) { }

    protected override async Task<int> RunAsync(JobBulkActionSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);

        if (!settings.IsBulk)
        {
            var job = await JobLookup.ResolveAsync(client, settings.Identifier!, ct);
            var updated = await client.PauseJobAsync(job.Id, ct);

            if (output.Json)
            {
                output.WriteJson(updated);
                return ExitCodes.Ok;
            }

            output.Success($"Paused '{job.Name}'.");
            return ExitCodes.Ok;
        }

        return await BulkHelper.RunAsync(
            settings, output, client,
            verb: "pause",
            action: j => client.PauseJobAsync(j.Id, ct),
            skipReason: _ => null,
            ct);
    }
}

/// <summary>`steadycron jobs resume` — resumes a single paused job, all jobs matching a tag, or every paused job.</summary>
public sealed class JobResumeCommand : SteadyCronCommandBase<JobBulkActionSettings>
{
    public JobResumeCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation) { }

    protected override async Task<int> RunAsync(JobBulkActionSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);

        if (!settings.IsBulk)
        {
            var job = await JobLookup.ResolveAsync(client, settings.Identifier!, ct);

            if (job.PausedReason == JobFormatting.UnverifiedEmailPauseReason)
            {
                throw new CliException(JobFormatting.UnverifiedEmailWarning, ExitCodes.Error);
            }

            var updated = await client.ResumeJobAsync(job.Id, ct);

            if (output.Json)
            {
                output.WriteJson(updated);
                return ExitCodes.Ok;
            }

            output.Success($"Resumed '{job.Name}'.");
            return ExitCodes.Ok;
        }

        return await BulkHelper.RunAsync(
            settings, output, client,
            verb: "resume",
            action: j => client.ResumeJobAsync(j.Id, ct),
            skipReason: j => j.PausedReason == JobFormatting.UnverifiedEmailPauseReason
                ? "unverified email — verify your address to resume"
                : null,
            ct);
    }
}

/// <summary>`steadycron jobs run` — triggers an immediate run (HTTP jobs only).</summary>
public sealed class JobRunNowCommand : SteadyCronCommandBase<JobTargetSettings>
{
    public JobRunNowCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation) { }

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

// ── Shared bulk helper (file-scoped) ──────────────────────────────────────────

file static class BulkHelper
{
    internal static async Task<int> RunAsync(
        JobBulkActionSettings settings,
        OutputContext output,
        SteadyCronClient client,
        string verb,
        Func<JobResponse, Task<JobResponse>> action,
        Func<JobResponse, string?> skipReason,
        CancellationToken ct)
    {
        var allJobs = await client.ListAllJobsAsync(ct: ct);

        var targets = settings.All
            ? allJobs.ToList()
            : allJobs.Where(j => MatchesAllTags(j, settings.Tags)).ToList();

        if (targets.Count == 0)
        {
            output.Info(settings.All
                ? "No jobs found in this account."
                : $"No jobs match tag(s): {string.Join(", ", settings.Tags)}");
            return ExitCodes.Ok;
        }

        var verbTitle = char.ToUpperInvariant(verb[0]) + verb[1..];

        output.Markup($"Will [bold]{verb}[/] {targets.Count} job(s):");
        foreach (var j in targets)
        {
            output.Markup($"  {output.Glyphs.Bullet} {OutputContext.Escape(j.Name)}");
        }

        output.Line();

        if (!settings.Yes && !Console.IsInputRedirected && !Console.IsOutputRedirected)
        {
            if (!AnsiConsole.Confirm(PromptFormatting.Marker("Continue?"), defaultValue: false))
            {
                output.Info("Aborted.");
                return ExitCodes.Ok;
            }

            output.Line();
        }

        var succeeded = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var j in targets)
        {
            var skip = skipReason(j);
            if (skip is not null)
            {
                output.Markup($"  skip   {OutputContext.Escape(j.Name)} ({skip})");
                skipped++;
                continue;
            }

            try
            {
                await action(j);
                output.Markup($"  [{Styles.Success}]{output.Glyphs.Success}[/]      {OutputContext.Escape(j.Name)}");
                succeeded++;
            }
            catch (Exception ex)
            {
                output.Markup($"  [{Styles.Error}]{output.Glyphs.Error}[/]      {OutputContext.Escape(j.Name)} ({OutputContext.Escape(ex.Message)})");
                failed++;
            }
        }

        output.Line();

        var summary = $"{verbTitle}d {succeeded} job(s)";
        if (skipped > 0) { summary += $", skipped {skipped}"; }
        if (failed > 0) { summary += $", {failed} error(s)"; }

        if (failed > 0)
        {
            output.Warn(summary + ".");
        }
        else
        {
            output.Success(summary + ".");
        }

        return failed > 0 ? ExitCodes.Error : ExitCodes.Ok;
    }

    private static bool MatchesAllTags(JobResponse job, string[] tagFilters)
    {
        var jobTags = job.Tags ?? [];
        foreach (var filter in tagFilters)
        {
            var colonIdx = filter.IndexOf(':');
            var key = colonIdx > 0 ? filter[..colonIdx] : filter;
            var value = colonIdx > 0 ? filter[(colonIdx + 1)..] : null;

            var hasTag = jobTags.Any(t =>
                string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase) &&
                (value is null || string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase)));

            if (!hasTag) { return false; }
        }

        return true;
    }
}
