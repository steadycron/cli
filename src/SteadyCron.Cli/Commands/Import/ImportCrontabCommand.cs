using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Import;

public sealed class ImportCrontabSettings : ImportBaseSettings
{
    [CommandArgument(0, "[PATH]")]
    [Description("Path to the crontab file. Reads from stdin when omitted.")]
    public string? Path { get; set; }

    [CommandOption("--system")]
    [Description("Parse as a system crontab or cron.d file (username column between schedule and command).")]
    public bool System { get; set; }

    [CommandOption("--as <MODE>")]
    [Description("Force job kind: 'auto' (default), 'http', or 'heartbeat'.")]
    public string? As { get; set; }

    public string EffectiveAs =>
        string.IsNullOrWhiteSpace(As) ? "auto" : As.Trim().ToLowerInvariant();
}

/// <summary>
/// <c>steadycron import crontab [PATH]</c> — converts a crontab file into a v2 manifest.
/// curl/wget/bare-URL commands become <c>http</c> jobs; everything else becomes a
/// <c>heartbeat</c> monitor. The generated manifest can be reviewed and applied with
/// <c>steadycron sync</c>.
/// </summary>
public sealed class ImportCrontabCommand : AsyncCommand<ImportCrontabSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ImportCrontabSettings settings)
    {
        var output = new OutputContext(json: false, quiet: false, noColor: false);

        if (!ValidateAs(settings.EffectiveAs, output))
        {
            return ExitCodes.Error;
        }

        // Read input
        IEnumerable<string> lines;
        string sourceName;
        try
        {
            if (settings.Path is not null)
            {
                if (!File.Exists(settings.Path))
                {
                    output.Error($"File not found: {settings.Path}");
                    return ExitCodes.Error;
                }

                lines = await File.ReadAllLinesAsync(settings.Path);
                sourceName = settings.Path;
            }
            else
            {
                var stdinText = await Console.In.ReadToEndAsync();
                lines = stdinText.Split(['\n', '\r'], StringSplitOptions.None);
                sourceName = "<stdin>";
            }
        }
        catch (IOException ex)
        {
            output.Error($"Could not read input: {ex.Message}");
            return ExitCodes.Error;
        }

        var result = CrontabParser.Parse(lines, settings.System, settings.EffectiveAs);

        foreach (var warning in result.Warnings)
        {
            output.Warn(warning);
        }

        if (settings.DryRun)
        {
            output.Err.MarkupLine(
                $"[grey]{result.HttpCount} http, {result.HeartbeatCount} heartbeat, " +
                $"{result.SkippedCount} skipped  (source: {Markup.Escape(sourceName)})[/]");
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
                $"Manifest written to {settings.Output} " +
                $"({result.HttpCount} http, {result.HeartbeatCount} heartbeat).");
        }
        else
        {
            Console.Write(manifestText);
        }

        // Print heartbeat ping snippets to stderr so they don't pollute the manifest on stdout
        foreach (var job in result.Jobs.Where(j => j.Kind == "heartbeat"))
        {
            output.Err.MarkupLineInterpolated(
                $"[yellow]![/] Heartbeat [bold]{job.Name ?? job.Id ?? "?"}[/] (id: {job.Id ?? "?"})");
            output.Err.MarkupLine(
                "   After sync, append this to your cron command:");
            output.Err.MarkupLine(
                "   [grey]&& curl -fsS 'https://ping.steadycron.com/<TOKEN>'[/]");
            output.Err.MarkupLine(
                $"   [grey](<TOKEN> available after: steadycron jobs get {Markup.Escape(job.Id ?? "?")})[/]");
        }

        return ExitCodes.Ok;
    }

    private static bool ValidateAs(string mode, OutputContext output)
    {
        if (mode is "auto" or "http" or "heartbeat")
        {
            return true;
        }

        output.Error($"Invalid --as value '{mode}'. Valid values: auto, http, heartbeat.");
        return false;
    }
}
