using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Validate;

public sealed class ValidateSettings : CommandSettings
{
    [CommandArgument(0, "[PATHS...]")]
    [Description("One or more manifest files or directories (default: jobs.yaml).")]
    public string[]? Paths { get; set; }

    [CommandOption("--no-warn-v1")]
    [Description("Suppress the v1 deprecation warning.")]
    public bool NoWarnV1 { get; set; }

    [CommandOption("--env-file <PATH>")]
    [Description("Load ${...} values from a .env file (repeatable) so the manifest interpolates during the lint.")]
    public string[]? EnvFiles { get; set; }

    public IEnumerable<string> EffectivePaths =>
        Paths is { Length: > 0 } ? Paths : ["jobs.yaml"];

    public IReadOnlyList<string> EnvFileList => EnvFiles ?? [];
}

/// <summary>
/// `steadycron validate [paths...]` — validates manifest schema and cross-references locally,
/// without hitting the API. Useful as a fast CI gate before sync.
/// </summary>
public sealed class ValidateCommand : Command<ValidateSettings>
{
    private readonly ManifestLoader _loader;
    private readonly ManifestValidator _validator;

    public ValidateCommand(ManifestLoader loader, ManifestValidator validator)
    {
        _loader = loader;
        _validator = validator;
    }

    public override int Execute(CommandContext context, ValidateSettings settings)
    {
        ManifestFile manifest;
        try
        {
            // Validate is a local read-only lint, so the env-file guardrail is not enforced here;
            // --env-file is supported only to let manifests with ${...} interpolate during the lint.
            var getVar = ManifestEnvironment.Build(
                settings.EffectivePaths, settings.EnvFileList,
                allowProcessEnv: true, enforceEnvFile: false);
            manifest = _loader.LoadFromPaths(settings.EffectivePaths, getVar);
        }
        catch (ManifestException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]✗[/] {ex.Message}");
            return ExitCodes.ManifestError;
        }

        if (!settings.NoWarnV1 && ManifestLoader.IsV1(manifest))
        {
            AnsiConsole.MarkupLine(
                "[yellow]⚠ Manifest version 1 is deprecated.[/] Run [bold]steadycron export[/] to upgrade to v2.");
        }

        var result = _validator.Validate(manifest);

        if (result.IsValid)
        {
            var jobCount = manifest.Jobs?.Count ?? 0;
            var channelCount = manifest.Channels?.Count ?? 0;
            var tagCount = manifest.Tags?.Count ?? 0;
            var varCount = manifest.Variables?.Count ?? 0;

            var summary = new List<string> { $"[green]{jobCount} job(s)[/]" };
            if (channelCount > 0) { summary.Add($"[green]{channelCount} channel(s)[/]"); }
            if (tagCount > 0) { summary.Add($"[green]{tagCount} tag(s)[/]"); }
            if (varCount > 0) { summary.Add($"[green]{varCount} variable(s)[/]"); }

            AnsiConsole.MarkupLine($"[green]✓ Manifest is valid.[/] {string.Join(", ", summary)}");
            return ExitCodes.Ok;
        }

        foreach (var error in result.Errors)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]✗[/] {error}");
        }

        AnsiConsole.MarkupLine(
            $"\n[red]{result.Errors.Count} validation error(s).[/] Fix the above and re-run.");
        return ExitCodes.ManifestError;
    }
}
