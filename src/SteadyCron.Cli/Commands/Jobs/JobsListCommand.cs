using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

public sealed class JobsListSettings : CliSettings
{
    [CommandOption("--kind <KIND>")]
    [Description("Filter by kind: http or heartbeat.")]
    public string? Kind { get; set; }

    [CommandOption("--status <STATUS>")]
    [Description("Filter by derived status, e.g. success, failure, late, missed, paused.")]
    public string? Status { get; set; }

    [CommandOption("--page <N>")]
    [Description("Page number (default 1). Ignored with --all.")]
    [DefaultValue(1)]
    public int Page { get; set; } = 1;

    [CommandOption("--page-size <N>")]
    [Description("Items per page, 1–100 (default 50). Ignored with --all.")]
    [DefaultValue(50)]
    public int PageSize { get; set; } = 50;

    [CommandOption("--all")]
    [Description("Fetch every page of results.")]
    public bool All { get; set; }
}

/// <summary>`steadycron jobs list` — lists jobs as a table (or JSON with --json).</summary>
public sealed class JobsListCommand : SteadyCronCommandBase<JobsListSettings>
{
    public JobsListCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(JobsListSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);

        var items = settings.All
            ? await client.ListAllJobsAsync(kind: settings.Kind, ct: ct)
            : (await client.ListJobsAsync(settings.Status, settings.Kind, settings.Page, settings.PageSize, ct)).Items;

        if (output.Json)
        {
            output.WriteJson(items);
            return ExitCodes.Ok;
        }

        if (items.Count == 0)
        {
            output.Info("No jobs found.");
            return ExitCodes.Ok;
        }

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("Name");
        table.AddColumn("Kind");
        table.AddColumn("Status");
        table.AddColumn("Schedule");
        table.AddColumn("Next run");
        table.AddColumn("Last run");
        table.AddColumn(new TableColumn("Id").NoWrap());

        foreach (var job in items)
        {
            table.AddRow(
                Markup.Escape(job.Name),
                Markup.Escape(job.Kind),
                JobFormatting.StatusMarkup(job.Status),
                Markup.Escape(JobFormatting.ScheduleWithTz(job)),
                Markup.Escape(job.Kind == "http" ? JobFormatting.When(job.NextFireAt) : "—"),
                Markup.Escape(JobFormatting.When(job.LastFireAt)),
                $"[grey]{job.Id}[/]");
        }

        output.Render(table);
        output.Info($"{items.Count} job(s).");

        if (items.Any(j => j.PausedReason == JobFormatting.UnverifiedEmailPauseReason))
        {
            output.Warn(JobFormatting.UnverifiedEmailWarning);
        }

        return ExitCodes.Ok;
    }
}
