using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Manifest.Generators;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Manifest;

public sealed class AddTagSettings : ManifestAddSettingsBase
{
    [CommandArgument(0, "[KEY]")]
    public string? Key { get; set; }

    [CommandArgument(1, "[VALUE]")]
    public string? Value { get; set; }

    [CommandOption("--color <COLOR>")]
    [Description("Optional tag color.")]
    public string? Color { get; set; }
}

/// <summary>
/// <c>steadycron manifest add tag &lt;key&gt; &lt;value&gt;</c> — appends a tag block (SPEC-21).
/// </summary>
public sealed class AddTagCommand : AsyncCommand<AddTagSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AddTagSettings settings)
    {
        var output = new OutputContext(json: false, quiet: false, noColor: false);

        if (settings.Terraform)
        {
            output.Error("--terraform is not yet supported for 'add'; see 'manifest scaffold --terraform'.");
            return ExitCodes.Error;
        }

        var interactive = TerminalHelper.IsInteractive();

        var key = ResolveValue(settings.Key, "Tag key:", "--key", interactive, output);
        if (key is null)
        {
            return ExitCodes.Error;
        }

        var value = ResolveValue(settings.Value, "Tag value:", "--value", interactive, output);
        if (value is null)
        {
            return ExitCodes.Error;
        }

        if (settings.Color is not null && !ManifestSchema.TagColors.Contains(settings.Color.Trim().ToLowerInvariant()))
        {
            output.Error($"--color must be one of {string.Join(", ", ManifestSchema.TagColors)} (got '{settings.Color}').");
            return ExitCodes.Error;
        }

        var spec = new NewTagSpec { Key = key, Value = value, Color = settings.Color?.Trim().ToLowerInvariant() };
        var block = new YamlBlockRenderer().RenderTag(spec);

        return await ManifestAddExecutor.ExecuteAsync(
            output,
            settings,
            ManifestSection.Tags,
            block,
            (editor, content) => editor.ResourceExists(content, ManifestSection.Tags, ("key", key), ("value", value)),
            $"tag '{key}:{value}' already exists in {settings.TargetPath}",
            $"tag {key}:{value}");
    }

    private static string? ResolveValue(string? given, string prompt, string flagName, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(given))
        {
            return given.Trim();
        }

        if (!interactive)
        {
            output.Error($"{flagName} is required (no terminal available to prompt for it).");
            return null;
        }

        return AnsiConsole.Prompt(new TextPrompt<string>(PromptFormatting.Marker(prompt)));
    }
}
