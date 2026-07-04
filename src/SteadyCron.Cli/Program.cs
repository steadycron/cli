using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli;
using SteadyCron.Cli.Commands;
using SteadyCron.Cli.Commands.Channels;
using SteadyCron.Cli.Commands.Config;
using SteadyCron.Cli.Commands.Cron;
using SteadyCron.Cli.Commands.Export;
using SteadyCron.Cli.Commands.Import;
using SteadyCron.Cli.Commands.Jobs;
using SteadyCron.Cli.Commands.Manifest;
using SteadyCron.Cli.Commands.Rules;
using SteadyCron.Cli.Commands.Sync;
using SteadyCron.Cli.Commands.Tags;
using SteadyCron.Cli.Commands.Validate;
using SteadyCron.Cli.Commands.Variables;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

var services = new ServiceCollection();

services.AddHttpClient("steadycron", client =>
{
    client.Timeout = TimeSpan.FromSeconds(100);
});

services.AddSingleton<CancellationProvider>();
services.AddSingleton<ConfigResolver>();
services.AddSingleton<SteadyCronClientFactory>();
services.AddSingleton<ManifestLoader>();
services.AddSingleton<ManifestValidator>();
services.AddSingleton<JobMapper>();
services.AddSingleton<SyncPlanner>();

var app = new CommandApp(new TypeRegistrar(services));

