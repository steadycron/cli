using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Manifest.Generators;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Manifest;

public sealed class AddJobSettings : ManifestAddSettingsBase
{
    [CommandOption("--kind <KIND>")]
    [Description("http | heartbeat | agent.")]
    public string? Kind { get; set; }

    [CommandOption("--name <NAME>")]
    public string? Name { get; set; }

    [CommandOption("--schedule <CRON>")]
    [Description("Cron expression, e.g. \"*/15 * * * *\". Mutually exclusive with --interval.")]
    public string? Schedule { get; set; }

    [CommandOption("--interval <SECONDS>")]
    [Description("Interval in seconds. Mutually exclusive with --schedule.")]
    public int? Interval { get; set; }

    [CommandOption("--timezone <TZ>")]
    [Description("IANA timezone (default UTC).")]
    public string? Timezone { get; set; }

    [CommandOption("--url <URL>")]
    [Description("http: target URL (required for HTTP jobs).")]
    public string? Url { get; set; }

    [CommandOption("--method <METHOD>")]
    [Description("http: GET (default), POST, PUT, PATCH, DELETE.")]
    public string? Method { get; set; }

    [CommandOption("--grace <SECONDS>")]
    [Description("heartbeat|agent: grace period seconds (default derived from the schedule).")]
    public int? Grace { get; set; }

    [CommandOption("--items-label <LABEL>")]
    [Description("agent: what this agent produces, e.g. \"tickets\" (max 40 chars).")]
    public string? ItemsLabel { get; set; }
}

