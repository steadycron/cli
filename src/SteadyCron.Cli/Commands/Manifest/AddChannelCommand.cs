using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Manifest.Generators;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Manifest;

public sealed class AddChannelSettings : ManifestAddSettingsBase
{
    [CommandOption("--kind <KIND>")]
    [Description("email | slack | discord | webhook | telegram.")]
    public string? Kind { get; set; }

    [CommandOption("--name <NAME>")]
    public string? Name { get; set; }

    [CommandOption("--to <EMAIL>")]
    [Description("email: destination address. Never used for other kinds — their config is always an ${ENV} placeholder.")]
    public string? To { get; set; }
}

/// <summary>
/// <c>steadycron manifest add channel</c> — appends an alert channel block (SPEC-21). Every
/// secret-bearing config value (webhook URLs, bot tokens) is emitted as an <c>${ENV}</c>
/// placeholder — this command never prompts for a secret.
/// </summary>
public sealed class AddChannelCommand : AsyncCommand<AddChannelSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AddChannelSettings settings)
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

        string? to = null;
        if (kind == "email")
        {
            to = ResolveTo(settings, interactive, output);
            if (to is null)
            {
                return ExitCodes.Error;
            }
        }

        var spec = new NewChannelSpec { Kind = kind, Name = name, To = to };
        var block = new YamlBlockRenderer().RenderChannel(spec);

        return await ManifestAddExecutor.ExecuteAsync(
            output,
            settings,
            ManifestSection.Channels,
            block,
            (editor, content) => editor.ResourceExists(content, ManifestSection.Channels, ("name", name)),
            $"channel '{name}' already exists in {settings.TargetPath}",
            $"{kind} channel {name}");
    }

    private static string? ResolveKind(AddChannelSettings settings, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(settings.Kind))
        {
            var k = settings.Kind.Trim().ToLowerInvariant();
            if (ManifestSchema.ChannelKinds.Contains(k))
            {
                return k;
            }

            output.Error($"--kind must be one of {string.Join(", ", ManifestSchema.ChannelKinds)} (got '{settings.Kind}').");
            return null;
        }

        if (!interactive)
        {
            output.Error($"--kind is required (no terminal available to prompt for it). Expected one of {string.Join(", ", ManifestSchema.ChannelKinds)}.");
            return null;
        }

        return AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Kind:").AddChoices(ManifestSchema.ChannelKinds));
    }

    private static string? ResolveName(AddChannelSettings settings, bool interactive, OutputContext output)
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

        return AnsiConsole.Prompt(new TextPrompt<string>("Channel name:"));
    }

    private static string? ResolveTo(AddChannelSettings settings, bool interactive, OutputContext output)
    {
        if (!string.IsNullOrWhiteSpace(settings.To))
        {
            return settings.To.Trim();
        }

        if (!interactive)
        {
            output.Error("--to is required for email channels (no terminal available to prompt for it).");
            return null;
        }

        return AnsiConsole.Prompt(new TextPrompt<string>("Email address:"));
    }
}
