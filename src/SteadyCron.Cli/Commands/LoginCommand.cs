using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Api.Models;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands;

/// <summary>
/// `steadycron login` — signs an existing account into a new machine: mints a fresh API key
/// and saves it locally. Never reveals or reuses another machine's key (SPEC-20b).
/// </summary>
public sealed class LoginCommand : SteadyCronCommandBase<CliSettings>
{
    public LoginCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(CliSettings settings, OutputContext output, CancellationToken ct)
    {
        if (!TerminalHelper.IsInteractive())
        {
            throw new CliException("login requires an interactive terminal.", ExitCodes.Error);
        }

        var config = ResolveConfig(settings);
        var client = ClientFactory.CreateUnauthenticated(config);

        var email = output.Out.Prompt(new TextPrompt<string>(PromptFormatting.Marker("Email:")));
        var password = output.Out.Prompt(new TextPrompt<string>(PromptFormatting.Marker("Password:")).Secret());

        CliProvisioningData provisioning;
        try
        {
            provisioning = await client.CliLoginAsync(email, password, ct);
        }
        catch (SteadyCronApiException ex) when (ex.ErrorCode == "email_not_verified")
        {
            var signupId = SignupHelpers.ExtractSignupId(ex);
            output.Warn("This account is not verified yet. Verification code sent to your email.");
            provisioning = await SignupHelpers.VerifyLoopAsync(client, signupId, output, ct);
            output.Success("Email verified.");
        }

        await SignupHelpers.ProvisionAndSaveAsync(client, config, settings, provisioning.ProvisioningToken, output, ct);

        output.Line();
        output.Markup(
            $"[{Styles.Hint}]Optional hygiene: stale cli-* keys from other machines can be revoked from the dashboard.[/]");

        return ExitCodes.Ok;
    }
}
