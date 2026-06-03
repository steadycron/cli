using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Tags;

internal static class TagLookup
{
    /// <summary>Resolves a tag from an id (GUID) or a <c>key:value</c> string.</summary>
    public static async Task<TagResponse> ResolveAsync(SteadyCronClient client, string identifier, CancellationToken ct)
    {
        var tags = await client.ListTagsAsync(ct);

        if (Guid.TryParse(identifier, out var id))
        {
            return tags.FirstOrDefault(t => t.Id == id)
                ?? throw new CliException($"No tag with id '{identifier}'.", ExitCodes.Error);
        }

        var colon = identifier.IndexOf(':');
        if (colon <= 0)
        {
            throw new CliException("Tag must be given as a key:value pair or an id.", ExitCodes.Error);
        }

        var key = identifier[..colon];
        var value = identifier[(colon + 1)..];
        return tags.FirstOrDefault(t =>
                   string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase))
               ?? throw new CliException($"No tag '{identifier}'.", ExitCodes.Error);
    }
}

public sealed class TagsListCommand : SteadyCronCommandBase<CliSettings>
{
    public TagsListCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(CliSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var tags = await client.ListTagsAsync(ct);

        if (output.Json)
        {
            output.WriteJson(tags);
            return ExitCodes.Ok;
        }

        if (tags.Count == 0)
        {
            output.Info("No tags defined.");
            return ExitCodes.Ok;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Tag");
        table.AddColumn("Color");
        table.AddColumn("Jobs");
        table.AddColumn("Id");
        foreach (var t in tags)
        {
            table.AddRow(
                Markup.Escape(t.Display),
                Markup.Escape(t.Color ?? "auto"),
                t.JobCount.ToString(),
                $"[grey]{t.Id}[/]");
        }

        output.Render(table);
        return ExitCodes.Ok;
    }
}

public sealed class TagCreateSettings : CliSettings
{
    [CommandArgument(0, "<KEY>")]
    [Description("Tag key, e.g. env.")]
    public string Key { get; set; } = string.Empty;

    [CommandArgument(1, "<VALUE>")]
    [Description("Tag value, e.g. prod.")]
    public string Value { get; set; } = string.Empty;

    [CommandOption("--color <COLOR>")]
    [Description("One of: slate, green, amber, red, blue, violet, teal, pink.")]
    public string? Color { get; set; }

    public override ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Value)
            ? ValidationResult.Error("Both key and value are required.")
            : ValidationResult.Success();
}

public sealed class TagCreateCommand : SteadyCronCommandBase<TagCreateSettings>
{
    public TagCreateCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(TagCreateSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var tag = await client.CreateTagAsync(new CreateTagRequest(settings.Key, settings.Value, settings.Color), ct);

        if (output.Json)
        {
            output.WriteJson(tag);
            return ExitCodes.Ok;
        }

        output.Success($"Tag '{tag.Display}' ready ({tag.Id}).");
        return ExitCodes.Ok;
    }
}

public sealed class TagDeleteSettings : CliSettings
{
    [CommandArgument(0, "<TAG>")]
    [Description("Tag id (GUID) or key:value.")]
    public string Identifier { get; set; } = string.Empty;

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt.")]
    public bool Yes { get; set; }
}

public sealed class TagDeleteCommand : SteadyCronCommandBase<TagDeleteSettings>
{
    public TagDeleteCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(TagDeleteSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var tag = await TagLookup.ResolveAsync(client, settings.Identifier, ct);

        if (!settings.Yes && !settings.Json && !Console.IsInputRedirected)
        {
            if (!output.Out.Confirm($"Delete tag '{Markup.Escape(tag.Display)}' (used by {tag.JobCount} job(s))?", false))
            {
                output.Info("Aborted.");
                return ExitCodes.Ok;
            }
        }

        await client.DeleteTagAsync(tag.Id, ct);
        output.Success($"Deleted tag '{tag.Display}'.");
        return ExitCodes.Ok;
    }
}
