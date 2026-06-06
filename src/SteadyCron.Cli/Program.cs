using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli;
using SteadyCron.Cli.Commands.Channels;
using SteadyCron.Cli.Commands.Config;
using SteadyCron.Cli.Commands.Cron;
using SteadyCron.Cli.Commands.Export;
using SteadyCron.Cli.Commands.Jobs;
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

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Validate a manifest locally (schema + cross-references, no API calls).")
        .WithExample("validate", "steadycron.yaml")
        .WithExample("validate", "./manifests/");

    config.AddCommand<SyncCommand>("sync")
        .WithDescription("Reconcile your account with a YAML manifest (infrastructure-as-code).")
        .WithExample("sync", "jobs.yaml")
        .WithExample("sync", "jobs.yaml", "--dry-run")
        .WithExample("sync", "jobs.yaml", "--prune", "--yes");

    config.AddCommand<PlanCommand>("plan")
        .WithDescription("Preview what sync would change (alias for sync --dry-run).")
        .WithExample("plan", "./manifests/", "--namespace", "prod");

    config.AddCommand<ApplyCommand>("apply")
        .WithDescription("Apply a manifest without confirmation prompt (alias for sync --yes).")
        .WithExample("apply", "./manifests/", "--namespace", "prod", "--prune")
        .WithExample("apply", "production.yaml", "-n", "prod", "--env-file", "secrets.env");

    config.AddCommand<ExportCommand>("export")
        .WithDescription("Export your account (or a subset) as a v2 manifest.")
        .WithExample("export", "-o", "steadycron.yaml")
        .WithExample("export", "-o", "steadycron.yaml", "--write-env", "secrets.env")
        .WithExample("export", "--scope", "jobs")
        .WithExample("export", "--scope", "job", "weekly-digest-email");

    config.AddBranch("jobs", jobs =>
    {
        jobs.SetDescription("List and manage jobs.");
        jobs.AddCommand<JobsListCommand>("list").WithAlias("ls").WithDescription("List jobs.");
        jobs.AddCommand<JobGetCommand>("get").WithDescription("Show a job's full definition.");
        jobs.AddCommand<JobCreateCommand>("create").WithDescription("Create a single job from flags.");
        jobs.AddCommand<JobLogsCommand>("logs").WithDescription("Show recent executions of an HTTP job.");
        jobs.AddCommand<JobPauseCommand>("pause").WithDescription("Pause a job.");
        jobs.AddCommand<JobResumeCommand>("resume").WithDescription("Resume a paused job.");
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
        rules.AddCommand<RulesListCommand>("list").WithAlias("ls").WithDescription("List a job's alert rules.");
        rules.AddCommand<RuleAddCommand>("add").WithDescription("Add an alert rule to a job.");
        rules.AddCommand<RuleDeleteCommand>("delete").WithAlias("rm").WithDescription("Delete an alert rule.");
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
