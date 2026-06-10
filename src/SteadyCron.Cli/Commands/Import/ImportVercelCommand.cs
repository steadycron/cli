using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Import;

public sealed class ImportVercelSettings : ImportBaseSettings
{
    [CommandArgument(0, "[PATH]")]
    [Description("Path to vercel.json. Defaults to 'vercel.json' in the current directory.")]
    public string? Path { get; set; }

    [CommandOption("--base-url <URL>")]
    [Description("Base URL prepended to each cron path (e.g. https://app.example.com). Required.")]
    public string? BaseUrl { get; set; }

    [CommandOption("--cron-secret-env <NAME>")]
    [Description(
        "Environment variable holding the Vercel cron secret. When set, emits " +
        "'Authorization: Bearer ${NAME}' on every job.")]
    public string? CronSecretEnv { get; set; }

    public string EffectivePath => Path ?? "vercel.json";
}

/// <summary>
/// <c>steadycron import vercel [PATH]</c> — converts a <c>vercel.json</c> cron configuration
/// into a v2 manifest. All jobs are HTTP GET with <c>timezone: UTC</c> to match Vercel's
/// execution environment.
/// </summary>
public sealed class ImportVercelCommand : AsyncCommand<ImportVercelSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ImportVercelSettings settings)
    {
        var output = new OutputContext(json: false, quiet: false, noColor: false);

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            output.Error("--base-url is required. Example: --base-url https://app.example.com");
            return ExitCodes.Error;
        }

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
        {
            output.Error($"--base-url '{settings.BaseUrl}' is not a valid absolute URL.");
            return ExitCodes.Error;
        }

        if (!File.Exists(settings.EffectivePath))
        {
            output.Error($"File not found: {settings.EffectivePath}");
            return ExitCodes.Error;
        }

        string vercelJson;
        try
        {
            vercelJson = await File.ReadAllTextAsync(settings.EffectivePath);
        }
        catch (IOException ex)
        {
            output.Error($"Could not read {settings.EffectivePath}: {ex.Message}");
            return ExitCodes.Error;
        }

        var result = VercelParser.Parse(vercelJson, settings.BaseUrl, settings.CronSecretEnv);

        foreach (var warning in result.Warnings)
        {
            output.Warn(warning);
        }

        if (settings.DryRun)
        {
            output.Err.MarkupLine(
                $"[grey]{result.Jobs.Count} http (UTC), 0 heartbeat, 0 skipped  " +
                $"(source: {Markup.Escape(settings.EffectivePath)})[/]");
            return ExitCodes.Ok;
        }

        if (result.Jobs.Count == 0)
        {
            output.Warn("No jobs imported. Check for warnings above.");
            return ExitCodes.Ok;
        }

        // Build manifest
        var manifest = new ManifestFile
        {
            Version = 2,
            Namespace = settings.Namespace,
            Jobs = [.. result.Jobs],
        };

        var tempYaml = ManifestSerializer.Serialize(manifest);
        var requiredVars = EnvInterpolator.FindPlaceholders(tempYaml);
        var manifestText = ManifestSerializer.Serialize(manifest, requiredVars);

        // Write manifest
        if (!string.IsNullOrWhiteSpace(settings.Output))
        {
            await File.WriteAllTextAsync(settings.Output, manifestText);
            output.Success(
                $"Manifest written to {settings.Output} ({result.Jobs.Count} http job(s)).");
        }
        else
        {
            Console.Write(manifestText);
        }

        // UTC migration note
        output.Warn(
            "All schedules are in UTC to match Vercel's execution environment. " +
            "After migrating, edit the 'timezone' field in each job to use your preferred timezone.");

        return ExitCodes.Ok;
    }
}