/// <summary>
/// <c>steadycron manifest add job</c> — appends a job block to an existing manifest (SPEC-21).
/// Client-side only: no API calls, no API key required. Missing values are prompted
/// interactively; fully-flagged invocations never prompt.
/// </summary>
public sealed class AddJobCommand : AsyncCommand<AddJobSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AddJobSettings settings)
    {
        var output = new OutputContext(json: false, quiet: false, noColor: false);

        if (settings.Terraform)
        {
            output.Error("--terraform is not yet supported for 'add'; see 'manifest scaffold --terraform'.");
            return ExitCodes.Error;
        }

        var interactive = TerminalHelper.IsInteractive();

        var kind = ResolveKind(settings, interactive, output);
        if (kind is null)
        {
            return ExitCodes.Error;
        }

        var name = ResolveName(settings, interactive, output);
        if (name is null)
        {
            return ExitCodes.Error;
        }

        if (settings.Schedule is not null && settings.Interval is not null)
        {
            output.Error("Specify either --schedule or --interval, not both.");
            return ExitCodes.Error;
        }

        string? schedule = null;
        int? interval = settings.Interval;

        if (interval is null)
        {
            schedule = ResolveSchedule(settings, interactive, output);
            if (schedule is null)
            {
                return ExitCodes.Error;
            }
        }

        var timezone = ResolveTimezone(settings, interactive, output);
        if (timezone is null)
        {
            return ExitCodes.Error;
        }

        int? grace = null;
        string? url = null;
        string? method = null;
        string? itemsLabel = null;

        if (JobKinds.IsPingDriven(kind))
        {
            grace = ResolveGrace(settings, schedule, interval, timezone, interactive, output);

            if (JobKinds.IsAgent(kind))
            {
                itemsLabel = ResolveItemsLabel(settings, interactive, output);
                if (itemsLabel is null)
                {
                    return ExitCodes.Error;
                }
            }
        }
        else
        {
            url = ResolveUrl(settings, interactive, output);
            if (url is null)
            {
                return ExitCodes.Error;
            }

            method = ResolveMethod(settings, interactive, output);
            if (method is null)
            {
                return ExitCodes.Error;
            }
        }

        var id = JobKeyGenerator.ToSlug(name);
        var spec = new NewJobSpec
        {
            Kind = kind,
            Name = name,
            Id = id,
            Schedule = schedule,
            Interval = interval,
            Timezone = timezone,
            Grace = grace,
            Url = url,
            Method = method,
            ItemsLabel = itemsLabel,
        };

        var block = new YamlBlockRenderer().RenderJob(spec);

        return await ManifestAddExecutor.ExecuteAsync(
            output,
            settings,
            ManifestSection.Jobs,
            block,
            (editor, content) =>
                editor.ResourceExists(content, ManifestSection.Jobs, ("id", id)) ||
                editor.ResourceExists(content, ManifestSection.Jobs, ("name", name)),
            $"job '{name}' already exists in {settings.TargetPath}",
            $"job {name}");
    }

    private static string? ResolveKind(AddJobSettings settings, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(settings.Kind))
        {
            var k = settings.Kind.Trim().ToLowerInvariant();
            if (ManifestSchema.JobKinds.Contains(k))
            {
                return k;
            }

            output.Error($"--kind must be one of {string.Join(", ", ManifestSchema.JobKinds)} (got '{settings.Kind}').");
            return null;
        }

        if (!interactive)
        {
            output.Error("--kind is required (no terminal available to prompt for it).");
            return null;
        }

        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(PromptFormatting.Marker("Kind:"))
                .AddChoices(JobKinds.Heartbeat, JobKinds.Http, JobKinds.Agent));
    }

    private const string DefaultItemsLabel = "items";

    /// <summary>
    /// Asking what the agent produces is the point of the kind — it is the unit the empty-result
    /// rule counts and the word every alert about this monitor will use.
    /// </summary>
    private static string? ResolveItemsLabel(AddJobSettings settings, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(settings.ItemsLabel))
        {
            var label = settings.ItemsLabel.Trim();
            if (label.Length > JobDefaults.ItemsLabelMaxLength)
            {
                output.Error($"--items-label must be {JobDefaults.ItemsLabelMaxLength} characters or fewer.");
                return null;
            }

            return label;
        }

        if (!interactive)
        {
            return DefaultItemsLabel;
        }

        while (true)
        {
            var raw = AnsiConsole.Prompt(
                new TextPrompt<string>(
                        PromptFormatting.Marker($"What does this agent produce? [{Styles.Hint}][[{DefaultItemsLabel}]][/]:"))
                    .AllowEmpty());

            var label = string.IsNullOrWhiteSpace(raw) ? DefaultItemsLabel : raw.Trim();
            if (label.Length <= JobDefaults.ItemsLabelMaxLength)
            {
                return label;
            }

            output.Warn($"Keep it to {JobDefaults.ItemsLabelMaxLength} characters or fewer — it is a noun like \"tickets\" or \"rows\".");
        }
    }

    private static string? ResolveName(AddJobSettings settings, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(settings.Name))
        {
            return settings.Name.Trim();
        }

        if (!interactive)
        {
            output.Error("--name is required (no terminal available to prompt for it).");
            return null;
        }

        return AnsiConsole.Prompt(new TextPrompt<string>(PromptFormatting.Marker("Job name:")));
    }

    private const string DefaultSchedule = "*/15 * * * *";

    private static string? ResolveSchedule(AddJobSettings settings, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(settings.Schedule))
        {
            try
            {
                CronScheduleHelper.Validate(settings.Schedule);
                return settings.Schedule.Trim();
            }
            catch (FormatException ex)
            {
                output.Error($"Invalid --schedule: {ex.Message}");
                return null;
            }
        }

        if (!interactive)
        {
            output.Error("--schedule or --interval is required (no terminal available to prompt for it).");
            return null;
        }

        while (true)
        {
            var raw = AnsiConsole.Prompt(new TextPrompt<string>(PromptFormatting.Marker($"Schedule (cron) [{Styles.Hint}][[{DefaultSchedule}]][/]:")).AllowEmpty());
            var expression = string.IsNullOrWhiteSpace(raw) ? DefaultSchedule : raw.Trim();

            try
            {
                CronScheduleHelper.Validate(expression);
                return expression;
            }
            catch (FormatException ex)
            {
                output.Warn($"Invalid cron expression: {ex.Message}");
            }
        }
    }

    private static string? ResolveTimezone(AddJobSettings settings, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(settings.Timezone))
        {
            if (!TimezoneHelper.IsValid(settings.Timezone))
            {
                output.Error($"Unknown timezone '{settings.Timezone}'.");
                return null;
            }

            return settings.Timezone.Trim();
        }

        if (!interactive)
        {
            return "UTC"; // schema default — no prompt possible, and UTC is always safe.
        }

        var localIana = TimezoneHelper.ResolveLocalIana();
        var localLabel = localIana is not null ? $"Local ({localIana})" : null;

        var choices = new List<string> { "UTC" };
        if (localLabel is not null)
        {
            choices.Add(localLabel);
        }

        const string otherChoice = "Other…";
        choices.Add(otherChoice);

        var choice = AnsiConsole.Prompt(new SelectionPrompt<string>().Title(PromptFormatting.Marker("Timezone:")).AddChoices(choices));
        if (choice != otherChoice)
        {
            return choice == localLabel ? localIana! : "UTC";
        }

        while (true)
        {
            var tz = AnsiConsole.Prompt(new TextPrompt<string>(PromptFormatting.Marker("IANA timezone:")));
            if (TimezoneHelper.IsValid(tz))
            {
                return tz;
            }

            output.Warn($"Unknown timezone '{tz}'. e.g. Europe/Berlin, America/New_York");
        }
    }

    private static int? ResolveGrace(
        AddJobSettings settings, string? schedule, int? interval, string timezone, bool interactive, OutputContext output)
    {
        if (settings.Grace is not null)
        {
            return settings.Grace;
        }

        var graceDefault = ComputeGraceDefault(schedule, interval, timezone);

        if (!interactive)
        {
            return graceDefault;
        }

        var raw = AnsiConsole.Prompt(new TextPrompt<string>(PromptFormatting.Marker($"Grace period seconds [{Styles.Hint}][[{graceDefault}]][/]:")).AllowEmpty());
        return string.IsNullOrWhiteSpace(raw) ? graceDefault : int.Parse(raw);
    }

    private static int ComputeGraceDefault(string? schedule, int? interval, string timezone)
    {
        if (interval is { } seconds)
        {
            return seconds switch
            {
                < 3600 => 300,
                < 86_400 => 1800,
                _ => 3600,
            };
        }

        try
        {
            var fires = CronScheduleHelper.GetNextFires(schedule!, timezone, 5);
            return CronScheduleHelper.ComputeGraceDefault(fires);
        }
        catch (Exception ex) when (ex is FormatException or TimeZoneNotFoundException)
        {
            return 1800;
        }
    }

    private static string? ResolveUrl(AddJobSettings settings, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(settings.Url))
        {
            return settings.Url.Trim();
        }

        if (!interactive)
        {
            output.Error("--url is required for HTTP jobs (no terminal available to prompt for it).");
            return null;
        }

        return AnsiConsole.Prompt(new TextPrompt<string>(PromptFormatting.Marker("URL to call:")));
    }

    private static string? ResolveMethod(AddJobSettings settings, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(settings.Method))
        {
            var m = settings.Method.Trim().ToUpperInvariant();
            if (!ManifestSchema.HttpMethods.Contains(m))
            {
                output.Error($"--method must be one of {string.Join(", ", ManifestSchema.HttpMethods)} (got '{settings.Method}').");
                return null;
            }

            return m;
        }

        if (!interactive)
        {
            return "GET";
        }

        var raw = AnsiConsole.Prompt(new TextPrompt<string>(PromptFormatting.Marker($"Method [{Styles.Hint}][[GET]][/]:")).AllowEmpty());
        return string.IsNullOrWhiteSpace(raw) ? "GET" : raw.Trim().ToUpperInvariant();
    }
}
