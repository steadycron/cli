using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Jobs;

public sealed class JobLogsSettings : JobTargetSettings
{
    [CommandOption("-n|--limit <N>")]
    [Description("Number of recent executions to show, 1–100 (default 20).")]
    [DefaultValue(20)]
    public int Limit { get; set; } = 20;
}

/// <summary>`steadycron jobs logs &lt;job&gt;` — shows recent executions of an HTTP job.</summary>
public sealed class JobLogsCommand : SteadyCronCommandBase<JobLogsSettings>
{
    public JobLogsCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(JobLogsSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var job = await JobLookup.ResolveAsync(client, settings.Identifier, ct);

        if (job.Kind != "http")
        {
            throw new CliException("Execution logs are only available for HTTP jobs. For heartbeats, view pings in the dashboard.", ExitCodes.Error);
        }

        var pageSize = Math.Clamp(settings.Limit, 1, 100);
        var response = await client.ListExecutionsAsync(job.Id, page: 1, pageSize: pageSize, ct);

        if (output.Json)
        {
            output.WriteJson(response.Items);
            return ExitCodes.Ok;
        }

        if (response.Items.Count == 0)
        {
            output.Info($"No executions recorded for '{job.Name}' yet.");
            return ExitCodes.Ok;
        }

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("Scheduled");
        table.AddColumn("Status");
        table.AddColumn("HTTP");
        table.AddColumn("Duration");
        table.AddColumn("Attempt");
        table.AddColumn("Error");

        foreach (var e in response.Items)
        {
            table.AddRow(
                Markup.Escape(e.ScheduledAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                JobFormatting.StatusMarkup(e.Status),
                Markup.Escape(e.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "—"),
                Markup.Escape(e.DurationMs is { } ms ? $"{ms} ms" : "—"),
                Markup.Escape(e.IsFinalAttempt ? e.Attempt.ToString(CultureInfo.InvariantCulture) : $"{e.Attempt} (retried)"),
                Markup.Escape(FormatError(e.ErrorKind, e.ErrorMessage)));
        }

        output.Render(table);
        output.Info($"Showing {response.Items.Count} of {response.TotalCount} execution(s).");
        return ExitCodes.Ok;
    }

    private static string FormatError(string? kind, string? message)
    {
        if (string.IsNullOrEmpty(kind) && string.IsNullOrEmpty(message))
        {
            return "—";
        }

        var text = string.IsNullOrEmpty(kind) ? message! : $"{kind}: {message}";
        return text.Length <= 60 ? text : text[..60] + "…";
    }
}