app.Configure(config =>
{
    config.SetApplicationName("steadycron");
    config.SetApplicationVersion(CliVersion.Value);

    config.AddCommand<ReportCommand>("report")
        .WithDescription("Print an account-wide activity digest for a time window (default: last 24 h).")
        .WithExample("report")
        .WithExample("report", "--hours", "6")
        .WithExample("report", "--hours", "168", "--verbose");

    config.AddCommand<LogstreamCommand>("logstream")
        .WithDescription("Stream live logbook events to the terminal (polls every 2 s, Ctrl+C to stop).")
        .WithExample("logstream")
        .WithExample("logstream", "--since", "300", "--domain", "heartbeats")
        .WithExample("logstream", "--severity", "critical", "--job", "nightly-backup")
        .WithExample("logstream", "--json");

    config.AddCommand<LogbookCommand>("logbook")
        .WithDescription("Browse the complete account event history: executions, heartbeats, alerts, job changes, and more.")
        .WithExample("logbook")
        .WithExample("logbook", "--hours", "168")
        .WithExample("logbook", "--domain", "executions", "--domain", "alerts")
        .WithExample("logbook", "--severity", "critical")
        .WithExample("logbook", "--job", "my-job-key")
        .WithExample("logbook", "--all", "--json");

    config.AddCommand<SignupCommand>("signup")
        .WithDescription("Create an account, verify your email, and provision an API key — entirely in-terminal.")
        .WithExample("signup");

    config.AddCommand<LoginCommand>("login")
        .WithDescription("Sign in on a new machine: mints a fresh API key and saves it locally.")
        .WithExample("login");

    config.AddCommand<InitWizardCommand>("init")
        .WithDescription("Interactive first-job wizard: create your first monitored job in one command.")
        .WithExample("init");

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Validate a manifest locally (schema + cross-references, no API calls).")
        .WithExample("validate", "steadycron.yaml")
        .WithExample("validate", "./manifests/");

    config.AddCommand<SyncCommand>("sync")
        .WithDescription("Reconcile your account with a YAML manifest (infrastructure-as-code).")
        .WithExample("sync", "steadycron.yaml")
        .WithExample("sync", "steadycron.yaml", "--dry-run")
        .WithExample("sync", "steadycron.yaml", "--prune", "--yes");

    config.AddCommand<PlanCommand>("plan")
        .WithDescription("Preview what sync would change (alias for sync --dry-run).")
        .WithExample("plan", "./manifests/", "--namespace", "prod");

    config.AddCommand<ApplyCommand>("apply")
        .WithDescription("Apply a manifest without confirmation prompt (alias for sync --yes).")
        .WithExample("apply", "./manifests/", "--namespace", "prod", "--prune")
        .WithExample("apply", "production.yaml", "-n", "prod", "--env-file", "secrets.env");

    config.AddCommand<ExportCommand>("export")
        .WithDescription("Export your account (or a subset) as a v2 manifest or Terraform HCL.")
        .WithExample("export", "--format", "terraform", "-o", "main.tf")
        .WithExample("export", "--format", "terraform", "--scope", "job", "weekly-digest-email", "-o", "job.tf")
        .WithExample("export", "-o", "steadycron.yaml")
        .WithExample("export", "-o", "steadycron.yaml", "--write-env")
        .WithExample("export", "--scope", "jobs")
        .WithExample("export", "--scope", "job", "weekly-digest-email");

    config.AddBranch("import", import =>
    {
        import.SetDescription("Generate a v2 manifest from an existing crontab or vercel.json.");
        import.AddCommand<ImportCrontabCommand>("crontab")
            .WithDescription("Convert a crontab file into a v2 manifest.")
            .WithExample("import", "crontab", "crontab.txt", "-o", "steadycron.yaml")
            .WithExample("import", "crontab", "--dry-run")
            .WithExample("import", "crontab", "--as", "heartbeat", "-o", "monitors.yaml");
        import.AddCommand<ImportVercelCommand>("vercel")
            .WithDescription("Convert a vercel.json cron config into a v2 manifest.")
            .WithExample("import", "vercel", "--base-url", "https://app.example.com", "-o", "steadycron.yaml")
            .WithExample("import", "vercel", "--base-url", "https://app.example.com",
                "--cron-secret-env", "VERCEL_CRON_SECRET");
    });

    config.AddBranch("manifest", manifest =>
    {
        manifest.SetDescription("Generate manifest boilerplate, or append a resource to an existing manifest.");
        manifest.AddCommand<InitCommand>("scaffold")
            .WithDescription("Generate a fully documented boilerplate manifest (or Terraform HCL) covering every SteadyCron feature.")
            .WithExample("manifest", "scaffold")
            .WithExample("manifest", "scaffold", "-o", "steadycron.yaml")
            .WithExample("manifest", "scaffold", "--terraform", "-o", "steadycron.tf");

        manifest.AddBranch("add", add =>
        {
            add.SetDescription("Append a single resource to an existing manifest (no API calls, no API key required).");
            add.AddCommand<AddJobCommand>("job")
                .WithDescription("Append a job to the manifest.")
                .WithExample("manifest", "add", "job", "--kind", "heartbeat", "--name", "nightly-backup")
                .WithExample("manifest", "add", "job", "--kind", "http", "--name", "daily-report", "--url", "https://api.example.com/jobs/daily-report");
            add.AddCommand<AddChannelCommand>("channel")
                .WithDescription("Append an alert channel to the manifest.")
                .WithExample("manifest", "add", "channel", "--kind", "slack", "--name", "ops-slack");
            add.AddCommand<AddTagCommand>("tag")
                .WithDescription("Append a tag to the manifest.")
                .WithExample("manifest", "add", "tag", "env", "staging", "--color", "yellow");
            add.AddCommand<AddVariableCommand>("variable")
                .WithDescription("Append a template variable to the manifest.")
                .WithExample("manifest", "add", "variable", "api_token");
        }).WithAlias("g");
    });

    config.AddBranch("jobs", jobs =>
    {
        jobs.SetDescription("List and manage jobs.");
        jobs.AddCommand<JobsListCommand>("list").WithAlias("ls").WithDescription("List jobs (includes job id for use with rules and other commands).");
        jobs.AddCommand<JobGetCommand>("get").WithDescription("Show a job's full definition.");
        jobs.AddCommand<JobCreateCommand>("create").WithDescription("Create a single job from flags.");
        jobs.AddCommand<JobLogsCommand>("logs").WithDescription("Show recent executions of an HTTP job.");
        jobs.AddCommand<JobPingUrlsCommand>("ping-urls")
            .WithDescription("Show ping URLs for heartbeat monitors (omit job to list all).")
            .WithExample("jobs", "ping-urls")
            .WithExample("jobs", "ping-urls", "nightly-backup");
        jobs.AddCommand<JobSnippetCommand>("snippet")
            .WithDescription("Generate integration code that wires a heartbeat monitor into a script or workflow.")
            .WithExample("jobs", "snippet", "nightly-backup")
            .WithExample("jobs", "snippet", "nightly-backup", "--lang", "python")
            .WithExample("jobs", "snippet", "nightly-backup", "--lang", "github-actions");
        jobs.AddCommand<JobPauseCommand>("pause")
            .WithDescription("Pause a job, all jobs matching a tag, or every job.")
            .WithExample("jobs", "pause", "nightly-backup")
            .WithExample("jobs", "pause", "--tag", "env:staging")
            .WithExample("jobs", "pause", "--all", "--yes");
        jobs.AddCommand<JobResumeCommand>("resume")
            .WithDescription("Resume a paused job, all jobs matching a tag, or every paused job.")
            .WithExample("jobs", "resume", "nightly-backup")
            .WithExample("jobs", "resume", "--tag", "env:staging");
        jobs.AddCommand<JobRunNowCommand>("run").WithAlias("run-now").WithDescription("Trigger an HTTP job immediately.");
        jobs.AddCommand<JobDeleteCommand>("delete").WithAlias("rm").WithDescription("Delete a job.");
    });

    config.AddBranch("cron", cron =>
    {
        cron.SetDescription("Cron expression utilities.");
        cron.AddCommand<CronPreviewCommand>("preview").WithDescription("Preview the next fire times for a cron expression.");
    });

    config.AddBranch("tags", tags =>
    {
        tags.SetDescription("Manage tags.");
        tags.AddCommand<TagsListCommand>("list").WithAlias("ls").WithDescription("List tags.");
        tags.AddCommand<TagCreateCommand>("create").WithDescription("Create a tag (idempotent).");
        tags.AddCommand<TagDeleteCommand>("delete").WithAlias("rm").WithDescription("Delete a tag.");
    });

    config.AddBranch("vars", vars =>
    {
        vars.SetDescription("Manage account template variables ({{name}} placeholders).");
        vars.AddCommand<VarsListCommand>("list").WithAlias("ls").WithDescription("List template variables.");
        vars.AddCommand<VarSetCommand>("set").WithDescription("Create or update a template variable.");
        vars.AddCommand<VarDeleteCommand>("delete").WithAlias("rm").WithDescription("Delete a template variable.");
    });

    config.AddBranch("channels", channels =>
    {
        channels.SetDescription("Manage alert channels.");
        channels.AddCommand<ChannelsListCommand>("list").WithAlias("ls").WithDescription("List alert channels.");
        channels.AddCommand<ChannelCreateCommand>("create").WithDescription("Create an alert channel.");
        channels.AddCommand<ChannelTestCommand>("test").WithDescription("Send a test alert to a channel.");
        channels.AddCommand<ChannelDeleteCommand>("delete").WithAlias("rm").WithDescription("Delete an alert channel.");
    });

    config.AddBranch("rules", rules =>
    {
        rules.SetDescription("Manage per-job alert rules.");
        rules.AddCommand<RulesListCommand>("list")
            .WithAlias("ls")
            .WithDescription("List a job's alert rules with channel kind and target.")
            .WithExample("rules", "list", "nightly-db-backup")
            .WithExample("rules", "ls", "018f1234-abcd-7000-8000-000000000001");
        rules.AddCommand<RuleAddCommand>("add").WithDescription("Add an alert rule to a job.");
        rules.AddCommand<RuleDeleteCommand>("delete").WithAlias("rm").WithDescription("Delete an alert rule.");
        rules.AddCommand<RulesTestCommand>("test")
            .WithDescription("Send a test notification on every channel configured for a job.")
            .WithExample("rules", "test", "nightly-db-backup")
            .WithExample("rules", "test", "018f1234-abcd-7000-8000-000000000001");
    });

    config.AddBranch("config", cfg =>
    {
        cfg.SetDescription("View and edit CLI configuration.");
        cfg.AddCommand<ConfigShowCommand>("show").WithDescription("Show the resolved configuration.");
        cfg.AddCommand<ConfigSetCommand>("set").WithDescription("Persist api_url / api_key to the config file.");
        cfg.AddCommand<ConfigPathCommand>("path").WithDescription("Print the config file path.");
    });

    config.SetExceptionHandler((ex, _) =>
    {
        AnsiConsole.MarkupLineInterpolated($"[red]✗ Unexpected error:[/] {ex.Message}");
        return ExitCodes.Error;
    });
});

return await app.RunAsync(args);
