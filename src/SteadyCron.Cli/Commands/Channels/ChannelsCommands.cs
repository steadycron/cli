using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Channels;

internal static class ChannelLookup
{
    public static async Task<AlertChannelResponse> ResolveAsync(SteadyCronClient client, string identifier, CancellationToken ct)
    {
        var channels = await client.ListChannelsAsync(ct);
        if (Guid.TryParse(identifier, out var id))
        {
            return channels.FirstOrDefault(c => c.Id == id)
                ?? throw new CliException($"No channel with id '{identifier}'.", ExitCodes.Error);
        }

        var matches = channels.Where(c => string.Equals(c.Name, identifier, StringComparison.Ordinal)).ToList();
        return matches.Count switch
        {
            0 => throw new CliException($"No channel named '{identifier}'.", ExitCodes.Error),
            1 => matches[0],
            _ => throw new CliException($"{matches.Count} channels are named '{identifier}'. Use the channel id.", ExitCodes.Error),
        };
    }

    public static string DescribeConfig(AlertChannelResponse c)
    {
        if (c.Config is not { Count: > 0 } cfg)
        {
            return "—";
        }

        return c.Kind switch
        {
            "email" => cfg.GetValueOrDefault("to") ?? "—",
            "telegram" => $"chat {cfg.GetValueOrDefault("chat_id") ?? "?"}",
            "webhook" => cfg.GetValueOrDefault("url") ?? "—",
            _ => "configured",
        };
    }
}

public sealed class ChannelsListCommand : SteadyCronCommandBase<CliSettings>
{
    public ChannelsListCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(CliSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var channels = await client.ListChannelsAsync(ct);

        if (output.Json)
        {
            output.WriteJson(channels);
            return ExitCodes.Ok;
        }

        if (channels.Count == 0)
        {
            output.Info("No alert channels defined.");
            return ExitCodes.Ok;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Kind");
        table.AddColumn("Target");
        table.AddColumn("Id");
        foreach (var c in channels)
        {
            table.AddRow(
                Markup.Escape(c.Name),
                Markup.Escape(c.Kind),
                Markup.Escape(ChannelLookup.DescribeConfig(c)),
                $"[grey]{c.Id}[/]");
        }

        output.Render(table);
        return ExitCodes.Ok;
    }
}

public sealed class ChannelCreateSettings : CliSettings
{
    [CommandOption("--name <NAME>")]
    [Description("Display name for the channel.")]
    public string? Name { get; set; }

    [CommandOption("--kind <KIND>")]
    [Description("email | slack | discord | webhook | telegram.")]
    public string? Kind { get; set; }

    [CommandOption("--to <EMAIL>")]
    [Description("email: destination address.")]
    public string? To { get; set; }

    [CommandOption("--webhook-url <URL>")]
    [Description("slack/discord: incoming webhook URL.")]
    public string? WebhookUrl { get; set; }

    [CommandOption("--url <URL>")]
    [Description("webhook: target URL (may contain {{variables}}).")]
    public string? Url { get; set; }

    [CommandOption("--method <METHOD>")]
    [Description("webhook: POST (default) or PUT.")]
    public string? Method { get; set; }

    [CommandOption("--header <HEADER>")]
    [Description("webhook: custom header as 'Name: value' (repeatable).")]
    public string[] Headers { get; set; } = [];

    [CommandOption("--secret <SECRET>")]
    [Description("webhook: HMAC signing secret.")]
    public string? Secret { get; set; }

    [CommandOption("--body-template <TEMPLATE>")]
    [Description("webhook: request body template.")]
    public string? BodyTemplate { get; set; }

    [CommandOption("--bot-token <TOKEN>")]
    [Description("telegram: bot token from @BotFather.")]
    public string? BotToken { get; set; }

    [CommandOption("--chat-id <ID>")]
    [Description("telegram: numeric chat id or @username.")]
    public string? ChatId { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return ValidationResult.Error("--name is required.");
        }

        if (string.IsNullOrWhiteSpace(Kind))
        {
            return ValidationResult.Error("--kind is required (email | slack | discord | webhook | telegram).");
        }

        return ValidationResult.Success();
    }
}

public sealed class ChannelCreateCommand : SteadyCronCommandBase<ChannelCreateSettings>
{
    private static readonly IReadOnlyList<string> KnownKinds = ManifestSchema.ChannelKinds;

