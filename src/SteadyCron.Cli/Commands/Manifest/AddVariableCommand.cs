using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest.Generators;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Manifest;

public sealed class AddVariableSettings : ManifestAddSettingsBase
{
    [CommandArgument(0, "[NAME]")]
    public string? Name { get; set; }
}

/// <summary>
/// <c>steadycron manifest add variable &lt;name&gt;</c> — appends a template variable block
/// (SPEC-21). The value is always <c>${NAME}</c> — variable values are write-only secrets, so
/// this command never prompts for one.
/// </summary>
public sealed class AddVariableCommand : AsyncCommand<AddVariableSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AddVariableSettings settings)
    {
        var output = new OutputContext(json: false, quiet: false, noColor: false);

        if (settings.Terraform)
        {
            output.Error("--terraform is not yet supported for 'add'; see 'manifest scaffold --terraform'.");
            return ExitCodes.Error;
        }

        var interactive = TerminalHelper.IsInteractive();

        string? name = settings.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (!interactive)
            {
                output.Error("A variable name is required (no terminal available to prompt for it).");
                return ExitCodes.Error;
            }

            name = AnsiConsole.Prompt(new TextPrompt<string>(PromptFormatting.Marker("Variable name:")));
        }

        name = name.Trim();
        var spec = new NewVariableSpec { Name = name };
        var block = new YamlBlockRenderer().RenderVariable(spec);

        return await ManifestAddExecutor.ExecuteAsync(
            output,
            settings,
            ManifestSection.Variables,
            block,
            (editor, content) => editor.ResourceExists(content, ManifestSection.Variables, ("name", name)),
            $"variable '{name}' already exists in {settings.TargetPath}",
            $"variable {name}");
    }
}
