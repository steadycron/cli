using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Commands.Manifest;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Manifest.Generators;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands;

/// <summary>
/// `steadycron init` — interactive first-job wizard (SPEC-20b, polished in SPEC-20c). Requires a
/// valid key already configured (via <c>signup</c>/<c>login</c>/<c>config set</c>); composes the
/// same services <c>jobs create</c> and <c>rules add</c> use rather than shelling out to them.
/// </summary>
public sealed class InitWizardCommand : SteadyCronCommandBase<CliSettings>
{
    private readonly JobMapper _mapper;
    private readonly ManifestLoader _manifestLoader;

    public InitWizardCommand(
        ConfigResolver resolver,
        SteadyCronClientFactory clientFactory,
        CancellationProvider cancellation,
        JobMapper mapper,
        ManifestLoader manifestLoader)
        : base(resolver, clientFactory, cancellation)
    {
        _mapper = mapper;
        _manifestLoader = manifestLoader;
    }

    private const string HeartbeatChoice = "Monitor an existing cron job (heartbeat)";
    private const string HttpChoice = "Schedule a new HTTP job (we call your URL)";
    private const string SkipChoice = "Skip — just set up the manifest workflow";

    private const string DefaultHeartbeatSchedule = "*/15 * * * *";

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
                .AddChoices(HeartbeatChoice, HttpChoice, SkipChoice));

        if (choice == SkipChoice)
        {
            output.Markup("Add jobs to steadycron.yaml and run [cyan]steadycron apply[/].");
            await WriteManifestFilesAsync(client, output, ct);
            PrintIaCBlock(output);
            OfferCiSetup(output);
            return ExitCodes.Ok;
        }

        var channel = await EnsureDefaultChannelAsync(client, output, ct);

        var job = choice == HeartbeatChoice
            ? await RunHeartbeatWizardAsync(client, channel.Id, output, ct)
            : await RunHttpWizardAsync(client, channel.Id, output, ct);

        output.Success(BuildAlertConfirmation(job.Kind, channel));

        output.Render(JobFormatting.BuildJobsTable([job]));
        output.Markup(
            $"{JobFormatting.Pluralize(1, "job")}. Check status anytime: " +
            $"[cyan]steadycron jobs get {OutputContext.Escape(job.JobKey ?? job.Id.ToString())}[/]");

        if (job.PingUrls is { } urls)
        {
            JobFormatting.RenderPingSnippet(output, urls, job.CronExpression);
        }

        PrintReadmeBadgeSnippet(output, job);

        await WriteManifestFilesAsync(client, output, ct);
        PrintIaCBlock(output);
        OfferCiSetup(output);

        return ExitCodes.Ok;
    }

    /// <summary>A copy-pasteable README snippet for the badge SteadyCron already generates for
    /// every job — badges are effectively free marketing surface once a repo README shows one.</summary>
    internal static void PrintReadmeBadgeSnippet(OutputContext output, JobResponse job)
    {
        if (job.BadgeUrl is null)
        {
            return;
        }

        // Plain markdown, written via RawLine: it's long (image URL + link URL) and must survive
        // copy-paste byte-for-byte, so it must neither be word-wrapped nor run through the markup
        // parser (its own literal '[' / ']' would otherwise be parsed as invalid style tags).
        var badgeMarkdown = $"[![{job.Name}]({job.BadgeUrl})](https://steadycron.com)";
        var jobKey = job.JobKey ?? job.Id.ToString();

        output.Line();
        output.Markup("Add a status badge to your README:");
        output.Line();
        output.RawLine($"  {badgeMarkdown}");
        if (job.PingUrls is { Success: var successUrl })
        {
            output.Markup($"  Pings on success: `curl -fsS {OutputContext.Escape(successUrl)}`");
        }

        output.Markup($"  Check status: `steadycron jobs get {jobKey}`");
    }

    internal static string BuildAlertConfirmation(string jobKind, AlertChannelResponse channel)
    {
        var email = channel.Kind == "email" ? channel.Config?.GetValueOrDefault("to") : null;
        var destination = email is not null ? $"email {email}" : "email your default alert channel";

        return jobKind == "heartbeat"
            ? $"If a ping doesn't arrive on time, we'll {destination}."
            : $"If a run fails, we'll {destination}.";
    }

    private static async Task<AlertChannelResponse> EnsureDefaultChannelAsync(SteadyCronClient client, OutputContext output, CancellationToken ct)
    {
        var channels = await client.ListChannelsAsync(ct);
        var existing = channels.FirstOrDefault(c => c.Kind == "email");
        if (existing is not null)
        {
            return existing;
        }

        // Pre-SPEC-20a account with no default channel yet — set one up transparently rather
        // than failing the wizard. The API has no way to look up "my account's email" from an
        // API-key-authenticated request, so ask for it directly.
        var email = output.Out.Prompt(new TextPrompt<string>("No alert channel found — email for alerts:"));
        return await client.CreateChannelAsync(
            new CreateAlertChannelRequest("Email (default)", "email", new Dictionary<string, string> { ["to"] = email }),
            ct);
    }

    private async Task<JobResponse> RunHeartbeatWizardAsync(SteadyCronClient client, Guid channelId, OutputContext output, CancellationToken ct)
    {
        var name = output.Out.Prompt(new TextPrompt<string>("Job name:"));

        var schedule = await PromptCronWithDefaultAsync(
            client, output, $"Expected schedule (cron) [[{DefaultHeartbeatSchedule}]]:", DefaultHeartbeatSchedule, ct);

        var timezone = await PromptTimezoneAsync(client, output, ct);

        var preview = await client.PreviewCronAsync(new CronPreviewRequest(schedule, timezone, 5), ct);
        PrintNextFires(output, preview, timezone);

        var graceDefault = CronScheduleHelper.ComputeGraceDefault(preview.NextFires);
        var graceText = output.Out.Prompt(new TextPrompt<string>($"Grace period seconds [[{graceDefault}]]:").AllowEmpty());
        var grace = string.IsNullOrWhiteSpace(graceText) ? graceDefault : int.Parse(graceText);

        var manifestJob = new ManifestJob
        {
            Name = name,
            Kind = "heartbeat",
            Schedule = schedule,
            Timezone = timezone,
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

        return job;
    }

    private async Task<JobResponse> RunHttpWizardAsync(SteadyCronClient client, Guid channelId, OutputContext output, CancellationToken ct)
    {
        var name = output.Out.Prompt(new TextPrompt<string>("Job name:"));
        var url = output.Out.Prompt(new TextPrompt<string>("URL to call:"));
        var method = output.Out.Prompt(new TextPrompt<string>("Method [[GET]]:").AllowEmpty());
        var timezone = await PromptTimezoneAsync(client, output, ct);
        var schedule = await PromptCronRequiredAsync(
            client, output, "Schedule (cron, e.g. \"*/15 * * * *\"):", timezone, ct);

        var manifestJob = new ManifestJob
        {
            Name = name,
            Kind = "http",
            Url = url,
            Method = string.IsNullOrWhiteSpace(method) ? null : method,
            Schedule = schedule,
            Timezone = timezone,
        };

        var desired = _mapper.ToDesired(manifestJob, 0);
        var job = await client.CreateJobAsync(_mapper.ToCreateRequest(desired), ct);
        output.Success($"Created HTTP job {job.Name}.");

        await client.CreateRuleAsync(job.Id, new CreateAlertRuleRequest
        {
            ChannelId = channelId,
            Trigger = AlertTrigger.OnFailure,
        }, ct);

        if (output.Out.Confirm("Run it now?", defaultValue: false))
        {
            await client.RunNowAsync(job.Id, ct);
            output.Success($"Triggered '{job.Name}'. It will fire within a few seconds.");
        }

        return job;
    }

    // ── Cron / timezone prompting ─────────────────────────────────────────────────

    /// <summary>Prompts for a cron expression with a default (Enter accepts it), validating
    /// syntax against a UTC placeholder — the real timezone isn't known yet at this point in the
    /// heartbeat flow, so the fires themselves aren't shown here (see <see cref="PrintNextFires"/>).</summary>
    private static async Task<string> PromptCronWithDefaultAsync(
        SteadyCronClient client, OutputContext output, string prompt, string defaultExpression, CancellationToken ct)
    {
        while (true)
        {
            var raw = output.Out.Prompt(new TextPrompt<string>(prompt).AllowEmpty());
            var expression = string.IsNullOrWhiteSpace(raw) ? defaultExpression : raw.Trim();

            try
            {
                await client.PreviewCronAsync(new CronPreviewRequest(expression, "UTC", 1), ct);
                return expression;
            }
            catch (SteadyCronApiException ex)
            {
                output.Warn($"Invalid cron expression: {ex.Message}");
            }
        }
    }

    /// <summary>Prompts for a required cron expression (no default) with the timezone already
    /// known, validating and printing the next-fires preview in one round trip — used by the HTTP
    /// branch, where the timezone selector runs before the schedule prompt.</summary>
    private static async Task<string> PromptCronRequiredAsync(
        SteadyCronClient client, OutputContext output, string prompt, string timezone, CancellationToken ct)
    {
        while (true)
        {
            var expression = output.Out.Prompt(new TextPrompt<string>(prompt));

            try
            {
                var preview = await client.PreviewCronAsync(new CronPreviewRequest(expression, timezone, 3), ct);
                PrintNextFires(output, preview, timezone);
                return expression;
            }
            catch (SteadyCronApiException ex)
            {
                output.Warn($"Invalid cron expression: {ex.Message}");
            }
        }
    }

    private static void PrintNextFires(OutputContext output, CronPreviewResponse preview, string timezone)
    {
        var text = string.Join(", ", preview.NextFires.Take(3).Select(f => f.ToString("yyyy-MM-dd HH:mm")));
        output.Markup($"  Next fires: {OutputContext.Escape(text)} ({OutputContext.Escape(timezone)})");
    }

    private static async Task<string> PromptTimezoneAsync(SteadyCronClient client, OutputContext output, CancellationToken ct)
    {
        var localIana = TimezoneHelper.ResolveLocalIana();
        var localLabel = localIana is not null ? $"Local ({localIana})" : null;

        var choices = new List<string> { "UTC" };
        if (localLabel is not null)
        {
            choices.Add(localLabel);
        }

        const string otherChoice = "Other…";
        choices.Add(otherChoice);

        var choice = output.Out.Prompt(new SelectionPrompt<string>().Title("Timezone:").AddChoices(choices));

        if (choice == otherChoice)
        {
            return await PromptOtherTimezoneAsync(client, output, ct);
        }

        return choice == localLabel ? localIana! : "UTC";
    }

    /// <summary>Free-text timezone entry, validated against the same IANA list the server accepts
    /// (via the cron preview endpoint against an always-valid placeholder expression) rather than
    /// trusting local ICU data, which may be absent (this CLI ships with InvariantGlobalization).</summary>
    private static async Task<string> PromptOtherTimezoneAsync(SteadyCronClient client, OutputContext output, CancellationToken ct)
    {
        while (true)
        {
            var tz = output.Out.Prompt(new TextPrompt<string>("IANA timezone:"));

            try
            {
                await client.PreviewCronAsync(new CronPreviewRequest("* * * * *", tz, 1), ct);
                return tz;
            }
            catch (SteadyCronApiException ex)
            {
                output.Warn($"Invalid timezone: {ex.Message}");
                output.Warn("e.g. Europe/Berlin, America/New_York");
            }
        }
    }

    // ── Manifest files + IaC block (§5/§6, all branches including Skip) ───────────

    private async Task WriteManifestFilesAsync(SteadyCronClient client, OutputContext output, CancellationToken ct)
    {
        var manifestText = await WriteYamlExportAsync(client, output, ct);
        WriteCheatSheet(output);
        WriteSecretsEnvFile(output, manifestText);
        EnsureGitignoreGuardsSecrets(output);
    }

    /// <summary>A committed secrets file is a real incident — the exported manifest may already
    /// reference `${SC_…}` secrets, and `steadycron_secrets.env` (below) exists specifically to
    /// hold them. Near-zero cost to guard against by default when the CWD is a git repo.</summary>
    private static void EnsureGitignoreGuardsSecrets(OutputContext output)
    {
        if (!GitRepositoryHelper.IsGitRepo("."))
        {
            return;
        }

        if (GitignoreGuard.EnsureSecretsIgnored(".gitignore"))
        {
            output.Success($"Added {ManifestEnvironment.DefaultSecretsFile} to .gitignore");
        }
    }

    /// <summary>Returns the exported (or already-existing) manifest text so
    /// <see cref="WriteSecretsEnvFile"/> can scan it for real <c>${...}</c> placeholders — null
    /// only when neither is available (a fresh export failed).</summary>
    private async Task<string?> WriteYamlExportAsync(SteadyCronClient client, OutputContext output, CancellationToken ct)
    {
        const string path = "steadycron.yaml";
        if (File.Exists(path))
        {
            output.Markup($"{path} already exists — skipped. Regenerate anytime with: [cyan]steadycron export -o {path}[/]");
            return await File.ReadAllTextAsync(path, ct);
        }

        string text;
        try
        {
            text = await client.ExportAccountAsync(ct: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or SteadyCronApiException)
        {
            output.Warn($"Could not export your account: {ex.Message}");
            output.Warn($"Run `steadycron export -o {path}` manually later.");
            return null;
        }

        await File.WriteAllTextAsync(path, text, ct);

        // The file is written either way; a summary is a nice-to-have, so a malformed export
        // (a server bug) degrades to a plain confirmation rather than failing the whole command.
        string summary;
        try
        {
            var manifest = _manifestLoader.Parse(text, path);
            var jobCount = manifest.Jobs?.Count ?? 0;
            var channelCount = manifest.Channels?.Count ?? 0;
            var ruleCount = manifest.Jobs?.Sum(j => j.Rules?.Count ?? 0) ?? 0;
            summary =
                $" ({JobFormatting.Pluralize(jobCount, "job")}, {JobFormatting.Pluralize(channelCount, "channel")}, {JobFormatting.Pluralize(ruleCount, "rule")})";
        }
        catch (ManifestException)
        {
            summary = string.Empty;
        }

        output.Success($"Wrote {path} — your account as code{summary}");
        return text;
    }

    private static void WriteCheatSheet(OutputContext output)
    {
        const string path = "steadycron_example.yaml";
        if (File.Exists(path))
        {
            output.Markup($"{path} already exists — skipped. Regenerate anytime with: [cyan]steadycron manifest scaffold -o {path}[/]");
            return;
        }

        File.WriteAllText(path, InitCommand.YamlBoilerplate);
        output.Success($"Wrote {path} — reference for every manifest feature");
    }

    /// <summary>
    /// Scaffolds <see cref="ManifestEnvironment.DefaultSecretsFile"/> from the manifest's real
    /// <c>${...}</c> placeholders (the same <see cref="EnvInterpolator.FindPlaceholders"/>
    /// <c>export --write-env</c> uses) — never a guessed or generic filename, so the
    /// <c>.gitignore</c> guard has exactly one name to protect, and `apply`/`sync`/`plan`/`validate`
    /// can auto-detect it with no flag needed.
    /// </summary>
    private static void WriteSecretsEnvFile(OutputContext output, string? manifestText) =>
        WriteSecretsEnvFile(output, manifestText, ManifestEnvironment.DefaultSecretsFile);

    /// <summary>Path-parameterized so tests can target a temp file instead of the real CWD.</summary>
    internal static void WriteSecretsEnvFile(OutputContext output, string? manifestText, string path)
    {
        if (File.Exists(path))
        {
            output.Markup($"{path} already exists — skipped.");
            return;
        }

        File.WriteAllText(path, BuildSecretsEnvContent(manifestText, out var placeholderCount));

        var summary = placeholderCount > 0 ? $" ({JobFormatting.Pluralize(placeholderCount, "secret")} to fill in)" : string.Empty;
        output.Success($"Wrote {path}{summary}");
    }

    internal static string BuildSecretsEnvContent(string? manifestText, out int placeholderCount)
    {
        var placeholders = manifestText is not null ? EnvInterpolator.FindPlaceholders(manifestText) : [];
        placeholderCount = placeholders.Count;

        var sb = new StringBuilder();
        sb.Append("# SteadyCron secrets — fill in real values, then `steadycron apply` uses them automatically.\n");
        sb.Append("# Format: NAME=value (one per line, no quotes needed).\n");
        sb.Append("#\n");
        sb.Append("# Example:\n");
        sb.Append("# MY_SECRET_KEY=MY_SECRET_VALUE\n");

        if (placeholders.Count > 0)
        {
            sb.Append('\n');
            foreach (var name in placeholders)
            {
                sb.Append(name).Append("=\n");
            }
        }

        return sb.ToString();
    }

    private static void PrintIaCBlock(OutputContext output)
    {
        output.Line();
        output.Markup("Manage everything as code:");
        output.Markup("  1. Add or edit jobs in steadycron.yaml");
        output.Markup("  2. [cyan]steadycron validate steadycron.yaml[/]    check locally, no API");
        output.Markup("  3. [cyan]steadycron plan steadycron.yaml[/]        preview changes");
        output.Markup("  4. [cyan]steadycron apply steadycron.yaml[/]       sync to your account");
    }

    // ── GitHub Actions opt-in (all branches including Skip) ───────────────────────

    /// <summary>
    /// Turns "here's the IaC workflow" into "the IaC workflow is installed": when the CWD looks
    /// like a GitHub-hosted repo, offers to install the plan-on-PR/apply-on-merge workflow this
    /// README already documents by hand. RunAsync already required an interactive terminal before
    /// any of this ran, so no further TerminalHelper check is needed here.
    /// </summary>
    private static void OfferCiSetup(OutputContext output)
    {
        if (!GitRepositoryHelper.IsGitRepo(".") ||
            (!Directory.Exists(".github") && !GitRepositoryHelper.HasGitHubRemote(".")))
        {
            return;
        }

        if (!output.Out.Confirm("Set up CI? Plan on PRs, apply on merge", defaultValue: false))
        {
            return;
        }

        const string workflowPath = ".github/workflows/steadycron.yml";
        if (File.Exists(workflowPath))
        {
            output.Markup($"{workflowPath} already exists — skipped.");
            return;
        }

        Directory.CreateDirectory(".github/workflows");
        File.WriteAllText(workflowPath, CiWorkflowTemplate);
        output.Success($"Wrote {workflowPath}");
        output.Markup("Add [bold]STEADYCRON_API_KEY[/] as a repository secret — consider a read-only key for plan-only.");
    }

    // Adapted from the README's "Cron and monitoring as code in CI" example: same single-workflow
    // plan-on-PR/apply-on-merge shape, but pointed at the single `steadycron.yaml` init actually
    // writes (the README's own example targets a `steadycron/` directory of manifests, and omits
    // `namespace`/`prune` — this account has neither yet, and prune requires a namespace).
    internal const string CiWorkflowTemplate =
        """
        # .github/workflows/steadycron.yml
        # Generated by `steadycron init` — plans on pull requests, applies on merge to main.
        name: SteadyCron

        on:
          pull_request:
            paths: ["steadycron.yaml"]
          push:
            branches: [main]
            paths: ["steadycron.yaml"]

        permissions:
          contents: read
          pull-requests: write

        jobs:
          steadycron:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4
              - uses: steadycron/action@v1
                with:
                  # plan on PRs, apply on push to main
                  command: ${{ github.event_name == 'pull_request' && 'plan' || 'apply' }}
                  manifest: steadycron.yaml
                  comment-on-pr: "true"
                env:
                  STEADYCRON_API_KEY: ${{ secrets.STEADYCRON_API_KEY }}
        """;
}