    public ChannelCreateCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(ChannelCreateSettings settings, OutputContext output, CancellationToken ct)
    {
        var kind = settings.Kind!.Trim().ToLowerInvariant();
        if (!KnownKinds.Contains(kind))
        {
            throw new CliException($"Unknown channel kind '{settings.Kind}'. Expected one of: {string.Join(", ", KnownKinds)}.", ExitCodes.Error);
        }

        var config = BuildConfig(kind, settings);

        var client = CreateClient(settings);
        var channel = await client.CreateChannelAsync(new CreateAlertChannelRequest(settings.Name!, kind, config), ct);

        if (output.Json)
        {
            output.WriteJson(channel);
            return ExitCodes.Ok;
        }

        output.Success($"Created {kind} channel '{channel.Name}' ({channel.Id}).");
        return ExitCodes.Ok;
    }

    private static Dictionary<string, string> BuildConfig(string kind, ChannelCreateSettings s)
    {
        var config = new Dictionary<string, string>(StringComparer.Ordinal);

        switch (kind)
        {
            case "email":
                Require(s.To, "--to", kind);
                config["to"] = s.To!.Trim();
                break;

            case "slack":
            case "discord":
                Require(s.WebhookUrl, "--webhook-url", kind);
                config["webhook_url"] = s.WebhookUrl!.Trim();
                break;

            case "webhook":
                Require(s.Url, "--url", kind);
                config["url"] = s.Url!.Trim();
                if (!string.IsNullOrWhiteSpace(s.Method)) { config["method"] = s.Method.Trim().ToUpperInvariant(); }
                if (!string.IsNullOrWhiteSpace(s.Secret)) { config["secret"] = s.Secret; }
                if (!string.IsNullOrWhiteSpace(s.BodyTemplate)) { config["body_template"] = s.BodyTemplate; }
                if (s.Headers.Length > 0) { config["headers"] = JsonSerializer.Serialize(ParseHeaders(s.Headers)); }
                break;

            case "telegram":
                Require(s.BotToken, "--bot-token", kind);
                Require(s.ChatId, "--chat-id", kind);
                config["bot_token"] = s.BotToken!.Trim();
                config["chat_id"] = s.ChatId!.Trim();
                break;
        }

        return config;
    }

    private static void Require(string? value, string flag, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException($"{flag} is required for {kind} channels.", ExitCodes.Error);
        }
    }

    private static Dictionary<string, string> ParseHeaders(IEnumerable<string> raw)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in raw)
        {
            var idx = entry.IndexOf(':');
            if (idx <= 0)
            {
                throw new CliException($"Invalid --header '{entry}'. Use 'Name: value'.", ExitCodes.Error);
            }

            headers[entry[..idx].Trim()] = entry[(idx + 1)..].Trim();
        }

        return headers;
    }
}

public class ChannelTargetSettings : CliSettings
{
    [CommandArgument(0, "<CHANNEL>")]
    [Description("Channel id (GUID) or exact name.")]
    public string Identifier { get; set; } = string.Empty;
}

public sealed class ChannelTestCommand : SteadyCronCommandBase<ChannelTargetSettings>
{
    public ChannelTestCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(ChannelTargetSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var channel = await ChannelLookup.ResolveAsync(client, settings.Identifier, ct);
        await client.TestChannelAsync(channel.Id, ct);
        output.Success($"Test alert enqueued for '{channel.Name}'. Check the channel for delivery.");
        return ExitCodes.Ok;
    }
}

public sealed class ChannelDeleteSettings : ChannelTargetSettings
{
    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt.")]
    public bool Yes { get; set; }
}

public sealed class ChannelDeleteCommand : SteadyCronCommandBase<ChannelDeleteSettings>
{
    public ChannelDeleteCommand(ConfigResolver r, SteadyCronClientFactory f, CancellationProvider c) : base(r, f, c) { }

    protected override async Task<int> RunAsync(ChannelDeleteSettings settings, OutputContext output, CancellationToken ct)
    {
        var client = CreateClient(settings);
        var channel = await ChannelLookup.ResolveAsync(client, settings.Identifier, ct);

        if (!settings.Yes && !settings.Json && !Console.IsInputRedirected)
        {
            if (!output.Out.Confirm($"Delete channel '{Markup.Escape(channel.Name)}' ({channel.Kind})?", false))
            {
                output.Info("Aborted.");
                return ExitCodes.Ok;
            }
        }

        await client.DeleteChannelAsync(channel.Id, ct);
        output.Success($"Deleted channel '{channel.Name}'.");
        return ExitCodes.Ok;
    }
}
