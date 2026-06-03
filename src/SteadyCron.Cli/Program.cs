using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli;
using SteadyCron.Cli.Commands.Config;
using SteadyCron.Cli.Commands.Cron;
using SteadyCron.Cli.Commands.Jobs;
using SteadyCron.Cli.Commands.Sync;
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
services.AddSingleton<JobMapper>();
services.AddSingleton<SyncPlanner>();

var app = new CommandApp(new TypeRegistrar(services));

app.Configure(config =>
{
    config.SetApplicationName("steadycron");
    config.SetApplicationVersion(CliVersion.Value);

    config.AddCommand<SyncCommand>("sync")
        .WithDescription("Reconcile your account with a YAML manifest (infrastructure-as-code).")
        .WithExample("sync", "jobs.yaml")
        .WithExample("sync", "jobs.yaml", "--dry-run")
        .WithExample("sync", "jobs.yaml", "--prune", "--yes");

    config.AddBranch("jobs", jobs =>
    {
        jobs.SetDescription("List and manage jobs.");
        jobs.AddCommand<JobsListCommand>("list").WithAlias("ls").WithDescription("List jobs.");
        jobs.AddCommand<JobGetCommand>("get").WithDescription("Show a job's full definition.");
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
