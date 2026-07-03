using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands;

/// <summary>
/// `steadycron init` — interactive first-job wizard (SPEC-20b). Requires a valid key already
/// configured (via <c>signup</c>/<c>login</c>/<c>config set</c>); composes the same services
/// <c>jobs create</c> and <c>rules add</c> use rather than shelling out to them.
/// </summary>
public sealed class InitWizardCommand : SteadyCronCommandBase<CliSettings>
{
    private readonly JobMapper _mapper;

    public InitWizardCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation, JobMapper mapper)
        : base(resolver, clientFactory, cancellation)
    {
        _mapper = mapper;
    }

    private const string HeartbeatChoice = "Monitor an existing cron job (heartbeat)";
    private const string HttpChoice = "Schedule a new HTTP job (we call your URL)";

    protected override async Task<int> RunAsync(CliSettings settings, OutputContext output, CancellationToken ct)
    {
        if (!TerminalHelper.IsInteractive())
        {
            throw new CliException("init requires an interactive terminal.", ExitCodes.Error);
        }

        // CreateClient throws the standard "no credentials" first-run message if unset.
        var client = CreateClient(settings);

        var choice = output.Out.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices(HeartbeatChoice, HttpChoice));

        var channelId = await EnsureDefaultChannelAsync(client, output, ct);

        if (choice == HeartbeatChoice)
        {
            await RunHeartbeatWizardAsync(client, channelId, output, ct);
        }
        else
        {
            await RunHttpWizardAsync(client, channelId, output, ct);
        }

        return ExitCodes.Ok;
    }

    private static async Task<Guid> EnsureDefaultChannelAsync(SteadyCronClient client, OutputContext output, CancellationToken ct)
    {
        var channels = await client.ListChannelsAsync(ct);
        var existing = channels.FirstOrDefault(c => c.Kind == "email");
        if (existing is not null)
        {
            return existing.Id;
        }

        // Pre-SPEC-20a account with no default channel yet — set one up transparently rather
        // than failing the wizard. The API has no way to look up "my account's email" from an
        // API-key-authenticated request, so ask for it directly.
        var email = output.Out.Prompt(new TextPrompt<string>("No alert channel found — email for alerts:"));
        var channel = await client.CreateChannelAsync(
            new CreateAlertChannelRequest("Email (default)", "email", new Dictionary<string, string> { ["to"] = email }),
            ct);
        return channel.Id;
    }

    private async Task RunHeartbeatWizardAsync(SteadyCronClient client, Guid channelId, OutputContext output, CancellationToken ct)
    {
        var name = output.Out.Prompt(new TextPrompt<string>("Job name:"));
        var timezone = output.Out.Prompt(new TextPrompt<string>("Timezone [[UTC]]:").AllowEmpty());
        var schedule = await PromptCronExpressionAsync(
            client, output, "Expected schedule (cron, e.g. \"0 2 * * *\"):", timezone, ct);
        var graceText = output.Out.Prompt(new TextPrompt<string>("Grace period seconds [[1800]]:").AllowEmpty());
        var grace = string.IsNullOrWhiteSpace(graceText) ? 1800 : int.Parse(graceText);

        var manifestJob = new ManifestJob
        {
            Name = name,
            Kind = "heartbeat",
            Schedule = schedule,
            Timezone = string.IsNullOrWhiteSpace(timezone) ? null : timezone,
            Grace = grace,
        };

        var desired = _mapper.ToDesired(manifestJob, 0);
        var job = await client.CreateJobAsync(_mapper.ToCreateRequest(desired), ct);
        output.Success($"Created heartbeat monitor {job.Name}.");

        await client.CreateRuleAsync(job.Id, new CreateAlertRuleRequest
        {
            ChannelId = channelId,
            Trigger = AlertTrigger.OnMissedHeartbeat,
        }, ct);
        output.Success("Alert rule: missed heartbeat → default email channel.");

        if (job.PingUrls is { } urls)
        {
            JobFormatting.RenderPingSnippet(output, urls, job.CronExpression);
        }

        output.Line();
        output.Markup("Waiting for your first ping — check anytime with:");
        output.Markup($"    [grey]steadycron jobs get {OutputContext.Escape(job.JobKey ?? job.Id.ToString())}[/]");
    }

    private async Task RunHttpWizardAsync(SteadyCronClient client, Guid channelId, OutputContext output, CancellationToken ct)
    {
        var name = output.Out.Prompt(new TextPrompt<string>("Job name:"));
        var url = output.Out.Prompt(new TextPrompt<string>("URL to call:"));
        var method = output.Out.Prompt(new TextPrompt<string>("Method [[GET]]:").AllowEmpty());
        var timezone = output.Out.Prompt(new TextPrompt<string>("Timezone [[UTC]]:").AllowEmpty());
        var schedule = await PromptCronExpressionAsync(
            client, output, "Schedule (cron, e.g. \"*/15 * * * *\"):", timezone, ct);

        var manifestJob = new ManifestJob
        {
            Name = name,
            Kind = "http",
            Url = url,
            Method = string.IsNullOrWhiteSpace(method) ? null : method,
            Schedule = schedule,
            Timezone = string.IsNullOrWhiteSpace(timezone) ? null : timezone,
        };

        var desired = _mapper.ToDesired(manifestJob, 0);
        var job = await client.CreateJobAsync(_mapper.ToCreateRequest(desired), ct);
        output.Success($"Created HTTP job {job.Name}.");

        await client.CreateRuleAsync(job.Id, new CreateAlertRuleRequest
        {
            ChannelId = channelId,
            Trigger = AlertTrigger.OnFailure,
        }, ct);
        output.Success("Alert rule: on failure → default email channel.");

        if (output.Out.Confirm("Run it now?", defaultValue: false))
        {
            await client.RunNowAsync(job.Id, ct);
            output.Success($"Triggered '{job.Name}'. It will fire within a few seconds.");
        }
    }

    /// <summary>Prompts for a cron expression, validating via the server's own preview endpoint
    /// (the same path <c>cron preview</c> uses) and re-prompting on parse failure until valid.</summary>
    private static async Task<string> PromptCronExpressionAsync(
        SteadyCronClient client, OutputContext output, string prompt, string timezone, CancellationToken ct)
    {
        var tz = string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone;

        while (true)
        {
            var expression = output.Out.Prompt(new TextPrompt<string>(prompt));

            try
            {
                var preview = await client.PreviewCronAsync(new CronPreviewRequest(expression, tz, 3), ct);
                var next = string.Join(", ", preview.NextFires.Select(f => f.ToString("yyyy-MM-dd HH:mm")));
                output.Info($"Next fires: {next}");
                return expression;
            }
            catch (SteadyCronApiException ex)
            {
                output.Warn($"Invalid cron expression: {ex.Message}");
            }
        }
    }
}
