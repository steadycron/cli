using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Variables;

internal static class VariableLookup
{
    public static async Task<TemplateVariableResponse> ResolveAsync(SteadyCronClient client, string identifier, CancellationToken ct)
    {
        var vars = await client.ListVariablesAsync(ct);
        if (Guid.TryParse(identifier, out var id))
        {
            return vars.FirstOrDefault(v => v.Id == id)
                ?? throw new CliException($"No variable with id '{identifier}'.", ExitCodes.Error);
        }

        return vars.FirstOrDefault(v => string.Equals(v.Name, identifier, StringComparison.Ordinal))
            ?? throw new CliException($"No variable named '{identifier}'.", ExitCodes.Error);
    }
}

public sealed class VarsListCommand : SteadyCronCommandBase<CliSettings>
{
    public VarsListCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(CliSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var vars = await client.ListVariablesAsync(ct);

        if (output.Json)
        {
            output.WriteJson(vars);
            return ExitCodes.Ok;
        }

        if (vars.Count == 0)
        {
            output.Info("No template variables defined.");
            return ExitCodes.Ok;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Value");
        table.AddColumn("Updated");
        foreach (var v in vars)
        {
            table.AddRow(
                Markup.Escape($"{{{{{v.Name}}}}}"),
                Markup.Escape(v.Value.Length > 60 ? v.Value[..60] + "…" : v.Value),
                Markup.Escape(JobFormatting.When(v.UpdatedAt)));
        }

        output.Render(table);
        return ExitCodes.Ok;
    }
}

public sealed class VarSetSettings : CliSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Variable name (letters, digits, underscores; must start with a letter or underscore).")]
    public string Name { get; set; } = string.Empty;

    [CommandArgument(1, "[VALUE]")]
    [Description("Variable value. Alternatively use --value.")]
    public string? ValueArg { get; set; }

    [CommandOption("--value <VALUE>")]
    [Description("Variable value (use this to set an empty or whitespace value explicitly).")]
    public string? ValueOpt { get; set; }

    public string? ResolvedValue => ValueArg ?? ValueOpt;

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return ValidationResult.Error("A variable name is required.");
        }

        return ValueArg is null && ValueOpt is null
            ? ValidationResult.Error("Provide a value as an argument or with --value (use --value \"\" to clear).")
            : ValidationResult.Success();
    }
}

/// <summary>`steadycron vars set NAME VALUE` — creates the variable, or updates it if it exists (upsert).</summary>
public sealed class VarSetCommand : SteadyCronCommandBase<VarSetSettings>
{
    public VarSetCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(VarSetSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var value = settings.ResolvedValue ?? string.Empty;

        var existing = (await client.ListVariablesAsync(ct))
            .FirstOrDefault(v => string.Equals(v.Name, settings.Name, StringComparison.Ordinal));

        TemplateVariableResponse result;
        bool created;
        if (existing is null)
        {
            result = await client.CreateVariableAsync(new CreateTemplateVariableRequest(settings.Name, value), ct);
            created = true;
        }
        else
        {
            result = await client.UpdateVariableAsync(existing.Id, new UpdateTemplateVariableRequest(Value: value), ct);
            created = false;
        }

        if (output.Json)
        {
            output.WriteJson(result);
            return ExitCodes.Ok;
        }

        output.Success($"{(created ? "Created" : "Updated")} variable {{{{{result.Name}}}}}.");
        return ExitCodes.Ok;
    }
}

public sealed class VarDeleteSettings : CliSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Variable name or id.")]
    public string Identifier { get; set; } = string.Empty;

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt.")]
    public bool Yes { get; set; }
}

public sealed class VarDeleteCommand : SteadyCronCommandBase<VarDeleteSettings>
{
    public VarDeleteCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(VarDeleteSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var variable = await VariableLookup.ResolveAsync(client, settings.Identifier, ct);

        if (!settings.Yes && !settings.Json && !Console.IsInputRedirected)
        {
            if (!output.Out.Confirm($"Delete variable '{Markup.Escape(variable.Name)}'?", false))
            {
                output.Info("Aborted.");
                return ExitCodes.Ok;
            }
        }

        await client.DeleteVariableAsync(variable.Id, ct);
        output.Success($"Deleted variable {{{{{variable.Name}}}}}.");
        return ExitCodes.Ok;
    }
}
