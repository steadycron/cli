using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Cron;

public sealed class CronPreviewSettings : CliSettings
{
    [CommandArgument(0, "<EXPRESSION>")]
    [Description("A 5-field cron expression, e.g. \"0 9 * * 1\". Quote it to protect the asterisks.")]
    public string Expression { get; set; } = string.Empty;

    [CommandOption("--timezone <TZ>")]
    [Description("IANA timezone for the preview (default UTC), e.g. Europe/Berlin.")]
    public string? Timezone { get; set; }

    [CommandOption("-n|--count <N>")]
    [Description("Number of upcoming fire times to show, 1–50 (default 10).")]
    [DefaultValue(10)]
    public int Count { get; set; } = 10;

    public override Spectre.Console.ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(Expression)
            ? Spectre.Console.ValidationResult.Error("A cron expression is required.")
            : Spectre.Console.ValidationResult.Success();
}

/// <summary>`steadycron cron preview "&lt;expr&gt;"` — shows the next N fire times via the API.</summary>
public sealed class CronPreviewCommand : SteadyCronCommandBase<CronPreviewSettings>
{
    public CronPreviewCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(CronPreviewSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var tz = settings.Timezone ?? "UTC";
        var response = await client.PreviewCronAsync(
            new CronPreviewRequest(settings.Expression, tz, Math.Clamp(settings.Count, 1, 50)), ct);

        if (output.Json)
        {
            output.WriteJson(response);
            return ExitCodes.Ok;
        }

        output.Markup($"Next [bold]{response.NextFires.Count}[/] fires of [yellow]{OutputContext.Escape(settings.Expression)}[/] ({OutputContext.Escape(tz)}):");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("#");
        table.AddColumn($"Fire time ({Markup.Escape(tz)})");
        table.AddColumn("Relative");

        var i = 1;
        foreach (var fire in response.NextFires)
        {
            table.AddRow(
                i.ToString(CultureInfo.InvariantCulture),
                Markup.Escape(fire.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Markup.Escape(JobFormatting.Relative(fire)));
            i++;
        }

        output.Render(table);
        return ExitCodes.Ok;
    }
}
